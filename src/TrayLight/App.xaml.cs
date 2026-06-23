using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using H.NotifyIcon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TrayLight.Services;
using TrayLight.Services.Actions;
using TrayLight.Services.Badges;
using TrayLight.Services.Logging;
using TrayLight.Services.Providers;
using TrayLight.Services.UserSettings;
using TrayLight.ViewModels;
using TrayLight.Views;

namespace TrayLight;

public partial class App : Application
{
    private const string MutexName = "Global\\TrayLight.SingleInstance.{8A0E1E2C-3333-4A1A-9A33-333333333333}";

    private Mutex? _singleInstanceMutex;
    private TaskbarIcon? _trayIcon;
    private ConfigurationService? _bootstrapConfig;
    private ILogger<App>? _appLogger;

    public IServiceProvider Services { get; private set; } = null!;

    public new static App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Single-instance enforcement
        _singleInstanceMutex = new Mutex(initiallyOwned: true, name: MutexName, out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);

        // Detect the Windows display language and align the process UI culture
        // before any window is created, so all localized strings resolve to the
        // matching language (German, French, or English fallback).
        LocalizationService.Initialize();

        // Bootstrap pass: load the config file once so the LoggerFactory can
        // honour the user's Logging settings before DI is built.
        _bootstrapConfig = new ConfigurationService();
        _bootstrapConfig.Load();

        // Allow admins to suppress auto-launch via policy without uninstalling
        // the app: setting Behavior\AutoStart = 0 makes TrayLight exit
        // immediately on startup. The MSI's HKLM Run key still launches the
        // process; this guard simply terminates it again.
        if (!_bootstrapConfig.Current.Behavior.AutoStart)
        {
            Shutdown();
            return;
        }

        var loggingConfig = _bootstrapConfig.Current.Logging;
        var loggerFactory = TrayLightLoggerFactory.Create(loggingConfig);

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(loggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        ConfigureServices(services);
        // Reuse the bootstrap-loaded config so we don't parse the file twice.
        services.AddSingleton<IConfigurationService>(_bootstrapConfig);
        Services = services.BuildServiceProvider();

        _appLogger = Services.GetRequiredService<ILogger<App>>();
        _bootstrapConfig.AttachLogger(Services.GetRequiredService<ILogger<ConfigurationService>>());

        // Inject loggers into providers resolved from the container so their
        // refresh / warning events flow through the structured pipeline.
        foreach (var provider in Services.GetServices<IInfoItemProvider>())
        {
            if (provider is InfoItemProviderBase baseProvider)
            {
                var providerLoggerType = typeof(ILogger<>).MakeGenericType(provider.GetType());
                if (Services.GetService(providerLoggerType) is ILogger providerLogger)
                    baseProvider.Logger = providerLogger;
            }
        }

        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        DispatcherUnhandledException             += OnDispatcherUnhandledException;

        _appLogger.LogInformation(LogEvents.AppStarted,
            "TrayLight started (config: {ConfigPath}).", _bootstrapConfig.ConfigPath);

        // Bootstrap config is already loaded; just start watching for changes.
        var configService = _bootstrapConfig;
        configService.StartWatching();

        _trayIcon = (TaskbarIcon)FindResource("AppTrayIcon")!;
        _trayIcon.DataContext = Services.GetRequiredService<TrayIconViewModel>();
        _trayIcon.ForceCreate();

        // Wire the balloon-notification service to the live tray icon so
        // action failures (and toast-style success messages) surface to the user.
        if (Services.GetRequiredService<INotificationService>() is TrayBalloonNotificationService balloon)
        {
            balloon.Attach(_trayIcon);
        }

        // Start the warning-aggregation pipeline and the throttled toast
        // forwarder, then kick off provider refresh loops so DataChanged
        // events start flowing into the badge service.
        Services.GetRequiredService<INotificationBadgeService>().Start();
        _ = Services.GetRequiredService<ToastWarningNotifier>(); // resolve to subscribe

        var refreshInterval = TimeSpan.FromMinutes(
            Math.Max(1, configService.Current.Behavior.RefreshIntervalMinutes));
        foreach (var provider in Services.GetServices<IInfoItemProvider>())
            provider.Start(refreshInterval);

        // App-level master refresh service. Owns its own timer that lives for
        // the entire process lifetime so popup open/close has no effect on
        // periodic refresh, and a wake-from-sleep triggers a catch-up cycle.
        Services.GetRequiredService<AppRefreshService>().Start();

        // First-launch welcome screen. The user-settings file lives in
        // %LOCALAPPDATA% so each Windows account gets its own "first run".
        // The ADMX policy Behavior\ShowWelcomeScreen always wins: when an admin
        // sets it to 0 (Disabled) the welcome screen is suppressed regardless of
        // the per-user "don't show again" state. When the policy is Enabled (1)
        // or Not Configured it falls back to the local user setting.
        var userSettings = Services.GetRequiredService<IUserSettingsService>();
        if (ShouldShowWelcome(configService.Current.Behavior.ShowWelcomeScreen,
                              userSettings.Current.WelcomeShown))
        {
            var welcome = Services.GetRequiredService<WelcomeWindow>();
            welcome.ShowDialog();
        }
    }

    /// <summary>
    /// Decides whether the first-launch welcome screen should be shown.
    /// Priority: the ADMX policy <c>Behavior\ShowWelcomeScreen</c> set to
    /// Disabled (false) suppresses the screen unconditionally; otherwise the
    /// per-user "don't show again" preference (<paramref name="welcomeShown"/>)
    /// decides.
    /// </summary>
    /// <param name="policyShowWelcome">
    /// Effective value of the ShowWelcomeScreen policy. False only when the
    /// policy is explicitly set to 0; true when Enabled or Not Configured.
    /// </param>
    /// <param name="welcomeShown">
    /// The per-user setting: true once the user dismissed the welcome screen
    /// with "don't show again".
    /// </param>
    internal static bool ShouldShowWelcome(bool policyShowWelcome, bool welcomeShown)
        => policyShowWelcome && !welcomeShown;

    private static void ConfigureServices(IServiceCollection services)
    {
        // Services (IConfigurationService is registered separately as the
        // bootstrap-loaded singleton so the registry is read only once).
        services.AddSingleton<ISystemInfoService, SystemInfoService>();
        services.AddSingleton<ISystemInfoDumpService, SystemInfoDumpService>();
        services.AddSingleton<IUserSettingsService, UserSettingsService>();
        services.AddSingleton<IPopupPositioningService, PopupPositioningService>();
        services.AddSingleton<IAutoStartService, AutoStartService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<ILogoService, LogoService>();

        // Info-tile providers (one singleton per known type).
        services.AddSingleton<IInfoItemProvider, ComputerNameProvider>();
        services.AddSingleton<IInfoItemProvider, OsVersionProvider>();
        services.AddSingleton<IInfoItemProvider, LastRebootProvider>();
        services.AddSingleton<IInfoItemProvider, StorageUsedProvider>();
        services.AddSingleton<IInfoItemProvider, NetworkInfoProvider>();
        services.AddSingleton<IInfoItemProvider, EntraIdStatusProvider>();
        services.AddSingleton<IInfoItemProvider, IntuneSyncProvider>();

        // App-level periodic refresh (timer rooted by this singleton).
        services.AddSingleton<AppRefreshService>();

        // Shortcut action handlers + executor (Strategy pattern).
        services.AddSingleton<IShortcutActionHandler, UrlActionHandler>();
        services.AddSingleton<IShortcutActionHandler, AppActionHandler>();
        services.AddSingleton<IShortcutActionHandler, CommandActionHandler>();
        services.AddSingleton<IConfirmationService, MessageBoxConfirmationService>();
        services.AddSingleton<IShortcutPlaceholderResolver, ShortcutPlaceholderResolver>();
        services.AddSingleton<TrayBalloonNotificationService>();
        services.AddSingleton<INotificationService>(sp => sp.GetRequiredService<TrayBalloonNotificationService>());
        services.AddSingleton<IActionExecutor, ActionExecutor>();

        // Notification badge pipeline. The tray icon itself is static (set
        // in XAML to the embedded app.ico) because H.NotifyIcon.Wpf has been
        // unstable when given dynamically-generated ImageSource objects.
        // Warning counts surface via the popup tiles and the tray tooltip.
        services.AddSingleton<INotificationBadgeService, NotificationBadgeService>();
        services.AddSingleton<ToastWarningNotifier>();

        // ViewModels
        services.AddSingleton<TrayIconViewModel>();
        services.AddTransient<TrayPopupViewModel>();
        services.AddTransient<WelcomeViewModel>();
        services.AddTransient<AboutViewModel>();

        // Views
        services.AddTransient<TrayPopupWindow>();
        services.AddTransient<WelcomeWindow>();
        services.AddTransient<AboutWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _appLogger?.LogInformation(LogEvents.AppStopped, "TrayLight stopped."); }
        catch { /* logging best effort */ }

        AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandledException;
        DispatcherUnhandledException             -= OnDispatcherUnhandledException;

        _trayIcon?.Dispose();
        (Services as IDisposable)?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        _appLogger?.LogCritical(LogEvents.UnhandledException,
            e.ExceptionObject as Exception,
            "Unhandled AppDomain exception (terminating: {IsTerminating}).", e.IsTerminating);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _appLogger?.LogCritical(LogEvents.UnhandledException, e.Exception,
            "Unhandled dispatcher exception.");
        // We do NOT mark e.Handled = true; let the default behaviour decide,
        // so a genuine crash still surfaces in development.
    }
}
