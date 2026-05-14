using Microsoft.Extensions.Logging;
using TrayLight.Models;

namespace TrayLight.Services.Logging;

/// <summary>
/// Builds an <see cref="ILoggerFactory"/> wired with the providers requested
/// by <see cref="LoggingConfig"/>. Used both at app startup and from tests.
/// </summary>
public static class TrayLightLoggerFactory
{
    public static ILoggerFactory Create(LoggingConfig config) =>
        Create(config, FileLoggerProvider.DefaultDirectory);

    public static ILoggerFactory Create(LoggingConfig config, string fileDirectory)
    {
        ArgumentNullException.ThrowIfNull(config);

        var minimumLevel = ParseLevel(config.MinimumLevel);

        return LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(minimumLevel);

            if (config.EnableEventLog && OperatingSystem.IsWindows())
                builder.AddProvider(new EventLogLoggerProvider(minimumLevel));

            if (config.EnableFileLog)
                builder.AddProvider(new FileLoggerProvider(
                    fileDirectory, minimumLevel, config.LogRetentionDays));
        });
    }

    internal static LogLevel ParseLevel(string? value) =>
        Enum.TryParse<LogLevel>(value, ignoreCase: true, out var level)
            ? level
            : LogLevel.Information;
}
