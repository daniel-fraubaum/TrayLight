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
    private readonly IPopupPositioningService _positioning;

    // Transparent margin around RootBorder (drop shadow) + gap to the taskbar.
    private const double EdgeMargin = 12;

    private static readonly Uri LightThemeUri = new("/Themes/Light.xaml", UriKind.Relative);
    private static readonly Uri DarkThemeUri  = new("/Themes/Dark.xaml",  UriKind.Relative);

    public TrayPopupWindow(TrayPopupViewModel viewModel, IThemeService themeService,
        IPopupPositioningService positioning)
    {
        InitializeComponent();
        DataContext = viewModel;
        _themeService = themeService;
        _positioning = positioning;

        ApplyTheme();
        _themeService.ThemeChanged += OnThemeChanged;

        // Re-anchor and apply the safety-net tile height whenever the content
        // resizes the window (SizeToContent=Height), so the popup grows upward
        // from a fixed bottom edge above the taskbar.
        SizeChanged += OnSizeChanged;

        // The window itself is cached by TrayIconViewModel (created once,
        // shown/hidden repeatedly). Recompute live tile data and reset the
        // "Last refreshed" timestamp every time the popup becomes visible.
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true && DataContext is TrayPopupViewModel vm)
                vm.Refresh();
        };
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var workArea = _positioning.GetWorkArea(this);

        // Height of everything except the scrollable tile area (header, accent,
        // quick actions, info text, footer). Independent of the tile count.
        var chrome = ActualHeight - TileScroll.ActualHeight;
        var availableForTiles = workArea.Height - (EdgeMargin * 2) - chrome;

        // Only cap the tile area at the usable screen height. While the content
        // fits, this cap is larger than the tiles so the ScrollViewer sizes to
        // content and never scrolls - the window just grows taller.
        if (availableForTiles > 0 && Math.Abs(TileScroll.MaxHeight - availableForTiles) > 0.5)
            TileScroll.MaxHeight = availableForTiles;

        // Bottom-anchored above the taskbar: the bottom edge stays put and the
        // top edge moves up as the window gets taller.
        Left = workArea.Right  - ActualWidth  - EdgeMargin;
        Top  = workArea.Bottom - ActualHeight - EdgeMargin;
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
