using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    [ObservableProperty] private string _title       = "Welcome to TrayLight";
    [ObservableProperty] private string _subtitle    = "Your IT support companion lives in the system tray.";
    [ObservableProperty] private string _logoSource  = string.Empty;
    [ObservableProperty] private string _companyName = string.Empty;
    [ObservableProperty] private Brush  _accentBrush = Brushes.DodgerBlue;
    [ObservableProperty] private bool   _doNotShowAgain;

    public ObservableCollection<FeatureCardViewModel> Features { get; } = new();

    public string PoweredBy        => PoweredByPrefix;
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
            Title     = "System information",
            Body      = "See computer name, OS version, uptime, storage, network, and Entra/Intune status at a glance."
        });
        Features.Add(new FeatureCardViewModel
        {
            IconGlyph = "\uE71B", // Link
            Title     = "Quick access",
            Body      = "One-click shortcuts to your IT portal, knowledge base, and the apps you use every day."
        });
        Features.Add(new FeatureCardViewModel
        {
            IconGlyph = "\uE939", // Help
            Title     = "IT support",
            Body      = "Right-click the tray icon → About → Copy system info to attach a diagnostic dump to your ticket."
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
