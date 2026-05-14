using System.Runtime.Versioning;
using Microsoft.Win32;

namespace TrayLight.Services.Providers;

/// <summary>
/// Reads the Windows version from <c>HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion</c>
/// and (best-effort) checks for pending updates via the COM Windows Update API.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class OsVersionProvider : InfoItemProviderBase
{
    public const string TypeKey = "osVersion";
    private const string RegPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    public override string Type => TypeKey;
    protected override string DefaultTitle => "Operating system";
    protected override string DefaultIcon => "Segoe Fluent Icons:E770"; // Tiles

    private readonly Func<bool> _updatesAvailable;

    public OsVersionProvider() : this(CheckForUpdatesViaCom) { }

    /// <summary>Test seam: inject the update-check callback.</summary>
    internal OsVersionProvider(Func<bool> updatesAvailable)
    {
        _updatesAvailable = updatesAvailable;
    }

    protected override Task<InfoItemData> CollectAsync(CancellationToken cancellationToken)
    {
        var (display, detail) = ReadVersionInfo();

        // OS-version tile is informational only - it never raises a warning.
        // (Pending Windows updates surface through the OS itself; duplicating
        // that in the tray icon led to false positives on fresh devices.)
        return Task.FromResult(new InfoItemData(
            Title: EffectiveTitle,
            Value: display,
            DetailText: detail,
            HasWarning: false,
            WarningMessage: string.Empty,
            Icon: EffectiveIcon));
    }

    protected override Task OnClickAsync(CancellationToken cancellationToken)
    {
        LaunchShell("ms-settings:windowsupdate");
        return Task.CompletedTask;
    }

    private static (string Display, string Detail) ReadVersionInfo()
    {
        using var key = Registry.LocalMachine.OpenSubKey(RegPath);
        if (key is null)
            return (Environment.OSVersion.VersionString, string.Empty);

        var product = key.GetValue("ProductName") as string ?? "Windows";
        var displayVersion = key.GetValue("DisplayVersion") as string;
        var releaseId = key.GetValue("ReleaseId") as string;
        var build = key.GetValue("CurrentBuild") as string ?? string.Empty;
        var ubr = key.GetValue("UBR");
        var edition = key.GetValue("EditionID") as string ?? string.Empty;

        // ProductName on Win11 still says "Windows 10"; correct it from build #.
        if (int.TryParse(build, out var b) && b >= 22000 && product.Contains("Windows 10"))
            product = product.Replace("Windows 10", "Windows 11");

        var version = displayVersion ?? releaseId ?? string.Empty;
        var display = string.IsNullOrEmpty(version)
            ? $"{product} ({build})"
            : $"{product} {version} ({build})";

        var detail = ubr is null
            ? $"Edition: {edition}"
            : $"Edition: {edition} · Build {build}.{ubr}";

        return (display, detail);
    }

    /// <summary>
    /// Late-bound call to <c>Microsoft.Update.Session</c> so we don't take a
    /// hard reference on WUApiLib. Returns false if the API is unavailable.
    /// </summary>
    private static bool CheckForUpdatesViaCom()
    {
        var t = System.Type.GetTypeFromProgID("Microsoft.Update.Session");
        if (t is null) return false;
        dynamic? session = Activator.CreateInstance(t);
        if (session is null) return false;
        try
        {
            var searcher = session.CreateUpdateSearcher();
            // Online=false avoids hammering Microsoft Update servers; a separate
            // background scan keeps the cache warm.
            searcher.Online = false;
            var result = searcher.Search("IsInstalled=0 and IsHidden=0");
            return result.Updates.Count > 0;
        }
        finally
        {
            if (session is not null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(session);
        }
    }
}
