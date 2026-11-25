using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Crypto;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent;
using EzCompany.Core.LanAgent.Discovery;
using EzCompany.Core.LanAgent.Models;
using EzCompany.Core.LanAgent.Pairing;
using EzCompany.Core.LanAgent.Security;
using EzCompany.Core.LanAgent.Vault;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EzCompany.Tests;

public sealed class Milestone2LanAgentTests : IAsyncLifetime
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "EzCompanyTests_M2_" + Guid.NewGuid().ToString("N"));
    private readonly List<IAsyncDisposable> _disposables = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (IAsyncDisposable d in _disposables)
        {
            await d.DisposeAsync();
        }

        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void SasCalculator_ProducesConsistentSixDigitCode_AndChangesOnSingleBitModification()
    {
        byte[] rootPub = RandomNumberGenerator.GetBytes(32);
        byte[] devPub = RandomNumberGenerator.GetBytes(32);
        byte[] clientNonce = RandomNumberGenerator.GetBytes(32);
        byte[] serverNonce = RandomNumberGenerator.GetBytes(32);

        string codeA = SasCalculator.ComputeSixDigitCode(rootPub, devPub, clientNonce, serverNonce);
        string codeB = SasCalculator.ComputeSixDigitCode(rootPub, devPub, clientNonce, serverNonce);
        Assert.Equal(codeA, codeB);
        Assert.Matches(@"^\d{3}-\d{3}$", codeA);

        byte[] tweakedNonce = (byte[])serverNonce.Clone();
        tweakedNonce[0] ^= 0x01;
        string codeTweaked = SasCalculator.ComputeSixDigitCode(rootPub, devPub, clientNonce, tweakedNonce);
        Assert.NotEqual(codeA, codeTweaked);
    }

    [Fact]
    public async Task KestrelMiddleware_EnforcesSignature_ReplayNonce_AndTimestampWindow()
    {
        (ServiceProvider dirSp, LanAgentHost dirHost, string dirEndpoint) = await StartNodeAsync("dir_auth", isDirector: true);
        (ServiceProvider empSp, LanAgentHost empHost, string empEndpoint) = await StartNodeAsync("emp_auth", isDirector: false);

        await PairNodesAsync(dirSp, dirEndpoint, empSp, "Nhân viên Auth", "authuser", "Pass123!", CompanyRoles.Staff);

        var dirClient = dirSp.GetRequiredService<DirectorLanClient>();
        WorkstationHealthDto health = await dirClient.GetHealthAsync(empEndpoint);
        Assert.Equal(empSp.GetRequiredService<ILocalStoragePaths>().MachineId, health.MachineId);

        // Forged signature must yield 401
        using var http = new HttpClient();
        using (var forgedReq = new HttpRequestMessage(HttpMethod.Get, $"{empEndpoint}/api/v1/health"))
        {
            forgedReq.Headers.Add(DirectorRequestSigner.TimestampHeader, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
            forgedReq.Headers.Add(DirectorRequestSigner.NonceHeader, Guid.NewGuid().ToString("N"));
            forgedReq.Headers.Add(DirectorRequestSigner.AdminNameHeader, "attacker");
            forgedReq.Headers.Add(DirectorRequestSigner.SignatureHeader, Convert.ToBase64String(new byte[64]));

            using HttpResponseMessage forgedResp = await http.SendAsync(forgedReq);
            Assert.Equal(HttpStatusCode.Unauthorized, forgedResp.StatusCode);
        }

        // Replaying the exact same signed headers/nonce must yield 401
        byte[] rootPriv = dirSp.GetRequiredService<LocalKeyStore>().LoadRootPrivateKey()!;
        var signer = dirSp.GetRequiredService<DirectorRequestSigner>();
        using (var firstReq = new HttpRequestMessage(HttpMethod.Get, $"{empEndpoint}/api/v1/health"))
        {
            signer.SignHttpRequest(firstReq, null, rootPriv, "director1");
            string ts = firstReq.Headers.GetValues(DirectorRequestSigner.TimestampHeader).First();
            string nonce = firstReq.Headers.GetValues(DirectorRequestSigner.NonceHeader).First();
            string sig = firstReq.Headers.GetValues(DirectorRequestSigner.SignatureHeader).First();

            using HttpResponseMessage okResp = await http.SendAsync(firstReq);
            Assert.Equal(HttpStatusCode.OK, okResp.StatusCode);

            using var replayReq = new HttpRequestMessage(HttpMethod.Get, $"{empEndpoint}/api/v1/health");
            replayReq.Headers.Add(DirectorRequestSigner.TimestampHeader, ts);
            replayReq.Headers.Add(DirectorRequestSigner.NonceHeader, nonce);
            replayReq.Headers.Add(DirectorRequestSigner.AdminNameHeader, "director1");
            replayReq.Headers.Add(DirectorRequestSigner.SignatureHeader, sig);

            using HttpResponseMessage replayResp = await http.SendAsync(replayReq);
            Assert.Equal(HttpStatusCode.Unauthorized, replayResp.StatusCode);
        }

        // Timestamp skew > 60 seconds must yield 401
        using (var staleReq = new HttpRequestMessage(HttpMethod.Get, $"{empEndpoint}/api/v1/health"))
        {
            signer.SignHttpRequest(staleReq, null, rootPriv, "director1", DateTimeOffset.UtcNow.AddMinutes(-3));
            using HttpResponseMessage staleResp = await http.SendAsync(staleReq);
            Assert.Equal(HttpStatusCode.Unauthorized, staleResp.StatusCode);
        }

        await dirHost.StopAsync(CancellationToken.None);
        await empHost.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task VaultDownload_BlocksPathTraversal_AndReturnsMatchingSha256()
    {
        (ServiceProvider dirSp, LanAgentHost dirHost, string dirEndpoint) = await StartNodeAsync("dir_vault", isDirector: true);
        (ServiceProvider empSp, LanAgentHost empHost, string empEndpoint) = await StartNodeAsync("emp_vault", isDirector: false);

        await PairNodesAsync(dirSp, dirEndpoint, empSp, "Nhân viên Vault", "vaultuser", "Pass123!", CompanyRoles.Staff);

        ILocalStoragePaths empPaths = empSp.GetRequiredService<ILocalStoragePaths>();
        string sampleFile = Path.Combine(empPaths.VaultDirectory, "Office", "report.txt");
        byte[] sampleContent = Encoding.UTF8.GetBytes("Confidential LAN Vault Content v1");
        await File.WriteAllBytesAsync(sampleFile, sampleContent);
        string expectedSha = Convert.ToHexString(SHA256.HashData(sampleContent)).ToLowerInvariant();

        var dirClient = dirSp.GetRequiredService<DirectorLanClient>();
        IReadOnlyList<VaultFileEntryDto> tree = await dirClient.GetVaultTreeAsync(empEndpoint);
        Assert.Contains(tree, f => f.RelativePath == "Office/report.txt");

        DownloadedArtifact dl = await dirClient.DownloadVaultFileAsync(empEndpoint, "Office/report.txt");
        Assert.Equal(sampleContent, dl.Bytes);
        Assert.Equal(expectedSha, dl.Sha256Header);

        // Traversal probes must all fail with 400 BadRequest
        string[] maliciousPaths =
        [
            "../Data/app.db",
            "..\\Keys\\device.key.dpapi",
            "Office/../../Keys/pinned_root.pub",
            @"C:\Windows\System32\drivers\etc\hosts"
        ];

        foreach (string badPath in maliciousPaths)
        {
            string query = $"/api/v1/vault/download?relativePath={Uri.EscapeDataString(badPath)}";
            using HttpResponseMessage resp = await dirClient.SendSignedAsync(HttpMethod.Get, empEndpoint, query, null);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        await dirHost.StopAsync(CancellationToken.None);
        await empHost.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task VacuumIntoSnapshot_SucceedsDuringConcurrentWrites_WithExactSha256AndIntegrityOk()
    {
        (ServiceProvider dirSp, LanAgentHost dirHost, string dirEndpoint) = await StartNodeAsync("dir_snap", isDirector: true);
        (ServiceProvider empSp, LanAgentHost empHost, string empEndpoint) = await StartNodeAsync("emp_snap", isDirector: false);

        await PairNodesAsync(dirSp, dirEndpoint, empSp, "Kế toán Snap", "snapuser", "Pass123!", CompanyRoles.Accounting);

        IAuditLogger empAudit = empSp.GetRequiredService<IAuditLogger>();
        ILocalStoragePaths empPaths = empSp.GetRequiredService<ILocalStoragePaths>();

        // Concurrent WAL write storm while VACUUM INTO runs
        using var cts = new CancellationTokenSource();
        Task writerTask = Task.Run(async () =>
        {
            for (int i = 0; i < 40 && !cts.Token.IsCancellationRequested; i++)
            {
                await empAudit.LogAsync("CONCURRENT_WRITE", ModuleIds.AccountingInvoiceXml, $"INV-{i}", null, "Info", cts.Token);
                await Task.Delay(10, cts.Token);
            }
        });

        await Task.Delay(50);
        var dirClient = dirSp.GetRequiredService<DirectorLanClient>();
        DownloadedArtifact snap = await dirClient.DownloadDatabaseSnapshotAsync(empEndpoint);
        await cts.CancelAsync();
        try { await writerTask; } catch (OperationCanceledException) { }

        Assert.NotEmpty(snap.Bytes);
        Assert.Equal(GenesisBootstrapService.CurrentAppSchemaVersion, snap.SchemaVersionHeader);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(snap.Bytes)).ToLowerInvariant(), snap.Sha256Header);

        // Temp snapshot files must be cleaned up on employee machine
        Assert.Empty(Directory.GetFiles(empPaths.TempDirectory, "snapshot_*.db"));

        // Verify downloaded SQLite snapshot integrity
        string verifyDbPath = Path.Combine(_tempRoot, "verified_snapshot.db");
        await File.WriteAllBytesAsync(verifyDbPath, snap.Bytes);
        await using (var conn = new SqliteConnection($"Data Source={verifyDbPath};Mode=ReadOnly"))
        {
            await conn.OpenAsync();
            await using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";
            string? integrity = (await cmd.ExecuteScalarAsync())?.ToString();
            Assert.Equal("ok", integrity, ignoreCase: true);
        }

        await dirHost.StopAsync(CancellationToken.None);
        await empHost.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task PendingPolicyQueue_And_TwoDirectorSync_WorkEndToEnd()
    {
        (ServiceProvider dir1Sp, LanAgentHost dir1Host, string dir1Endpoint) = await StartNodeAsync("dir1_sync", isDirector: true);
        (ServiceProvider empSp, LanAgentHost empHost, string empEndpoint) = await StartNodeAsync("emp_sync", isDirector: false);

        await PairNodesAsync(dir1Sp, dir1Endpoint, empSp, "Pháp chế Sync", "legaluser", "Pass123!", CompanyRoles.Legal);

        string empMachineId = empSp.GetRequiredService<ILocalStoragePaths>().MachineId;
        var empPermissions = empSp.GetRequiredService<IPermissionService>();
        Assert.False(empPermissions.HasModuleAccess(ModuleIds.AccountingInvoiceXml));
        Assert.True(empPermissions.HasModuleAccess(ModuleIds.LegalContractDiff));

        // Simulate employee offline by clearing endpoint in Director's DB before issuing v2 policy
        IDbContextFactory<AppDbContext> dir1DbFactory = dir1Sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (AppDbContext db = await dir1DbFactory.CreateDbContextAsync())
        {
            PairedDeviceEntity dev = await db.PairedDevices.FirstAsync(x => x.MachineId == empMachineId);
            dev.LastKnownIp = null;
            dev.LastKnownPort = 0;
            await db.SaveChangesAsync();
        }

        var dir1Client = dir1Sp.GetRequiredService<DirectorLanClient>();
        (bool pushedNow, string queuedPolicyJson) = await dir1Client.IssueAndDispatchPolicyAsync(
            empMachineId,
            CompanyRoles.Accounting,
            [ModuleIds.AccountingInvoiceXml, ModuleIds.LegalContractDiff, ModuleIds.OfficePdfStudio],
            "stable",
            "0.2.0",
            "Director One");

        Assert.False(pushedNow);
        Assert.NotEmpty(queuedPolicyJson);

        // Employee returns online -> Director pushes queued policy -> live hot-reload updates employee modules immediately
        ApplyPolicyResponseDto applied = await dir1Client.PushPolicyAsync(empEndpoint, queuedPolicyJson);
        Assert.True(applied.Applied);
        Assert.Equal(2, applied.PolicyVersion);
        Assert.True(empPermissions.HasModuleAccess(ModuleIds.AccountingInvoiceXml));

        // Verify 2-Director merge keeps higher PolicyVersion (v2 overrides v1)
        var dir2Sp = BuildNodeServices("dir2_sync");
        _disposables.Add(dir2Sp);
        await MigrateNodeAsync(dir2Sp);
        string recoveryPath = Path.Combine(_tempRoot, "dir1_sync.recovery");
        await dir2Sp.GetRequiredService<GenesisBootstrapService>().RestoreSecondaryDirectorAsync(
            recoveryPath,
            "Recovery123!",
            "Director Two",
            "director2",
            "DirPass2!");

        IReadOnlyList<RegistryDeviceSyncDto> remoteEntries = await dir1Client.GetDirectorRegistryAsync(dir1Endpoint);
        int merged = await LanAgentHost.MergeDirectorRegistryAsync(
            dir2Sp.GetRequiredService<IDbContextFactory<AppDbContext>>(),
            remoteEntries);
        Assert.True(merged >= 1);

        await using (AppDbContext dir2Db = await dir2Sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            PairedDeviceEntity syncedEmp = await dir2Db.PairedDevices.FirstAsync(x => x.MachineId == empMachineId);
            Assert.Equal(2, syncedEmp.CurrentPolicyVersion);
            Assert.Equal(CompanyRoles.Accounting, syncedEmp.Role);
        }

        await dir1Host.StopAsync(CancellationToken.None);
        await empHost.StopAsync(CancellationToken.None);
    }

    private async Task<(ServiceProvider Sp, LanAgentHost Host, string Endpoint)> StartNodeAsync(string profile, bool isDirector)
    {
        ServiceProvider sp = BuildNodeServices(profile);
        _disposables.Add(sp);
        await MigrateNodeAsync(sp);

        if (isDirector)
        {
            string kitPath = Path.Combine(_tempRoot, $"{profile}.recovery");
            await sp.GetRequiredService<GenesisBootstrapService>().InitializeGenesisDirectorAsync(
                "Sample Co",
                "0301234567",
                "Director One",
                "director1",
                "DirPass1!",
                "Recovery123!",
                kitPath);
        }

        var host = sp.GetRequiredService<LanAgentHost>();
        await host.StartAsync(CancellationToken.None);
        return (sp, host, $"http://127.0.0.1:{host.BoundPort}");
    }

    private static async Task PairNodesAsync(
        ServiceProvider dirSp,
        string dirEndpoint,
        ServiceProvider empSp,
        string empFullName,
        string empUsername,
        string empPassword,
        string roleOrModuleHint)
    {
        string targetRole = CompanyRoles.All.Contains(roleOrModuleHint) ? roleOrModuleHint : CompanyRoles.Staff;
        var pairingMgr = dirSp.GetRequiredService<PairingSessionManager>();
        var empPairingClient = empSp.GetRequiredService<LanPairingClient>();

        // Closed window must reject pairing
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            empPairingClient.InitiatePairingAsync(dirEndpoint, empFullName, empUsername));

        pairingMgr.OpenPairingWindow(TimeSpan.FromMinutes(2));
        StartedPairingSession started = await empPairingClient.InitiatePairingAsync(dirEndpoint, empFullName, empUsername);

        PendingPairingRequest pending = Assert.Single(pairingMgr.GetPendingRequests());
        Assert.Equal(pending.SasCode, started.SasCode);

        _ = await pairingMgr.ApproveRequestAsync(
            started.RequestId,
            targetRole,
            CompanyRoles.GetDefaultModulesForRole(targetRole),
            "0.1.0",
            "stable",
            "Director One");

        PairingStatusResponse final = await empPairingClient.WaitAndCompleteOnboardingAsync(
            dirEndpoint,
            started,
            empFullName,
            empUsername,
            empPassword,
            TimeSpan.FromSeconds(10));

        Assert.Equal(PairingRequestState.Approved, final.State);
    }

    private ServiceProvider BuildNodeServices(string profile)
    {
        var paths = new LocalStoragePaths(profile, _tempRoot);
        var services = new ServiceCollection();
        services.AddSingleton<ILocalStoragePaths>(paths);
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton<ISessionContext, SessionContext>();
        services.AddLogging();
        services.AddEzCrypto();
        services.AddEzData(paths);
        services.AddEzLanAgent(bindLoopbackOnly: true);
        return services.BuildServiceProvider();
    }

    private static async Task MigrateNodeAsync(IServiceProvider sp)
    {
        await using AppDbContext appDb = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        await appDb.Database.MigrateAsync();
        await using TelemetryDbContext telDb = await sp.GetRequiredService<IDbContextFactory<TelemetryDbContext>>().CreateDbContextAsync();
        await telDb.Database.MigrateAsync();
    }
}
