using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TrayLight.Services;
using TrayLight.Services.Badges;
using TrayLight.Services.Logging;
using TrayLight.Views;

namespace TrayLight.ViewModels;

public partial class TrayIconViewModel : ObservableObject, IDisposable
{
    // Pre-built resource URIs. These are plain WPF Resources embedded into
    // the TrayLight assembly via <Resource> in the .csproj — no runtime
    // compositing happens, so H.NotifyIcon.Wpf gets a BitmapImage with a
    // valid UriSource it can stream to a Win32 HICON.
    private static readonly Uri NormalIconUri = new(
        "pack://application:,,,/TrayLight;component/Assets/app-normal.ico",
        UriKind.Absolute);
    private static readonly Uri WarningIconUri = new(
        "pack://application:,,,/TrayLight;component/Assets/app-warning.ico",
        UriKind.Absolute);

    private static readonly ImageSource? NormalIcon  = TryLoadIcon(NormalIconUri);
    private static readonly ImageSource? WarningIcon = TryLoadIcon(WarningIconUri);

    /// <summary>Cache file for a tray icon downloaded from an ADMX URL.</summary>
    private static string CachedTrayIconPath =>
        Path.Combine(LogoService.CacheDirectory, "tray-icon.ico");

    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(10);

    private readonly IServiceProvider _services;
    private readonly IPopupPositioningService _positioning;
    private readonly INotificationBadgeService _badges;
    private readonly IConfigurationService _config;
    private readonly ILogger<TrayIconViewModel> _log;
    private readonly HttpClient _http;

    // Custom default-state icon resolved from Branding\TrayIcon (null = use
    // the embedded app-normal.ico). The warning state always uses the embedded
    // app-warning.ico.
    private ImageSource? _customNormalIcon;
    private string _currentTrayIconSource = string.Empty;

    private TrayPopupWindow? _popup;

    [ObservableProperty] private ImageSource? _iconSource = NormalIcon;
    [ObservableProperty] private string _toolTipText = "TrayLight";

    public TrayIconViewModel(
        IServiceProvider services,
        IPopupPositioningService positioning,
        INotificationBadgeService badges,
        IConfigurationService config,
        ILogger<TrayIconViewModel> log)
    {
        _services       = services;
        _positioning    = positioning;
        _badges         = badges;
        _config         = config;
        _log            = log;

        _http = new HttpClient { Timeout = HttpTimeout };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("TrayLight/1.0");

        // Resolve any policy-supplied custom icon before the first paint so the
        // tray shows the branded icon immediately when a local path is set.
        ApplyTrayIconFromConfig(_config.Current.Branding.TrayIcon);
        _config.PropertyChanged += OnConfigChanged;

        ApplyBadgeState(_badges.Current);
        _badges.BadgeChanged += (_, state) =>
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
                ApplyBadgeState(state);
            else
                dispatcher.BeginInvoke(() => ApplyBadgeState(state));
        };
    }

    private void ApplyBadgeState(BadgeState state)
    {
        try
        {
            // Warnings always use the embedded warning icon; the normal state
            // uses the policy-supplied custom icon when one is available.
            var target = state.HasWarnings ? WarningIcon : (_customNormalIcon ?? NormalIcon);
            if (target is not null)
                IconSource = target;
        }
        catch
        {
            // Never let a tray-icon swap crash the app — keep the previous icon.
        }

        // Rebuild the rich hover tooltip (computer name, IP, Intune, warnings)
        // off the UI thread so the registry / network reads never block it.
        _ = RefreshTooltipAsync(state);
    }

    /// <summary>
    /// Builds the multi-line tray tooltip on a background thread and assigns it
    /// on the UI thread. Any failure falls back to the plain product name.
    /// </summary>
    private async Task RefreshTooltipAsync(BadgeState state)
    {
        string tooltip;
        try
        {
            tooltip = await Task.Run(() => BuildTooltip(state)).ConfigureAwait(true);
        }
        catch
        {
            tooltip = "TrayLight";
        }
        ToolTipText = tooltip;
    }

    /// <summary>
    /// Composes the compact tray hover summary:
    ///   line 1: computer name
    ///   line 2: IPv4 of the physical adapter (see NetworkAdapterSelector)
    ///   line 3: last Intune sync ("Intune: 3m ago" / "Intune: Not enrolled")
    ///   line 4: warning count (only when warnings are active)
    /// All text is localized via the language resources.
    /// </summary>
    private static string BuildTooltip(BadgeState state)
    {
        var lines = new List<string>
        {
            // Line 1 — computer name.
            Environment.MachineName,
        };

        // Line 2 — IPv4 of the selected physical adapter (empty => offline).
        var selection = Services.Providers.NetworkAdapterSelector.SelectBest(
            Services.Providers.NetworkAdapterSelector.EnumerateLiveAdapters());
        lines.Add(string.IsNullOrEmpty(selection?.IPv4)
            ? Resources.Strings.StatusOffline
            : selection!.IPv4);

        // Line 3 — last Intune sync (mirrors the Intune tile).
        var (enrolled, lastSync) = TrayPopupViewModel.GetIntuneSummary();
        string intuneValue;
        if (!enrolled && lastSync is null)
            intuneValue = Resources.Strings.StatusNotEnrolled;
        else if (lastSync is { } t)
            intuneValue = TrayPopupViewModel.FormatRelative(DateTime.Now - t);
        else
            intuneValue = Resources.Strings.StatusUnknown;
        lines.Add(Resources.Strings.Format("TrayTooltipIntuneFormat", intuneValue));

        // Line 4 — warnings (only when active).
        if (state.HasWarnings && state.Count > 0)
        {
            lines.Add(Resources.Strings.Format(
                state.Count == 1 ? "TrayTooltipWarningSingularFormat" : "TrayTooltipWarningsFormat",
                state.Count));
        }

        return string.Join("\n", lines);
    }


    private void OnConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IConfigurationService.Current)) return;
        var newSource = _config.Current.Branding.TrayIcon ?? string.Empty;
        if (string.Equals(newSource, _currentTrayIconSource, StringComparison.OrdinalIgnoreCase))
            return;

        // The config poll fires on a background thread; marshal the icon swap
        // (and the BitmapImage load that backs it) onto the UI thread.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            ApplyTrayIconFromConfig(newSource);
        else
            dispatcher.BeginInvoke(() => ApplyTrayIconFromConfig(newSource));
    }

    /// <summary>
    /// Resolves the <c>Branding\TrayIcon</c> policy value into the custom
    /// default-state icon. Local paths are loaded directly; URLs are cached to
    /// <c>%ProgramData%\TrayLight\Cache\tray-icon.ico</c> and downloaded in the
    /// background. Any failure falls back to the embedded default icon.
    /// </summary>
    private void ApplyTrayIconFromConfig(string? source)
    {
        source ??= string.Empty;
        _currentTrayIconSource = source;

        try
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                _customNormalIcon = null;
            }
            else if (IsHttpUrl(source))
            {
                // Surface a cached copy immediately so the icon doesn't flicker
                // while the (re-)download runs in the background.
                _customNormalIcon = File.Exists(CachedTrayIconPath)
                    ? TryLoadIconFromFile(CachedTrayIconPath)
                    : null;
                _ = DownloadAndApplyAsync(source);
            }
            else
            {
                _customNormalIcon = File.Exists(source) ? TryLoadIconFromFile(source) : null;
                if (_customNormalIcon is null)
                    _log.LogWarning(
                        "Custom tray icon '{Path}' not found or could not be loaded; using default icon.",
                        source);
            }
        }
        catch (Exception ex)
        {
            _customNormalIcon = null;
            _log.LogWarning(ex,
                "Failed to apply custom tray icon '{Source}'; using default icon.", source);
        }

        ApplyBadgeState(_badges.Current);
    }

    private async Task DownloadAndApplyAsync(string url)
    {
        try
        {
            Directory.CreateDirectory(LogoService.CacheDirectory);

            using var response = await _http.GetAsync(url).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            // Write to a temp file then atomically replace, so a partial
            // download never overwrites the previous good icon.
            var tmp = CachedTrayIconPath + ".tmp";
            await using (var fs = File.Create(tmp))
                await response.Content.CopyToAsync(fs).ConfigureAwait(false);
            File.Move(tmp, CachedTrayIconPath, overwrite: true);

            void Apply()
            {
                var icon = TryLoadIconFromFile(CachedTrayIconPath);
                if (icon is null) return;
                _customNormalIcon = icon;
                ApplyBadgeState(_badges.Current);
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
                Apply();
            else
                dispatcher.BeginInvoke((Action)Apply);

            _log.LogInformation(LogEvents.ConfigLoaded,
                "Custom tray icon downloaded from {Url} to {Cache}.", url, CachedTrayIconPath);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "Failed to download custom tray icon from {Url}; using default icon.", url);
        }
    }

    private static bool IsHttpUrl(string source) =>
        source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private static ImageSource? TryLoadIconFromFile(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption   = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource     = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? TryLoadIcon(Uri uri)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource   = uri;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    [RelayCommand]
    private void ShowPopup()
    {
        if (_popup is { IsVisible: true })
        {
            _popup.Hide();
            return;
        }

        _popup ??= _services.GetRequiredService<TrayPopupWindow>();
        _positioning.PositionAboveTray(_popup);
        _popup.Show();
        _popup.Activate();
    }

    [RelayCommand]
    private void About()
    {
        var window = _services.GetRequiredService<AboutWindow>();
        window.ShowDialog();
    }

    [RelayCommand]
    private void Refresh()
    {
        _services.GetRequiredService<IConfigurationService>().Load();
    }

    [RelayCommand]
    private void Quit()
    {
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _config.PropertyChanged -= OnConfigChanged;
        _http.Dispose();
        GC.SuppressFinalize(this);
    }
}
