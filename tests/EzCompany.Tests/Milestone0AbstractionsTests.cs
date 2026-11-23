using System.IO;
using EzCompany.Core.Abstractions;
using Xunit;

namespace EzCompany.Tests;

public sealed class Milestone0AbstractionsTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "EzCompanyTests_M0_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void LocalStoragePaths_IsolatesProfilesAndGeneratesDeterministicMachineIds()
    {
        var directorPaths = new LocalStoragePaths("director1", _tempRoot);
        var directorPathsAgain = new LocalStoragePaths("director1", _tempRoot);
        var accountantPaths = new LocalStoragePaths("accountant1", _tempRoot);

        Assert.Equal(directorPaths.MachineId, directorPathsAgain.MachineId);
        Assert.NotEqual(directorPaths.MachineId, accountantPaths.MachineId);
        Assert.StartsWith("EZ-", directorPaths.MachineId);
        Assert.NotEqual(directorPaths.ProfileRoot, accountantPaths.ProfileRoot);
        Assert.True(Directory.Exists(directorPaths.DataDirectory));
        Assert.True(Directory.Exists(directorPaths.KeysDirectory));
        Assert.True(Directory.Exists(Path.Combine(directorPaths.VaultDirectory, "Accounting", "Invoices")));
        Assert.True(Directory.Exists(Path.Combine(directorPaths.VaultDirectory, "Legal", "Contracts")));
        Assert.True(Directory.Exists(Path.Combine(directorPaths.VaultDirectory, "Office")));
    }

    [Fact]
    public void ParseProfileFromArgs_HandlesEqualsAndSpaceForms()
    {
        Assert.Equal("director2", LocalStoragePaths.ParseProfileFromArgs(["--profile=Director2"]));
        Assert.Equal("legal1", LocalStoragePaths.ParseProfileFromArgs(["--profile", "legal1"]));
        Assert.Equal("default", LocalStoragePaths.ParseProfileFromArgs([]));
    }

    [Fact]
    public void InMemoryEventBus_PublishesAndUnsubscribesCleanly()
    {
        var bus = new InMemoryEventBus();
        int count = 0;

        using (bus.Subscribe<InternalNoticeEvent>(_ => count++))
        {
            bus.Publish(new InternalNoticeEvent(Guid.NewGuid(), "admin", "Title", "Body", DateTimeOffset.UtcNow));
            Assert.Equal(1, count);
        }

        bus.Publish(new InternalNoticeEvent(Guid.NewGuid(), "admin", "Title2", "Body2", DateTimeOffset.UtcNow));
        Assert.Equal(1, count);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
