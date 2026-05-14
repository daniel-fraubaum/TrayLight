using TrayLight.Models;
using TrayLight.Services;
using TrayLight.Services.UserSettings;
using TrayLight.ViewModels;
using Xunit;

namespace TrayLight.Tests.UserSettings;

public class WelcomeViewModelTests
{
    private sealed class StubConfig : IConfigurationService
    {
        public AppConfiguration Current { get; set; } = new();
        public string ConfigPath { get; } = string.Empty;
        public DateTime? LastLoadedUtc { get; set; } = DateTime.UtcNow;
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public AppConfiguration Load() => Current;
        public void StartWatching() { }
        public void Dispose() { }

        // Suppresses CS0067 — required to satisfy interface contract.
        internal void RaisePropertyChanged() => PropertyChanged?.Invoke(this, new(""));
    }

    private sealed class FakeUserSettings : IUserSettingsService
    {
        public TrayLight.Services.UserSettings.UserSettings Current { get; private set; } = new();
        public TrayLight.Services.UserSettings.UserSettings? Saved { get; private set; }
        public TrayLight.Services.UserSettings.UserSettings Load() => Current;
        public void Save(TrayLight.Services.UserSettings.UserSettings settings)
        {
            Saved = settings;
            Current = settings;
        }
    }

    private sealed class StubLogo : ILogoService
    {
        public string ResolvedLogoPath => string.Empty;
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public void Refresh() { }
        internal void Raise() => PropertyChanged?.Invoke(this, new(""));
    }

    [Fact]
    public void GetStarted_persists_when_DoNotShowAgain_is_set()
    {
        var settings = new FakeUserSettings();
        var sut = new WelcomeViewModel(new StubConfig(), settings, new StubLogo()) { DoNotShowAgain = true };

        var raised = 0;
        sut.CloseRequested += (_, _) => raised++;
        sut.GetStartedCommand.Execute(null);

        Assert.Equal(1, raised);
        Assert.NotNull(settings.Saved);
        Assert.True(settings.Saved!.WelcomeShown);
    }

    [Fact]
    public void GetStarted_does_not_persist_when_DoNotShowAgain_is_unset()
    {
        var settings = new FakeUserSettings();
        var sut = new WelcomeViewModel(new StubConfig(), settings, new StubLogo());
        sut.GetStartedCommand.Execute(null);
        Assert.Null(settings.Saved);
    }

    [Fact]
    public void Constructor_seeds_three_feature_cards()
    {
        var sut = new WelcomeViewModel(new StubConfig(), new FakeUserSettings(), new StubLogo());
        Assert.Equal(3, sut.Features.Count);
        Assert.Contains(sut.Features, f => f.Title.Contains("System"));
        Assert.Contains(sut.Features, f => f.Title.Contains("Quick"));
        Assert.Contains(sut.Features, f => f.Title.Contains("IT support"));
    }

    [Fact]
    public void PoweredBy_is_hardcoded_and_not_overridable()
    {
        var sut = new WelcomeViewModel(new StubConfig(), new FakeUserSettings(), new StubLogo());
        Assert.Equal("Powered by ", sut.PoweredBy);
        Assert.Equal("headsinthecloud.blog", sut.PoweredByLink);
        Assert.Equal("https://headsinthecloud.blog", sut.PoweredByLinkUrl);
    }
}
