using System.Runtime.InteropServices;
using System.Windows;
using EzCompany.App.Services;
using EzCompany.App.ViewModels;
using EzCompany.App.Views;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Crypto;
using EzCompany.Core.Data;
using EzCompany.Core.LanAgent;
using EzCompany.Core.Telemetry;
using EzCompany.Core.Updater;
using EzCompany.Modules.Accounting;
using EzCompany.Modules.Legal;
using EzCompany.Modules.Office;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Velopack;

namespace EzCompany.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        VelopackApp.Build().Run();

        string profile = LocalStoragePaths.ParseProfileFromArgs(args);
        var storagePaths = new LocalStoragePaths(profile);
        using var fileLoggerProvider = new RollingFileLoggerProvider(storagePaths);

        using IHost host = CreateHostBuilder(args, storagePaths, fileLoggerProvider).Build();
        ILogger startupLogger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("EzCompany.Startup");

        AppDomain.CurrentDomain.UnhandledException += (_, evt) =>
        {
            startupLogger.LogCritical(
                evt.ExceptionObject as Exception,
                "Unhandled AppDomain exception (IsTerminating={IsTerminating}).",
                evt.IsTerminating);
            fileLoggerProvider.Flush();
        };

        TaskScheduler.UnobservedTaskException += (_, evt) =>
        {
            startupLogger.LogError(evt.Exception, "Unobserved background task exception.");
            fileLoggerProvider.Flush();
        };

        startupLogger.LogInformation(
            "Starting Ez Company | Profile={Profile} | MachineId={MachineId} | OS={OS} | Runtime={Runtime} | TelemetryDebug={TelemetryDebug} | LogFile={LogFile}",
            storagePaths.ProfileName,
            storagePaths.MachineId,
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription,
            Environment.GetEnvironmentVariable("EZCOMPANY_TELEMETRY_DEBUG") ?? "0",
            fileLoggerProvider.CurrentLogFilePath);

        SafeMigrationResult migration = host.Services
            .GetRequiredService<SafeMigrationService>()
            .MigrateAllSafelyAsync()
            .GetAwaiter()
            .GetResult();

        startupLogger.LogInformation(
            "SafeMigration completed: Succeeded={Succeeded}, RolledBack={RolledBack}, Message={Message}",
            migration.Succeeded,
            migration.RolledBack,
            migration.Message);

        if (!migration.Succeeded)
        {
            fileLoggerProvider.Flush();
            MessageBox.Show(
                $"{migration.Message}\n\nChi tiết nhật ký: {fileLoggerProvider.CurrentLogFilePath}",
                "Ez Company — SafeMigration Rollback",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return 1;
        }

        host.StartAsync().GetAwaiter().GetResult();
        foreach (IAppModule mod in host.Services.GetServices<IAppModule>())
        {
            mod.InitializeAsync().GetAwaiter().GetResult();
            startupLogger.LogInformation("Loaded module {ModuleId} v{Version}", mod.Metadata.ModuleId, mod.Metadata.Version);
        }

        try
        {
            var app = new App();
            app.InitializeComponent();
            app.ShutdownMode = ShutdownMode.OnLastWindowClose;

            app.DispatcherUnhandledException += (_, evt) =>
            {
                startupLogger.LogError(evt.Exception, "Unhandled WPF Dispatcher exception.");
                fileLoggerProvider.Flush();
                MessageBox.Show(
                    $"Đã xảy ra lỗi giao diện: {evt.Exception.Message}\n\nXem chi tiết log tại:\n{fileLoggerProvider.CurrentLogFilePath}",
                    "Ez Company — Lỗi không mong muốn",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                evt.Handled = true;
            };

            ThemeService theme = host.Services.GetRequiredService<ThemeService>();
            theme.ApplyTheme(true);

            MainWindow mainWindow = host.Services.GetRequiredService<MainWindow>();
            app.MainWindow = mainWindow;
            mainWindow.Show();

            int exitCode = app.Run();
            startupLogger.LogInformation("Ez Company exited cleanly with code {ExitCode}.", exitCode);
            return exitCode;
        }
        finally
        {
            host.StopAsync().GetAwaiter().GetResult();
            fileLoggerProvider.Flush();
        }
    }

    public static IHostBuilder CreateHostBuilder(
        string[] args,
        ILocalStoragePaths storagePaths,
        RollingFileLoggerProvider? fileLoggerProvider = null) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
                if (fileLoggerProvider is not null)
                {
                    logging.AddProvider(fileLoggerProvider);
                    logging.SetMinimumLevel(fileLoggerProvider.MinimumLevel);
                }
                else
                {
                    logging.SetMinimumLevel(LogLevel.Information);
                }

                logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
                logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton(storagePaths);
                if (fileLoggerProvider is not null)
                {
                    services.AddSingleton(fileLoggerProvider);
                }

                services.AddSingleton<IEventBus, InMemoryEventBus>();
                services.AddSingleton<ISessionContext, SessionContext>();
                services.AddEzCrypto();
                services.AddEzData(storagePaths);
                services.AddEzLanAgent();
                services.AddEzTelemetry();
                services.AddEzUpdater(context.Configuration);

                RegisterModule(services, new AccountingModule());
                RegisterModule(services, new LegalModule());
                RegisterModule(services, new OfficeModule());

                services.AddSingleton<ThemeService>();
                services.AddSingleton<ReadOnlyDbInspectorService>();
                services.AddTransient<MainWindowViewModel>();
                services.AddTransient<MainWindow>();
                services.AddTransient<WorkspaceWindowViewModel>();
                services.AddTransient<WorkspaceWindow>();
                services.AddTransient<ExecutiveDashboardViewModel>();
                services.AddTransient<ExecutiveDashboardView>();
                services.AddTransient<DbInspectorViewModel>();
                services.AddTransient<DbInspectorView>();
                services.AddTransient<UpdatesSettingsViewModel>();
                services.AddTransient<UpdatesSettingsView>();
            });

    private static void RegisterModule(IServiceCollection services, IAppModule module)
    {
        services.AddSingleton<IAppModule>(module);
        module.RegisterServices(services);
    }
}
