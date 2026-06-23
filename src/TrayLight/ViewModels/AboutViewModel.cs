using System.Reflection;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrayLight.Resources;
using TrayLight.Services;

namespace TrayLight.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    public const string AuthorName = "Daniel Fraubaum";
    public const string BlogText   = "headsinthecloud.blog";
    public const string BlogUrl    = "https://headsinthecloud.blog";

    private readonly ISystemInfoDumpService _dump;

    [ObservableProperty] private string _appName     = "TrayLight";
    [ObservableProperty] private string _version     = "0.0.0";
    [ObservableProperty] private string _companyName = string.Empty;
    [ObservableProperty] private string _copyright   = string.Empty;
    [ObservableProperty] private string _logoSource  = string.Empty;
    [ObservableProperty] private Brush  _accentBrush = Brushes.DodgerBlue;
    [ObservableProperty] private string _systemInfoDump = string.Empty;
    [ObservableProperty] private string _copyButtonText = Strings.CopySystemInfo;

    public string BlogLinkText => BlogText;
    public string BlogLinkUrl  => BlogUrl;

    public AboutViewModel(IConfigurationService configService, ISystemInfoDumpService dump, ILogoService logoService)
    {
        _dump = dump;

        var asm  = Assembly.GetExecutingAssembly();
        Version  = asm.GetName().Version?.ToString(3) ?? "0.0.0";
        // No year in the copyright line so the dialog never looks stale if a
        // build is shipped late in the year or after a quiet period.
        Copyright = $"© {BlogText} - {AuthorName}. All rights reserved.";

        var branding = configService.Current.Branding;
        LogoSource   = logoService.ResolvedLogoPath;
        CompanyName  = string.IsNullOrWhiteSpace(branding.CompanyName)
            ? string.Empty
            : $"Deployed for {branding.CompanyName}";
        AccentBrush  = ParseAccent(branding.AccentColor);

        // Build the dump lazily on the UI thread when the view first binds —
        // it reads registry + WMI so doing it on the ctor would block startup.
        SystemInfoDump = Strings.CollectingSystemInfo;
        _ = LoadDumpAsync();
    }

    private async Task LoadDumpAsync()
    {
        try
        {
            var dump = await Task.Run(() => _dump.BuildDump());
            SystemInfoDump = dump;
        }
        catch (Exception ex)
        {
            SystemInfoDump = $"(failed to collect system info: {ex.Message})";
        }
    }

    [RelayCommand]
    private void CopySystemInfo()
    {
        try
        {
            Clipboard.SetText(SystemInfoDump);
            CopyButtonText = Strings.Copied;
        }
        catch
        {
            CopyButtonText = Strings.CopyFailed;
        }
    }

    private static Brush ParseAccent(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Brushes.DodgerBlue;
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
        catch
        {
            return Brushes.DodgerBlue;
        }
    }
}
