using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrayLight.Models;
using TrayLight.Services.Actions;

namespace TrayLight.ViewModels;

/// <summary>Viewmodel backing a single shortcut button.</summary>
public partial class ShortcutViewModel : ObservableObject
{
    private readonly IActionExecutor? _executor;
    private readonly ShortcutConfig _config;

    public ShortcutViewModel() : this(new ShortcutConfig(), executor: null) { }

    public ShortcutViewModel(ShortcutConfig config, IActionExecutor? executor)
    {
        _config = config;
        _executor = executor;
        Title    = config.Title;
        Subtitle = config.Subtitle;
        Position = config.Position;
        ActionType = config.ActionType;
        Action     = config.Action;
    }

    [ObservableProperty] private string _title    = string.Empty;
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _iconGlyph = "\uE8A7"; // generic open

    public int Position { get; init; } = -1;
    public ShortcutActionType ActionType { get; init; } = ShortcutActionType.Unknown;
    public string Action { get; init; } = string.Empty;

    [RelayCommand]
    private async Task ActivateAsync()
    {
        if (_executor is null) return;
        await _executor.ExecuteAsync(_config).ConfigureAwait(false);
    }
}
