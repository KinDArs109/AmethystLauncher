using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Launcher.App.Services;
using Launcher.App.ViewModels;
using Launcher.App.Views.Pages;
using Launcher.Auth;
using Launcher.Backend;
using Launcher.Core;
using Launcher.Loaders;
using Launcher.ModSources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Velopack;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Launcher.App;

public partial class App
{
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MinecraftLauncher",
        "logs");

    private readonly IHost _host = Host.CreateDefaultBuilder()
        .UseSerilog((_, services, configuration) => configuration
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.Debug()
            .WriteTo.File(
                Path.Combine(LogDirectory, "launcher-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14))
        .ConfigureServices((_, services) =>
        {
            services.AddLauncherCore();
            services.AddLauncherAuth();
            services.AddLauncherBackend();
            services.AddLauncherLoaders();
            services.AddLauncherModSources();

            services.AddSingleton<Services.IActiveAccountResolver, Services.ActiveAccountResolver>();
            services.AddSingleton<Services.IDownloadCenter, Services.DownloadCenter>();
            services.AddSingleton<Services.IInstanceLibrary, Services.InstanceLibrary>();
            services.AddSingleton<Services.IInstanceVersionResolver, Services.InstanceVersionResolver>();
            services.AddSingleton<Services.ISelectedInstanceContext, Services.SelectedInstanceContext>();
            services.AddSingleton<Services.IInstanceFileService, Services.InstanceFileService>();
            services.AddSingleton<Services.IInstanceLogHistoryService, Services.InstanceLogHistoryService>();
            services.AddSingleton<Services.IModpackInstaller, Services.ModpackInstaller>();
            services.AddSingleton<Services.IInstanceLaunchService, Services.InstanceLaunchService>();
            services.AddSingleton<Services.PresenceService>();
            services.AddSingleton<Services.ISkinModInstaller, Services.SkinModInstaller>();
            services.AddSingleton<Services.IBypassService, Services.BypassService>();
            services.AddSingleton<Services.IUpdateService, Services.UpdateService>();
            services.AddHostedService(sp => sp.GetRequiredService<Services.PresenceService>());
            services.AddTransient<CreateInstanceViewModel>();
            services.AddTransient<InstallToInstanceViewModel>();

            services.AddSingleton<INavigationViewPageProvider, PageService>();
            services.AddSingleton<INavigationService, NavigationService>();
            services.AddSingleton<ISnackbarService, SnackbarService>();
            services.AddSingleton<IContentDialogService, ContentDialogService>();

            services.AddSingleton<INavigationWindow, MainWindow>();
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<FriendsPanelViewModel>();

            services.AddHostedService<ApplicationHostService>();

            services.AddTransient<HomePage>();
            services.AddTransient<HomeViewModel>();
            services.AddTransient<LibraryPage>();
            services.AddTransient<LibraryViewModel>();
            services.AddTransient<InstanceDetailPage>();
            services.AddTransient<InstanceDetailViewModel>();
            services.AddTransient<ModsPage>();
            services.AddTransient<ModsViewModel>();
            services.AddTransient<ModpackBuilderPage>();
            services.AddTransient<ModpackBuilderViewModel>();
            services.AddTransient<NewsPage>();
            services.AddTransient<NewsViewModel>();
            services.AddTransient<SupportPage>();
            services.AddTransient<SupportViewModel>();
            services.AddTransient<AccountsPage>();
            services.AddTransient<AccountsViewModel>();
            services.AddTransient<SettingsPage>();
            services.AddTransient<SettingsViewModel>();
        })
        .Build();

    // Two processes writing instances.json at the same time (e.g. the launcher started twice) can
    // race each other even with the file-level locking in InstanceManager, since that locking is only
    // in-process. A named mutex is the simplest way to guarantee only one process ever touches the
    // instance index at all.
    private Mutex? _singleInstanceMutex;

    public App()
    {
        // Must run before any UI: when the process is launched by the Velopack installer/updater with
        // its hook arguments, this handles install/update/uninstall and exits. In a normal launch it
        // returns immediately.
        VelopackApp.Build().Run();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    /// <summary>Resolves a service from the host container; used where DI can't reach (e.g. converters).</summary>
    public static T GetService<T>() where T : class =>
        ((App)Current)._host.Services.GetRequiredService<T>();

    protected override async void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, "Local\\MinecraftLauncher.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show(
                "Лаунчер уже запущен. Проверьте панель задач.",
                "Amethyst Launcher",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            Shutdown();
            return;
        }

        ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.None, updateAccent: false);
        ApplicationAccentColorManager.Apply(
            systemAccent: (Color)ColorConverter.ConvertFromString("#7C3AED"),
            primaryAccent: (Color)ColorConverter.ConvertFromString("#7C3AED"),
            secondaryAccent: (Color)ColorConverter.ConvertFromString("#9F67F5"),
            tertiaryAccent: (Color)ColorConverter.ConvertFromString("#5B21B6"));

        // Show the splash immediately, run the update check on it, then hand off to the main window.
        var splash = new Views.SplashWindow();
        splash.Show();

        await CheckForUpdatesAsync(splash);

        splash.SetStatus("Запуск лаунчера...");
        await _host.StartAsync();
        base.OnStartup(e);

        // The splash was the first window shown, so WPF made it Application.MainWindow; repoint that at
        // the real window before retiring the splash so dialogs/owners resolve correctly.
        if (_host.Services.GetRequiredService<INavigationWindow>() is Window mainWindow)
        {
            MainWindow = mainWindow;
        }

        splash.Close();
    }

    /// <summary>Runs the Velopack update check against GitHub Releases on the splash. If a newer release
    /// exists it is downloaded and applied (which restarts the process, so this never returns). Any
    /// failure is swallowed so a broken/offline update path can never stop the launcher from starting.</summary>
    private async Task CheckForUpdatesAsync(Views.SplashWindow splash)
    {
        try
        {
            var updateService = _host.Services.GetRequiredService<Services.IUpdateService>();
            if (!updateService.IsInstalled)
            {
                return;
            }

            splash.SetStatus("Проверка обновлений...");
            var update = await updateService.CheckAsync();
            if (update is null)
            {
                return;
            }

            splash.SetStatus($"Загрузка обновления {update.TargetFullRelease.Version}...");
            await updateService.DownloadAndApplyAsync(update, splash.SetProgress);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Update check on startup failed");
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        // Stop the DPI-bypass (if the user turned it on) so it doesn't keep running after the launcher
        // itself has closed. Once the scheduled tasks are registered this runs the elevated stop task
        // silently — no UAC prompt at exit. The BypassEnabled setting is untouched, so the next launch
        // re-arms it automatically if it was on.
        try
        {
            var bypassService = _host.Services.GetRequiredService<Services.IBypassService>();
            if (bypassService.IsRunning)
            {
                await bypassService.StopAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to stop the bypass on exit");
        }

        await _host.StopAsync();
        _host.Dispose();
        Log.CloseAndFlush();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    // Logging a crash here is a best-effort last resort; letting the app die afterwards would
    // just trade one crash for another, so we keep it alive and rely on the log for diagnosis.
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Unhandled UI-thread exception");
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            Log.Fatal(ex, "Unhandled exception (IsTerminating={IsTerminating})", e.IsTerminating);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }
}
