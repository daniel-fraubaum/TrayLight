using Microsoft.Extensions.Logging;
using TrayLight.Models;
using TrayLight.Services.Logging;
using Xunit;

namespace TrayLight.Tests.Logging;

public class TrayLightLoggerFactoryTests
{
    [Fact]
    public void Create_returns_a_factory_that_produces_loggers()
    {
        var factory = TrayLightLoggerFactory.Create(new LoggingConfig
        {
            EnableEventLog = false,
            EnableFileLog  = false,
            MinimumLevel   = "Warning"
        });

        var logger = factory.CreateLogger("X");
        Assert.NotNull(logger);
        // Without providers nothing should be enabled below Warning.
        Assert.False(logger.IsEnabled(LogLevel.Information));
    }

    [Theory]
    [InlineData("Trace",       LogLevel.Trace)]
    [InlineData("debug",       LogLevel.Debug)]
    [InlineData("INFORMATION", LogLevel.Information)]
    [InlineData("warning",     LogLevel.Warning)]
    [InlineData("Error",       LogLevel.Error)]
    [InlineData("Critical",    LogLevel.Critical)]
    [InlineData("None",        LogLevel.None)]
    [InlineData("nonsense",    LogLevel.Information)]
    [InlineData(null,          LogLevel.Information)]
    public void ParseLevel_is_case_insensitive_and_falls_back_to_Information(string? value, LogLevel expected)
    {
        Assert.Equal(expected, TrayLightLoggerFactory.ParseLevel(value));
    }
}
