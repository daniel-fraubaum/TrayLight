using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace TrayLight.Services.Logging;

/// <summary>
/// Daily rolling file logger. Files live at
/// <c>%LOCALAPPDATA%\TrayLight\Logs\traylight-YYYY-MM-DD.log</c> and any
/// file older than <c>retentionDays</c> is purged the first time a logger
/// in the provider is created.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    public const string FileNamePrefix = "traylight-";
    public const string FileNameSuffix = ".log";

    private readonly string _directory;
    private readonly LogLevel _minimumLevel;
    private readonly object _writeGate = new();

    public FileLoggerProvider(string directory, LogLevel minimumLevel, int retentionDays)
    {
        _directory    = directory ?? throw new ArgumentNullException(nameof(directory));
        _minimumLevel = minimumLevel;

        try
        {
            Directory.CreateDirectory(_directory);
            PruneOldFiles(_directory, Math.Max(0, retentionDays));
        }
        catch
        {
            // File logging is best-effort; never crash the host on init.
        }
    }

    /// <summary>Default location: <c>%LOCALAPPDATA%\TrayLight\Logs</c>.</summary>
    public static string DefaultDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TrayLight",
            "Logs");

    public ILogger CreateLogger(string categoryName) =>
        new FileLogger(categoryName, _minimumLevel, _directory, _writeGate);

    public void Dispose() { }

    /// <summary>Deletes log files whose UTC date is older than <paramref name="retentionDays"/>.</summary>
    public static int PruneOldFiles(string directory, int retentionDays)
    {
        if (!Directory.Exists(directory)) return 0;

        var cutoff = DateTime.UtcNow.Date.AddDays(-Math.Max(0, retentionDays));
        int removed = 0;
        foreach (var file in Directory.EnumerateFiles(directory, FileNamePrefix + "*" + FileNameSuffix))
        {
            if (TryParseDate(Path.GetFileName(file), out var date) && date < cutoff)
            {
                try { File.Delete(file); removed++; }
                catch { /* skip locked / inaccessible files */ }
            }
        }
        return removed;
    }

    internal static bool TryParseDate(string fileName, out DateTime date)
    {
        date = default;
        if (!fileName.StartsWith(FileNamePrefix, StringComparison.OrdinalIgnoreCase)) return false;
        if (!fileName.EndsWith(FileNameSuffix, StringComparison.OrdinalIgnoreCase))   return false;

        var middle = fileName.Substring(
            FileNamePrefix.Length,
            fileName.Length - FileNamePrefix.Length - FileNameSuffix.Length);

        return DateTime.TryParseExact(
            middle, "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out date);
    }

    internal static string FileNameFor(DateTime utc) =>
        FileNamePrefix + utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + FileNameSuffix;

    private sealed class FileLogger : ILogger
    {
        private readonly string _category;
        private readonly LogLevel _minimumLevel;
        private readonly string _directory;
        private readonly object _writeGate;

        public FileLogger(string category, LogLevel minimumLevel, string directory, object writeGate)
        {
            _category     = category;
            _minimumLevel = minimumLevel;
            _directory    = directory;
            _writeGate    = writeGate;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull =>
            NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel != LogLevel.None && logLevel >= _minimumLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel) || formatter is null) return;

            var message = formatter(state, exception);
            if (string.IsNullOrEmpty(message) && exception is null) return;

            var sb = new StringBuilder(256);
            sb.Append(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
              .Append(" [").Append(LevelTag(logLevel)).Append("] ");
            if (eventId.Id != 0)
                sb.Append("EVT").Append(eventId.Id.ToString(CultureInfo.InvariantCulture)).Append(' ');
            sb.Append('[').Append(_category).Append("] ").Append(message);
            if (exception is not null)
                sb.Append(Environment.NewLine).Append(exception);
            sb.Append(Environment.NewLine);

            var path = Path.Combine(_directory, FileNameFor(DateTime.UtcNow));
            try
            {
                lock (_writeGate)
                {
                    File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // Best-effort logging — never throw from a logger.
            }
        }

        private static string LevelTag(LogLevel level) => level switch
        {
            LogLevel.Trace       => "TRC",
            LogLevel.Debug       => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning     => "WRN",
            LogLevel.Error       => "ERR",
            LogLevel.Critical    => "CRT",
            _                    => "   ",
        };
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
