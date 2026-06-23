using System.Collections.Generic;
using TrayLight.Services.Actions;
using Xunit;

namespace TrayLight.Tests.Actions;

public class ShortcutPlaceholdersTests
{
    private static IReadOnlyDictionary<string, string?> SampleValues() => new Dictionary<string, string?>
    {
        ["ComputerName"] = "DESK-01",
        ["OsVersion"]    = "Win 11 Ent 25H2",
        ["SerialNumber"] = "ABC 123",
        ["IntuneSync"]   = "13m ago",
    };

    [Fact]
    public void Expand_replaces_all_known_placeholders_raw_for_non_url()
    {
        const string input = "app:{{ComputerName}}|{{OsVersion}}|{{SerialNumber}}|{{IntuneSync}}";

        var result = ShortcutPlaceholders.Expand(input, SampleValues());

        // App/command actions get the raw value (no encoding), spaces preserved.
        Assert.Equal("app:DESK-01|Win 11 Ent 25H2|ABC 123|13m ago", result);
    }

    [Theory]
    [InlineData("mailto:it@example.com?subject={{ComputerName}}")]
    [InlineData("https://portal/{{ComputerName}}")]
    public void Expand_url_encodes_values_for_mailto_and_https(string template)
    {
        var values = new Dictionary<string, string?> { ["ComputerName"] = "DESK 01/A&B" };

        var result = ShortcutPlaceholders.Expand(template, values);

        // "DESK 01/A&B" -> "DESK%2001%2FA%26B"
        Assert.Contains("DESK%2001%2FA%26B", result);
        Assert.DoesNotContain("DESK 01/A&B", result);
    }

    [Fact]
    public void Expand_mailto_body_encodes_spaces()
    {
        const string input = "mailto:it@example.com?subject=Support%20-%20{{ComputerName}}&body=OS:%20{{OsVersion}}";

        var result = ShortcutPlaceholders.Expand(input, SampleValues());

        Assert.Contains("subject=Support%20-%20DESK-01", result);
        Assert.Contains("OS:%20Win%2011%20Ent%2025H2", result);
    }

    [Fact]
    public void Expand_unknown_placeholder_falls_back_to_NA_raw_for_app()
    {
        const string input = "app:{{DoesNotExist}}";

        var result = ShortcutPlaceholders.Expand(input, SampleValues());

        Assert.Equal("app:N/A", result);
    }

    [Fact]
    public void Expand_missing_value_for_known_token_falls_back_to_NA()
    {
        const string input = "app:{{ComputerName}}";

        var result = ShortcutPlaceholders.Expand(input, new Dictionary<string, string?>());

        Assert.Equal("app:N/A", result);
    }

    [Fact]
    public void Expand_is_case_insensitive_for_token_names()
    {
        const string input = "app:{{computername}}";

        var result = ShortcutPlaceholders.Expand(input, SampleValues());

        Assert.Equal("app:DESK-01", result);
    }

    [Fact]
    public void ContainsTokens_detects_presence()
    {
        Assert.True(ShortcutPlaceholders.ContainsTokens("x {{ComputerName}} y"));
        Assert.False(ShortcutPlaceholders.ContainsTokens("no tokens here"));
        Assert.False(ShortcutPlaceholders.ContainsTokens(""));
    }

    [Fact]
    public void RequiresUrlEncoding_only_for_mailto_and_https()
    {
        Assert.True(ShortcutPlaceholders.RequiresUrlEncoding("mailto:a@b.com"));
        Assert.True(ShortcutPlaceholders.RequiresUrlEncoding("https://x"));
        Assert.False(ShortcutPlaceholders.RequiresUrlEncoding("app:notepad"));
        Assert.False(ShortcutPlaceholders.RequiresUrlEncoding("cmd /c echo"));
        Assert.False(ShortcutPlaceholders.RequiresUrlEncoding("http://x"));
    }
}
