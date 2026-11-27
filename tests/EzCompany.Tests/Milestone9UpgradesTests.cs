using System.IO;
using EzCompany.App.Services;
using EzCompany.App.ViewModels;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Crypto;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent;
using EzCompany.Core.Telemetry;
using EzCompany.Core.Updater;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EzCompany.Tests;

public sealed class Milestone9UpgradesTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "EzCompanyTests_M9_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void TimelineTrackLayoutEngine_RendersEmptyParallelTracksAndToggles3To5AndZoom()
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);

        TimelineLayoutResult defaultEmpty = TimelineTrackLayoutEngine.BuildLayout(
            [],
            today,
            TimelineZoomMode.Normal30Days,
            TimelineTrackLayoutEngine.DefaultTrackCount,
            viewportWidth: 900);

        Assert.Equal(3, defaultEmpty.Tracks.Count);
        Assert.Equal(30, defaultEmpty.TotalDays);
        Assert.NotEmpty(defaultEmpty.Ticks);
        Assert.NotNull(defaultEmpty.TodayX);
        Assert.All(defaultEmpty.Tracks, t => Assert.Empty(t.Bars));

        TimelineLayoutResult expanded5 = TimelineTrackLayoutEngine.BuildLayout(
            [],
            today,
            TimelineZoomMode.Overview60Days,
            TimelineTrackLayoutEngine.ExpandedTrackCount,
            viewportWidth: 900);

        Assert.Equal(5, expanded5.Tracks.Count);
        Assert.Equal(60, expanded5.TotalDays);

        TimelineLayoutResult detailed14 = TimelineTrackLayoutEngine.BuildLayout(
            [],
            today,
            TimelineZoomMode.Detailed14Days,
            TimelineTrackLayoutEngine.DefaultTrackCount,
            viewportWidth: 900);

        Assert.Equal(14, detailed14.TotalDays);
    }

    [Fact]
    public void TimelineTrackLayoutEngine_PacksOverlappingPlansAcrossParallelTracksAndReportsOverflow()
    {
        DateOnly anchor = DateOnly.FromDateTime(DateTime.Today);
        DateTimeOffset start = new DateTimeOffset(anchor.ToDateTime(TimeOnly.MinValue), DateTimeOffset.Now.Offset);
        DateTimeOffset end = start.AddDays(4);

        TimelinePlanEntity MakePlan(string title, string category) => new()
        {
            Id = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(),
            Title = title,
            Category = category,
            Status = "Planned",
            StartDateUtc = start,
            EndDateUtc = end,
            CreatedByFullName = "Tester"
        };

        TimelinePlanEntity[] overlapping =
        [
            MakePlan("Kế hoạch 1", TimelineCategories.Plan),
            MakePlan("Mốc 2", TimelineCategories.Milestone),
            MakePlan("Họp 3", TimelineCategories.Meeting),
            MakePlan("Hạn chót 4", TimelineCategories.Deadline)
        ];

        TimelineLayoutResult layout3 = TimelineTrackLayoutEngine.BuildLayout(
            overlapping,
            anchor,
            TimelineZoomMode.Normal30Days,
            TimelineTrackLayoutEngine.DefaultTrackCount,
            900);

        Assert.Equal(3, layout3.Tracks.Count);
        Assert.Equal(1, layout3.OverflowPlanCount);
        Assert.Single(layout3.Tracks[0].Bars);
        Assert.Single(layout3.Tracks[1].Bars);
        Assert.Single(layout3.Tracks[2].Bars);

        TimelineLayoutResult layout5 = TimelineTrackLayoutEngine.BuildLayout(
            overlapping,
            anchor,
            TimelineZoomMode.Normal30Days,
            TimelineTrackLayoutEngine.ExpandedTrackCount,
            900);

        Assert.Equal(5, layout5.Tracks.Count);
        Assert.Equal(0, layout5.OverflowPlanCount);
        Assert.Equal(4, layout5.Tracks.Sum(t => t.Bars.Count));
    }

    [Fact]
    public void ChatDockGeometry_DetectsDragOverlapForAutomaticRedock()
    {
        var host = new ScreenRect(100, 100, 1100, 800);

        // Far outside
        Assert.False(ChatDockGeometry.ShouldRedockToMainWindow(new ScreenRect(1200, 100, 1800, 700), host));

        // Center dropped inside host
        Assert.True(ChatDockGeometry.ShouldRedockToMainWindow(new ScreenRect(300, 200, 900, 700), host));

        // Area >= 25% overlapping (top-left overlaps 300x500 of 600x500 = 50% even if center is outside? Here 800..1100 is 300 wide inside 800..1400)
        var partial50 = new ScreenRect(800, 200, 1400, 700); // center 1100 == Right edge, inter width = 300/600 = 50%
        Assert.True(ChatDockGeometry.ShouldRedockToMainWindow(partial50, host));

        // Tiny <10% edge graze (1050..1650 -> inter width 50/600 = 8.3%)
        var tinyGraze = new ScreenRect(1050, 200, 1650, 700);
        Assert.False(ChatDockGeometry.ShouldRedockToMainWindow(tinyGraze, host));
    }

    [Fact]
    public void ChatPeerSearch_MatchesVietnameseNameAndBilingualRoleAccentInsensitive()
    {
        var peers = new[]
        {
            new ChatPeerPresenceItem
            {
                MachineId = "EZ-1",
                Username = "chau",
                FullName = "Phạm Minh Châu",
                ShortName = "Châu",
                RoleRaw = "Accounting",
                RoleBilingual = CompanyRoles.FormatRolesBilingual("Accounting"),
                IsOnline = true
            },
            new ChatPeerPresenceItem
            {
                MachineId = "EZ-2",
                Username = "linh",
                FullName = "Trần Thùy Linh",
                ShortName = "Linh",
                RoleRaw = "Legal",
                RoleBilingual = CompanyRoles.FormatRolesBilingual("Legal"),
                IsOnline = false
            }
        };

        IReadOnlyList<ChatPeerPresenceItem> byName = ChatWorkspaceViewModel.FilterPeersByQuery(peers, "chau");
        Assert.Single(byName);
        Assert.Equal("EZ-1", byName[0].MachineId);

        IReadOnlyList<ChatPeerPresenceItem> byRoleVi = ChatWorkspaceViewModel.FilterPeersByQuery(peers, "ke toan");
        Assert.Single(byRoleVi);
        Assert.Equal("EZ-1", byRoleVi[0].MachineId);

        IReadOnlyList<ChatPeerPresenceItem> byRoleEn = ChatWorkspaceViewModel.FilterPeersByQuery(peers, "legal");
        Assert.Single(byRoleEn);
        Assert.Equal("EZ-2", byRoleEn[0].MachineId);
    }

    [Fact]
    public void WindowTransparencyPercent_SyncsTwoWayWithWindowOpacityAndClampsSafely()
    {
        var paths = new LocalStoragePaths("transp_pct", _tempRoot);
        var services = new ServiceCollection();
        IConfiguration config = new ConfigurationBuilder().Build();
        services.AddLogging();
        services.AddSingleton<ILocalStoragePaths>(paths);
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton<ISessionContext, SessionContext>();
        services.AddEzCrypto();
        services.AddEzData(paths);
        services.AddEzLanAgent(bindLoopbackOnly: true);
        services.AddEzTelemetry();
        services.AddEzUpdater(config);
        services.AddSingleton<AppearanceSettingsService>();
        services.AddSingleton<AppDiagnosticsAndRepairService>();
        services.AddTransient<UpdatesSettingsViewModel>();
        services.AddTransient<AccountAndAppearanceViewModel>();

        using ServiceProvider sp = services.BuildServiceProvider();
        AccountAndAppearanceViewModel vm = sp.GetRequiredService<AccountAndAppearanceViewModel>();

        vm.WindowTransparencyPercent = 0;
        Assert.Equal(1.00, vm.WindowOpacity);
        Assert.Equal(100, vm.WindowOpacityPercent);

        vm.WindowTransparencyPercent = 30;
        Assert.Equal(0.70, vm.WindowOpacity);
        Assert.Equal(70, vm.WindowOpacityPercent);

        vm.WindowTransparencyPercent = 70;
        Assert.Equal(0.30, vm.WindowOpacity);

        vm.WindowTransparencyInput = "95";
        vm.ApplyWindowTransparencyInputCommand.Execute(null);
        Assert.Equal(70, vm.WindowTransparencyPercent);
        Assert.Equal(0.30, vm.WindowOpacity);

        vm.WindowTransparencyInput = "-10";
        vm.ApplyWindowTransparencyInputCommand.Execute(null);
        Assert.Equal(0, vm.WindowTransparencyPercent);
        Assert.Equal(1.00, vm.WindowOpacity);
    }
}
