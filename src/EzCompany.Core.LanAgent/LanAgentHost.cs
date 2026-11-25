using System.Diagnostics;
using System.Net;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Crypto;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent.Database;
using EzCompany.Core.LanAgent.Discovery;
using EzCompany.Core.LanAgent.Models;
using EzCompany.Core.LanAgent.Pairing;
using EzCompany.Core.LanAgent.Recovery;
using EzCompany.Core.LanAgent.Security;
using EzCompany.Core.LanAgent.Vault;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EzCompany.Core.LanAgent;

public sealed record LanAgentOptions(bool BindLoopbackOnly = false, int PreferredPort = 0);

/// <summary>
/// Embedded Kestrel Minimal API host running inside every Ez Company workstation (Zero-SMB architecture).
/// </summary>
public sealed class LanAgentHost : IHostedService, IAsyncDisposable
{
    private readonly IServiceProvider _rootServices;
    private readonly LanAgentOptions _options;
    private readonly ISessionContext _session;
    private readonly ILogger<LanAgentHost> _logger;
    private WebApplication? _app;

    public LanAgentHost(
        IServiceProvider rootServices,
        LanAgentOptions options,
        ISessionContext session,
        ILogger<LanAgentHost> logger)
    {
        _rootServices = rootServices;
        _options = options;
        _session = session;
        _logger = logger;
    }

    public int BoundPort { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = Array.Empty<string>()
        });

        builder.Logging.ClearProviders();

        IPAddress bindIp = _options.BindLoopbackOnly ? IPAddress.Loopback : IPAddress.Any;
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Limits.MaxRequestBodySize = 32 * 1024 * 1024;
            kestrel.Listen(bindIp, _options.PreferredPort);
        });

        // Bridge core singletons and logger factory from parent host into embedded Kestrel DI
        ILoggerFactory? parentLoggerFactory = _rootServices.GetService<ILoggerFactory>();
        if (parentLoggerFactory is not null)
        {
            builder.Services.AddSingleton(parentLoggerFactory);
        }

        builder.Services.AddSingleton(_rootServices.GetRequiredService<ILocalStoragePaths>());
        builder.Services.AddSingleton(_rootServices.GetRequiredService<ISessionContext>());
        builder.Services.AddSingleton(_rootServices.GetRequiredService<IPermissionService>());
        builder.Services.AddSingleton(_rootServices.GetRequiredService<IEventBus>());
        builder.Services.AddSingleton(_rootServices.GetRequiredService<IAuditLogger>());
        builder.Services.AddSingleton(_rootServices.GetRequiredService<TimeProvider>());
        builder.Services.AddSingleton(_rootServices.GetRequiredService<Ed25519Service>());
        builder.Services.AddSingleton(_rootServices.GetRequiredService<LocalKeyStore>());
        builder.Services.AddSingleton(_rootServices.GetRequiredService<PolicyEngine>());
        builder.Services.AddSingleton(_rootServices.GetRequiredService<PasswordResetTicketService>());
        builder.Services.AddSingleton(_rootServices.GetRequiredService<PairingSessionManager>());
        builder.Services.AddSingleton(_rootServices.GetRequiredService<AccountRecoveryManager>());
        builder.Services.AddSingleton(_rootServices.GetRequiredService<WorkTaskService>());
        builder.Services.AddSingleton(_rootServices.GetRequiredService<CompanyBrandingService>());
        builder.Services.AddSingleton(_rootServices.GetRequiredService<ChatService>());
        builder.Services.AddSingleton(_rootServices.GetRequiredService<TimelinePlanService>());
        builder.Services.AddSingleton(_rootServices.GetRequiredService<HrAttendanceService>());
        builder.Services.AddSingleton(_rootServices.GetRequiredService<NonceReplayCache>());
        builder.Services.AddSingleton(_rootServices.GetRequiredService<VaultPathGuard>());
        builder.Services.AddSingleton(_rootServices.GetRequiredService<DatabaseSnapshotService>());
        builder.Services.AddSingleton(_rootServices.GetRequiredService<IDbContextFactory<AppDbContext>>());
        builder.Services.AddSingleton(_rootServices.GetRequiredService<IDbContextFactory<TelemetryDbContext>>());

        _app = builder.Build();

        _app.Use(async (context, next) =>
        {
            var sw = Stopwatch.StartNew();
            await next().ConfigureAwait(false);
            sw.Stop();
            string admin = context.Items.TryGetValue("EzAdminName", out object? a) && a is string s ? s : "-";
            _logger.LogInformation(
                "LAN HTTP {Method} {Path} -> {StatusCode} ({ElapsedMs}ms) [Admin={Admin}]",
                context.Request.Method,
                context.Request.Path.Value,
                context.Response.StatusCode,
                sw.ElapsedMilliseconds,
                admin);
        });

        _app.UseMiddleware<DirectorSignatureAuthMiddleware>();

        MapEndpoints(_app);

        await _app.StartAsync(cancellationToken).ConfigureAwait(false);

        IServerAddressesFeature? addrFeature = _app.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>();

        string? firstAddr = addrFeature?.Addresses.FirstOrDefault();
        if (firstAddr is not null && Uri.TryCreate(firstAddr.Replace("0.0.0.0", "127.0.0.1"), UriKind.Absolute, out Uri? uri))
        {
            BoundPort = uri.Port;
            _session.CurrentLanPort = BoundPort;
        }

        _logger.LogInformation("EzCompany LAN Agent listening on port {Port}.", BoundPort);

        ILanDiscoveryService? discovery = _rootServices.GetService<ILanDiscoveryService>();
        discovery?.RefreshAdvertisement();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_app is not null)
        {
            await _app.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync().ConfigureAwait(false);
            _app = null;
        }
    }

    private static void MapEndpoints(WebApplication app)
    {
        // 1. LAN Pairing (gated by Director Pairing Window; no Director signature yet)
        app.MapPost("/api/v1/pairing/initiate", async (
            HttpContext http,
            PairingInitiateRequest req,
            PairingSessionManager pairing,
            CancellationToken ct) =>
        {
            string? remoteIp = http.Connection.RemoteIpAddress?.ToString();
            (bool accepted, string? error, PairingInitiateResponse? response) =
                await pairing.AcceptIncomingRequestAsync(req, remoteIp, ct).ConfigureAwait(false);

            return accepted
                ? Results.Ok(response)
                : Results.Json(new { message = error }, statusCode: StatusCodes.Status403Forbidden);
        });

        app.MapGet("/api/v1/pairing/status/{requestId:guid}", (
            Guid requestId,
            string machineId,
            string clientNonce,
            PairingSessionManager pairing) =>
        {
            PairingStatusResponse status = pairing.GetStatus(requestId, machineId, clientNonce);
            return Results.Ok(status);
        });

        // 2. Health & Telemetry Diagnostics (Director signed)
        app.MapGet("/api/v1/health", async (
            ILocalStoragePaths paths,
            ISessionContext session,
            IPermissionService permissions,
            IDbContextFactory<AppDbContext> appDbFactory,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            await using AppDbContext db = await appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            CompanyProfileEntity? company = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);

            long dbSize = File.Exists(paths.AppDbPath) ? new FileInfo(paths.AppDbPath).Length : 0;
            if (File.Exists(paths.TelemetryDbPath))
            {
                dbSize += new FileInfo(paths.TelemetryDbPath).Length;
            }

            long vaultSize = Directory.Exists(paths.VaultDirectory)
                ? Directory.EnumerateFiles(paths.VaultDirectory, "*", SearchOption.AllDirectories)
                    .Sum(f => new FileInfo(f).Length)
                : 0;

            double freeDiskPercent = ComputeFreeDiskPercent(paths.ProfileRoot);

            return Results.Ok(new WorkstationHealthDto(
                MachineId: paths.MachineId,
                CompanyId: company?.CompanyId ?? Guid.Empty,
                AppVersion: session.CurrentAppVersion,
                SchemaVersion: company?.AppSchemaVersion ?? 1,
                DbSizeBytes: dbSize,
                VaultSizeBytes: vaultSize,
                FreeDiskPercent: freeDiskPercent,
                ActivePolicyVersion: permissions.CurrentPolicy?.PolicyVersion ?? 0,
                IsPolicyLocked: permissions.IsLocked,
                PolicyLockReason: permissions.LockReason,
                UtcNow: timeProvider.GetUtcNow()));
        });

        // 3. Hot Policy Apply (updates policy.lic live without app restart)
        app.MapPost("/api/v1/policy/apply", async (
            ApplyPolicyRequestDto dto,
            LocalKeyStore keyStore,
            PolicyEngine policyEngine,
            ILocalStoragePaths paths,
            IPermissionService permissions,
            IAuditLogger audit,
            CancellationToken ct) =>
        {
            byte[]? pinnedRootPub = keyStore.LoadPinnedRootPublicKey();
            byte[]? devicePub = keyStore.LoadDevicePublicKey();

            PolicyVerificationResult verified = policyEngine.VerifyOffline(
                dto.SignedPolicyJson,
                pinnedRootPub,
                paths.MachineId,
                devicePub);

            if (!verified.IsValid || verified.Snapshot is null)
            {
                await audit.LogAsync("POLICY_APPLY_REJECTED", "core.lan", paths.MachineId, verified.Message, "Warning", ct)
                    .ConfigureAwait(false);
                return Results.BadRequest(new ApplyPolicyResponseDto(false, 0, verified.Message));
            }

            keyStore.SavePolicyLicenseJson(dto.SignedPolicyJson);
            permissions.ApplyPolicySnapshot(verified.Snapshot);

            await audit.LogAsync(
                "POLICY_APPLIED_LIVE",
                "core.lan",
                verified.Snapshot.Username,
                $"PolicyVersion={verified.Snapshot.PolicyVersion};Role={verified.Snapshot.Role}",
                "Info",
                ct).ConfigureAwait(false);

            return Results.Ok(new ApplyPolicyResponseDto(true, verified.Snapshot.PolicyVersion, "Đã cập nhật phân quyền tức thì."));
        });

        // 4. Vault Tree & Verified Download (strict Path Traversal protection)
        app.MapGet("/api/v1/vault/tree", (VaultPathGuard guard) =>
            Results.Ok(guard.ListVaultFiles()));

        app.MapGet("/api/v1/vault/download", async (
            string? relativePath,
            VaultPathGuard guard,
            HttpContext http,
            IAuditLogger audit,
            CancellationToken ct) =>
        {
            if (!guard.TryResolveSafeFile(relativePath, out string fullPath, out string error))
            {
                await audit.LogAsync("VAULT_TRAVERSAL_BLOCKED", "core.lan", relativePath, error, "Warning", ct)
                    .ConfigureAwait(false);
                return Results.BadRequest(new { message = error });
            }

            if (!File.Exists(fullPath))
            {
                return Results.NotFound(new { message = "Tệp không tồn tại trong Vault." });
            }

            string sha256 = await VaultPathGuard.ComputeSha256HexAsync(fullPath, ct).ConfigureAwait(false);
            http.Response.Headers["X-Ez-Sha256"] = sha256;
            string fileName = Path.GetFileName(fullPath);

            byte[] fileBytes = await File.ReadAllBytesAsync(fullPath, ct).ConfigureAwait(false);
            return Results.File(fileBytes, "application/octet-stream", fileName);
        });

        // 5. Consistent Non-Blocking SQLite Snapshot (VACUUM INTO)
        app.MapPost("/api/v1/database/snapshot", async (
            DatabaseSnapshotService snapshotService,
            HttpContext http,
            IAuditLogger audit,
            CancellationToken ct) =>
        {
            DatabaseSnapshotArtifact artifact = await snapshotService.CreateConsistentSnapshotAsync(ct).ConfigureAwait(false);
            try
            {
                http.Response.Headers["X-Ez-Sha256"] = artifact.Sha256Hex;
                http.Response.Headers["X-Ez-Schema-Version"] = artifact.SchemaVersion.ToString();

                await audit.LogAsync(
                    "DB_SNAPSHOT_EXPORTED",
                    "core.lan",
                    "app.db",
                    $"Sha256={artifact.Sha256Hex};Size={artifact.SizeBytes}",
                    "Info",
                    ct).ConfigureAwait(false);

                byte[] bytes = await File.ReadAllBytesAsync(artifact.TempFilePath, ct).ConfigureAwait(false);
                return Results.File(bytes, "application/x-sqlite3", $"snapshot_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.db");
            }
            finally
            {
                DatabaseSnapshotService.SafeDeleteTempFile(artifact.TempFilePath);
            }
        });

        // 6. Internal LAN Notice
        app.MapPost("/api/v1/notice", async (
            NoticeRequestDto notice,
            HttpContext http,
            IEventBus eventBus,
            IAuditLogger audit,
            CancellationToken ct) =>
        {
            string adminName = http.Items["EzAdminName"] as string ?? "Giám đốc";
            eventBus.Publish(new InternalNoticeEvent(notice.NoticeId, adminName, notice.Title, notice.Message, notice.SentAtUtc));

            await audit.LogAsync("LAN_NOTICE_RECEIVED", "core.lan", notice.Title, notice.Message, "Info", ct)
                .ConfigureAwait(false);

            return Results.Ok(new { received = true });
        });

        // 7. Audit logs & 2-Director Registry Sync
        app.MapGet("/api/v1/audit/recent", async (int? limit, IAuditLogger audit, CancellationToken ct) =>
            Results.Ok(await audit.GetRecentAsync(limit ?? 200, ct).ConfigureAwait(false)));

        app.MapGet("/api/v1/director/registry", async (
            IDbContextFactory<AppDbContext> appDbFactory,
            CancellationToken ct) =>
        {
            await using AppDbContext db = await appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            List<RegistryDeviceSyncDto> items = await db.PairedDevices.AsNoTracking()
                .Select(d => new RegistryDeviceSyncDto(
                    d.CompanyId,
                    d.MachineId,
                    d.DevicePublicKeyBase64,
                    d.UserId,
                    d.Username,
                    d.FullName,
                    d.Role,
                    d.CurrentPolicyVersion,
                    d.CurrentPolicyJson,
                    d.PendingPolicyJson,
                    d.LastKnownIp,
                    d.LastKnownPort,
                    d.AppVersion,
                    d.SchemaVersion,
                    d.PairedAtUtc,
                    d.PairedByAdminName))
                .ToListAsync(ct)
                .ConfigureAwait(false);

            return Results.Ok(items);
        });

        app.MapPost("/api/v1/director/registry/sync", async (
            List<RegistryDeviceSyncDto> incoming,
            IDbContextFactory<AppDbContext> appDbFactory,
            CancellationToken ct) =>
        {
            int mergedCount = await MergeDirectorRegistryAsync(appDbFactory, incoming, ct).ConfigureAwait(false);
            return Results.Ok(new { merged = mergedCount });
        });

        // 8. Telemetry Delta Pull & On-Demand Full Snapshot (for Milestone 3)
        app.MapGet("/api/v1/telemetry/delta", async (
            DateTimeOffset? sinceUtc,
            ILocalStoragePaths paths,
            IDbContextFactory<TelemetryDbContext> telDbFactory,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            DateTimeOffset since = sinceUtc ?? DateTimeOffset.MinValue;
            await using TelemetryDbContext db = await telDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

            List<TelemetryPresenceDto> presence = await db.PresenceSessions.AsNoTracking()
                .Where(x => x.UpdatedAtUtc > since)
                .OrderBy(x => x.UpdatedAtUtc)
                .Take(500)
                .Select(x => new TelemetryPresenceDto(
                    x.Id, x.MachineId, x.UserId, x.Username, x.DateKey,
                    x.StartedAtUtc, x.EndedAtUtc, x.State, x.DurationSeconds, x.UpdatedAtUtc))
                .ToListAsync(ct)
                .ConfigureAwait(false);

            List<TelemetryAppUsageDto> usage = await db.AppUsageRecords.AsNoTracking()
                .Where(x => x.LastSeenUtc > since)
                .OrderBy(x => x.LastSeenUtc)
                .Take(500)
                .Select(x => new TelemetryAppUsageDto(
                    x.Id, x.MachineId, x.UserId, x.Username, x.DateKey,
                    x.ProcessName, x.SanitizedWindowTitle, x.ActiveSeconds, x.LastSeenUtc))
                .ToListAsync(ct)
                .ConfigureAwait(false);

            List<TelemetrySnapshotMetaDto> snapshots = await db.EncryptedSnapshots.AsNoTracking()
                .Where(x => x.CapturedAtUtc > since)
                .OrderBy(x => x.CapturedAtUtc)
                .Take(200)
                .Select(x => new TelemetrySnapshotMetaDto(
                    x.Id, x.MachineId, x.UserId, x.Username, x.CapturedAtUtc,
                    x.Width, x.Height, x.ThumbnailEncryptedBlob,
                    x.ThumbnailSizeBytes, x.FullImageSizeBytes))
                .ToListAsync(ct)
                .ConfigureAwait(false);

            return Results.Ok(new TelemetryDeltaResponseDto(
                paths.MachineId,
                timeProvider.GetUtcNow(),
                presence,
                usage,
                snapshots));
        });

        app.MapGet("/api/v1/telemetry/snapshot/{id:guid}", async (
            Guid id,
            IDbContextFactory<TelemetryDbContext> telDbFactory,
            CancellationToken ct) =>
        {
            await using TelemetryDbContext db = await telDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            EncryptedSnapshotEntity? snap = await db.EncryptedSnapshots.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, ct)
                .ConfigureAwait(false);

            if (snap is null || snap.FullImageEncryptedBlob.Length == 0)
            {
                return Results.NotFound();
            }

            return Results.Ok(new TelemetryFullSnapshotDto(
                snap.Id,
                snap.MachineId,
                snap.CapturedAtUtc,
                snap.FullImageEncryptedBlob));
        });

        // 9. Employee Account / Password Reset (Device-signed requests + Root-signed ticket approval)
        app.MapPost("/api/v1/account-recovery/request", async (
            HttpContext http,
            AccountResetInitiateRequestDto req,
            AccountRecoveryManager recoveryManager,
            CancellationToken ct) =>
        {
            string devicePubB64 = http.Items["EzDevicePubKey"] as string ?? string.Empty;
            string? remoteIp = http.Connection.RemoteIpAddress?.ToString();
            (bool accepted, string? error, AccountResetInitiateResponseDto? resp) =
                await recoveryManager.AcceptRequestAsync(req, devicePubB64, remoteIp, ct).ConfigureAwait(false);

            return accepted
                ? Results.Ok(resp)
                : Results.Json(new { message = error }, statusCode: StatusCodes.Status403Forbidden);
        });

        app.MapGet("/api/v1/account-recovery/status/{requestId:guid}", async (
            Guid requestId,
            HttpContext http,
            AccountRecoveryManager recoveryManager,
            CancellationToken ct) =>
        {
            string callerMachineId = http.Items["EzDeviceMachineId"] as string ?? string.Empty;
            AccountResetStatusResponseDto status =
                await recoveryManager.GetStatusAsync(requestId, callerMachineId, ct).ConfigureAwait(false);
            return Results.Ok(status);
        });

        // 10. Company Peer Directory & Profile Update (Device-signed)
        app.MapGet("/api/v1/peers/directory", async (
            IDbContextFactory<AppDbContext> appDbFactory,
            CancellationToken ct) =>
        {
            await using AppDbContext db = await appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            List<PairedDeviceEntity> devices = await db.PairedDevices.AsNoTracking()
                .OrderBy(d => d.Role)
                .ThenBy(d => d.FullName)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            List<PeerDirectoryEntryDto> result = devices.Select(d => new PeerDirectoryEntryDto(
                d.MachineId,
                d.Username,
                d.FullName,
                d.Role,
                d.IsOnline,
                !string.IsNullOrWhiteSpace(d.LastKnownIp) && d.LastKnownPort > 0
                    ? $"{d.LastKnownIp}:{d.LastKnownPort}"
                    : null)).ToList();

            return Results.Ok(result);
        });

        app.MapPost("/api/v1/peers/profile-update", async (
            HttpContext http,
            ProfileUpdateNotificationDto dto,
            IDbContextFactory<AppDbContext> appDbFactory,
            IAuditLogger audit,
            CancellationToken ct) =>
        {
            string callerMachineId = http.Items["EzDeviceMachineId"] as string ?? string.Empty;
            if (!string.Equals(callerMachineId, dto.MachineId, StringComparison.OrdinalIgnoreCase))
            {
                return Results.Forbid();
            }

            await using AppDbContext db = await appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            PairedDeviceEntity? dev = await db.PairedDevices
                .FirstOrDefaultAsync(x => x.MachineId == callerMachineId, ct)
                .ConfigureAwait(false);

            if (dev is not null)
            {
                dev.Username = dto.Username.Trim().ToLowerInvariant();
                dev.FullName = dto.FullName.Trim();
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                await audit.LogAsync("PEER_PROFILE_UPDATED", "core.lan", callerMachineId, $"{dev.Username} ({dev.FullName})", "Info", ct)
                    .ConfigureAwait(false);
            }

            return Results.Ok(new { updated = true });
        });

        // 11. Peer-to-Peer Task Assignment & Handover Sync (Device-signed + Sender policy.lic verified against pinned RootPublicKey)
        app.MapPost("/api/v1/tasks/sync", async (
            HttpContext http,
            TaskSyncPayloadDto dto,
            LocalKeyStore keyStore,
            PolicyEngine policyEngine,
            WorkTaskService taskService,
            CancellationToken ct) =>
        {
            string callerMachineId = http.Items["EzDeviceMachineId"] as string ?? string.Empty;
            string callerDevicePubB64 = http.Items["EzDevicePubKey"] as string ?? string.Empty;
            byte[]? pinnedRootPub = keyStore.LoadPinnedRootPublicKey();
            if (pinnedRootPub is null || string.IsNullOrWhiteSpace(callerDevicePubB64))
            {
                return Results.Unauthorized();
            }

            byte[] callerDevicePub = Convert.FromBase64String(callerDevicePubB64);
            PolicyVerificationResult verified = policyEngine.VerifyOffline(
                dto.SenderSignedPolicyJson,
                pinnedRootPub,
                callerMachineId,
                callerDevicePub,
                commitClockGuard: false);

            if (!verified.IsValid || verified.Snapshot is null)
            {
                return Results.Json(
                    new TaskSyncAckDto(false, $"Giấy phép policy.lic của máy gửi không hợp lệ: {verified.Message}"),
                    statusCode: StatusCodes.Status403Forbidden);
            }

            byte[]? attachmentBytes = null;
            if (!string.IsNullOrWhiteSpace(dto.HandoverFileBase64))
            {
                attachmentBytes = Convert.FromBase64String(dto.HandoverFileBase64);
            }

            byte[]? assignmentAttachmentBytes = null;
            if (!string.IsNullOrWhiteSpace(dto.AssignmentFileBase64))
            {
                assignmentAttachmentBytes = Convert.FromBase64String(dto.AssignmentFileBase64);
            }

            var incoming = new WorkTaskEntity
            {
                Id = dto.TaskId,
                CompanyId = dto.CompanyId,
                TaskKind = dto.TaskKind,
                Title = dto.Title,
                Description = dto.Description,
                Priority = dto.Priority,
                Status = dto.Status,
                AssignerMachineId = dto.AssignerMachineId,
                AssignerUsername = dto.AssignerUsername,
                AssignerFullName = dto.AssignerFullName,
                AssignerRole = dto.AssignerRole,
                AssigneeMachineId = dto.AssigneeMachineId,
                AssigneeUsername = dto.AssigneeUsername,
                AssigneeFullName = dto.AssigneeFullName,
                AssigneeRole = dto.AssigneeRole,
                DueDateUtc = dto.DueDateUtc,
                CreatedAtUtc = dto.CreatedAtUtc,
                UpdatedAtUtc = dto.UpdatedAtUtc,
                CompletedAtUtc = dto.CompletedAtUtc,
                AssignmentFileName = dto.AssignmentFileName,
                AssignmentFileSha256 = dto.AssignmentFileSha256,
                HandoverNotes = dto.HandoverNotes,
                HandoverFileName = dto.HandoverFileName,
                HandoverFileSha256 = dto.HandoverFileSha256,
                HandedOverAtUtc = dto.HandedOverAtUtc,
                ReviewNotes = dto.ReviewNotes
            };

            await taskService.UpsertIncomingSyncedTaskAsync(
                incoming,
                attachmentBytes,
                callerMachineId,
                verified.Snapshot.Username,
                verified.Snapshot.FullName,
                dto.SyncAction,
                dto.HistoryNote,
                ct,
                assignmentAttachmentBytes).ConfigureAwait(false);

            return Results.Ok(new TaskSyncAckDto(true, "Đã nhận và lưu công việc đồng bộ qua LAN."));
        });

        // 12. Company Branding Logo Sync (GET=Device-signed, POST=Director-signed)
        app.MapGet("/api/v1/company/branding", async (
            CompanyBrandingService branding,
            CancellationToken ct) =>
        {
            (string? logoB64, DateTimeOffset? updatedAt, Guid companyId) =
                await branding.GetCurrentBrandingTupleAsync(ct).ConfigureAwait(false);
            return Results.Ok(new CompanyBrandingDto(companyId, logoB64, updatedAt));
        });

        app.MapPost("/api/v1/company/branding", async (
            CompanyBrandingDto dto,
            CompanyBrandingService branding,
            CancellationToken ct) =>
        {
            bool applied = await branding.ApplyIncomingLogoSyncAsync(
                dto.CompanyId,
                dto.CompanyLogoBase64,
                dto.UpdatedAtUtc,
                ct).ConfigureAwait(false);
            return Results.Ok(new { applied });
        });

        // 13. Internal P2P Chat Sync + Reverse History & Attachment Retrieval
        app.MapPost("/api/v1/chat/sync", async (
            HttpContext http,
            ChatSyncEnvelopeDto dto,
            LocalKeyStore keyStore,
            PolicyEngine policyEngine,
            ChatService chat,
            CancellationToken ct) =>
        {
            string callerMachineId = http.Items["EzDeviceMachineId"] as string ?? string.Empty;
            string callerDevicePubB64 = http.Items["EzDevicePubKey"] as string ?? string.Empty;
            byte[]? pinnedRootPub = keyStore.LoadPinnedRootPublicKey();
            if (pinnedRootPub is null || string.IsNullOrWhiteSpace(callerDevicePubB64))
            {
                return Results.Unauthorized();
            }

            PolicyVerificationResult verified = policyEngine.VerifyOffline(
                dto.SenderSignedPolicyJson,
                pinnedRootPub,
                callerMachineId,
                Convert.FromBase64String(callerDevicePubB64),
                commitClockGuard: false);

            if (!verified.IsValid)
            {
                return Results.Json(new { message = verified.Message }, statusCode: StatusCodes.Status403Forbidden);
            }

            byte[]? attachBytes = string.IsNullOrWhiteSpace(dto.AttachmentBase64)
                ? null
                : Convert.FromBase64String(dto.AttachmentBase64);

            var conv = new ChatConversationEntity
            {
                Id = dto.ConversationId,
                CompanyId = dto.CompanyId,
                ConversationType = dto.ConversationType,
                Name = dto.ConversationName,
                MemberMachineIdsJson = dto.MemberMachineIdsJson,
                MemberNamesJson = dto.MemberNamesJson,
                PinnedTaskId = dto.PinnedTaskId,
                PinnedTaskSummary = dto.PinnedTaskSummary,
                CreatedAtUtc = dto.SentAtUtc,
                UpdatedAtUtc = dto.SentAtUtc
            };

            var msg = new ChatMessageEntity
            {
                Id = dto.MessageId,
                ConversationId = dto.ConversationId,
                CompanyId = dto.CompanyId,
                SenderMachineId = dto.SenderMachineId,
                SenderUsername = verified.Snapshot!.Username,
                SenderFullName = verified.Snapshot.FullName,
                Content = dto.Content,
                AttachmentFileName = dto.AttachmentFileName,
                AttachmentSha256 = dto.AttachmentSha256,
                AttachmentSizeBytes = dto.AttachmentSizeBytes,
                LinkedTaskId = dto.LinkedTaskId,
                SentAtUtc = dto.SentAtUtc
            };

            await chat.UpsertIncomingChatSyncAsync(conv, msg, attachBytes, ct).ConfigureAwait(false);
            return Results.Ok(new { synced = true });
        });

        app.MapGet("/api/v1/chat/history", async (
            DateTimeOffset? sinceUtc,
            ChatService chat,
            LocalKeyStore keyStore,
            ILocalStoragePaths paths,
            CancellationToken ct) =>
        {
            string localPolicy = keyStore.LoadPolicyLicenseJson() ?? string.Empty;
            IReadOnlyList<(ChatConversationEntity Conv, ChatMessageEntity Msg)> pairs = await chat
                .GetMessagesSinceAsync(sinceUtc ?? DateTimeOffset.UtcNow.AddDays(-14), 200, ct)
                .ConfigureAwait(false);

            var items = new List<ChatSyncEnvelopeDto>(pairs.Count);
            foreach ((ChatConversationEntity c, ChatMessageEntity m) in pairs)
            {
                string? b64 = null;
                if (!string.IsNullOrWhiteSpace(m.AttachmentVaultPath))
                {
                    string full = Path.GetFullPath(Path.Combine(paths.VaultDirectory, m.AttachmentVaultPath.Replace('/', Path.DirectorySeparatorChar)));
                    if (File.Exists(full))
                    {
                        b64 = Convert.ToBase64String(await File.ReadAllBytesAsync(full, ct).ConfigureAwait(false));
                    }
                }

                items.Add(new ChatSyncEnvelopeDto(
                    c.Id, c.CompanyId, c.ConversationType, c.Name,
                    c.MemberMachineIdsJson, c.MemberNamesJson,
                    c.PinnedTaskId, c.PinnedTaskSummary,
                    m.Id, m.SenderMachineId, m.SenderUsername, m.SenderFullName,
                    m.Content, m.AttachmentFileName, m.AttachmentSha256, b64, m.AttachmentSizeBytes,
                    m.LinkedTaskId, m.SentAtUtc, localPolicy));
            }

            return Results.Ok(new ChatHistoryPullResponseDto(items));
        });

        app.MapGet("/api/v1/chat/attachment/{messageId:guid}", async (
            Guid messageId,
            IDbContextFactory<AppDbContext> appDbFactory,
            ILocalStoragePaths paths,
            HttpContext http,
            CancellationToken ct) =>
        {
            await using AppDbContext db = await appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            ChatMessageEntity? msg = await db.ChatMessages.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == messageId, ct)
                .ConfigureAwait(false);

            if (msg is null || string.IsNullOrWhiteSpace(msg.AttachmentVaultPath))
            {
                return Results.NotFound();
            }

            string full = Path.GetFullPath(Path.Combine(paths.VaultDirectory, msg.AttachmentVaultPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(full))
            {
                return Results.NotFound();
            }

            byte[] bytes = await File.ReadAllBytesAsync(full, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(msg.AttachmentSha256))
            {
                http.Response.Headers["X-Ez-Sha256"] = msg.AttachmentSha256;
            }

            return Results.File(bytes, "application/octet-stream", msg.AttachmentFileName ?? "attachment.bin");
        });

        // 14. Shared Timeline Plan Sync (Editable by both Director & Employees)
        app.MapPost("/api/v1/timeline/sync", async (
            HttpContext http,
            TimelineSyncRequestDto req,
            LocalKeyStore keyStore,
            PolicyEngine policyEngine,
            TimelinePlanService timeline,
            CancellationToken ct) =>
        {
            string callerMachineId = http.Items["EzDeviceMachineId"] as string ?? string.Empty;
            string callerDevicePubB64 = http.Items["EzDevicePubKey"] as string ?? string.Empty;
            byte[]? pinnedRootPub = keyStore.LoadPinnedRootPublicKey();
            if (pinnedRootPub is null || string.IsNullOrWhiteSpace(callerDevicePubB64))
            {
                return Results.Unauthorized();
            }

            PolicyVerificationResult verified = policyEngine.VerifyOffline(
                req.SenderSignedPolicyJson,
                pinnedRootPub,
                callerMachineId,
                Convert.FromBase64String(callerDevicePubB64),
                commitClockGuard: false);

            if (!verified.IsValid)
            {
                return Results.Json(new { message = verified.Message }, statusCode: StatusCodes.Status403Forbidden);
            }

            int changed = await timeline.UpsertIncomingPlansAsync(
                req.Plans.Select(p => new TimelinePlanEntity
                {
                    Id = p.Id,
                    CompanyId = p.CompanyId,
                    Title = p.Title,
                    Description = p.Description,
                    Category = p.Category,
                    StartDateUtc = p.StartDateUtc,
                    EndDateUtc = p.EndDateUtc,
                    Status = p.Status,
                    CreatedByMachineId = p.CreatedByMachineId,
                    CreatedByUsername = p.CreatedByUsername,
                    CreatedByFullName = p.CreatedByFullName,
                    CreatedAtUtc = p.CreatedAtUtc,
                    UpdatedAtUtc = p.UpdatedAtUtc,
                    IsDeleted = p.IsDeleted
                }),
                ct).ConfigureAwait(false);

            return Results.Ok(new { changed });
        });

        app.MapGet("/api/v1/timeline/entries", async (
            TimelinePlanService timeline,
            CancellationToken ct) =>
        {
            IReadOnlyList<TimelinePlanEntity> all = await timeline.GetAllForSyncAsync(ct).ConfigureAwait(false);
            return Results.Ok(all.Select(p => new TimelinePlanDto(
                p.Id, p.CompanyId, p.Title, p.Description, p.Category,
                p.StartDateUtc, p.EndDateUtc, p.Status,
                p.CreatedByMachineId, p.CreatedByUsername, p.CreatedByFullName,
                p.CreatedAtUtc, p.UpdatedAtUtc, p.IsDeleted)).ToArray());
        });

        // 15. HR Attendance Sync (Employee check-in/check-out pushed to Director/HR)
        app.MapPost("/api/v1/hr/attendance/sync", async (
            HttpContext http,
            AttendanceSyncDto dto,
            LocalKeyStore keyStore,
            PolicyEngine policyEngine,
            HrAttendanceService hr,
            CancellationToken ct) =>
        {
            string callerMachineId = http.Items["EzDeviceMachineId"] as string ?? string.Empty;
            string callerDevicePubB64 = http.Items["EzDevicePubKey"] as string ?? string.Empty;
            byte[]? pinnedRootPub = keyStore.LoadPinnedRootPublicKey();
            if (pinnedRootPub is null || string.IsNullOrWhiteSpace(callerDevicePubB64))
            {
                return Results.Unauthorized();
            }

            PolicyVerificationResult verified = policyEngine.VerifyOffline(
                dto.SenderSignedPolicyJson,
                pinnedRootPub,
                callerMachineId,
                Convert.FromBase64String(callerDevicePubB64),
                commitClockGuard: false);

            if (!verified.IsValid)
            {
                return Results.Json(new { message = verified.Message }, statusCode: StatusCodes.Status403Forbidden);
            }

            await hr.UpsertAttendanceRecordAsync(new AttendanceRecordEntity
            {
                Id = dto.Id,
                CompanyId = dto.CompanyId,
                EmployeeCode = dto.EmployeeCode,
                EmployeeName = dto.EmployeeName,
                MachineId = dto.MachineId,
                DateKey = dto.DateKey,
                CheckInUtc = dto.CheckInUtc,
                CheckOutUtc = dto.CheckOutUtc,
                WorkedHours = dto.WorkedHours,
                Status = dto.Status,
                Notes = dto.Notes,
                UpdatedAtUtc = dto.UpdatedAtUtc
            }, ct).ConfigureAwait(false);

            return Results.Ok(new { synced = true });
        });
    }

    public static async Task<int> MergeDirectorRegistryAsync(
        IDbContextFactory<AppDbContext> appDbFactory,
        IEnumerable<RegistryDeviceSyncDto> incoming,
        CancellationToken ct = default)
    {
        await using AppDbContext db = await appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        CompanyProfileEntity? company = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (company is null)
        {
            return 0;
        }

        int changes = 0;
        foreach (RegistryDeviceSyncDto item in incoming)
        {
            if (item.CompanyId != company.CompanyId)
            {
                continue;
            }

            PairedDeviceEntity? local = await db.PairedDevices
                .FirstOrDefaultAsync(x => x.MachineId == item.MachineId, ct)
                .ConfigureAwait(false);

            if (local is null)
            {
                db.PairedDevices.Add(new PairedDeviceEntity
                {
                    Id = Guid.NewGuid(),
                    CompanyId = item.CompanyId,
                    MachineId = item.MachineId,
                    DevicePublicKeyBase64 = item.DevicePublicKeyBase64,
                    UserId = item.UserId,
                    Username = item.Username,
                    FullName = item.FullName,
                    Role = item.Role,
                    CurrentPolicyVersion = item.CurrentPolicyVersion,
                    CurrentPolicyJson = item.CurrentPolicyJson,
                    PendingPolicyJson = item.PendingPolicyJson,
                    LastKnownIp = item.LastKnownIp,
                    LastKnownPort = item.LastKnownPort,
                    AppVersion = item.AppVersion,
                    SchemaVersion = item.SchemaVersion,
                    PairedAtUtc = item.PairedAtUtc,
                    PairedByAdminName = item.PairedByAdminName
                });
                changes++;
            }
            else if (item.CurrentPolicyVersion > local.CurrentPolicyVersion)
            {
                local.DevicePublicKeyBase64 = item.DevicePublicKeyBase64;
                local.UserId = item.UserId;
                local.Username = item.Username;
                local.FullName = item.FullName;
                local.Role = item.Role;
                local.CurrentPolicyVersion = item.CurrentPolicyVersion;
                local.CurrentPolicyJson = item.CurrentPolicyJson;
                local.PendingPolicyJson = item.PendingPolicyJson;
                local.PairedByAdminName = item.PairedByAdminName;
                changes++;
            }
            else if (item.CurrentPolicyVersion == local.CurrentPolicyVersion &&
                     string.IsNullOrEmpty(local.PendingPolicyJson) &&
                     !string.IsNullOrEmpty(item.PendingPolicyJson))
            {
                local.PendingPolicyJson = item.PendingPolicyJson;
                changes++;
            }
        }

        if (changes > 0)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return changes;
    }

    private static double ComputeFreeDiskPercent(string path)
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root))
            {
                return 100.0;
            }

            var drive = new DriveInfo(root);
            if (!drive.IsReady || drive.TotalSize <= 0)
            {
                return 100.0;
            }

            return Math.Round((double)drive.AvailableFreeSpace / drive.TotalSize * 100.0, 2);
        }
        catch
        {
            return 100.0;
        }
    }
}
