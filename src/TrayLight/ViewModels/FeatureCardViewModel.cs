using CommunityToolkit.Mvvm.ComponentModel;

namespace TrayLight.ViewModels;

/// <summary>
/// Display model for one of the feature cards on the welcome screen.
/// </summary>
public sealed class FeatureCardViewModel : ObservableObject
{
    public string IconGlyph { get; init; } = string.Empty;
    public string Title     { get; init; } = string.Empty;
    public string Body      { get; init; } = string.Empty;
}
