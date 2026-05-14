using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using TrayLight.Services;
using TrayLight.Services.Badges;
using TrayLight.Views;

namespace TrayLight.ViewModels;

public partial class TrayIconViewModel : ObservableObject
{
    // Pre-built resource URIs. These are plain WPF Resources embedded into
    // the TrayLight assembly via <Resource> in the .csproj — no runtime
    // compositing happens, so H.NotifyIcon.Wpf gets a BitmapImage with a
    // valid UriSource it can stream to a Win32 HICON.
    private static readonly Uri NormalIconUri = new(
        "pack://application:,,,/TrayLight;component/Assets/app-normal.ico",
        UriKind.Absolute);
    private static readonly Uri WarningIconUri = new(
        "pack://application:,,,/TrayLight;component/Assets/app-warning.ico",
        UriKind.Absolute);

    private static readonly ImageSource? NormalIcon  = TryLoadIcon(NormalIconUri);
    private static readonly ImageSource? WarningIcon = TryLoadIcon(WarningIconUri);

    private readonly IServiceProvider _services;
    private readonly IPopupPositioningService _positioning;
    private readonly INotificationBadgeService _badges;
    private TrayPopupWindow? _popup;

    [ObservableProperty] private ImageSource? _iconSource = NormalIcon;
    [ObservableProperty] private string _toolTipText = "TrayLight";

    public TrayIconViewModel(
        IServiceProvider services,
        IPopupPositioningService positioning,
        INotificationBadgeService badges)
    {
        _services       = services;
        _positioning    = positioning;
        _badges         = badges;

        ApplyBadgeState(_badges.Current);
        _badges.BadgeChanged += (_, state) =>
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
                ApplyBadgeState(state);
            else
                dispatcher.BeginInvoke(() => ApplyBadgeState(state));
        };
    }

    private void ApplyBadgeState(BadgeState state)
    {
        try
        {
            var target = state.HasWarnings ? WarningIcon : NormalIcon;
            if (target is not null)
                IconSource = target;
        }
        catch
        {
            // Never let a tray-icon swap crash the app — keep the previous icon.
        }

        ToolTipText = state.HasWarnings
            ? $"TrayLight — {state.Count} warning{(state.Count == 1 ? "" : "s")}"
            : "TrayLight";
    }

    private static ImageSource? TryLoadIcon(Uri uri)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource   = uri;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    [RelayCommand]
    private void ShowPopup()
    {
        if (_popup is { IsVisible: true })
        {
            _popup.Hide();
            return;
        }

        _popup ??= _services.GetRequiredService<TrayPopupWindow>();
        _positioning.PositionAboveTray(_popup);
        _popup.Show();
        _popup.Activate();
    }

    [RelayCommand]
    private void About()
    {
        var window = _services.GetRequiredService<AboutWindow>();
        window.ShowDialog();
    }

    [RelayCommand]
    private void Refresh()
    {
        _services.GetRequiredService<IConfigurationService>().Load();
    }

    [RelayCommand]
    private void Quit()
    {
        Application.Current.Shutdown();
    }
}
