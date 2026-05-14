using System.Text.Json.Serialization;

namespace TrayLight.Models;

public class FooterConfig
{
    /// <summary>
    /// Configurable customer footer line. The fixed
    /// "Powered by headsinthecloud.blog" line is hardcoded in
    /// the UI and is rendered <em>below</em> this text.
    /// </summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("showLastSync")]
    public bool ShowLastSync { get; set; } = true;

    /// <summary>
    /// Free-text information block rendered between the Quick Actions
    /// section and the footer. Supports a tiny markup:
    /// <c>*bold*</c>, <c>|</c> for line breaks, <c>||</c> for blank lines.
    /// Empty string hides the section entirely.
    /// </summary>
    [JsonPropertyName("infoText")]
    public string InfoText { get; set; } = string.Empty;
}
