using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using TrayLight.Models;

namespace TrayLight.Services.Actions;

/// <summary>
/// Runs a PowerShell command in the user's context with no visible window
/// and a 60-second hard timeout. Stdout/stderr are captured and surfaced via
/// the returned <see cref="ActionResult.Message"/> on failure.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CommandActionHandler : IShortcutActionHandler
{
    public static readonly TimeSpan ExecutionTimeout = TimeSpan.FromSeconds(60);

    private readonly Func<string, CancellationToken, Task<CommandResult>> _runner;

    public CommandActionHandler() : this(RunPowerShellAsync) { }

    internal CommandActionHandler(Func<string, CancellationToken, Task<CommandResult>> runner)
    {
        _runner = runner;
    }

    public ShortcutActionType ActionType => ShortcutActionType.Command;

    public bool IsAvailable(ShortcutConfig config) => !string.IsNullOrWhiteSpace(config.Action);

    public async Task<ActionResult> ExecuteAsync(ShortcutConfig config, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.Action))
            return ActionResult.Fail("No command configured.");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(ExecutionTimeout);

        try
        {
            var result = await _runner(config.Action, cts.Token).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
                return ActionResult.Fail(
                    $"Command exited with code {result.ExitCode}. {Truncate(detail, 200)}");
            }
            return ActionResult.Ok(config.SuccessMessage);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ActionResult.Fail($"Command timed out after {ExecutionTimeout.TotalSeconds:n0}s.");
        }
        catch (Exception ex)
        {
            return ActionResult.Fail("Command failed to start.", ex);
        }
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? string.Empty :
        s.Length <= max ? s : s[..max] + "…";

    public sealed record CommandResult(int ExitCode, string StdOut, string StdErr);

    private static async Task<CommandResult> RunPowerShellAsync(string command, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command.Replace("\"", "\\\"")}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        using var p = Process.Start(psi)
                      ?? throw new InvalidOperationException("powershell.exe could not be started.");

        var stdout = p.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = p.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await p.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw;
        }
        return new CommandResult(p.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }
}
