using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace TrayLight.Services.Logging;

/// <summary>
/// <see cref="ILoggerProvider"/> that writes structured entries to the Windows
/// Application event log under the source <c>TrayLight</c>. The numeric value
/// of <see cref="EventId.Id"/> is used verbatim as the Windows event id.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EventLogLoggerProvider : ILoggerProvider
{
    public const string SourceName = "TrayLight";
    public const string LogName    = "Application";

    private readonly LogLevel _minimumLevel;
    private readonly bool _sourceReady;

    public EventLogLoggerProvider(LogLevel minimumLevel)
    {
        _minimumLevel = minimumLevel;
        _sourceReady  = EnsureSource();
    }

    public ILogger CreateLogger(string categoryName) =>
        new EventLogLogger(categoryName, _minimumLevel, _sourceReady);

    public void Dispose() { }

    private static bool EnsureSource()
    {
        try
        {
            if (EventLog.SourceExists(SourceName)) return true;
            // CreateEventSource requires admin. If we are not elevated this
            // will throw and we silently disable event-log writes.
            EventLog.CreateEventSource(SourceName, LogName);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class EventLogLogger : ILogger
    {
        private readonly string _category;
        private readonly LogLevel _minimumLevel;
        private readonly bool _sourceReady;

        public EventLogLogger(string category, LogLevel minimumLevel, bool sourceReady)
        {
            _category = category;
            _minimumLevel = minimumLevel;
            _sourceReady = sourceReady;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull =>
            NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) =>
            _sourceReady && logLevel != LogLevel.None && logLevel >= _minimumLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            if (formatter is null) return;

            var message = formatter(state, exception);
            if (string.IsNullOrEmpty(message) && exception is null) return;

            var text = exception is null
                ? $"[{_category}] {message}"
                : $"[{_category}] {message}{Environment.NewLine}{exception}";

            // Event-Log entries are capped at 32 766 characters.
            if (text.Length > 30000) text = text[..30000] + "…";

            try
            {
                EventLog.WriteEntry(SourceName, text, MapEntryType(logLevel), eventId.Id);
            }
            catch
            {
                // Best-effort logging.
            }
        }

        private static EventLogEntryType MapEntryType(LogLevel level) => level switch
        {
            LogLevel.Critical or LogLevel.Error => EventLogEntryType.Error,
            LogLevel.Warning                    => EventLogEntryType.Warning,
            _                                   => EventLogEntryType.Information,
        };
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
