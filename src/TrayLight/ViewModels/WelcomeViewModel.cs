using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrayLight.Resources;
using TrayLight.Services;
using TrayLight.Services.UserSettings;

namespace TrayLight.ViewModels;

public partial class WelcomeViewModel : ObservableObject
{
    /// <summary>Hardcoded "Powered by" line — required by spec to always show.</summary>
    public const string PoweredByPrefix   = TrayPopupViewModel.PoweredByPrefix;
    public const string PoweredByLinkText = TrayPopupViewModel.PoweredByLinkText;
    public const string PoweredByUrl      = TrayPopupViewModel.PoweredByUrl;

    private readonly IUserSettingsService _userSettings;

    [ObservableProperty] private string _title       = Strings.WelcomeTitle;
    [ObservableProperty] private string _subtitle    = Strings.WelcomeSubtitle;
    [ObservableProperty] private string _logoSource  = string.Empty;
    [ObservableProperty] private string _companyName = string.Empty;
    [ObservableProperty] private Brush  _accentBrush = Brushes.DodgerBlue;
    [ObservableProperty] private bool   _doNotShowAgain;

    public ObservableCollection<FeatureCardViewModel> Features { get; } = new();

    public string PoweredBy        => Strings.PoweredBy;
    public string PoweredByLink    => PoweredByLinkText;
    public string PoweredByLinkUrl => PoweredByUrl;

    /// <summary>Raised when the user clicks "Get started". The view subscribes to close itself.</summary>
    public event EventHandler? CloseRequested;

    public WelcomeViewModel(IConfigurationService configService, IUserSettingsService userSettings, ILogoService logoService)
    {
        _userSettings = userSettings;

        var branding = configService.Current.Branding;
        LogoSource   = logoService.ResolvedLogoPath;
        CompanyName  = branding.CompanyName;
        AccentBrush  = ParseAccent(branding.AccentColor);

        Features.Add(new FeatureCardViewModel
        {
            IconGlyph = "\uE7F4", // Info / Devices
            Title     = Strings.FeatureSystemInfoTitle,
            Body      = Strings.FeatureSystemInfoBody
        });
        Features.Add(new FeatureCardViewModel
        {
            IconGlyph = "\uE71B", // Link
            Title     = Strings.FeatureQuickAccessTitle,
            Body      = Strings.FeatureQuickAccessBody
        });
        Features.Add(new FeatureCardViewModel
        {
            IconGlyph = "\uE939", // Help
            Title     = Strings.FeatureItSupportTitle,
            Body      = Strings.FeatureItSupportBody
        });
    }

    [RelayCommand]
    private void GetStarted()
    {
        if (DoNotShowAgain)
        {
            try
            {
                var settings = _userSettings.Current;
                settings.WelcomeShown = true;
                _userSettings.Save(settings);
            }
            catch
            {
                // Persisting the preference is best-effort; do not block dismissal.
            }
        }
        CloseRequested?.Invoke(this, EventArgs.Empty);
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
