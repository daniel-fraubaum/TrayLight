using System.Text.RegularExpressions;

namespace TrayLight.Services.Actions;

/// <summary>
/// Pure, side-effect-free engine that expands <c>{{Placeholder}}</c> tokens in a
/// shortcut <see cref="Models.ShortcutConfig.Action"/> string. The actual live
/// values are resolved by <see cref="IShortcutPlaceholderResolver"/> at
/// click-time; this class only performs the textual substitution so it can be
/// unit-tested in isolation.
/// </summary>
public static partial class ShortcutPlaceholders
{
    /// <summary>Value substituted when a placeholder cannot be resolved.</summary>
    public const string Unresolved = "N/A";

    /// <summary>The set of tokens TrayLight knows how to resolve.</summary>
    public static readonly IReadOnlyList<string> KnownTokens = new[]
    {
        "ComputerName",
        "OsVersion",
        "LastReboot",
        "Storage",
        "SerialNumber",
        "IntuneSync",
        "Network",
        "UserName",
        "DomainName",
    };

    [GeneratedRegex(@"\{\{\s*(?<token>[A-Za-z0-9_]+)\s*\}\}", RegexOptions.Compiled)]
    private static partial Regex TokenRegex();

    /// <summary>True when <paramref name="input"/> contains at least one token.</summary>
    public static bool ContainsTokens(string? input) =>
        !string.IsNullOrEmpty(input) &&
        input.Contains("{{", StringComparison.Ordinal) &&
        TokenRegex().IsMatch(input);

    /// <summary>
    /// Whether resolved values must be URL-encoded. Per spec this applies when
    /// the action is a <c>mailto:</c> or <c>https://</c> link so spaces and
    /// special characters do not break the URL.
    /// </summary>
    public static bool RequiresUrlEncoding(string action) =>
        !string.IsNullOrEmpty(action) &&
        (action.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
         action.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns each distinct token name referenced in <paramref name="input"/>.</summary>
    public static IEnumerable<string> ExtractTokens(string input)
    {
        if (string.IsNullOrEmpty(input)) yield break;
        foreach (Match m in TokenRegex().Matches(input))
            yield return m.Groups["token"].Value;
    }

    /// <summary>
    /// Replaces every <c>{{Token}}</c> in <paramref name="input"/> with the
    /// matching entry from <paramref name="values"/> (case-insensitive lookup).
    /// Missing / empty values fall back to <see cref="Unresolved"/>. When the
    /// action is a mailto/https link every substituted value is URL-encoded.
    /// </summary>
    public static string Expand(string input, IReadOnlyDictionary<string, string?> values)
    {
        if (string.IsNullOrEmpty(input)) return input ?? string.Empty;
        ArgumentNullException.ThrowIfNull(values);

        var encode = RequiresUrlEncoding(input);

        return TokenRegex().Replace(input, match =>
        {
            var token = match.Groups["token"].Value;
            var resolved = Lookup(values, token);
            var value = string.IsNullOrEmpty(resolved) ? Unresolved : resolved!;
            return encode ? Uri.EscapeDataString(value) : value;
        });
    }

    private static string? Lookup(IReadOnlyDictionary<string, string?> values, string token)
    {
        if (values.TryGetValue(token, out var direct)) return direct;
        foreach (var kv in values)
            if (string.Equals(kv.Key, token, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        return null;
    }
}
