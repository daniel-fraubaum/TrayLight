using System.Runtime.Versioning;

namespace TrayLight.Services.Configuration;

/// <summary>
/// Abstraction over the registry hive that backs TrayLight's policy.
/// Production uses <see cref="HklmPolicyRegistrySource"/>; tests inject
/// <see cref="InMemoryRegistrySource"/>.
///
/// All sub-paths are relative to the policy root
/// <c>HKLM\SOFTWARE\Policies\TrayLight</c> and use <c>\</c> as separator.
/// An empty string represents the root key itself.
/// </summary>
public interface IRegistryConfigurationSource
{
    /// <summary>Human-readable description of where the data is read from.</summary>
    string RootDescription { get; }

    string? GetString(string subPath, string valueName);

    int? GetInt(string subPath, string valueName);

    /// <summary>Returns the names of the immediate sub-keys, or empty when missing.</summary>
    IReadOnlyList<string> GetSubKeyNames(string subPath);
}

[SupportedOSPlatform("windows")]
public sealed class HklmPolicyRegistrySource : IRegistryConfigurationSource
{
    public const string PolicyRoot = @"SOFTWARE\Policies\TrayLight";

    public string RootDescription => @"HKLM\" + PolicyRoot;

    public string? GetString(string subPath, string valueName)
    {
        try
        {
            using var key = Open(subPath);
            // Treat an empty REG_SZ as unset so the in-app default kicks in.
            // Intune / GPO write empty strings when an admin clears a textBox
            // in the policy editor, and we don't want "" to override the
            // built-in title/icon/etc.
            return key?.GetValue(valueName) is string s && s.Length > 0 ? s : null;
        }
        catch
        {
            return null;
        }
    }

    public int? GetInt(string subPath, string valueName)
    {
        try
        {
            using var key = Open(subPath);
            var raw = key?.GetValue(valueName);
            return raw switch
            {
                int i        => i,
                long l       => unchecked((int)l),
                string s     => int.TryParse(s, out var n) ? n : null,
                _            => null,
            };
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyList<string> GetSubKeyNames(string subPath)
    {
        try
        {
            using var key = Open(subPath);
            return key?.GetSubKeyNames() ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static Microsoft.Win32.RegistryKey? Open(string subPath)
    {
        var path = string.IsNullOrEmpty(subPath)
            ? PolicyRoot
            : PolicyRoot + "\\" + subPath.Trim('\\');
        return Microsoft.Win32.Registry.LocalMachine.OpenSubKey(path);
    }
}

/// <summary>In-memory registry double for unit tests.</summary>
public sealed class InMemoryRegistrySource : IRegistryConfigurationSource
{
    private readonly Dictionary<string, Dictionary<string, object>> _values =
        new(StringComparer.OrdinalIgnoreCase);

    public string RootDescription => "in-memory://TrayLight";

    public InMemoryRegistrySource Set(string subPath, string valueName, object? value)
    {
        if (value is null) return this;
        if (!_values.TryGetValue(Norm(subPath), out var bucket))
        {
            bucket = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            _values[Norm(subPath)] = bucket;
        }
        bucket[valueName] = value;
        return this;
    }

    public string? GetString(string subPath, string valueName) =>
        _values.TryGetValue(Norm(subPath), out var bucket) && bucket.TryGetValue(valueName, out var v)
            ? v as string
            : null;

    public int? GetInt(string subPath, string valueName) =>
        _values.TryGetValue(Norm(subPath), out var bucket) && bucket.TryGetValue(valueName, out var v)
            ? v switch { int i => i, long l => (int)l, string s when int.TryParse(s, out var n) => n, _ => null }
            : null;

    public IReadOnlyList<string> GetSubKeyNames(string subPath)
    {
        var prefix = Norm(subPath);
        prefix = prefix.Length == 0 ? "" : prefix + "\\";

        var children = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in _values.Keys)
        {
            if (path.Length <= prefix.Length) continue;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var tail = path[prefix.Length..];
            var slash = tail.IndexOf('\\');
            children.Add(slash < 0 ? tail : tail[..slash]);
        }
        return children.ToArray();
    }

    private static string Norm(string subPath) => (subPath ?? string.Empty).Trim('\\');
}
