using System.Security.Cryptography;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Crypto;
using EzCompany.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EzCompany.Core.Data;

public sealed record AuthenticationResult(
    bool Succeeded,
    string Message,
    bool IsPolicyLocked,
    PolicySnapshot? PolicySnapshot,
    CompanyProfileEntity? CompanyProfile);

/// <summary>
/// Handles Genesis Director initialization, Secondary Director restoration via <c>.recovery</c>,
/// employee onboarding persistence, and 100% offline Argon2id + <c>policy.lic</c> login.
/// </summary>
public sealed class GenesisBootstrapService
{
    public const int CurrentAppSchemaVersion = 4;

    private readonly IDbContextFactory<AppDbContext> _appDbFactory;
    private readonly ILocalStoragePaths _paths;
    private readonly ISessionContext _session;
    private readonly IPermissionService _permissions;
    private readonly Ed25519Service _ed25519;
    private readonly EciesCryptoService _ecies;
    private readonly Argon2PasswordHasher _argon2;
    private readonly LocalKeyStore _keyStore;
    private readonly RecoveryKitService _recoveryKit;
    private readonly PolicyEngine _policyEngine;
    private readonly IAuditLogger _audit;
    private readonly TimeProvider _timeProvider;
    private readonly PasswordResetTicketService _ticketService;
    private readonly IEventBus? _eventBus;

    public GenesisBootstrapService(
        IDbContextFactory<AppDbContext> appDbFactory,
        ILocalStoragePaths paths,
        ISessionContext session,
        IPermissionService permissions,
        Ed25519Service ed25519,
        EciesCryptoService ecies,
        Argon2PasswordHasher argon2,
        LocalKeyStore keyStore,
        RecoveryKitService recoveryKit,
        PolicyEngine policyEngine,
        IAuditLogger audit,
        TimeProvider? timeProvider = null,
        PasswordResetTicketService? ticketService = null,
        IEventBus? eventBus = null)
    {
        _appDbFactory = appDbFactory;
        _paths = paths;
        _session = session;
        _permissions = permissions;
        _ed25519 = ed25519;
        _ecies = ecies;
        _argon2 = argon2;
        _keyStore = keyStore;
        _recoveryKit = recoveryKit;
        _policyEngine = policyEngine;
        _audit = audit;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _ticketService = ticketService ?? new PasswordResetTicketService(ed25519, _timeProvider);
        _eventBus = eventBus;
    }

    public async Task<CompanyProfileEntity?> GetCompanyProfileAsync(CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    public async Task<LocalUserEntity?> GetPrimaryLocalUserAsync(CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.LocalUsers.AsNoTracking().FirstOrDefaultAsync(x => x.IsActive, ct).ConfigureAwait(false);
    }

    public async Task<bool> IsInitializedAsync(CancellationToken ct = default)
    {
        CompanyProfileEntity? profile = await GetCompanyProfileAsync(ct).ConfigureAwait(false);
        return profile is { IsGenesisCompleted: true } && _keyStore.HasPolicyLicense;
    }

    public async Task<CompanyProfileEntity> InitializeGenesisDirectorAsync(
        string companyName,
        string taxCode,
        string fullName,
        string username,
        string loginPassword,
        string recoveryPassword,
        string recoveryOutputPath,
        CancellationToken ct = default)
    {
        ValidateCommonInputs(companyName, taxCode, fullName, username, loginPassword);
        if (string.IsNullOrWhiteSpace(recoveryOutputPath))
        {
            throw new ArgumentException("Bắt buộc chọn đường dẫn lưu tệp Master Recovery Kit (.recovery).", nameof(recoveryOutputPath));
        }

        if (await IsInitializedAsync(ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Hồ sơ này đã được khởi tạo trước đó.");
        }

        Ed25519KeyPair rootPair = _ed25519.GenerateKeyPair();
        Ed25519KeyPair devicePair = _ed25519.GenerateKeyPair();
        byte[] companyEncPub = _ecies.DeriveCompanyX25519PublicKey(rootPair.PrivateKey);

        Guid companyId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        DateTimeOffset now = _timeProvider.GetUtcNow();
        string normalizedUser = username.Trim().ToLowerInvariant();

        try
        {
            // Mandatory .recovery export FIRST + round-trip check before completing Genesis
            var recoveryPayload = new RecoveryKitPayload(
                companyId,
                companyName.Trim(),
                taxCode.Trim(),
                Convert.ToBase64String(rootPair.PrivateKey),
                Convert.ToBase64String(rootPair.PublicKey),
                now,
                fullName.Trim());

            await _recoveryKit.ExportToFileAsync(recoveryOutputPath, recoveryPayload, recoveryPassword, ct)
                .ConfigureAwait(false);

            // Verify exported recovery kit can be read back immediately
            _ = await _recoveryKit.ImportFromFileAsync(recoveryOutputPath, recoveryPassword, ct)
                .ConfigureAwait(false);

            _keyStore.SaveRootKeyPair(rootPair.PrivateKey, rootPair.PublicKey);
            _keyStore.SaveDeviceKeyPair(devicePair.PrivateKey, devicePair.PublicKey);
            _keyStore.SavePinnedCompanyEncryptionPublicKey(companyEncPub);

            var payload = new PolicyPayload(
                CompanyId: companyId,
                MachineId: _paths.MachineId,
                DevicePublicKey: devicePair.PublicKeyBase64,
                CompanyEncryptionPublicKey: Convert.ToBase64String(companyEncPub),
                UserId: userId,
                Username: normalizedUser,
                FullName: fullName.Trim(),
                Role: CompanyRoles.Director,
                EnabledModules: CompanyRoles.GetDefaultModulesForRole(CompanyRoles.Director),
                EnabledPermissions: CompanyRoles.GetDefaultPermissionsForRole(CompanyRoles.Director),
                PolicyVersion: 1,
                IssuedAtUtc: now,
                ExpiresAtUtc: now.AddDays(PolicyEngine.DefaultValidityDays),
                AllowedUpdateChannel: "stable",
                MaxApprovedVersion: _session.CurrentAppVersion,
                IssuedByAdminName: fullName.Trim());

            SignedPolicyDocument signedPolicy = _policyEngine.IssueSignedPolicy(payload, rootPair.PrivateKey);
            string policyJson = _policyEngine.SerializeDocument(signedPolicy);
            _keyStore.SavePolicyLicenseJson(policyJson);

            PolicyVerificationResult verified = _policyEngine.VerifyOffline(
                policyJson,
                rootPair.PublicKey,
                _paths.MachineId,
                devicePair.PublicKey);

            if (!verified.IsValid || verified.Snapshot is null)
            {
                throw new InvalidOperationException($"Lỗi xác thực policy gốc: {verified.Message}");
            }

            var profile = new CompanyProfileEntity
            {
                CompanyId = companyId,
                CompanyName = companyName.Trim(),
                TaxCode = taxCode.Trim(),
                RootPublicKeyBase64 = rootPair.PublicKeyBase64,
                CompanyEncryptionPublicKeyBase64 = Convert.ToBase64String(companyEncPub),
                IsDirectorNode = true,
                IsGenesisCompleted = true,
                AppSchemaVersion = CurrentAppSchemaVersion,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            var user = new LocalUserEntity
            {
                UserId = userId,
                CompanyId = companyId,
                Username = normalizedUser,
                FullName = fullName.Trim(),
                PasswordHash = _argon2.HashPassword(loginPassword),
                Role = CompanyRoles.Director,
                IsActive = true,
                CreatedAtUtc = now,
                LastLoginUtc = now
            };

            var selfDevice = new PairedDeviceEntity
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                MachineId = _paths.MachineId,
                DevicePublicKeyBase64 = devicePair.PublicKeyBase64,
                UserId = userId,
                Username = normalizedUser,
                FullName = fullName.Trim(),
                Role = CompanyRoles.Director,
                CurrentPolicyVersion = 1,
                CurrentPolicyJson = policyJson,
                IsOnline = true,
                AppVersion = _session.CurrentAppVersion,
                SchemaVersion = CurrentAppSchemaVersion,
                LastSeenUtc = now,
                PairedAtUtc = now,
                PairedByAdminName = fullName.Trim()
            };

            await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            db.CompanyProfiles.Add(profile);
            db.LocalUsers.Add(user);
            db.PairedDevices.Add(selfDevice);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            _session.SetCompanyContext(profile.CompanyId, profile.CompanyName, profile.TaxCode, true);
            _session.SetAuthenticatedUser(user.UserId, user.Username, user.FullName, user.Role);
            _permissions.ApplyPolicySnapshot(verified.Snapshot);

            await _audit.LogAsync("GENESIS_BOOTSTRAP", "core.bootstrap", profile.CompanyName, null, "Info", ct)
                .ConfigureAwait(false);

            return profile;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rootPair.PrivateKey);
            CryptographicOperations.ZeroMemory(devicePair.PrivateKey);
        }
    }

    public async Task<CompanyProfileEntity> RestoreSecondaryDirectorAsync(
        string recoveryFilePath,
        string recoveryPassword,
        string fullName,
        string username,
        string loginPassword,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(recoveryFilePath) || !File.Exists(recoveryFilePath))
        {
            throw new FileNotFoundException("Không tìm thấy tệp khôi phục (.recovery).", recoveryFilePath);
        }

        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(loginPassword))
        {
            throw new ArgumentException("Vui lòng nhập đầy đủ thông tin tài khoản Giám đốc.");
        }

        if (await IsInitializedAsync(ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Hồ sơ này đã được khởi tạo trước đó.");
        }

        RecoveryKitPayload recovered = await _recoveryKit.ImportFromFileAsync(recoveryFilePath, recoveryPassword, ct)
            .ConfigureAwait(false);

        byte[] rootPriv = Convert.FromBase64String(recovered.RootPrivateKeyBase64);
        byte[] rootPub = Convert.FromBase64String(recovered.RootPublicKeyBase64);
        Ed25519KeyPair devicePair = _ed25519.GenerateKeyPair();
        byte[] companyEncPub = _ecies.DeriveCompanyX25519PublicKey(rootPriv);

        try
        {
            Guid userId = Guid.NewGuid();
            DateTimeOffset now = _timeProvider.GetUtcNow();
            string normalizedUser = username.Trim().ToLowerInvariant();

            _keyStore.SaveRootKeyPair(rootPriv, rootPub);
            _keyStore.SaveDeviceKeyPair(devicePair.PrivateKey, devicePair.PublicKey);
            _keyStore.SavePinnedCompanyEncryptionPublicKey(companyEncPub);

            var payload = new PolicyPayload(
                CompanyId: recovered.CompanyId,
                MachineId: _paths.MachineId,
                DevicePublicKey: devicePair.PublicKeyBase64,
                CompanyEncryptionPublicKey: Convert.ToBase64String(companyEncPub),
                UserId: userId,
                Username: normalizedUser,
                FullName: fullName.Trim(),
                Role: CompanyRoles.Director,
                EnabledModules: CompanyRoles.GetDefaultModulesForRole(CompanyRoles.Director),
                EnabledPermissions: CompanyRoles.GetDefaultPermissionsForRole(CompanyRoles.Director),
                PolicyVersion: 1,
                IssuedAtUtc: now,
                ExpiresAtUtc: now.AddDays(PolicyEngine.DefaultValidityDays),
                AllowedUpdateChannel: "stable",
                MaxApprovedVersion: _session.CurrentAppVersion,
                IssuedByAdminName: fullName.Trim());

            SignedPolicyDocument signedPolicy = _policyEngine.IssueSignedPolicy(payload, rootPriv);
            string policyJson = _policyEngine.SerializeDocument(signedPolicy);
            _keyStore.SavePolicyLicenseJson(policyJson);

            PolicyVerificationResult verified = _policyEngine.VerifyOffline(
                policyJson,
                rootPub,
                _paths.MachineId,
                devicePair.PublicKey);

            if (!verified.IsValid || verified.Snapshot is null)
            {
                throw new InvalidOperationException($"Lỗi xác thực policy Giám đốc 2: {verified.Message}");
            }

            var profile = new CompanyProfileEntity
            {
                CompanyId = recovered.CompanyId,
                CompanyName = recovered.CompanyName,
                TaxCode = recovered.TaxCode,
                RootPublicKeyBase64 = recovered.RootPublicKeyBase64,
                CompanyEncryptionPublicKeyBase64 = Convert.ToBase64String(companyEncPub),
                IsDirectorNode = true,
                IsGenesisCompleted = true,
                AppSchemaVersion = CurrentAppSchemaVersion,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            var user = new LocalUserEntity
            {
                UserId = userId,
                CompanyId = recovered.CompanyId,
                Username = normalizedUser,
                FullName = fullName.Trim(),
                PasswordHash = _argon2.HashPassword(loginPassword),
                Role = CompanyRoles.Director,
                IsActive = true,
                CreatedAtUtc = now,
                LastLoginUtc = now
            };

            var selfDevice = new PairedDeviceEntity
            {
                Id = Guid.NewGuid(),
                CompanyId = recovered.CompanyId,
                MachineId = _paths.MachineId,
                DevicePublicKeyBase64 = devicePair.PublicKeyBase64,
                UserId = userId,
                Username = normalizedUser,
                FullName = fullName.Trim(),
                Role = CompanyRoles.Director,
                CurrentPolicyVersion = 1,
                CurrentPolicyJson = policyJson,
                IsOnline = true,
                AppVersion = _session.CurrentAppVersion,
                SchemaVersion = CurrentAppSchemaVersion,
                LastSeenUtc = now,
                PairedAtUtc = now,
                PairedByAdminName = recovered.ExportedByAdminName
            };

            await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            db.CompanyProfiles.Add(profile);
            db.LocalUsers.Add(user);
            db.PairedDevices.Add(selfDevice);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            _session.SetCompanyContext(profile.CompanyId, profile.CompanyName, profile.TaxCode, true);
            _session.SetAuthenticatedUser(user.UserId, user.Username, user.FullName, user.Role);
            _permissions.ApplyPolicySnapshot(verified.Snapshot);

            await _audit.LogAsync("SECONDARY_DIRECTOR_RESTORE", "core.bootstrap", profile.CompanyName, null, "Info", ct)
                .ConfigureAwait(false);

            return profile;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rootPriv);
            CryptographicOperations.ZeroMemory(devicePair.PrivateKey);
        }
    }

    public async Task<CompanyProfileEntity> CompleteEmployeeOnboardingAsync(
        Guid companyId,
        string companyName,
        string taxCode,
        byte[] rootPublicKey,
        byte[] companyEncryptionPublicKey,
        Guid userId,
        string username,
        string fullName,
        string loginPassword,
        string signedPolicyJson,
        CancellationToken ct = default)
    {
        byte[]? devicePub = _keyStore.LoadDevicePublicKey();
        if (devicePub is null)
        {
            throw new InvalidOperationException("Thiết bị chưa tạo cặp khóa Ed25519 cục bộ.");
        }

        PolicyVerificationResult verified = _policyEngine.VerifyOffline(
            signedPolicyJson,
            rootPublicKey,
            _paths.MachineId,
            devicePub);

        if (!verified.IsValid || verified.Snapshot is null)
        {
            throw new InvalidOperationException($"Giấy phép nhận từ Giám đốc không hợp lệ: {verified.Message}");
        }

        _keyStore.SavePinnedRootPublicKey(rootPublicKey);
        _keyStore.SavePinnedCompanyEncryptionPublicKey(companyEncryptionPublicKey);
        _keyStore.SavePolicyLicenseJson(signedPolicyJson);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        string normalizedUser = username.Trim().ToLowerInvariant();
        string rootPubB64 = Convert.ToBase64String(rootPublicKey);
        string encPubB64 = Convert.ToBase64String(companyEncryptionPublicKey);

        var profile = new CompanyProfileEntity
        {
            CompanyId = companyId,
            CompanyName = companyName,
            TaxCode = taxCode,
            RootPublicKeyBase64 = rootPubB64,
            CompanyEncryptionPublicKeyBase64 = encPubB64,
            IsDirectorNode = false,
            IsGenesisCompleted = true,
            AppSchemaVersion = CurrentAppSchemaVersion,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        var user = new LocalUserEntity
        {
            UserId = userId,
            CompanyId = companyId,
            Username = normalizedUser,
            FullName = fullName.Trim(),
            PasswordHash = _argon2.HashPassword(loginPassword),
            Role = verified.Snapshot.Role,
            IsActive = true,
            CreatedAtUtc = now,
            LastLoginUtc = now
        };

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.CompanyProfiles.RemoveRange(db.CompanyProfiles);
        db.LocalUsers.RemoveRange(db.LocalUsers);
        db.CompanyProfiles.Add(profile);
        db.LocalUsers.Add(user);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        _session.SetCompanyContext(profile.CompanyId, profile.CompanyName, profile.TaxCode, false);
        _session.SetAuthenticatedUser(user.UserId, user.Username, user.FullName, user.Role);
        _permissions.ApplyPolicySnapshot(verified.Snapshot);

        await _audit.LogAsync("EMPLOYEE_LAN_PAIRED", "core.bootstrap", normalizedUser, null, "Info", ct)
            .ConfigureAwait(false);

        return profile;
    }

    public async Task<AuthenticationResult> AuthenticateOfflineAsync(
        string username,
        string password,
        CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        CompanyProfileEntity? company = await db.CompanyProfiles.FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (company is null || !company.IsGenesisCompleted)
        {
            return new AuthenticationResult(false, "Máy trạm chưa được khởi tạo hoặc ghép đôi.", true, null, null);
        }

        string normalized = username.Trim().ToLowerInvariant();
        LocalUserEntity? user = await db.LocalUsers
            .FirstOrDefaultAsync(x => x.Username == normalized && x.IsActive, ct)
            .ConfigureAwait(false);

        if (user is null || !_argon2.VerifyPassword(password, user.PasswordHash))
        {
            await _audit.LogAsync("LOGIN_FAILED", "core.auth", normalized, null, "Warning", ct).ConfigureAwait(false);
            return new AuthenticationResult(false, "Tên đăng nhập hoặc mật khẩu không đúng.", true, null, company);
        }

        byte[]? pinnedRootPub = _keyStore.LoadPinnedRootPublicKey()
            ?? Convert.FromBase64String(company.RootPublicKeyBase64);
        byte[]? devicePub = _keyStore.LoadDevicePublicKey();
        string? policyJson = _keyStore.LoadPolicyLicenseJson();

        PolicyVerificationResult verification = _policyEngine.VerifyOffline(
            policyJson,
            pinnedRootPub,
            _paths.MachineId,
            devicePub);

        // If a Director's local policy expired (e.g., offline >14 days) without clock rollback, renew locally via RootPrivateKey.
        if (!verification.IsValid &&
            verification.FailureReason == PolicyFailureReason.PolicyExpired &&
            company.IsDirectorNode &&
            _keyStore.HasRootPrivateKey &&
            verification.Payload is not null)
        {
            byte[]? rootPriv = _keyStore.LoadRootPrivateKey();
            if (rootPriv is not null)
            {
                try
                {
                    DateTimeOffset now = _timeProvider.GetUtcNow();
                    PolicyPayload renewed = verification.Payload with
                    {
                        PolicyVersion = verification.Payload.PolicyVersion + 1,
                        IssuedAtUtc = now,
                        ExpiresAtUtc = now.AddDays(PolicyEngine.DefaultValidityDays)
                    };
                    string renewedJson = _policyEngine.SerializeDocument(_policyEngine.IssueSignedPolicy(renewed, rootPriv));
                    _keyStore.SavePolicyLicenseJson(renewedJson);
                    verification = _policyEngine.VerifyOffline(renewedJson, pinnedRootPub, _paths.MachineId, devicePub);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(rootPriv);
                }
            }
        }

        user.LastLoginUtc = _timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        _session.SetCompanyContext(company.CompanyId, company.CompanyName, company.TaxCode, company.IsDirectorNode);
        _session.SetAuthenticatedUser(user.UserId, user.Username, user.FullName, user.Role);

        if (verification.IsValid && verification.Snapshot is not null)
        {
            _permissions.ApplyPolicySnapshot(verification.Snapshot);
            await _audit.LogAsync("LOGIN_SUCCESS", "core.auth", user.Username, null, "Info", ct).ConfigureAwait(false);
            return new AuthenticationResult(true, "Đăng nhập thành công.", false, verification.Snapshot, company);
        }

        if (verification.Snapshot is not null)
        {
            _permissions.ApplyPolicySnapshot(verification.Snapshot);
        }
        else
        {
            _permissions.Lock(verification.Message);
        }

        await _audit.LogAsync(
            "LOGIN_POLICY_LOCKED",
            "core.auth",
            user.Username,
            verification.Message,
            "Warning",
            ct).ConfigureAwait(false);

        return new AuthenticationResult(
            true,
            verification.Message,
            true,
            _permissions.CurrentPolicy,
            company);
    }

    /// <summary>
    /// Allows an authenticated Director or Employee to update their display name, username,
    /// and optionally local Argon2id login password after verifying their current password.
    /// </summary>
    public async Task<LocalUserEntity> ChangeCredentialsAsync(
        Guid userId,
        string currentPassword,
        string newFullName,
        string newUsername,
        string? newPassword,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(currentPassword))
        {
            throw new ArgumentException("Vui lòng nhập mật khẩu hiện tại để xác nhận.", nameof(currentPassword));
        }

        if (string.IsNullOrWhiteSpace(newFullName))
        {
            throw new ArgumentException("Họ và tên không được để trống.", nameof(newFullName));
        }

        if (string.IsNullOrWhiteSpace(newUsername))
        {
            throw new ArgumentException("Tên đăng nhập không được để trống.", nameof(newUsername));
        }

        if (!string.IsNullOrEmpty(newPassword) && newPassword.Length < 6)
        {
            throw new ArgumentException("Mật khẩu mới phải có ít nhất 6 ký tự.", nameof(newPassword));
        }

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        CompanyProfileEntity? company = await db.CompanyProfiles.FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (company is null || !company.IsGenesisCompleted)
        {
            throw new InvalidOperationException("Máy trạm chưa được khởi tạo.");
        }

        LocalUserEntity? user = await db.LocalUsers
            .FirstOrDefaultAsync(x => x.UserId == userId && x.IsActive, ct)
            .ConfigureAwait(false);
        if (user is null)
        {
            throw new InvalidOperationException("Không tìm thấy tài khoản cục bộ.");
        }

        if (!_argon2.VerifyPassword(currentPassword, user.PasswordHash))
        {
            await _audit.LogAsync("CREDENTIALS_CHANGE_DENIED", "core.auth", user.Username, "Sai mật khẩu hiện tại", "Warning", ct)
                .ConfigureAwait(false);
            throw new UnauthorizedAccessException("Mật khẩu hiện tại không chính xác.");
        }

        string normalizedNewUsername = newUsername.Trim().ToLowerInvariant();
        bool duplicateUsername = await db.LocalUsers
            .AnyAsync(x => x.UserId != userId && x.Username == normalizedNewUsername, ct)
            .ConfigureAwait(false);
        if (duplicateUsername)
        {
            throw new InvalidOperationException($"Tên đăng nhập '{normalizedNewUsername}' đã tồn tại trên máy này.");
        }

        string trimmedFullName = newFullName.Trim();
        user.FullName = trimmedFullName;
        user.Username = normalizedNewUsername;
        if (!string.IsNullOrEmpty(newPassword))
        {
            user.PasswordHash = _argon2.HashPassword(newPassword);
        }

        PairedDeviceEntity? selfDevice = await db.PairedDevices
            .FirstOrDefaultAsync(x => x.MachineId == _paths.MachineId, ct)
            .ConfigureAwait(false);
        if (selfDevice is not null)
        {
            selfDevice.Username = normalizedNewUsername;
            selfDevice.FullName = trimmedFullName;
        }

        if (company.IsDirectorNode && _keyStore.HasRootPrivateKey)
        {
            await ReissueLocalDirectorPolicyAsync(selfDevice, normalizedNewUsername, trimmedFullName, ct)
                .ConfigureAwait(false);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        if (_session.IsAuthenticated && _session.UserId == user.UserId)
        {
            _session.SetAuthenticatedUser(user.UserId, user.Username, user.FullName, user.Role);
        }

        await _audit.LogAsync(
            "CREDENTIALS_UPDATED",
            "core.auth",
            user.Username,
            $"FullName={user.FullName};PasswordChanged={!string.IsNullOrEmpty(newPassword)}",
            "Info",
            ct).ConfigureAwait(false);

        _eventBus?.Publish(new UserProfileUpdatedEvent(user.UserId, user.Username, user.FullName, user.Role));
        return user;
    }

    /// <summary>
    /// Recovers local Director credentials on an already-initialized Director machine using
    /// the company's <c>.recovery</c> Master Recovery Kit and recovery password.
    /// </summary>
    public async Task<LocalUserEntity> RecoverDirectorAccountWithRecoveryKitAsync(
        string recoveryFilePath,
        string recoveryPassword,
        string? newUsername,
        string newLoginPassword,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(recoveryFilePath) || !File.Exists(recoveryFilePath))
        {
            throw new FileNotFoundException("Không tìm thấy tệp khôi phục (.recovery).", recoveryFilePath);
        }

        if (string.IsNullOrEmpty(recoveryPassword))
        {
            throw new ArgumentException("Vui lòng nhập mật khẩu giải mã tệp .recovery.", nameof(recoveryPassword));
        }

        if (string.IsNullOrEmpty(newLoginPassword) || newLoginPassword.Length < 6)
        {
            throw new ArgumentException("Mật khẩu mới phải có ít nhất 6 ký tự.", nameof(newLoginPassword));
        }

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        CompanyProfileEntity? company = await db.CompanyProfiles.FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (company is null || !company.IsGenesisCompleted || !company.IsDirectorNode)
        {
            throw new InvalidOperationException("Chức năng này chỉ dành cho máy Giám đốc đã khởi tạo.");
        }

        RecoveryKitPayload recovered = await _recoveryKit.ImportFromFileAsync(recoveryFilePath, recoveryPassword, ct)
            .ConfigureAwait(false);

        if (recovered.CompanyId != company.CompanyId ||
            !string.Equals(recovered.RootPublicKeyBase64, company.RootPublicKeyBase64, StringComparison.Ordinal))
        {
            await _audit.LogAsync("DIRECTOR_RECOVERY_MISMATCH", "core.auth", company.CompanyName, null, "Warning", ct)
                .ConfigureAwait(false);
            throw new InvalidOperationException("Tệp .recovery không thuộc doanh nghiệp hoặc cặp khóa gốc trên máy này.");
        }

        byte[] rootPriv = Convert.FromBase64String(recovered.RootPrivateKeyBase64);
        byte[] rootPub = Convert.FromBase64String(recovered.RootPublicKeyBase64);
        try
        {
            if (!_keyStore.HasRootPrivateKey)
            {
                _keyStore.SaveRootKeyPair(rootPriv, rootPub);
            }

            LocalUserEntity? directorUser = await db.LocalUsers
                .FirstOrDefaultAsync(x => x.IsActive, ct)
                .ConfigureAwait(false);
            if (directorUser is null)
            {
                throw new InvalidOperationException("Không tìm thấy tài khoản Giám đốc cục bộ.");
            }

            if (!string.IsNullOrWhiteSpace(newUsername))
            {
                string normalizedUser = newUsername.Trim().ToLowerInvariant();
                bool conflict = await db.LocalUsers
                    .AnyAsync(x => x.UserId != directorUser.UserId && x.Username == normalizedUser, ct)
                    .ConfigureAwait(false);
                if (conflict)
                {
                    throw new InvalidOperationException($"Tên đăng nhập '{normalizedUser}' đã được sử dụng.");
                }

                directorUser.Username = normalizedUser;
            }

            directorUser.PasswordHash = _argon2.HashPassword(newLoginPassword);

            PairedDeviceEntity? selfDevice = await db.PairedDevices
                .FirstOrDefaultAsync(x => x.MachineId == _paths.MachineId, ct)
                .ConfigureAwait(false);
            if (selfDevice is not null)
            {
                selfDevice.Username = directorUser.Username;
                selfDevice.FullName = directorUser.FullName;
            }

            await ReissueLocalDirectorPolicyAsync(selfDevice, directorUser.Username, directorUser.FullName, ct)
                .ConfigureAwait(false);

            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            await _audit.LogAsync(
                "DIRECTOR_ACCOUNT_RECOVERED",
                "core.auth",
                directorUser.Username,
                $"MachineId={_paths.MachineId}",
                "Info",
                ct).ConfigureAwait(false);

            return directorUser;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rootPriv);
        }
    }

    /// <summary>
    /// Verifies a Director Ed25519 Root-signed password reset ticket locally on the employee workstation
    /// and updates the employee's local Argon2id password without ever sending passwords over LAN.
    /// </summary>
    public async Task<LocalUserEntity> ApplyApprovedPasswordResetAsync(
        string signedResetTicketJson,
        string newLocalPassword,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(newLocalPassword) || newLocalPassword.Length < 6)
        {
            throw new ArgumentException("Mật khẩu mới phải có ít nhất 6 ký tự.", nameof(newLocalPassword));
        }

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        CompanyProfileEntity? company = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (company is null || !company.IsGenesisCompleted)
        {
            throw new InvalidOperationException("Máy trạm chưa được ghép đôi với công ty.");
        }

        byte[]? pinnedRootPub = _keyStore.LoadPinnedRootPublicKey()
            ?? Convert.FromBase64String(company.RootPublicKeyBase64);
        byte[]? devicePub = _keyStore.LoadDevicePublicKey();

        (bool isValid, string message, PasswordResetTicketPayload? ticket) = _ticketService.VerifyTicketJson(
            signedResetTicketJson,
            pinnedRootPub,
            _paths.MachineId,
            devicePub,
            company.CompanyId);

        if (!isValid || ticket is null)
        {
            await _audit.LogAsync("EMPLOYEE_RESET_TICKET_INVALID", "core.auth", _paths.MachineId, message, "Warning", ct)
                .ConfigureAwait(false);
            throw new InvalidOperationException(message);
        }

        LocalUserEntity? localUser = await db.LocalUsers
            .FirstOrDefaultAsync(x => x.IsActive, ct)
            .ConfigureAwait(false);
        if (localUser is null)
        {
            throw new InvalidOperationException("Không tìm thấy tài khoản nhân viên cục bộ.");
        }

        if (!string.IsNullOrWhiteSpace(ticket.ApprovedUsername))
        {
            localUser.Username = ticket.ApprovedUsername.Trim().ToLowerInvariant();
        }

        localUser.PasswordHash = _argon2.HashPassword(newLocalPassword);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(
            "EMPLOYEE_PASSWORD_RESET_COMPLETED",
            "core.auth",
            localUser.Username,
            $"ApprovedBy={ticket.ApprovedByAdminName};RequestId={ticket.RequestId}",
            "Info",
            ct).ConfigureAwait(false);

        return localUser;
    }

    private async Task ReissueLocalDirectorPolicyAsync(
        PairedDeviceEntity? selfDevice,
        string username,
        string fullName,
        CancellationToken ct)
    {
        byte[]? rootPriv = _keyStore.LoadRootPrivateKey();
        byte[]? rootPub = _keyStore.LoadPinnedRootPublicKey();
        byte[]? devicePub = _keyStore.LoadDevicePublicKey();
        string? existingJson = _keyStore.LoadPolicyLicenseJson();

        if (rootPriv is null || rootPub is null || devicePub is null || string.IsNullOrWhiteSpace(existingJson))
        {
            return;
        }

        try
        {
            SignedPolicyDocument? doc = _policyEngine.DeserializeDocument(existingJson);
            if (doc is null)
            {
                return;
            }

            PolicyPayload? payload = _policyEngine.TryDecodePayloadUnverified(doc);
            if (payload is null)
            {
                return;
            }

            DateTimeOffset now = _timeProvider.GetUtcNow();
            PolicyPayload updated = payload with
            {
                Username = username,
                FullName = fullName,
                PolicyVersion = payload.PolicyVersion + 1,
                IssuedAtUtc = now,
                ExpiresAtUtc = now.AddDays(PolicyEngine.DefaultValidityDays)
            };

            string renewedJson = _policyEngine.SerializeDocument(_policyEngine.IssueSignedPolicy(updated, rootPriv));
            _keyStore.SavePolicyLicenseJson(renewedJson);

            if (selfDevice is not null)
            {
                selfDevice.CurrentPolicyVersion = updated.PolicyVersion;
                selfDevice.CurrentPolicyJson = renewedJson;
            }

            PolicyVerificationResult verified = _policyEngine.VerifyOffline(renewedJson, rootPub, _paths.MachineId, devicePub);
            if (verified.IsValid && verified.Snapshot is not null)
            {
                _permissions.ApplyPolicySnapshot(verified.Snapshot);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rootPriv);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static void ValidateCommonInputs(
        string companyName,
        string taxCode,
        string fullName,
        string username,
        string loginPassword)
    {
        if (string.IsNullOrWhiteSpace(companyName) ||
            string.IsNullOrWhiteSpace(taxCode) ||
            string.IsNullOrWhiteSpace(fullName) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrEmpty(loginPassword))
        {
            throw new ArgumentException("Vui lòng điền đầy đủ thông tin công ty và tài khoản quản trị.");
        }
    }
}
