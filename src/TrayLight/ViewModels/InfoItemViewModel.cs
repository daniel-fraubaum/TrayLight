using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrayLight.Models;

namespace TrayLight.ViewModels;

/// <summary>Viewmodel backing a single tile in the info grid.</summary>
public partial class InfoItemViewModel : ObservableObject
{
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _value = string.Empty;
    [ObservableProperty] private string _valueTooltip = string.Empty;
    [ObservableProperty] private string _iconGlyph = "\uE946";   // info circle
    [ObservableProperty] private bool   _hasWarning;
    [ObservableProperty] private string _warningSeverity = "warning"; // "warning" | "danger"
    [ObservableProperty] private bool   _isClickable = true;

    public InfoItemType Type { get; init; } = InfoItemType.Unknown;
    public int Position    { get; init; } = -1;
    public ICommand? ClickCommand { get; set; }

    /// <summary>Default click handler — override via <see cref="ClickCommand"/>.</summary>
    [RelayCommand]
    private void Activate() => ClickCommand?.Execute(this);
}
