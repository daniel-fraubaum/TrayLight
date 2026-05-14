using System.Diagnostics;
using System.Runtime.Versioning;

namespace TrayLight.Services.Providers;

/// <summary>
/// Shows time since last boot. Warning when the configured
/// <see cref="Models.InfoItemConfig.UptimeDaysLimit"/> is exceeded. Click runs
/// <c>shutdown /r /t 60 /f</c> after a confirmation handled by the host.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LastRebootProvider : InfoItemProviderBase
{
    public const string TypeKey = "lastReboot";

    public override string Type => TypeKey;
    protected override string DefaultTitle => "Last reboot";
    protected override string DefaultIcon => "Segoe Fluent Icons:E777"; // Refresh

    private readonly Func<TimeSpan> _uptimeProvider;
    private readonly Action<string, string> _shellRunner;
    private readonly Func<int> _warningDaysProvider;

    public LastRebootProvider() : this(GetUptime, RunShell, () => 7) { }

    /// <summary>Production constructor: pulls the threshold from the live config.</summary>
    public LastRebootProvider(TrayLight.Services.IConfigurationService configService)
        : this(GetUptime, RunShell, () => configService.Current.Behavior.RebootWarningDays) { }

    internal LastRebootProvider(
        Func<TimeSpan> uptimeProvider,
        Action<string, string> shellRunner,
        Func<int>? warningDaysProvider = null)
    {
        _uptimeProvider = uptimeProvider;
        _shellRunner = shellRunner;
        _warningDaysProvider = warningDaysProvider ?? (() => 7);
    }

    protected override Task<InfoItemData> CollectAsync(CancellationToken cancellationToken)
    {
        var uptime = _uptimeProvider();
        var bootTime = DateTime.Now - uptime;

        string display;
        if (uptime < TimeSpan.FromHours(24))
        {
            display = "Today";
        }
        else
        {
            var days = (int)uptime.TotalDays;
            var hours = uptime.Hours;
            display = hours > 0
                ? $"{days} d, {hours} h ago"
                : $"{days} d ago";
        }

        var limit = _warningDaysProvider();
        var hasWarning = limit > 0 && uptime.TotalDays >= limit;
        var warningMessage = hasWarning
            ? $"System has been running for {(int)uptime.TotalDays} days. Consider rebooting."
            : string.Empty;

        var detail = $"Booted: {bootTime:g}";

        return Task.FromResult(new InfoItemData(
            Title: EffectiveTitle,
            Value: display,
            DetailText: detail,
            HasWarning: hasWarning,
            WarningMessage: warningMessage,
            Icon: EffectiveIcon));
    }

    protected override Task OnClickAsync(CancellationToken cancellationToken)
    {
        // shutdown.exe with a 60-second countdown and a notice. The host UI
        // is expected to display a confirmation flyout *before* invoking this.
        _shellRunner("shutdown.exe",
            "/r /t 60 /f /c \"TrayLight is restarting your PC in 60 seconds. Save your work.\"");
        return Task.CompletedTask;
    }

    private static TimeSpan GetUptime() => TimeSpan.FromMilliseconds(Environment.TickCount64);

    private static void RunShell(string file, string args)
    {
        Process.Start(new ProcessStartInfo(file, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }
}
