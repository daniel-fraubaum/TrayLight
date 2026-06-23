using Xunit;

namespace TrayLight.Tests.Startup;

/// <summary>
/// Verifies the welcome-screen visibility precedence: the ADMX policy
/// <c>Behavior\ShowWelcomeScreen</c> overrides the per-user setting.
/// </summary>
public class WelcomeVisibilityTests
{
    [Theory]
    // policy Disabled (false) => never show, regardless of the user setting.
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    // policy Enabled / Not Configured (true) => fall back to the user setting:
    // show only when the user has not yet dismissed it.
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void ShouldShowWelcome_RespectsPolicyOverUserSetting(
        bool policyShowWelcome, bool welcomeShown, bool expected)
    {
        var actual = global::TrayLight.App.ShouldShowWelcome(policyShowWelcome, welcomeShown);
        Assert.Equal(expected, actual);
    }
}
