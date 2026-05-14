namespace TrayLight.Services;

/// <summary>
/// Aggregates the data points commonly required for an IT helpdesk ticket
/// into a single human-readable text block.
/// </summary>
public interface ISystemInfoDumpService
{
    /// <summary>Build a plain-text dump suitable for clipboard/email.</summary>
    string BuildDump();
}
