using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using TrayLight.Helpers;
using TrayLight.Services;
using TrayLight.ViewModels;

namespace TrayLight.Views;

public partial class AboutWindow : Window
{
    private readonly IThemeService _themeService;

    private static readonly Uri LightThemeUri = new("/Themes/Light.xaml", UriKind.Relative);
    private static readonly Uri DarkThemeUri  = new("/Themes/Dark.xaml",  UriKind.Relative);

    public AboutWindow(AboutViewModel vm, IThemeService themeService)
    {
        InitializeComponent();
        DataContext = vm;
        _themeService = themeService;
        ApplyTheme();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        MicaHelper.Apply(this, useDarkMode: _themeService.Current == AppTheme.Dark);
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch { /* best effort */ }
        e.Handled = true;
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
}
