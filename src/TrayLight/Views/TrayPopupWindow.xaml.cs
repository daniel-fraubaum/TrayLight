using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using TrayLight.Helpers;
using TrayLight.Services;
using TrayLight.ViewModels;

namespace TrayLight.Views;

public partial class TrayPopupWindow : Window
{
    private readonly IThemeService _themeService;

    private static readonly Uri LightThemeUri = new("/Themes/Light.xaml", UriKind.Relative);
    private static readonly Uri DarkThemeUri  = new("/Themes/Dark.xaml",  UriKind.Relative);

    public TrayPopupWindow(TrayPopupViewModel viewModel, IThemeService themeService)
    {
        InitializeComponent();
        DataContext = viewModel;
        _themeService = themeService;

        ApplyTheme();
        _themeService.ThemeChanged += OnThemeChanged;

        // The window itself is cached by TrayIconViewModel (created once,
        // shown/hidden repeatedly). Recompute live tile data and reset the
        // "Last refreshed" timestamp every time the popup becomes visible.
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true && DataContext is TrayPopupViewModel vm)
                vm.Refresh();
        };
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Note: with AllowsTransparency=True we cannot use Mica/Acrylic
        // backdrops (they require WS_EX_NOREDIRECTIONBITMAP-style composition
        // which conflicts with WPF transparency). The rounded corners + drop
        // shadow are drawn entirely in XAML on the RootBorder instead, so the
        // popup stays stable across all Windows builds.
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        // Hide the popup when the user clicks outside of it (Action Center style).
        Hide();
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(ApplyTheme);
    }

    private void ApplyTheme()
    {
        var dict = Resources.MergedDictionaries.FirstOrDefault(d =>
            d.Source != null &&
            (d.Source.OriginalString.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase) ||
             d.Source.OriginalString.EndsWith("Dark.xaml",  StringComparison.OrdinalIgnoreCase)));
        if (dict is null) return;

        dict.Source = _themeService.Current == AppTheme.Dark ? DarkThemeUri : LightThemeUri;
    }

    private void PoweredBy_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // Best-effort; ignore browser launch failures.
        }
        e.Handled = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _themeService.ThemeChanged -= OnThemeChanged;
        base.OnClosed(e);
    }
}
