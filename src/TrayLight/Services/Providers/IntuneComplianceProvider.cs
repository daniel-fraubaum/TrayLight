using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace TrayLight.Services.Providers;

/// <summary>
/// Reports Microsoft Intune (MDM) enrollment + last sync time. Reads:
///   HKLM\SOFTWARE\Microsoft\Enrollments\{enrollmentId}     (ProviderID)
///   HKLM\SOFTWARE\Microsoft\Provisioning\OMADM\Accounts\{id}\Protected\
///     ConnInfo\ServerLastSuccessTime                       (last sync, primary)
///   HKLM\SOFTWARE\Microsoft\IntuneManagementExtension      (last sync, fallback)
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class IntuneSyncProvider : InfoItemProviderBase
{
    public const string TypeKey = "intuneSync";

    public override string Type => TypeKey;
    protected override string DefaultTitle => "Intune";
    protected override string DefaultIcon => "Segoe Fluent Icons:E73E"; // Checkmark

    private readonly Func<IntuneStatus> _statusReader;

    public IntuneSyncProvider() : this(ReadStatus) { }

    internal IntuneSyncProvider(Func<IntuneStatus> statusReader)
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
        // are ACL-restricted to SYSTEM/Administrators on most builds, so the
        // last check-in time cannot be read from there as the interactive user.
        // The authoritative, user-readable source is the OMA-DM
        // ServerLastSuccessTime value (same as Windows Settings > Access work
        // or school > Info and the Intune tile). We enumerate the MDM
        // enrollments, confirm enrollment via ProviderID, and read that value.
        var enrolled = false;
        DateTime? lastSyncUtc = null;

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
                        if (!string.Equals(provider, "MS DM Server", StringComparison.OrdinalIgnoreCase))
                            continue;

                        enrolled = true;

                        // PRIMARY sync source: OMA-DM ServerLastSuccessTime.
                        var utc = ReadOmaDmServerLastSuccessUtc(sub);
                        if (utc is { } t && (lastSyncUtc is null || t > lastSyncUtc))
                            lastSyncUtc = t;
                    }
                    catch (System.Security.SecurityException) { /* per-enrollment ACL */ }
                    catch (UnauthorizedAccessException) { /* per-enrollment ACL */ }
                }
            }
        }
        catch (System.Security.SecurityException) { /* HKLM\...\Enrollments locked down */ }
        catch (UnauthorizedAccessException) { /* HKLM\...\Enrollments locked down */ }

        // Fallback: the IME check-in value, which is usually ACL-restricted and
        // therefore normally returns null.
        lastSyncUtc ??= ReadImeCheckinUtc();

        return new IntuneStatus(enrolled, lastSyncUtc);
    }

    /// <summary>
    /// Reads the OMA-DM <c>ServerLastSuccessTime</c> for a specific enrollment
    /// and returns it as UTC, or <c>null</c> when missing/unreadable. Despite
    /// the trailing <c>Z</c> the value is stored in local wall time on current
    /// Windows builds (verified against MDM event-log entries), so it is pinned
    /// to Local and then converted to UTC.
    /// </summary>
    private static DateTime? ReadOmaDmServerLastSuccessUtc(string enrollmentGuid)
    {
        try
        {
            using var conn = Registry.LocalMachine.OpenSubKey(
                $@"SOFTWARE\Microsoft\Provisioning\OMADM\Accounts\{enrollmentGuid}\Protected\ConnInfo");
            var raw = conn?.GetValue("ServerLastSuccessTime") as string;
            if (string.IsNullOrEmpty(raw))
                return null;

            var bare = raw!.EndsWith('Z') ? raw[..^1] : raw;
            if (DateTime.TryParseExact(bare, "yyyyMMddTHHmmss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return DateTime.SpecifyKind(parsed, DateTimeKind.Local).ToUniversalTime();
            }
        }
        catch (System.Security.SecurityException) { /* Protected key ACL */ }
        catch (UnauthorizedAccessException) { /* Protected key ACL */ }
        return null;
    }

    /// <summary>
    /// Reads the IntuneManagementExtension last check-in time as UTC. This key
    /// is ACL-restricted on most builds, so this normally returns <c>null</c>
    /// and only serves as a best-effort fallback.
    /// </summary>
    private static DateTime? ReadImeCheckinUtc()
    {
        try
        {
            using var ime = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\IntuneManagementExtension");
            var raw = ime?.GetValue("LastCheckinTimeUTC")
                   ?? ime?.GetValue("LastCheckinTime")
                   ?? ime?.GetValue("LastSyncTime");
            if (raw is string s &&
                DateTime.TryParse(s, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                return parsed;
            }
        }
        catch (System.Security.SecurityException) { /* IME key locked down */ }
        catch (UnauthorizedAccessException) { /* IME key locked down */ }
        return null;
    }

    public sealed record IntuneStatus(bool IsEnrolled, DateTime? LastSyncUtc);
}
