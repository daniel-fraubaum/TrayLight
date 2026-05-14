using System.IO;
using System.Runtime.Versioning;

namespace TrayLight.Services.Providers;

/// <summary>System-drive (C:) used / total / percent with optional warning.</summary>
[SupportedOSPlatform("windows")]
public sealed class StorageUsedProvider : InfoItemProviderBase
{
    public const string TypeKey = "storageUsed";

    public override string Type => TypeKey;
    protected override string DefaultTitle => "Storage";
    protected override string DefaultIcon => "Segoe Fluent Icons:EDA2"; // HardDrive

    private readonly Func<DriveStats?> _driveStatsProvider;

    public StorageUsedProvider() : this(ReadSystemDrive) { }

    internal StorageUsedProvider(Func<DriveStats?> driveStatsProvider)
    {
        _driveStatsProvider = driveStatsProvider;
    }

    protected override Task<InfoItemData> CollectAsync(CancellationToken cancellationToken)
    {
        var stats = _driveStatsProvider();
        if (stats is null)
        {
            return Task.FromResult(InfoItemData.Unavailable(EffectiveTitle, EffectiveIcon,
                "System drive not accessible."));
        }

        var usedGb = ToGb(stats.TotalBytes - stats.FreeBytes);
        var totalGb = ToGb(stats.TotalBytes);
        var freeGb = ToGb(stats.FreeBytes);
        var percent = stats.TotalBytes == 0 ? 0
            : (int)Math.Round(100.0 * (stats.TotalBytes - stats.FreeBytes) / stats.TotalBytes);

        var limit = Config?.StorageLimit ?? 0;
        var hasWarning = limit > 0 && percent >= limit;
        var warningMessage = hasWarning
            ? $"System drive is {percent}% full."
            : string.Empty;

        var data = new InfoItemData(
            Title: EffectiveTitle,
            Value: $"{usedGb} / {totalGb} GB ({percent}%)",
            DetailText: $"{freeGb} GB free",
            HasWarning: hasWarning,
            WarningMessage: warningMessage,
            Icon: EffectiveIcon);
        return Task.FromResult(data);
    }

    protected override Task OnClickAsync(CancellationToken cancellationToken)
    {
        LaunchShell("ms-settings:storagesense");
        return Task.CompletedTask;
    }

    private static int ToGb(long bytes) => (int)Math.Round(bytes / 1024.0 / 1024.0 / 1024.0);

    private static DriveStats? ReadSystemDrive()
    {
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        var drive = new DriveInfo(systemRoot);
        if (!drive.IsReady) return null;
        return new DriveStats(drive.TotalSize, drive.AvailableFreeSpace);
    }

    public sealed record DriveStats(long TotalBytes, long FreeBytes);
}
