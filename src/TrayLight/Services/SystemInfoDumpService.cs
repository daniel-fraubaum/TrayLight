using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;
using TrayLight.Services.Providers;

namespace TrayLight.Services;

/// <summary>
/// Collects the helpdesk-relevant system information into a single multi-line
/// string. Reuses the read paths from the existing info-item providers so
/// the values stay consistent with what the popup shows.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SystemInfoDumpService : ISystemInfoDumpService
{
    private readonly ISystemInfoService _system;

    public SystemInfoDumpService(ISystemInfoService system)
    {
        _system = system;
    }

    public string BuildDump()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== TrayLight system information ===");
        sb.AppendLine($"Generated:        {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine();

        // ----- Identity / OS -------------------------------------------------
        sb.AppendLine("[Device]");
        sb.AppendLine($"Computer name:    {_system.MachineName}");
        AppendIfPresent(sb, "Hostname (DNS):", TryDnsHostName());
        sb.AppendLine($"User:             {_system.UserName}");
        sb.AppendLine($"Domain:           {Environment.UserDomainName}");
        sb.AppendLine();

        sb.AppendLine("[Operating system]");
        var (osName, osDisplay, osBuild) = ReadOsInfo();
        sb.AppendLine($"Product:          {osName}");
        AppendIfPresent(sb, "Version:", osDisplay);
        sb.AppendLine($"Build:            {osBuild}");
        sb.AppendLine($"Architecture:     {RuntimeInformation.OSArchitecture}");
        sb.AppendLine();

        // ----- Entra ID ------------------------------------------------------
        sb.AppendLine("[Entra ID / Workplace]");
        try
        {
            var parsed = EntraIdStatusProvider.ReadFromRegistry();
            sb.AppendLine($"Join state:       {parsed.StateDisplay}");

            var (tenantId, tenantName, userEmail) = ReadEntraTenant();
            tenantName ??= parsed.TenantName;
            // Omit tenant lines entirely when nothing reliable is available.
            AppendIfPresent(sb, "Tenant:", tenantName);
            AppendIfPresent(sb, "Tenant ID:", tenantId);
            AppendIfPresent(sb, "User (Entra):", userEmail);
        }
        catch
        {
            // Registry unreadable - omit the section body (rule: no "unknown").
        }
        sb.AppendLine();

        // ----- Intune --------------------------------------------------------
        sb.AppendLine("[Intune]");
        try
        {
            // Same data source as the Intune tile: OMA-DM ServerLastSuccessTime.
            var intune = IntuneSyncProvider.ReadStatus();
            sb.AppendLine($"Enrolled:         {(intune.IsEnrolled ? "Yes" : "No")}");
            if (intune.LastSyncUtc is { } sync)
            {
                sb.AppendLine($"Last sync (UTC):   {sync:yyyy-MM-dd HH:mm:ss} UTC");
                sb.AppendLine($"Last sync (local): {sync.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}");
            }
            // else: omit the last-sync lines entirely.
        }
        catch
        {
            // Registry unreadable - omit the section body (rule: no "unknown").
        }
        sb.AppendLine();

        // ----- Network -------------------------------------------------------
        sb.AppendLine("[Network adapters]");
        AppendNetwork(sb);

        return sb.ToString();
    }

    private static string? TryDnsHostName()
    {
        try { return Dns.GetHostName(); } catch { return null; }
    }

    /// <summary>
    /// Appends "<paramref name="label"/> <paramref name="value"/>" only when the
    /// value is present. Missing values are omitted entirely rather than printed
    /// as "unknown" / "(none)" — a helpdesk dump should contain only reliable data.
    /// </summary>
    private static void AppendIfPresent(StringBuilder sb, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        sb.AppendLine($"{label,-17} {value}");
    }

    /// <summary>
    /// Reads Entra ID tenant details from
    /// <c>HKLM\SYSTEM\CurrentControlSet\Control\CloudDomainJoin\JoinInfo\{GUID}</c>.
    /// Returns nulls when the device is not Entra-joined or the key is unreadable.
    /// </summary>
    private static (string? TenantId, string? TenantName, string? UserEmail) ReadEntraTenant()
    {
        try
        {
            using var joinInfo = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\CloudDomainJoin\JoinInfo");
            var deviceKeys = joinInfo?.GetSubKeyNames();
            if (deviceKeys is { Length: > 0 })
            {
                using var deviceKey = joinInfo!.OpenSubKey(deviceKeys[0]);
                var tenantId   = deviceKey?.GetValue("TenantId")   as string;
                var tenantName = deviceKey?.GetValue("TenantName") as string;
                var userEmail  = deviceKey?.GetValue("UserEmail")  as string;
                return (tenantId, tenantName, userEmail);
            }
        }
        catch { /* not Entra-joined or ACL-restricted */ }
        return (null, null, null);
    }
    private static (string Product, string Display, string Build) ReadOsInfo()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key is null) return ("Windows", string.Empty, Environment.OSVersion.Version.ToString());

            var product   = key.GetValue("ProductName")    as string ?? "Windows";
            var display   = key.GetValue("DisplayVersion") as string ?? string.Empty;
            var build     = key.GetValue("CurrentBuild")   as string ?? Environment.OSVersion.Version.Build.ToString();
            var ubr       = key.GetValue("UBR");
            var fullBuild = ubr is int u ? $"{build}.{u}" : build;

            // CurrentVersion's ProductName still says "Windows 10" on Win11 —
            // upgrade to "Windows 11" when build >= 22000 to match what users see.
            if (int.TryParse(build, out var b) && b >= 22000 && product.Contains("Windows 10"))
                product = product.Replace("Windows 10", "Windows 11");

            return (product, display, fullBuild);
        }
        catch
        {
            return ("Windows", string.Empty, Environment.OSVersion.Version.ToString());
        }
    }

    /// <summary>
    /// Name/description substrings that identify Windows filter-driver bindings
    /// and pseudo adapters that clutter the dump. Matching adapters are skipped.
    /// </summary>
    private static readonly string[] AdapterExclusions =
    {
        "Filter", "QoS Packet Scheduler", "LightWeight", "LWF", "Npcap", "Loopback",
    };

    private static void AppendNetwork(StringBuilder sb)
    {
        NetworkInterface[] nics;
        try { nics = NetworkInterface.GetAllNetworkInterfaces(); }
        catch { return; }

        // Dedupe by MAC: a physical NIC surfaces once per filter-driver binding,
        // but only the real entry carries IP addresses. Requiring IPs before
        // claiming the MAC guarantees we keep the useful entry.
        var seenMacs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var any = false;

        foreach (var nic in nics.Where(n => n.OperationalStatus == OperationalStatus.Up))
        {
            // Skip filter-driver bindings / pseudo adapters by name or description.
            if (AdapterExclusions.Any(m =>
                    nic.Name.Contains(m, StringComparison.OrdinalIgnoreCase) ||
                    nic.Description.Contains(m, StringComparison.OrdinalIgnoreCase)))
                continue;

            // Require a real MAC address.
            var macBytes = nic.GetPhysicalAddress().GetAddressBytes();
            if (macBytes.Length == 0)
                continue;
            var mac = string.Join(":", macBytes.Select(b => b.ToString("X2")));

            // Require at least one IP address.
            var ips = nic.GetIPProperties().UnicastAddresses
                .Where(a => a.Address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                .Select(a => a.Address.ToString())
                .ToArray();
            if (ips.Length == 0)
                continue;

            // Skip duplicate bindings of the same physical NIC.
            if (!seenMacs.Add(mac))
                continue;

            sb.AppendLine($"- {nic.Name}");
            sb.AppendLine($"    Type:    {nic.NetworkInterfaceType}");
            sb.AppendLine($"    MAC:     {mac}");
            sb.AppendLine($"    IP(s):   {string.Join(", ", ips)}");
            any = true;
        }

        if (!any) sb.AppendLine("  (no active network adapters)");
    }
}
