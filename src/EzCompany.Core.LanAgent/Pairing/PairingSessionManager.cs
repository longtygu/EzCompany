using System.Collections.Concurrent;
using System.Security.Cryptography;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Crypto;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent.Discovery;
using Microsoft.EntityFrameworkCore;

namespace EzCompany.Core.LanAgent.Pairing;

/// <summary>
/// Manages the Director's time-bounded pairing window (default 5 minutes),
/// pending SAS 6-digit approval queue, and signed <c>policy.lic</c> issuance.
/// </summary>
public sealed class PairingSessionManager : IDisposable
{
    private readonly IDbContextFactory<AppDbContext> _appDbFactory;
    private readonly LocalKeyStore _keyStore;
    private readonly EciesCryptoService _ecies;
    private readonly PolicyEngine _policyEngine;
    private readonly ILanDiscoveryService _discovery;
    private readonly IEventBus _eventBus;
    private readonly IAuditLogger _audit;
    private readonly TimeProvider _timeProvider;

    private readonly ConcurrentDictionary<Guid, PendingPairingRequest> _requests = new();
    private readonly object _gate = new();
    private DateTimeOffset? _windowExpiresAtUtc;
    private Timer? _autoCloseTimer;

    public PairingSessionManager(
        IDbContextFactory<AppDbContext> appDbFactory,
        LocalKeyStore keyStore,
        EciesCryptoService ecies,
        PolicyEngine policyEngine,
        ILanDiscoveryService discovery,
        IEventBus eventBus,
        IAuditLogger audit,
        TimeProvider? timeProvider = null)
    {
        _appDbFactory = appDbFactory;
        _keyStore = keyStore;
        _ecies = ecies;
        _policyEngine = policyEngine;
        _discovery = discovery;
        _eventBus = eventBus;
        _audit = audit;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public event EventHandler? PairingStateChanged;

    public bool IsPairingOpen
    {
        get
        {
            lock (_gate)
            {
                if (_windowExpiresAtUtc is null)
                {
                    return false;
                }

                if (_timeProvider.GetUtcNow() >= _windowExpiresAtUtc.Value)
                {
                    _windowExpiresAtUtc = null;
                    return false;
                }

                return true;
            }
        }
    }

    public TimeSpan RemainingWindow
    {
        get
        {
            lock (_gate)
            {
                if (_windowExpiresAtUtc is null)
                {
                    return TimeSpan.Zero;
                }

                TimeSpan remaining = _windowExpiresAtUtc.Value - _timeProvider.GetUtcNow();
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
        }
    }

    public void OpenPairingWindow(TimeSpan? duration = null)
    {
        TimeSpan effective = duration ?? TimeSpan.FromMinutes(5);
        lock (_gate)
        {
            _windowExpiresAtUtc = _timeProvider.GetUtcNow().Add(effective);
            _autoCloseTimer?.Dispose();
            _autoCloseTimer = new Timer(_ => ClosePairingWindow(), null, effective, Timeout.InfiniteTimeSpan);
        }

        _discovery.RefreshAdvertisement(pairingOpenOverride: true);
        PairingStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClosePairingWindow()
    {
        lock (_gate)
        {
            _windowExpiresAtUtc = null;
            _autoCloseTimer?.Dispose();
            _autoCloseTimer = null;
        }

        _discovery.RefreshAdvertisement(pairingOpenOverride: false);
        PairingStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<PendingPairingRequest> GetPendingRequests()
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        foreach (PendingPairingRequest req in _requests.Values)
        {
            if (req.State == PairingRequestState.Pending && now > req.ExpiresAtUtc)
            {
                req.State = PairingRequestState.Expired;
            }
        }

        return _requests.Values
            .Where(r => r.State == PairingRequestState.Pending)
            .OrderByDescending(r => r.RequestedAtUtc)
            .ToArray();
    }

    public async Task<(bool Accepted, string? Error, PairingInitiateResponse? Response)> AcceptIncomingRequestAsync(
        PairingInitiateRequest request,
        string? remoteIp,
        CancellationToken ct = default)
    {
        if (!IsPairingOpen)
        {
            return (false, "Cổng ghép đôi trên máy Giám đốc hiện đang đóng.", null);
        }

        if (string.IsNullOrWhiteSpace(request.MachineId) ||
            string.IsNullOrWhiteSpace(request.DevicePublicKeyBase64) ||
            string.IsNullOrWhiteSpace(request.Username) ||
            string.IsNullOrWhiteSpace(request.FullName) ||
            string.IsNullOrWhiteSpace(request.ClientNonceBase64))
        {
            return (false, "Thiếu thông tin thiết bị ghép đôi.", null);
        }

        byte[] devicePub;
        byte[] clientNonce;
        try
        {
            devicePub = Convert.FromBase64String(request.DevicePublicKeyBase64);
            clientNonce = Convert.FromBase64String(request.ClientNonceBase64);
        }
        catch (FormatException)
        {
            return (false, "Định dạng khóa hoặc nonce không hợp lệ.", null);
        }

        if (devicePub.Length != Ed25519Service.KeySizeBytes || clientNonce.Length < 16)
        {
            return (false, "Độ dài khóa hoặc nonce không hợp lệ.", null);
        }

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        CompanyProfileEntity? company = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (company is null || !company.IsDirectorNode || !_keyStore.HasRootPrivateKey)
        {
            return (false, "Máy này không phải máy Giám đốc hợp lệ.", null);
        }

        byte[] rootPub = Convert.FromBase64String(company.RootPublicKeyBase64);
        byte[] serverNonce = RandomNumberGenerator.GetBytes(32);
        string sasCode = SasCalculator.ComputeSixDigitCode(rootPub, devicePub, clientNonce, serverNonce);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateTimeOffset expiresAt = now.AddMinutes(5);
        Guid requestId = Guid.NewGuid();

        var entry = new PendingPairingRequest
        {
            RequestId = requestId,
            MachineId = request.MachineId.Trim(),
            DevicePublicKeyBase64 = request.DevicePublicKeyBase64,
            FullName = request.FullName.Trim(),
            Username = request.Username.Trim().ToLowerInvariant(),
            ClientNonceBase64 = request.ClientNonceBase64,
            ServerNonceBase64 = Convert.ToBase64String(serverNonce),
            SasCode = sasCode,
            AppVersion = string.IsNullOrWhiteSpace(request.AppVersion) ? "0.1.0" : request.AppVersion,
            RequestedAtUtc = now,
            ExpiresAtUtc = expiresAt,
            RemoteIpAddress = remoteIp,
            RemoteListenPort = request.ListenPort
        };

        _requests[requestId] = entry;

        _eventBus.Publish(new PairingRequestReceivedEvent(
            requestId,
            entry.MachineId,
            entry.FullName,
            entry.Username,
            sasCode,
            now));

        PairingStateChanged?.Invoke(this, EventArgs.Empty);

        var response = new PairingInitiateResponse(
            requestId,
            company.CompanyId,
            company.CompanyName,
            company.TaxCode,
            company.RootPublicKeyBase64,
            company.CompanyEncryptionPublicKeyBase64,
            entry.ServerNonceBase64,
            expiresAt);

        return (true, null, response);
    }

    public PairingStatusResponse GetStatus(Guid requestId, string machineId, string clientNonceBase64)
    {
        if (!_requests.TryGetValue(requestId, out PendingPairingRequest? req))
        {
            return new PairingStatusResponse(requestId, PairingRequestState.Expired, "Không tìm thấy yêu cầu ghép đôi.", null, null);
        }

        if (!string.Equals(req.MachineId, machineId, StringComparison.Ordinal) ||
            !string.Equals(req.ClientNonceBase64, clientNonceBase64, StringComparison.Ordinal))
        {
            return new PairingStatusResponse(requestId, PairingRequestState.Rejected, "Thông tin xác thực phiên ghép đôi không khớp.", null, null);
        }

        if (req.State == PairingRequestState.Pending && _timeProvider.GetUtcNow() > req.ExpiresAtUtc)
        {
            req.State = PairingRequestState.Expired;
            req.DecisionMessage = "Yêu cầu ghép đôi đã hết hạn.";
        }

        return new PairingStatusResponse(
            requestId,
            req.State,
            req.DecisionMessage,
            req.State == PairingRequestState.Approved ? req.SignedPolicyJson : null,
            req.State == PairingRequestState.Approved ? req.AssignedUserId : null);
    }

    public async Task<string> ApproveRequestAsync(
        Guid requestId,
        string role,
        IReadOnlyList<string> enabledModules,
        string maxApprovedVersion,
        string allowedUpdateChannel,
        string adminName,
        CancellationToken ct = default)
    {
        if (!_requests.TryGetValue(requestId, out PendingPairingRequest? req) || req.State != PairingRequestState.Pending)
        {
            throw new InvalidOperationException("Yêu cầu ghép đôi không còn ở trạng thái chờ duyệt.");
        }

        byte[] rootPriv = _keyStore.LoadRootPrivateKey()
            ?? throw new InvalidOperationException("Không tìm thấy khóa gốc Root trên máy Giám đốc.");

        try
        {
            await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            CompanyProfileEntity company = await db.CompanyProfiles.FirstAsync(ct).ConfigureAwait(false);

            PairedDeviceEntity? existing = await db.PairedDevices
                .FirstOrDefaultAsync(x => x.MachineId == req.MachineId, ct)
                .ConfigureAwait(false);

            Guid userId = existing?.UserId ?? Guid.NewGuid();
            long nextVersion = (existing?.CurrentPolicyVersion ?? 0) + 1;
            DateTimeOffset now = _timeProvider.GetUtcNow();
            byte[] encPub = _ecies.DeriveCompanyX25519PublicKey(rootPriv);

            var payload = new PolicyPayload(
                CompanyId: company.CompanyId,
                MachineId: req.MachineId,
                DevicePublicKey: req.DevicePublicKeyBase64,
                CompanyEncryptionPublicKey: Convert.ToBase64String(encPub),
                UserId: userId,
                Username: req.Username,
                FullName: req.FullName,
                Role: role,
                EnabledModules: enabledModules.ToArray(),
                EnabledPermissions: CompanyRoles.GetDefaultPermissionsForRole(role),
                PolicyVersion: nextVersion,
                IssuedAtUtc: now,
                ExpiresAtUtc: now.AddDays(PolicyEngine.DefaultValidityDays),
                AllowedUpdateChannel: string.IsNullOrWhiteSpace(allowedUpdateChannel) ? "stable" : allowedUpdateChannel,
                MaxApprovedVersion: string.IsNullOrWhiteSpace(maxApprovedVersion) ? "0.1.0" : maxApprovedVersion,
                IssuedByAdminName: adminName);

            string policyJson = _policyEngine.SerializeDocument(_policyEngine.IssueSignedPolicy(payload, rootPriv));

            if (existing is null)
            {
                db.PairedDevices.Add(new PairedDeviceEntity
                {
                    Id = Guid.NewGuid(),
                    CompanyId = company.CompanyId,
                    MachineId = req.MachineId,
                    DevicePublicKeyBase64 = req.DevicePublicKeyBase64,
                    UserId = userId,
                    Username = req.Username,
                    FullName = req.FullName,
                    Role = role,
                    CurrentPolicyVersion = nextVersion,
                    CurrentPolicyJson = policyJson,
                    LastKnownIp = req.RemoteIpAddress,
                    LastKnownPort = req.RemoteListenPort,
                    IsOnline = true,
                    AppVersion = req.AppVersion,
                    LastSeenUtc = now,
                    PairedAtUtc = now,
                    PairedByAdminName = adminName
                });
            }
            else
            {
                existing.DevicePublicKeyBase64 = req.DevicePublicKeyBase64;
                existing.UserId = userId;
                existing.Username = req.Username;
                existing.FullName = req.FullName;
                existing.Role = role;
                existing.CurrentPolicyVersion = nextVersion;
                existing.CurrentPolicyJson = policyJson;
                existing.PendingPolicyJson = null;
                existing.LastKnownIp = req.RemoteIpAddress;
                existing.LastKnownPort = req.RemoteListenPort;
                existing.IsOnline = true;
                existing.LastSeenUtc = now;
                existing.PairedByAdminName = adminName;
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            req.AssignedUserId = userId;
            req.SignedPolicyJson = policyJson;
            req.DecisionMessage = "Đã duyệt ghép đôi.";
            req.State = PairingRequestState.Approved;

            await _audit.LogAsync(
                "PAIRING_APPROVED",
                "core.lan",
                $"{req.Username}@{req.MachineId}",
                $"Role={role};SAS={req.SasCode}",
                "Info",
                ct).ConfigureAwait(false);

            PairingStateChanged?.Invoke(this, EventArgs.Empty);
            return policyJson;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rootPriv);
        }
    }

    public void RejectRequest(Guid requestId, string reason = "Giám đốc đã từ chối yêu cầu ghép đôi.")
    {
        if (_requests.TryGetValue(requestId, out PendingPairingRequest? req) && req.State == PairingRequestState.Pending)
        {
            req.DecisionMessage = reason;
            req.State = PairingRequestState.Rejected;
            PairingStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        _autoCloseTimer?.Dispose();
    }
}
