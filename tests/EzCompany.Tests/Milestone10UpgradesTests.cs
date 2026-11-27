using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using EzCompany.App.Controls;
using EzCompany.App.Services;
using EzCompany.App.ViewModels;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Crypto;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent;
using EzCompany.Core.LanAgent.Collaboration;
using EzCompany.Core.LanAgent.Models;
using EzCompany.Core.LanAgent.Pairing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EzCompany.Tests;

[Collection("WpfUiSta")]
public sealed class Milestone10UpgradesTests : IAsyncLifetime
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "EzCompanyTests_M10_" + Guid.NewGuid().ToString("N"));
    private readonly List<IAsyncDisposable> _asyncDisposables = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (IAsyncDisposable item in _asyncDisposables)
        {
            await item.DisposeAsync();
        }

        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempRoot))
        {
            try
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void Palette_AtZeroTransparency_ForcesFullyOpaqueSurfaceAndPanelAcrossMaterials()
    {
        foreach (string backdrop in new[] { "None", "Blur", "Acrylic", "Mica" })
        {
            var settings = new AppearanceSettings
            {
                WindowOpacity = 1.0,
                BackdropType = backdrop
            };

            Assert.True(AppearancePaletteBuilder.IsSolid(settings));
            AppearancePalette palette = AppearancePaletteBuilder.Build(settings);
            Assert.Equal(255, palette.WindowSurface.A);
            Assert.Equal(255, palette.Panel.A);
        }

        AppearancePalette blurAt30 = AppearancePaletteBuilder.Build(new AppearanceSettings
        {
            WindowOpacity = 0.30,
            BackdropType = "Blur"
        });
        Assert.InRange(blurAt30.WindowSurface.A, (byte)37, (byte)39);
    }

    [Fact]
    public void WindowTransparencyHelper_ForceOpaque_SetsOpaqueCompositionBackgroundAndDescribesSolid()
    {
        Exception? threadEx = null;
        string material = string.Empty;
        Color compBg = default;
        byte winBgAlpha = 0;

        Thread sta = new(() =>
        {
            try
            {
                var window = new EzFluentWindow
                {
                    Width = 400,
                    Height = 300,
                    Left = -3200,
                    Top = -3200,
                    ShowInTaskbar = false,
                    ShowActivated = false
                };

                window.Show();

                AppearancePalette palette = AppearancePaletteBuilder.Build(new AppearanceSettings
                {
                    IsDarkMode = true,
                    WindowOpacity = 1.0,
                    BackdropType = "Mica"
                });

                material = WindowTransparencyHelper.Apply(window, palette, "Mica", isDarkMode: true, forceOpaque: true);
                winBgAlpha = ((SolidColorBrush)window.Background).Color.A;

                nint hwnd = new WindowInteropHelper(window).Handle;
                HwndSource? hs = HwndSource.FromHwnd(hwnd);
                Assert.NotNull(hs?.CompositionTarget);
                compBg = hs!.CompositionTarget!.BackgroundColor;

                window.Close();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });

        sta.SetApartmentState(ApartmentState.STA);
        sta.Start();
        sta.Join();

        Assert.Null(threadEx);
        Assert.Equal(255, compBg.A);
        Assert.Equal(255, winBgAlpha);
        Assert.Contains("Đặc", material, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChatConnectionState_ComputesNetworkAndLanStatusesAccurately()
    {
        (bool c1, string t1) = ChatWorkspaceViewModel.ComputeConnectionState(false, true, 2, 3);
        Assert.False(c1);
        Assert.Contains("mạng", t1, StringComparison.OrdinalIgnoreCase);

        (bool c2, string _) = ChatWorkspaceViewModel.ComputeConnectionState(true, false, 2, 3);
        Assert.False(c2);

        (bool c3, string _) = ChatWorkspaceViewModel.ComputeConnectionState(true, true, 0, 3);
        Assert.False(c3);

        (bool c4, string t4) = ChatWorkspaceViewModel.ComputeConnectionState(true, true, 2, 3);
        Assert.True(c4);
        Assert.Contains("2/3", t4, StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotViewerStatics_ClampZoomComputeFitAndSanitizeExportFileName()
    {
        Assert.Equal(10.0, SnapshotViewerViewModel.ClampZoom(5.0));
        Assert.Equal(800.0, SnapshotViewerViewModel.ClampZoom(900.0));
        Assert.Equal(125.0, SnapshotViewerViewModel.ZoomStepIn(100.0));
        Assert.Equal(100.0, SnapshotViewerViewModel.ZoomStepOut(125.0));
        Assert.Equal(50.0, SnapshotViewerViewModel.ComputeFitZoom(640.0, 360.0, 1280.0, 720.0));
        Assert.Equal(100.0, SnapshotViewerViewModel.ComputeFitZoom(2000.0, 2000.0, 100.0, 100.0));

        string fileName = SnapshotViewerViewModel.BuildExportFileName(new SnapshotTimelineItem
        {
            Id = Guid.NewGuid(),
            MachineId = "M1",
            Username = "a/b:c",
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Caption = "cap"
        });

        Assert.EndsWith(".jpg", fileName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/", fileName);
        Assert.DoesNotContain(":", fileName);
        Assert.DoesNotContain(fileName, ch => Path.GetInvalidFileNameChars().Contains(ch));
    }

    [Fact]
    public async Task HrAttendance_AllowsUpdatingAlreadyCheckedDayAndReturnsNullForUncheckedDay()
    {
        (ServiceProvider dirSp, _, _) = await StartNodeAsync("dir_att_edit", isDirector: true);
        var hr = dirSp.GetRequiredService<HrAttendanceService>();

        AttendanceRecordEntity initial = await hr.CheckInTodayAsync();
        DateOnly today = DateOnly.FromDateTime(DateTime.Now);

        AttendanceRecordEntity updated = await hr.SaveCustomAttendanceAsync(
            initial.EmployeeProfileId,
            today,
            new TimeSpan(8, 0, 0),
            new TimeSpan(17, 0, 0),
            "Present",
            1.0,
            0.0,
            "sửa");

        AttendanceRecordEntity? fetched = await hr.GetAttendanceRecordForDayAsync(initial.EmployeeProfileId, today);
        Assert.NotNull(fetched);
        Assert.Equal(initial.Id, fetched!.Id);
        Assert.True(fetched.IsManualEntry);
        Assert.Equal(9.0, fetched.WorkedHours);
        Assert.Equal("sửa", fetched.Notes);
        Assert.Equal(updated.Id, fetched.Id);

        AttendanceRecordEntity? missing = await hr.GetAttendanceRecordForDayAsync(initial.EmployeeProfileId, today.AddDays(-10));
        Assert.Null(missing);
    }

    [Fact]
    public async Task InternalChat_UnpinTaskFromConversation_SyncsClearedPinToPeer()
    {
        (ServiceProvider dirSp, _, string dirEndpoint) = await StartNodeAsync("dir_unpin", isDirector: true);
        (ServiceProvider empSp, _, _) = await StartNodeAsync("emp_unpin", isDirector: false);

        await PairNodesAsync(dirSp, dirEndpoint, empSp, "Nhân viên Unpin", "empunpin", "Pass123!", CompanyRoles.Staff);

        string empMachine = empSp.GetRequiredService<ILocalStoragePaths>().MachineId;
        var dirChat = dirSp.GetRequiredService<ChatService>();
        var empChat = empSp.GetRequiredService<ChatService>();

        WorkTaskEntity task = await dirSp.GetRequiredService<WorkTaskService>().CreateAssignedTaskAsync(
            empMachine,
            "empunpin",
            "Nhân viên Unpin",
            CompanyRoles.Staff,
            "Kiểm tra báo cáo",
            "Nội dung chi tiết",
            WorkTaskPriorities.Normal,
            null);

        ChatConversationEntity dmDir = await dirChat.GetOrCreateDirectConversationAsync(empMachine, "Nhân viên Unpin");
        await dirChat.PinTaskToConversationAsync(dmDir.Id, task.Id, [empMachine]);

        int firstPull = await empSp.GetRequiredService<LanCollaborationClient>().PullChatHistoryFromPeerAsync(dirEndpoint);
        Assert.True(firstPull >= 1);
        ChatConversationEntity empPinned = Assert.Single(await empChat.GetConversationsAsync(), c => c.Id == dmDir.Id);
        Assert.Equal(task.Id, empPinned.PinnedTaskId);

        await dirChat.UnpinTaskFromConversationAsync(dmDir.Id, [empMachine]);
        int secondPull = await empSp.GetRequiredService<LanCollaborationClient>().PullChatHistoryFromPeerAsync(dirEndpoint);
        Assert.True(secondPull >= 1);

        ChatConversationEntity empUnpinned = Assert.Single(await empChat.GetConversationsAsync(), c => c.Id == dmDir.Id);
        Assert.Null(empUnpinned.PinnedTaskId);
        Assert.True(string.IsNullOrEmpty(empUnpinned.PinnedTaskSummary));

        IReadOnlyList<ChatMessageEntity> empMsgs = await empChat.GetMessagesAsync(dmDir.Id);
        Assert.Contains(empMsgs, m => string.Equals(m.MessageType, "UnpinnedTask", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<(ServiceProvider Sp, LanAgentHost Host, string Endpoint)> StartNodeAsync(string profile, bool isDirector)
    {
        var paths = new LocalStoragePaths(profile, _tempRoot);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ILocalStoragePaths>(paths);
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton<ISessionContext, SessionContext>();
        services.AddEzCrypto();
        services.AddEzData(paths);
        services.AddEzLanAgent(bindLoopbackOnly: true);

        ServiceProvider sp = services.BuildServiceProvider();
        _asyncDisposables.Add(sp);

        await using (AppDbContext appDb = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            await appDb.Database.MigrateAsync();
        }

        if (isDirector)
        {
            string recoveryPath = Path.Combine(_tempRoot, $"{profile}_master.recovery");
            await sp.GetRequiredService<GenesisBootstrapService>().InitializeGenesisDirectorAsync(
                "Ez Upgrade M10 Corp",
                "0319998881",
                "Giám đốc Gốc",
                "director",
                "AdminPass123!",
                "RecoveryPass123!",
                recoveryPath);
        }

        var host = sp.GetRequiredService<LanAgentHost>();
        await host.StartAsync(CancellationToken.None);
        return (sp, host, $"http://127.0.0.1:{host.BoundPort}");
    }

    private static async Task PairNodesAsync(
        ServiceProvider dirSp,
        string dirEndpoint,
        ServiceProvider empSp,
        string fullName,
        string username,
        string localPassword,
        string role)
    {
        var pairingMgr = dirSp.GetRequiredService<PairingSessionManager>();
        pairingMgr.OpenPairingWindow(TimeSpan.FromMinutes(2));

        var empClient = empSp.GetRequiredService<LanPairingClient>();
        StartedPairingSession started = await empClient.InitiatePairingAsync(dirEndpoint, fullName, username);

        IReadOnlyList<string> mods = CompanyRoles.GetDefaultModulesForRole(role);
        await pairingMgr.ApproveRequestAsync(started.RequestId, role, mods, "0.5.0", "stable", "director");

        PairingStatusResponse final = await empClient.WaitAndCompleteOnboardingAsync(
            dirEndpoint,
            started,
            fullName,
            username,
            localPassword,
            TimeSpan.FromSeconds(10));

        Assert.Equal(PairingRequestState.Approved, final.State);
    }
}
