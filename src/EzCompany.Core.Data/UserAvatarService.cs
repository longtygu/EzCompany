using EzCompany.Core.Abstractions;
using EzCompany.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EzCompany.Core.Data;

public sealed record UserAvatarSyncItem(
    string MachineId,
    string? AvatarBase64,
    DateTimeOffset? UpdatedAtUtc);

/// <summary>
/// Manages local employee/director personal avatars and synchronizes them across paired LAN peers.
/// </summary>
public sealed class UserAvatarService
{
    public const int MaxAvatarBytes = 512 * 1024;

    private readonly IDbContextFactory<AppDbContext> _appDbFactory;
    private readonly ILocalStoragePaths _paths;
    private readonly ISessionContext _session;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;

    public UserAvatarService(
        IDbContextFactory<AppDbContext> appDbFactory,
        ILocalStoragePaths paths,
        ISessionContext session,
        IEventBus eventBus,
        TimeProvider? timeProvider = null)
    {
        _appDbFactory = appDbFactory;
        _paths = paths;
        _session = session;
        _eventBus = eventBus;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task SaveMyAvatarAsync(byte[] imageBytes, CancellationToken ct = default)
    {
        if (!_session.IsAuthenticated)
        {
            throw new InvalidOperationException("Phiên làm việc chưa đăng nhập.");
        }

        if (imageBytes is null || imageBytes.Length == 0)
        {
            throw new ArgumentException("Dữ liệu ảnh đại diện rỗng.", nameof(imageBytes));
        }

        if (imageBytes.Length > MaxAvatarBytes)
        {
            throw new InvalidOperationException("Ảnh đại diện vượt quá giới hạn 512 KB.");
        }

        string base64 = Convert.ToBase64String(imageBytes);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        LocalUserEntity? localUser = await db.LocalUsers
            .FirstOrDefaultAsync(u => u.Username == _session.Username, ct)
            .ConfigureAwait(false);
        if (localUser is not null)
        {
            localUser.AvatarBase64 = base64;
            localUser.AvatarUpdatedAtUtc = now;
        }

        PairedDeviceEntity? selfDev = await db.PairedDevices
            .FirstOrDefaultAsync(d => d.MachineId == _paths.MachineId, ct)
            .ConfigureAwait(false);
        if (selfDev is not null)
        {
            selfDev.AvatarBase64 = base64;
            selfDev.AvatarUpdatedAtUtc = now;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _eventBus.Publish(new UserAvatarUpdatedEvent(_paths.MachineId, now));
    }

    public async Task ClearMyAvatarAsync(CancellationToken ct = default)
    {
        if (!_session.IsAuthenticated)
        {
            return;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        LocalUserEntity? localUser = await db.LocalUsers
            .FirstOrDefaultAsync(u => u.Username == _session.Username, ct)
            .ConfigureAwait(false);
        if (localUser is not null)
        {
            localUser.AvatarBase64 = null;
            localUser.AvatarUpdatedAtUtc = now;
        }

        PairedDeviceEntity? selfDev = await db.PairedDevices
            .FirstOrDefaultAsync(d => d.MachineId == _paths.MachineId, ct)
            .ConfigureAwait(false);
        if (selfDev is not null)
        {
            selfDev.AvatarBase64 = null;
            selfDev.AvatarUpdatedAtUtc = now;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _eventBus.Publish(new UserAvatarUpdatedEvent(_paths.MachineId, now));
    }

    public async Task<byte[]?> GetMyAvatarBytesAsync(CancellationToken ct = default)
    {
        if (!_session.IsAuthenticated)
        {
            return null;
        }

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        LocalUserEntity? localUser = await db.LocalUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == _session.Username, ct)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(localUser?.AvatarBase64))
        {
            try
            {
                return Convert.FromBase64String(localUser.AvatarBase64);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    public async Task<IReadOnlyList<UserAvatarSyncItem>> GetAllAvatarsForSyncAsync(CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var map = new Dictionary<string, UserAvatarSyncItem>(StringComparer.OrdinalIgnoreCase);

        List<PairedDeviceEntity> devices = await db.PairedDevices.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        foreach (PairedDeviceEntity dev in devices)
        {
            if (dev.AvatarUpdatedAtUtc.HasValue)
            {
                map[dev.MachineId] = new UserAvatarSyncItem(dev.MachineId, dev.AvatarBase64, dev.AvatarUpdatedAtUtc);
            }
        }

        if (_session.IsAuthenticated)
        {
            LocalUserEntity? me = await db.LocalUsers.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Username == _session.Username, ct)
                .ConfigureAwait(false);
            if (me?.AvatarUpdatedAtUtc is DateTimeOffset myUpdated)
            {
                if (!map.TryGetValue(_paths.MachineId, out UserAvatarSyncItem? existing) ||
                    existing.UpdatedAtUtc is null ||
                    myUpdated >= existing.UpdatedAtUtc.Value)
                {
                    map[_paths.MachineId] = new UserAvatarSyncItem(_paths.MachineId, me.AvatarBase64, myUpdated);
                }
            }
        }

        return map.Values.ToArray();
    }

    public async Task<bool> UpsertPeerAvatarAsync(
        string machineId,
        string? avatarBase64,
        DateTimeOffset updatedAtUtc,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(machineId))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(avatarBase64))
        {
            try
            {
                byte[] decoded = Convert.FromBase64String(avatarBase64);
                if (decoded.Length > MaxAvatarBytes)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        PairedDeviceEntity? dev = await db.PairedDevices
            .FirstOrDefaultAsync(d => d.MachineId == machineId, ct)
            .ConfigureAwait(false);

        if (dev is null)
        {
            return false;
        }

        if (dev.AvatarUpdatedAtUtc.HasValue && dev.AvatarUpdatedAtUtc.Value >= updatedAtUtc)
        {
            return false;
        }

        dev.AvatarBase64 = string.IsNullOrWhiteSpace(avatarBase64) ? null : avatarBase64;
        dev.AvatarUpdatedAtUtc = updatedAtUtc;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _eventBus.Publish(new UserAvatarUpdatedEvent(machineId, updatedAtUtc));
        return true;
    }

    public async Task<Dictionary<string, byte[]>> GetAvatarMapAsync(CancellationToken ct = default)
    {
        IReadOnlyList<UserAvatarSyncItem> items = await GetAllAvatarsForSyncAsync(ct).ConfigureAwait(false);
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (UserAvatarSyncItem item in items)
        {
            if (string.IsNullOrWhiteSpace(item.AvatarBase64))
            {
                continue;
            }

            try
            {
                result[item.MachineId] = Convert.FromBase64String(item.AvatarBase64);
            }
            catch
            {
            }
        }

        return result;
    }
}
