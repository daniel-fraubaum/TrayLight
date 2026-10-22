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

    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(10);

    private readonly IServiceProvider _services;
    private readonly IPopupPositioningService _positioning;
    private readonly INotificationBadgeService _badges;
    private readonly IConfigurationService _config;
    private readonly AppRefreshService _appRefresh;
    private readonly ILogger<TrayIconViewModel> _log;
    private readonly HttpClient _http;

    // Policy-supplied custom icons sharing one loader: Branding\TrayIcon
    // replaces the embedded app-normal.ico, Branding\TrayIconWarning replaces
    // app-warning.ico. A null Icon means "use the embedded default".
    private readonly CustomIconSlot _normalSlot  = new("TrayIcon", "tray-icon.ico");
    private readonly CustomIconSlot _warningSlot = new("TrayIconWarning", "tray-icon-warning.ico");

    private TrayPopupWindow? _popup;

    [ObservableProperty] private ImageSource? _iconSource = NormalIcon;
    [ObservableProperty] private string _toolTipText = "TrayLight";

    public TrayIconViewModel(
        IServiceProvider services,
        IPopupPositioningService positioning,
        INotificationBadgeService badges,
        IConfigurationService config,
        AppRefreshService appRefresh,
        ILogger<TrayIconViewModel> log)
    {
        _services       = services;
        _positioning    = positioning;
        _badges         = badges;
        _config         = config;
        _appRefresh     = appRefresh;
        _log            = log;

        _http = new HttpClient { Timeout = HttpTimeout };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("TrayLight/1.0");

        // Resolve any policy-supplied custom icons before the first paint so the
        // tray shows the branded icon immediately when a local path is set.
        ApplyTrayIconFromConfig(_normalSlot, _config.Current.Branding.TrayIcon);
        ApplyTrayIconFromConfig(_warningSlot, _config.Current.Branding.TrayIconWarning);
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

        // The hover tooltip embeds live values (IP, Intune sync time, warning
        // count) that change every cycle even when the badge state does not, so
        // it must be rebuilt on every AppRefreshService tick - not only on
        // BadgeChanged - otherwise it shows a stale sync time versus the tiles.
        _appRefresh.RefreshCompleted += OnRefreshCompleted;
    }

    private void OnRefreshCompleted(object? sender, DateTime completedAtUtc)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            _ = RefreshTooltipAsync(_badges.Current);
        else
            dispatcher.BeginInvoke(() => _ = RefreshTooltipAsync(_badges.Current));
    }

    private void ApplyBadgeState(BadgeState state)
    {
        try
        {
            // Each state uses its policy-supplied custom icon when one is
            // available, otherwise the embedded default.
            var target = state.HasWarnings
                ? (_warningSlot.Icon ?? WarningIcon)
                : (_normalSlot.Icon ?? NormalIcon);
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

        // Line 2 — IPv4 of the routing-active adapter (empty => offline).
        var selection = Services.Providers.NetworkAdapterSelector.SelectBest(
            Services.Providers.NetworkAdapterSelector.EnumerateLiveAdapters(),
            Services.Providers.NetworkAdapterSelector.GetRouteInterfaceIndex());
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
        var branding = _config.Current.Branding;
        ApplyIfChanged(_normalSlot, branding.TrayIcon);
        ApplyIfChanged(_warningSlot, branding.TrayIconWarning);
    }

    private void ApplyIfChanged(CustomIconSlot slot, string? source)
    {
        var newSource = source ?? string.Empty;
        if (string.Equals(newSource, slot.Source, StringComparison.OrdinalIgnoreCase))
            return;

        // The config poll fires on a background thread; marshal the icon swap
        // (and the BitmapImage load that backs it) onto the UI thread.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            ApplyTrayIconFromConfig(slot, newSource);
        else
            dispatcher.BeginInvoke(() => ApplyTrayIconFromConfig(slot, newSource));
    }

    /// <summary>
    /// Resolves a <c>Branding\TrayIcon</c> / <c>Branding\TrayIconWarning</c>
    /// policy value into the slot's custom icon. Local paths are loaded
    /// directly; URLs are cached to <c>%ProgramData%\TrayLight\Cache\</c>
    /// (<c>tray-icon.ico</c> / <c>tray-icon-warning.ico</c>) and downloaded in
    /// the background. Any failure falls back to the embedded default icon.
    /// </summary>
    private void ApplyTrayIconFromConfig(CustomIconSlot slot, string? source)
    {
        source ??= string.Empty;
        slot.Source = source;

        try
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                slot.Icon = null;
            }
            else if (IsHttpUrl(source))
            {
                // Surface a cached copy immediately so the icon doesn't flicker
                // while the (re-)download runs in the background.
                slot.Icon = File.Exists(slot.CachePath)
                    ? TryLoadIconFromFile(slot.CachePath)
                    : null;
                _ = DownloadAndApplyAsync(slot, source);
            }
            else
            {
                slot.Icon = File.Exists(source) ? TryLoadIconFromFile(source) : null;
                if (slot.Icon is null)
                    _log.LogWarning(
                        "Custom tray icon {Policy} '{Path}' not found or could not be loaded; using default icon.",
                        slot.PolicyName, source);
            }
        }
        catch (Exception ex)
        {
            slot.Icon = null;
            _log.LogWarning(ex,
                "Failed to apply custom tray icon {Policy} '{Source}'; using default icon.",
                slot.PolicyName, source);
        }

        ApplyBadgeState(_badges.Current);
    }

    private async Task DownloadAndApplyAsync(CustomIconSlot slot, string url)
    {
        var cachePath = slot.CachePath;
        try
        {
            Directory.CreateDirectory(LogoService.CacheDirectory);

            using var response = await _http.GetAsync(url).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            // Write to a temp file then atomically replace, so a partial
            // download never overwrites the previous good icon.
            var tmp = cachePath + ".tmp";
            await using (var fs = File.Create(tmp))
                await response.Content.CopyToAsync(fs).ConfigureAwait(false);
            File.Move(tmp, cachePath, overwrite: true);

            void Apply()
            {
                // The policy may have changed while the download was running.
                if (!string.Equals(slot.Source, url, StringComparison.OrdinalIgnoreCase)) return;
                var icon = TryLoadIconFromFile(cachePath);
                if (icon is null) return;
                slot.Icon = icon;
                ApplyBadgeState(_badges.Current);
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
                Apply();
            else
                dispatcher.BeginInvoke((Action)Apply);

            _log.LogInformation(LogEvents.ConfigLoaded,
                "Custom tray icon {Policy} downloaded from {Url} to {Cache}.",
                slot.PolicyName, url, cachePath);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "Failed to download custom tray icon {Policy} from {Url}; using default icon.",
                slot.PolicyName, url);
        }
    }

    /// <summary>One policy-driven tray icon (normal or warning state).</summary>
    private sealed class CustomIconSlot(string policyName, string cacheFileName)
    {
        public string PolicyName { get; } = policyName;

        /// <summary>Cache file for an icon downloaded from an ADMX URL.</summary>
        public string CachePath => Path.Combine(LogoService.CacheDirectory, cacheFileName);

        public string Source { get; set; } = string.Empty;
        public ImageSource? Icon { get; set; }
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
        _appRefresh.RefreshCompleted -= OnRefreshCompleted;
        _http.Dispose();
        GC.SuppressFinalize(this);
    }
}
