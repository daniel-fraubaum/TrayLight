using System.Globalization;
using TrayLight.Services;

namespace TrayLight.Resources;

/// <summary>
/// Strongly-typed accessor for the localized UI strings. The values now come
/// from the runtime, file-based <see cref="LocalizationService"/> (JSON files in
/// the <c>Languages</c> folder) rather than compiled <c>.resx</c> satellite
/// assemblies, so new languages can be added by dropping a file next to the
/// executable without rebuilding the app.
///
/// <para>
/// This facade is kept because the WPF views bind to it via
/// <c>{x:Static res:Strings.X}</c> and the strongly-typed members give
/// compile-time safety in C#. Every member simply delegates to
/// <see cref="LocalizationService.Instance"/>.
/// </para>
/// </summary>
public static class Strings
{
    /// <summary>
    /// Looks up a resource by key for the current UI culture, falling back to
    /// the key itself when the resource is missing (so a typo is visible rather
    /// than throwing at runtime).
    /// </summary>
    public static string Get(string key) => LocalizationService.Instance.GetString(key);

    /// <summary>
    /// Looks up a composite-format resource by key and formats it with
    /// <paramref name="args"/> using the current culture.
    /// </summary>
    public static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    // --- Window chrome / shared --------------------------------------------
    public static string WelcomeTitle          => Get(nameof(WelcomeTitle));
    public static string WelcomeSubtitle       => Get(nameof(WelcomeSubtitle));
    public static string AboutTitle            => Get(nameof(AboutTitle));
    public static string Close                 => Get(nameof(Close));
    public static string DoNotShowAgain        => Get(nameof(DoNotShowAgain));
    public static string GetStarted            => Get(nameof(GetStarted));
    public static string Version               => Get(nameof(Version));
    public static string CreatedBy             => Get(nameof(CreatedBy));
    public static string SystemInformation     => Get(nameof(SystemInformation));
    public static string CopySystemInfo        => Get(nameof(CopySystemInfo));
    public static string Copied                => Get(nameof(Copied));
    public static string CopyFailed            => Get(nameof(CopyFailed));
    public static string CollectingSystemInfo  => Get(nameof(CollectingSystemInfo));
    public static string QuickActions          => Get(nameof(QuickActions));
    public static string PoweredBy             => Get(nameof(PoweredBy));

    // --- Context menu ------------------------------------------------------
    public static string MenuAbout             => Get(nameof(MenuAbout));
    public static string MenuRefresh           => Get(nameof(MenuRefresh));
    public static string MenuQuit              => Get(nameof(MenuQuit));

    // --- Welcome feature cards --------------------------------------------
    public static string FeatureSystemInfoTitle => Get(nameof(FeatureSystemInfoTitle));
    public static string FeatureSystemInfoBody  => Get(nameof(FeatureSystemInfoBody));
    public static string FeatureQuickAccessTitle => Get(nameof(FeatureQuickAccessTitle));
    public static string FeatureQuickAccessBody  => Get(nameof(FeatureQuickAccessBody));
    public static string FeatureItSupportTitle  => Get(nameof(FeatureItSupportTitle));
    public static string FeatureItSupportBody   => Get(nameof(FeatureItSupportBody));

    // --- Tile titles -------------------------------------------------------
    public static string TileComputerName      => Get(nameof(TileComputerName));
    public static string TileOsVersion         => Get(nameof(TileOsVersion));
    public static string TileLastReboot        => Get(nameof(TileLastReboot));
    public static string TileStorage           => Get(nameof(TileStorage));
    public static string TileNetwork           => Get(nameof(TileNetwork));
    public static string TileSerialNumber      => Get(nameof(TileSerialNumber));
    public static string TileIntuneSync        => Get(nameof(TileIntuneSync));
    public static string TileInfo              => Get(nameof(TileInfo));

    // --- Status / value strings -------------------------------------------
    public static string StatusNotEnrolled     => Get(nameof(StatusNotEnrolled));
    public static string StatusUnknown         => Get(nameof(StatusUnknown));
    public static string StatusDetecting       => Get(nameof(StatusDetecting));
    public static string StatusUnavailable     => Get(nameof(StatusUnavailable));
    public static string StatusSyncing         => Get(nameof(StatusSyncing));
    public static string StatusOffline         => Get(nameof(StatusOffline));
    public static string StatusVirtualMachineSerial => Get(nameof(StatusVirtualMachineSerial));
    public static string NetworkEthernet       => Get(nameof(NetworkEthernet));
    public static string NetworkWifi           => Get(nameof(NetworkWifi));
    public static string NetworkVpn            => Get(nameof(NetworkVpn));
    public static string NetworkActiveMarker   => Get(nameof(NetworkActiveMarker));
    public static string RelativeUnknown       => Get(nameof(RelativeUnknown));
    public static string RelativeJustNow       => Get(nameof(RelativeJustNow));

    // --- Tile tooltips -----------------------------------------------------
    public static string TooltipClickToCopy           => Get("Tooltip_ClickToCopy");
    public static string TooltipClickToSyncNow        => Get("Tooltip_ClickToSyncNow");
    public static string TooltipNotIntuneManaged      => Get("Tooltip_NotIntuneManaged");
    public static string TooltipIntuneSyncTimeUnknown => Get("Tooltip_IntuneSyncTimeUnknown");
    public static string TooltipNoNetworkConnection   => Get("Tooltip_NoNetworkConnection");

    // --- Quick Actions defaults -------------------------------------------
    public static string DefaultShortcutTitle    => Get(nameof(DefaultShortcutTitle));
    public static string DefaultShortcutSubtitle => Get(nameof(DefaultShortcutSubtitle));
}
