using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace TrayLight.Services.Providers;

/// <summary>
/// Reports Microsoft Intune (MDM) enrollment + last sync time. Reads:
///   HKLM\SOFTWARE\Microsoft\Enrollments\{enrollmentId}     (provider name)
///   HKLM\SOFTWARE\Microsoft\IntuneManagementExtension      (last check-in)
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class IntuneComplianceProvider : InfoItemProviderBase
{
    public const string TypeKey = "intuneCompliance";

    public override string Type => TypeKey;
    protected override string DefaultTitle => "Intune";
    protected override string DefaultIcon => "Segoe Fluent Icons:E73E"; // Checkmark

    private readonly Func<IntuneStatus> _statusReader;

    public IntuneComplianceProvider() : this(ReadStatus) { }

    internal IntuneComplianceProvider(Func<IntuneStatus> statusReader)
    {
        _statusReader = statusReader;
    }

    protected override Task<InfoItemData> CollectAsync(CancellationToken cancellationToken)
    {
        var status = _statusReader();
        if (!status.IsEnrolled)
        {
            // Enrollment status is informational - non-MDM devices (e.g. BYOD,
            // VMs, dev boxes) should not raise warnings just for not being
            // managed.
            return Task.FromResult(new InfoItemData(
                Title: EffectiveTitle,
                Value: "Not enrolled",
                DetailText: string.Empty,
                HasWarning: false,
                WarningMessage: string.Empty,
                Icon: EffectiveIcon));
        }

        var detail = status.LastSyncUtc is { } sync
            ? $"Last sync: {sync.ToLocalTime():g}"
            : "Last sync: unknown";

        return Task.FromResult(new InfoItemData(
            Title: EffectiveTitle,
            Value: "Enrolled",
            DetailText: detail,
            HasWarning: false,
            WarningMessage: string.Empty,
            Icon: EffectiveIcon));
    }

    protected override Task OnClickAsync(CancellationToken cancellationToken)
    {
        // Prefer the Company Portal store URI; falls back to settings if not installed.
        try { LaunchShell("companyportal:"); }
        catch { LaunchShell("ms-settings:workplace"); }
        return Task.CompletedTask;
    }

    internal static IntuneStatus ReadStatus()
    {
        // HKLM\SOFTWARE\Microsoft\Enrollments and \IntuneManagementExtension
        // are ACL-restricted to SYSTEM/Administrators on most builds. TrayLight
        // runs as the interactive user, so accessing them throws
        // SecurityException ("Requested registry access is not allowed.").
        // We swallow those access errors and treat the device as not enrolled
        // rather than surfacing the raw exception text in the tile.
        var enrolled = false;
        try
        {
            using var enrollments = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Enrollments");
            if (enrollments is not null)
            {
                foreach (var sub in enrollments.GetSubKeyNames())
                {
                    try
                    {
                        using var k = enrollments.OpenSubKey(sub);
                        var provider = k?.GetValue("ProviderID") as string;
                        if (string.Equals(provider, "MS DM Server", StringComparison.OrdinalIgnoreCase))
                        {
                            enrolled = true;
                            break;
                        }
                    }
                    catch (System.Security.SecurityException) { /* per-enrollment ACL */ }
                    catch (UnauthorizedAccessException) { /* per-enrollment ACL */ }
                }
            }
        }
        catch (System.Security.SecurityException) { /* HKLM\...\Enrollments locked down */ }
        catch (UnauthorizedAccessException) { /* HKLM\...\Enrollments locked down */ }

        DateTime? lastSync = null;
        try
        {
            using var ime = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\IntuneManagementExtension");
            // The IME logs its last check-in under a few alternate value names
            // depending on the agent build. Try the most common ones.
            var raw = ime?.GetValue("LastCheckinTimeUTC")
                   ?? ime?.GetValue("LastCheckinTime")
                   ?? ime?.GetValue("LastSyncTime");
            if (raw is string s &&
                DateTime.TryParse(s, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                lastSync = parsed;
            }
        }
        catch (System.Security.SecurityException) { /* IME key locked down */ }
        catch (UnauthorizedAccessException) { /* IME key locked down */ }

        return new IntuneStatus(enrolled, lastSync);
    }

    public sealed record IntuneStatus(bool IsEnrolled, DateTime? LastSyncUtc);
}
