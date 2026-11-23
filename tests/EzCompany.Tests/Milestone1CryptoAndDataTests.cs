using System.Data.Common;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Crypto;
using EzCompany.Core.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EzCompany.Tests;

public sealed class Milestone1CryptoAndDataTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "EzCompanyTests_M1_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Ed25519_SignVerify_DetectsOneByteTampering()
    {
        var ed = new Ed25519Service();
        Ed25519KeyPair pair = ed.GenerateKeyPair();
        byte[] message = Encoding.UTF8.GetBytes("ezcompany-policy-payload");

        byte[] signature = ed.Sign(pair.PrivateKey, message);
        Assert.True(ed.Verify(pair.PublicKey, message, signature));

        byte[] tamperedMessage = (byte[])message.Clone();
        tamperedMessage[0] ^= 0x01;
        Assert.False(ed.Verify(pair.PublicKey, tamperedMessage, signature));

        byte[] tamperedSig = (byte[])signature.Clone();
        tamperedSig[^1] ^= 0x01;
        Assert.False(ed.Verify(pair.PublicKey, message, tamperedSig));
    }

    [Fact]
    public void Argon2PasswordHasher_HashesAndVerifiesCorrectly()
    {
        var hasher = new Argon2PasswordHasher();
        string hash = hasher.HashPassword("StrongP@ssword!2026");

        Assert.StartsWith("$argon2id$", hash);
        Assert.True(hasher.VerifyPassword("StrongP@ssword!2026", hash));
        Assert.False(hasher.VerifyPassword("WrongPassword", hash));
    }

    [Fact]
    public async Task RecoveryKit_ExportImportRoundtrip_RejectsWrongPasswordAndTamper()
    {
        var ed = new Ed25519Service();
        Ed25519KeyPair root = ed.GenerateKeyPair();
        var service = new RecoveryKitService(new Argon2PasswordHasher(), new AesGcmService(), ed);

        var payload = new RecoveryKitPayload(
            Guid.NewGuid(),
            "Demo Enterprise",
            "0101234567",
            Convert.ToBase64String(root.PrivateKey),
            root.PublicKeyBase64,
            DateTimeOffset.UtcNow,
            "Director One");

        string kitPath = Path.Combine(_tempRoot, "master.recovery");
        await service.ExportToFileAsync(kitPath, payload, "RecoverySecret#123");

        RecoveryKitPayload restored = await service.ImportFromFileAsync(kitPath, "RecoverySecret#123");
        Assert.Equal(payload.CompanyId, restored.CompanyId);
        Assert.Equal(payload.RootPrivateKeyBase64, restored.RootPrivateKeyBase64);
        Assert.Equal(payload.RootPublicKeyBase64, restored.RootPublicKeyBase64);

        await Assert.ThrowsAnyAsync<CryptographicException>(
            () => service.ImportFromFileAsync(kitPath, "WrongSecret#999"));

        // Tamper 1 byte in ciphertext inside JSON envelope
        byte[] raw = await File.ReadAllBytesAsync(kitPath);
        RecoveryKitFileEnvelope env = JsonSerializer.Deserialize<RecoveryKitFileEnvelope>(raw)!;
        byte[] cipher = Convert.FromBase64String(env.CiphertextBase64);
        cipher[0] ^= 0xFF;
        RecoveryKitFileEnvelope tampered = env with { CiphertextBase64 = Convert.ToBase64String(cipher) };
        byte[] tamperedBytes = JsonSerializer.SerializeToUtf8Bytes(tampered);

        Assert.ThrowsAny<CryptographicException>(() => service.ImportBytes(tamperedBytes, "RecoverySecret#123"));
    }

    [Fact]
    public void PolicyEngine_VerifiesValidLicense_RejectsTamperWrongMachineAndDowngrade()
    {
        var paths = new LocalStoragePaths("policytest", _tempRoot);
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero));
        var ed = new Ed25519Service();
        var ecies = new EciesCryptoService();
        var dpapi = new DpapiSecretStore();
        var keyStore = new LocalKeyStore(paths, dpapi);
        var clockGuard = new ClockGuardService(keyStore, dpapi, time);
        var engine = new PolicyEngine(ed, clockGuard);

        Ed25519KeyPair root = ed.GenerateKeyPair();
        Ed25519KeyPair device = ed.GenerateKeyPair();
        byte[] encPub = ecies.DeriveCompanyX25519PublicKey(root.PrivateKey);

        PolicyPayload payloadV2 = new(
            Guid.NewGuid(),
            paths.MachineId,
            device.PublicKeyBase64,
            Convert.ToBase64String(encPub),
            Guid.NewGuid(),
            "acct1",
            "Accountant One",
            CompanyRoles.Accounting,
            [ModuleIds.AccountingInvoiceXml, ModuleIds.OfficePdfStudio],
            CompanyRoles.GetDefaultPermissionsForRole(CompanyRoles.Accounting),
            PolicyVersion: 2,
            IssuedAtUtc: time.GetUtcNow(),
            ExpiresAtUtc: time.GetUtcNow().AddDays(14),
            AllowedUpdateChannel: "stable",
            MaxApprovedVersion: "0.1.0",
            IssuedByAdminName: "Director One");

        string docV2Json = engine.SerializeDocument(engine.IssueSignedPolicy(payloadV2, root.PrivateKey));
        PolicyVerificationResult ok = engine.VerifyOffline(docV2Json, root.PublicKey, paths.MachineId, device.PublicKey);
        Assert.True(ok.IsValid);
        Assert.Equal(PolicyFailureReason.None, ok.FailureReason);

        // 1-byte tamper in signature
        SignedPolicyDocument docObj = engine.DeserializeDocument(docV2Json)!;
        byte[] payloadBytes = Convert.FromBase64String(docObj.PayloadBase64);
        payloadBytes[2] ^= 0x01;
        string tamperedDocJson = engine.SerializeDocument(docObj with { PayloadBase64 = Convert.ToBase64String(payloadBytes) });
        PolicyVerificationResult tamperedRes = engine.VerifyOffline(tamperedDocJson, root.PublicKey, paths.MachineId, device.PublicKey);
        Assert.False(tamperedRes.IsValid);
        Assert.Equal(PolicyFailureReason.InvalidSignature, tamperedRes.FailureReason);

        // Wrong MachineId
        PolicyVerificationResult wrongMachine = engine.VerifyOffline(docV2Json, root.PublicKey, "EZ-WRONGMACHINE", device.PublicKey);
        Assert.False(wrongMachine.IsValid);
        Assert.Equal(PolicyFailureReason.MachineIdMismatch, wrongMachine.FailureReason);

        // Downgrade PolicyVersion (v1 < v2)
        PolicyPayload payloadV1 = payloadV2 with { PolicyVersion = 1 };
        string docV1Json = engine.SerializeDocument(engine.IssueSignedPolicy(payloadV1, root.PrivateKey));
        PolicyVerificationResult downgrade = engine.VerifyOffline(docV1Json, root.PublicKey, paths.MachineId, device.PublicKey);
        Assert.False(downgrade.IsValid);
        Assert.Equal(PolicyFailureReason.PolicyVersionDowngrade, downgrade.FailureReason);
    }

    [Fact]
    public void PolicyEngine_ClockGuard_DetectsRollbackBeyondFiveMinutesAndExpiry()
    {
        var paths = new LocalStoragePaths("clocktest", _tempRoot);
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero));
        var ed = new Ed25519Service();
        var ecies = new EciesCryptoService();
        var dpapi = new DpapiSecretStore();
        var keyStore = new LocalKeyStore(paths, dpapi);
        var clockGuard = new ClockGuardService(keyStore, dpapi, time);
        var engine = new PolicyEngine(ed, clockGuard);

        Ed25519KeyPair root = ed.GenerateKeyPair();
        Ed25519KeyPair device = ed.GenerateKeyPair();
        byte[] encPub = ecies.DeriveCompanyX25519PublicKey(root.PrivateKey);

        PolicyPayload payload = new(
            Guid.NewGuid(),
            paths.MachineId,
            device.PublicKeyBase64,
            Convert.ToBase64String(encPub),
            Guid.NewGuid(),
            "staff1",
            "Staff One",
            CompanyRoles.Staff,
            [ModuleIds.OfficePdfStudio],
            CompanyRoles.GetDefaultPermissionsForRole(CompanyRoles.Staff),
            PolicyVersion: 1,
            IssuedAtUtc: time.GetUtcNow(),
            ExpiresAtUtc: time.GetUtcNow().AddDays(14),
            AllowedUpdateChannel: "stable",
            MaxApprovedVersion: "0.1.0",
            IssuedByAdminName: "Director One");

        string docJson = engine.SerializeDocument(engine.IssueSignedPolicy(payload, root.PrivateKey));
        Assert.True(engine.VerifyOffline(docJson, root.PublicKey, paths.MachineId, device.PublicKey).IsValid);

        // 4 minutes back is within 5-minute tolerance
        time.Advance(TimeSpan.FromMinutes(-4));
        Assert.True(engine.VerifyOffline(docJson, root.PublicKey, paths.MachineId, device.PublicKey).IsValid);

        // 6 minutes behind recorded LastRunTimestampUtc triggers rollback lock
        time.SetUtcNow(new DateTimeOffset(2026, 11, 23, 11, 54, 0, TimeSpan.Zero));
        PolicyVerificationResult rollback = engine.VerifyOffline(docJson, root.PublicKey, paths.MachineId, device.PublicKey);
        Assert.False(rollback.IsValid);
        Assert.Equal(PolicyFailureReason.ClockRollbackDetected, rollback.FailureReason);

        // Move past 14-day expiry triggers PolicyExpired
        time.SetUtcNow(new DateTimeOffset(2026, 12, 10, 0, 0, 0, TimeSpan.Zero));
        PolicyVerificationResult expired = engine.VerifyOffline(docJson, root.PublicKey, paths.MachineId, device.PublicKey);
        Assert.False(expired.IsValid);
        Assert.Equal(PolicyFailureReason.PolicyExpired, expired.FailureReason);
    }

    [Fact]
    public async Task SqliteDatabases_EnableWalMode_AndMigrateCleanly_PlusGenesisAndDirector2Restore()
    {
        // 1. Director 1 Genesis Bootstrap
        var dir1Paths = new LocalStoragePaths("dir1_m1", _tempRoot);
        using ServiceProvider dir1Services = BuildCoreProvider(dir1Paths);

        await MigrateAllAsync(dir1Services);

        IDbContextFactory<AppDbContext> appFactory = dir1Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (AppDbContext db = await appFactory.CreateDbContextAsync())
        {
            await db.Database.OpenConnectionAsync();
            await using DbCommand cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode;";
            string? mode = (await cmd.ExecuteScalarAsync())?.ToString();
            Assert.Equal("wal", mode, ignoreCase: true);
        }

        string recoveryPath = Path.Combine(_tempRoot, "company.recovery");
        var dir1Bootstrap = dir1Services.GetRequiredService<GenesisBootstrapService>();
        var created = await dir1Bootstrap.InitializeGenesisDirectorAsync(
            "Sample Co",
            "0300000001",
            "Director One",
            "director1",
            "Pass123!",
            "Recovery123!",
            recoveryPath);

        Assert.True(created.IsGenesisCompleted);
        Assert.True(created.IsDirectorNode);
        Assert.True(File.Exists(recoveryPath));

        AuthenticationResult auth1 = await dir1Bootstrap.AuthenticateOfflineAsync("director1", "Pass123!");
        Assert.True(auth1.Succeeded);
        Assert.False(auth1.IsPolicyLocked);

        // 2. Director 2 Restore (Approach A - shared Root Key)
        var dir2Paths = new LocalStoragePaths("dir2_m1", _tempRoot);
        using ServiceProvider dir2Services = BuildCoreProvider(dir2Paths);
        await MigrateAllAsync(dir2Services);

        var dir2Bootstrap = dir2Services.GetRequiredService<GenesisBootstrapService>();
        var restored = await dir2Bootstrap.RestoreSecondaryDirectorAsync(
            recoveryPath,
            "Recovery123!",
            "Director Two",
            "director2",
            "Pass456!");

        Assert.Equal(created.CompanyId, restored.CompanyId);
        Assert.Equal(created.RootPublicKeyBase64, restored.RootPublicKeyBase64);
        Assert.Equal(created.CompanyEncryptionPublicKeyBase64, restored.CompanyEncryptionPublicKeyBase64);
        Assert.True(restored.IsDirectorNode);

        AuthenticationResult auth2 = await dir2Bootstrap.AuthenticateOfflineAsync("director2", "Pass456!");
        Assert.True(auth2.Succeeded);
        Assert.False(auth2.IsPolicyLocked);
    }

    private static ServiceProvider BuildCoreProvider(ILocalStoragePaths paths)
    {
        var services = new ServiceCollection();
        services.AddSingleton(paths);
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton<ISessionContext, SessionContext>();
        services.AddEzCrypto();
        services.AddEzData(paths);
        return services.BuildServiceProvider();
    }

    private static async Task MigrateAllAsync(IServiceProvider services)
    {
        await using AppDbContext appDb = await services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        await appDb.Database.MigrateAsync();
        await using TelemetryDbContext telDb = await services.GetRequiredService<IDbContextFactory<TelemetryDbContext>>().CreateDbContextAsync();
        await telDb.Database.MigrateAsync();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset _utc = initialUtc;
        public override DateTimeOffset GetUtcNow() => _utc;
        public void SetUtcNow(DateTimeOffset utc) => _utc = utc;
        public void Advance(TimeSpan delta) => _utc = _utc.Add(delta);
    }
}
