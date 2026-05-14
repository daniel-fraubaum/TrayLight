using Microsoft.Extensions.Logging;

namespace TrayLight.Services.Logging;

/// <summary>
/// Canonical event ids emitted by TrayLight. The numeric value is also used
/// as the Windows Event Log event id, so external monitoring (Defender for
/// Endpoint, Splunk, Sentinel, ...) can pivot on it.
/// </summary>
public static class LogEvents
{
    public static readonly EventId AppStarted         = new(1000, nameof(AppStarted));
    public static readonly EventId AppStopped         = new(1001, nameof(AppStopped));
    public static readonly EventId ConfigLoaded       = new(1002, nameof(ConfigLoaded));
    public static readonly EventId ConfigError        = new(1003, nameof(ConfigError));
    public static readonly EventId InfoItemUpdated    = new(2000, nameof(InfoItemUpdated));
    public static readonly EventId InfoItemWarning    = new(2001, nameof(InfoItemWarning));
    public static readonly EventId ActionExecuted     = new(3000, nameof(ActionExecuted));
    public static readonly EventId ActionFailed       = new(3001, nameof(ActionFailed));
    public static readonly EventId UnhandledException = new(9000, nameof(UnhandledException));
}
