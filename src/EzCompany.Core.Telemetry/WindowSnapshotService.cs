using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Crypto;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EzCompany.Core.Telemetry;

public sealed record CapturedSnapshotFrames(
    int Width,
    int Height,
    byte[] FullJpeg,
    byte[] ThumbnailJpeg);

/// <summary>
/// Antivirus-safe Method B snapshot service:
/// renders ONLY the application's own <c>WorkspaceWindow</c> via WPF <see cref="RenderTargetBitmap"/>
/// on a random 15–30 minute interval (60s when <c>EZCOMPANY_TELEMETRY_DEBUG=1</c>),
/// then seals images end-to-end with the Company X25519 Encryption Public Key so employee nodes cannot decrypt them.
/// </summary>
public sealed class WindowSnapshotService : BackgroundService
{
    public const int FullMaxDimension = 1280;
    public const int FullJpegQuality = 60;
    public const int ThumbMaxDimension = 320;
    public const int ThumbJpegQuality = 50;

    private readonly IWorkspaceWindowAccessor _windowAccessor;
    private readonly IDbContextFactory<TelemetryDbContext> _telDbFactory;
    private readonly LocalKeyStore _keyStore;
    private readonly EciesCryptoService _ecies;
    private readonly ISessionContext _session;
    private readonly ILocalStoragePaths _paths;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WindowSnapshotService> _logger;

    public WindowSnapshotService(
        IWorkspaceWindowAccessor windowAccessor,
        IDbContextFactory<TelemetryDbContext> telDbFactory,
        LocalKeyStore keyStore,
        EciesCryptoService ecies,
        ISessionContext session,
        ILocalStoragePaths paths,
        ILogger<WindowSnapshotService> logger,
        TimeProvider? timeProvider = null)
    {
        _windowAccessor = windowAccessor;
        _telDbFactory = telDbFactory;
        _keyStore = keyStore;
        _ecies = ecies;
        _session = session;
        _paths = paths;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public static bool IsDebugCadenceEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("EZCOMPANY_TELEMETRY_DEBUG"), "1", StringComparison.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay = IsDebugCadenceEnabled
                ? TimeSpan.FromSeconds(60)
                : TimeSpan.FromMinutes(Random.Shared.Next(15, 31));

            try
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                await TryCaptureActiveWindowOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "WindowSnapshotService iteration error.");
            }
        }
    }

    public async Task<bool> TryCaptureActiveWindowOnceAsync(CancellationToken ct = default)
    {
        if (!_session.IsAuthenticated || Win32ActivityInterop.GetIdleSeconds() >= TelemetryCollectorService.IdleThresholdSeconds)
        {
            return false;
        }

        Window? window = _windowAccessor.GetActiveWindow();
        if (window is null)
        {
            return false;
        }

        CapturedSnapshotFrames? frames = await window.Dispatcher.InvokeAsync(() => CaptureWpfWindow(window));
        if (frames is null)
        {
            return false;
        }

        try
        {
            await EncryptAndStoreSnapshotAsync(
                frames.FullJpeg,
                frames.ThumbnailJpeg,
                frames.Width,
                frames.Height,
                ct).ConfigureAwait(false);
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(frames.FullJpeg);
            CryptographicOperations.ZeroMemory(frames.ThumbnailJpeg);
        }
    }

    public async Task<Guid> EncryptAndStoreSnapshotAsync(
        byte[] fullJpegPlaintext,
        byte[] thumbnailJpegPlaintext,
        int width,
        int height,
        CancellationToken ct = default)
    {
        byte[] encPub = _keyStore.LoadPinnedCompanyEncryptionPublicKey()
            ?? throw new InvalidOperationException("Chưa có khóa công khai mã hóa Telemetry của công ty.");

        byte[] encryptedThumb = _ecies.Encrypt(encPub, thumbnailJpegPlaintext);
        byte[] encryptedFull = _ecies.Encrypt(encPub, fullJpegPlaintext);

        Guid id = Guid.NewGuid();
        await using TelemetryDbContext db = await _telDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.EncryptedSnapshots.Add(new EncryptedSnapshotEntity
        {
            Id = id,
            MachineId = _paths.MachineId,
            UserId = _session.UserId,
            Username = string.IsNullOrWhiteSpace(_session.Username) ? "unknown" : _session.Username,
            CapturedAtUtc = _timeProvider.GetUtcNow(),
            Width = width,
            Height = height,
            ThumbnailEncryptedBlob = encryptedThumb,
            FullImageEncryptedBlob = encryptedFull,
            ThumbnailSizeBytes = encryptedThumb.Length,
            FullImageSizeBytes = encryptedFull.Length,
            IsFullDownloadedOnDirector = false
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return id;
    }

    private static CapturedSnapshotFrames? CaptureWpfWindow(Window window)
    {
        if (!window.IsVisible || window.WindowState == WindowState.Minimized || !window.IsActive)
        {
            return null;
        }

        int pixelWidth = Math.Max(1, (int)Math.Round(window.ActualWidth));
        int pixelHeight = Math.Max(1, (int)Math.Round(window.ActualHeight));
        if (pixelWidth <= 10 || pixelHeight <= 10)
        {
            return null;
        }

        var rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(window);
        rtb.Freeze();

        (byte[] fullBytes, int fullW, int fullH) = EncodeScaledJpeg(rtb, FullMaxDimension, FullJpegQuality);
        (byte[] thumbBytes, _, _) = EncodeScaledJpeg(rtb, ThumbMaxDimension, ThumbJpegQuality);

        return new CapturedSnapshotFrames(fullW, fullH, fullBytes, thumbBytes);
    }

    private static (byte[] JpegBytes, int Width, int Height) EncodeScaledJpeg(
        BitmapSource source,
        int maxDimension,
        int quality)
    {
        BitmapSource target = source;
        double maxSide = Math.Max(source.PixelWidth, source.PixelHeight);
        if (maxSide > maxDimension)
        {
            double scale = maxDimension / maxSide;
            var transformed = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            transformed.Freeze();
            target = transformed;
        }

        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return (ms.ToArray(), target.PixelWidth, target.PixelHeight);
    }
}
