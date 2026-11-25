using System.Security.Cryptography;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Crypto;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent.Models;
using Microsoft.EntityFrameworkCore;

namespace EzCompany.Core.LanAgent.Recovery;

/// <summary>
/// Director-side manager for handling employee password/account reset requests arriving over LAN
/// and issuing Ed25519 Root-signed authorization tickets upon Director approval.
/// </summary>
public sealed class AccountRecoveryManager
{
    private readonly IDbContextFactory<AppDbContext> _appDbFactory;
    private readonly LocalKeyStore _keyStore;
    private readonly PasswordResetTicketService _ticketService;
    private readonly IEventBus _eventBus;
    private readonly IAuditLogger _audit;
    private readonly TimeProvider _timeProvider;

    public AccountRecoveryManager(
        IDbContextFactory<AppDbContext> appDbFactory,
        LocalKeyStore keyStore,
        PasswordResetTicketService ticketService,
        IEventBus eventBus,
        IAuditLogger audit,
        TimeProvider? timeProvider = null)
    {
        _appDbFactory = appDbFactory;
        _keyStore = keyStore;
        _ticketService = ticketService;
        _eventBus = eventBus;
        _audit = audit;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<(bool Accepted, string? Error, AccountResetInitiateResponseDto? Response)> AcceptRequestAsync(
        AccountResetInitiateRequestDto request,
        string authenticatedDevicePubKeyB64,
        string? remoteIp,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.MachineId) || string.IsNullOrWhiteSpace(request.Username))
        {
            return (false, "Thiếu MachineId hoặc tên tài khoản cần cấp lại.", null);
        }

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        CompanyProfileEntity? company = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (company is null || !company.IsDirectorNode || !_keyStore.HasRootPrivateKey)
        {
            return (false, "Máy này không phải nút Giám đốc có quyền cấp lại tài khoản.", null);
        }

        PairedDeviceEntity? device = await db.PairedDevices.AsNoTracking()
            .FirstOrDefaultAsync(d => d.MachineId == request.MachineId.Trim(), ct)
            .ConfigureAwait(false);

        if (device is null || !string.Equals(device.DevicePublicKeyBase64, authenticatedDevicePubKeyB64, StringComparison.Ordinal))
        {
            return (false, "Thiết bị chưa được ghép đôi hoặc khóa thiết bị không khớp.", null);
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateTimeOffset expiresAt = now.AddMinutes(15);
        Guid requestId = Guid.NewGuid();

        var entity = new PasswordResetRequestEntity
        {
            RequestId = requestId,
            CompanyId = company.CompanyId,
            MachineId = device.MachineId,
            DevicePublicKeyBase64 = device.DevicePublicKeyBase64,
            Username = string.IsNullOrWhiteSpace(request.Username) ? device.Username : request.Username.Trim().ToLowerInvariant(),
            FullName = string.IsNullOrWhiteSpace(request.FullName) ? device.FullName : request.FullName.Trim(),
            RequestedNewUsername = string.IsNullOrWhiteSpace(request.RequestedNewUsername)
                ? null
                : request.RequestedNewUsername.Trim().ToLowerInvariant(),
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? "Nhân viên yêu cầu cấp lại mật khẩu đăng nhập." : request.Reason.Trim(),
            State = "Pending",
            RequestedAtUtc = now,
            ExpiresAtUtc = expiresAt,
            RemoteIpAddress = remoteIp
        };

        db.PasswordResetRequests.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(
            "PASSWORD_RESET_REQUESTED",
            "core.auth",
            $"{entity.Username}@{entity.MachineId}",
            entity.Reason,
            "Warning",
            ct).ConfigureAwait(false);

        _eventBus.Publish(new PasswordResetRequestedEvent(
            entity.RequestId,
            entity.MachineId,
            entity.Username,
            entity.FullName,
            entity.Reason,
            now));

        return (true, null, new AccountResetInitiateResponseDto(
            entity.RequestId,
            entity.State,
            "Đã gửi yêu cầu cấp lại mật khẩu tới máy Giám đốc. Vui lòng chờ Giám đốc duyệt.",
            entity.ExpiresAtUtc));
    }

    public async Task<IReadOnlyList<PasswordResetRequestEntity>> GetPendingRequestsAsync(CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        List<PasswordResetRequestEntity> pending = await db.PasswordResetRequests
            .Where(x => x.State == "Pending")
            .ToListAsync(ct)
            .ConfigureAwait(false);

        bool dirty = false;
        foreach (PasswordResetRequestEntity item in pending)
        {
            if (now > item.ExpiresAtUtc)
            {
                item.State = "Expired";
                item.DecidedAtUtc = now;
                item.DecisionMessage = "Yêu cầu đã hết hạn.";
                dirty = true;
            }
        }

        if (dirty)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return pending
            .Where(x => x.State == "Pending")
            .OrderByDescending(x => x.RequestedAtUtc)
            .ToArray();
    }

    public async Task<AccountResetStatusResponseDto> GetStatusAsync(
        Guid requestId,
        string callerMachineId,
        CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        PasswordResetRequestEntity? entity = await db.PasswordResetRequests
            .FirstOrDefaultAsync(x => x.RequestId == requestId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return new AccountResetStatusResponseDto(requestId, "Expired", "Không tìm thấy yêu cầu cấp lại mật khẩu.", null);
        }

        if (!string.Equals(entity.MachineId, callerMachineId, StringComparison.OrdinalIgnoreCase))
        {
            return new AccountResetStatusResponseDto(requestId, "Rejected", "Thiết bị truy vấn không khớp với yêu cầu.", null);
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (entity.State == "Pending" && now > entity.ExpiresAtUtc)
        {
            entity.State = "Expired";
            entity.DecidedAtUtc = now;
            entity.DecisionMessage = "Yêu cầu đã hết hạn.";
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return new AccountResetStatusResponseDto(
            entity.RequestId,
            entity.State,
            entity.DecisionMessage,
            entity.State == "Approved" ? entity.SignedResetTicketJson : null);
    }

    public async Task<string> ApproveRequestAsync(
        Guid requestId,
        string adminName,
        CancellationToken ct = default)
    {
        byte[] rootPriv = _keyStore.LoadRootPrivateKey()
            ?? throw new InvalidOperationException("Không tìm thấy khóa gốc Root trên máy Giám đốc.");

        try
        {
            await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            PasswordResetRequestEntity entity = await db.PasswordResetRequests
                .FirstOrDefaultAsync(x => x.RequestId == requestId, ct)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("Không tìm thấy yêu cầu cấp lại mật khẩu.");

            if (entity.State != "Pending")
            {
                throw new InvalidOperationException($"Yêu cầu không còn ở trạng thái chờ duyệt ({entity.State}).");
            }

            DateTimeOffset now = _timeProvider.GetUtcNow();
            string approver = string.IsNullOrWhiteSpace(adminName) ? "Director" : adminName.Trim();

            var payload = new PasswordResetTicketPayload(
                RequestId: entity.RequestId,
                CompanyId: entity.CompanyId,
                MachineId: entity.MachineId,
                DevicePublicKeyBase64: entity.DevicePublicKeyBase64,
                ApprovedUsername: entity.RequestedNewUsername,
                IssuedAtUtc: now,
                ExpiresAtUtc: now.AddMinutes(15),
                ApprovedByAdminName: approver);

            string signedTicketJson = _ticketService.IssueSignedTicketJson(payload, rootPriv);

            entity.State = "Approved";
            entity.DecidedAtUtc = now;
            entity.DecidedByAdminName = approver;
            entity.DecisionMessage = $"Giám đốc {approver} đã duyệt cấp lại tài khoản.";
            entity.SignedResetTicketJson = signedTicketJson;

            if (!string.IsNullOrWhiteSpace(entity.RequestedNewUsername))
            {
                PairedDeviceEntity? device = await db.PairedDevices
                    .FirstOrDefaultAsync(d => d.MachineId == entity.MachineId, ct)
                    .ConfigureAwait(false);
                if (device is not null)
                {
                    device.Username = entity.RequestedNewUsername;
                }
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            await _audit.LogAsync(
                "PASSWORD_RESET_APPROVED",
                "core.auth",
                $"{entity.Username}@{entity.MachineId}",
                $"ApprovedBy={approver};NewUsername={entity.RequestedNewUsername ?? "-"}",
                "Info",
                ct).ConfigureAwait(false);

            return signedTicketJson;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rootPriv);
        }
    }

    public async Task RejectRequestAsync(
        Guid requestId,
        string adminName,
        string reason = "Giám đốc đã từ chối yêu cầu cấp lại mật khẩu.",
        CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        PasswordResetRequestEntity? entity = await db.PasswordResetRequests
            .FirstOrDefaultAsync(x => x.RequestId == requestId, ct)
            .ConfigureAwait(false);

        if (entity is null || entity.State != "Pending")
        {
            return;
        }

        entity.State = "Rejected";
        entity.DecidedAtUtc = _timeProvider.GetUtcNow();
        entity.DecidedByAdminName = string.IsNullOrWhiteSpace(adminName) ? "Director" : adminName.Trim();
        entity.DecisionMessage = reason;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(
            "PASSWORD_RESET_REJECTED",
            "core.auth",
            $"{entity.Username}@{entity.MachineId}",
            reason,
            "Warning",
            ct).ConfigureAwait(false);
    }
}
