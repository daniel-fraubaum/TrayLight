namespace TrayLight.Services.Badges;

/// <summary>One active warning attributed to a specific info-item provider.</summary>
public sealed record WarningEntry(string TypeKey, string Title, string Message);

/// <summary>Snapshot of the aggregate badge state at a moment in time.</summary>
public sealed record BadgeState(IReadOnlyList<WarningEntry> Warnings)
{
    public static readonly BadgeState Empty = new(Array.Empty<WarningEntry>());

    public int Count => Warnings.Count;
    public bool HasWarnings => Warnings.Count > 0;
}
