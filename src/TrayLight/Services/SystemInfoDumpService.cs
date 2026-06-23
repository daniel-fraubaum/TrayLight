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
        sb.AppendLine($"Hostname (DNS):   {SafeDnsHostName()}");
        sb.AppendLine($"User:             {_system.UserName}");
        sb.AppendLine($"Domain:           {Environment.UserDomainName}");
        sb.AppendLine();

        sb.AppendLine("[Operating system]");
        var (osName, osDisplay, osBuild) = ReadOsInfo();
        sb.AppendLine($"Product:          {osName}");
        sb.AppendLine($"Version:          {osDisplay}");
        sb.AppendLine($"Build:            {osBuild}");
        sb.AppendLine($"Architecture:     {RuntimeInformation.OSArchitecture}");
        sb.AppendLine();

        // ----- Entra ID ------------------------------------------------------
        sb.AppendLine("[Entra ID / Workplace]");
        try
        {
            var parsed = EntraIdStatusProvider.ReadFromRegistry();
            sb.AppendLine($"Join state:       {parsed.StateDisplay}");
            sb.AppendLine($"Tenant:           {parsed.TenantName ?? "(unknown)"}");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Join state:       (unavailable: {ex.Message})");
        }
        sb.AppendLine();

        // ----- Intune --------------------------------------------------------
        sb.AppendLine("[Intune]");
        try
        {
            var intune = IntuneSyncProvider.ReadStatus();
            sb.AppendLine($"Enrolled:         {(intune.IsEnrolled ? "Yes" : "No")}");
            sb.AppendLine(intune.LastSyncUtc is { } sync
                ? $"Last sync (UTC):  {sync:yyyy-MM-dd HH:mm:ss}"
                : "Last sync (UTC):  unknown");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Status:           (unavailable: {ex.Message})");
        }
        sb.AppendLine();

        // ----- Network -------------------------------------------------------
        sb.AppendLine("[Network adapters]");
        AppendNetwork(sb);

        return sb.ToString();
    }

    private static string SafeDnsHostName()
    {
        try { return Dns.GetHostName(); } catch { return "(unavailable)"; }
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

    private static void AppendNetwork(StringBuilder sb)
    {
        NetworkInterface[] nics;
        try { nics = NetworkInterface.GetAllNetworkInterfaces(); }
        catch (Exception ex) { sb.AppendLine($"  (unavailable: {ex.Message})"); return; }

        var any = false;
        foreach (var nic in nics
                 .Where(n => n.OperationalStatus == OperationalStatus.Up)
                 .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                             n.NetworkInterfaceType != NetworkInterfaceType.Tunnel))
        {
            any = true;
            var ips = nic.GetIPProperties().UnicastAddresses
                .Where(a => a.Address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                .Select(a => a.Address.ToString())
                .ToArray();
            var mac = nic.GetPhysicalAddress();
            var macFormatted = mac.GetAddressBytes().Length > 0
                ? string.Join(":", mac.GetAddressBytes().Select(b => b.ToString("X2")))
                : "(none)";

            sb.AppendLine($"- {nic.Name}");
            sb.AppendLine($"    Type:    {nic.NetworkInterfaceType}");
            sb.AppendLine($"    MAC:     {macFormatted}");
            sb.AppendLine($"    IP(s):   {(ips.Length == 0 ? "(none)" : string.Join(", ", ips))}");
        }
        if (!any) sb.AppendLine("  (no active network adapters)");
    }
}
