namespace TrayLight.Services.Actions;

/// <summary>
/// Resolves <c>{{Placeholder}}</c> tokens in a shortcut action to their current
/// values <em>at click-time</em>, so the substituted data (computer name, last
/// Intune sync, …) is always live rather than captured when the policy was read.
/// </summary>
public interface IShortcutPlaceholderResolver
{
    /// <summary>
    /// Expands every supported token in <paramref name="action"/>. Returns the
    /// input unchanged when it contains no tokens. Never throws — a token that
    /// cannot be resolved is replaced with <see cref="ShortcutPlaceholders.Unresolved"/>.
    /// </summary>
    Task<string> ExpandAsync(string action, CancellationToken cancellationToken = default);
}
