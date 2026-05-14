using System.IO;
using Microsoft.Extensions.Logging;
using TrayLight.Services.Logging;
using Xunit;

namespace TrayLight.Tests.Logging;

public class FileLoggerProviderTests : IDisposable
{
    private readonly string _dir;

    public FileLoggerProviderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "TrayLight.Tests.Logs." + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Writing_an_entry_creates_a_dated_log_file()
    {
        using var provider = new FileLoggerProvider(_dir, LogLevel.Information, retentionDays: 7);
        var logger = provider.CreateLogger("Cat.Test");

        logger.LogInformation(LogEvents.AppStarted, "Hello {What}", "world");

        var fileName = FileLoggerProvider.FileNamePrefix
                     + DateTime.UtcNow.ToString("yyyy-MM-dd")
                     + FileLoggerProvider.FileNameSuffix;
        var path = Path.Combine(_dir, fileName);
        Assert.True(File.Exists(path));

        var content = File.ReadAllText(path);
        Assert.Contains("EVT1000", content);
        Assert.Contains("[Cat.Test]", content);
        Assert.Contains("Hello world", content);
        Assert.Contains("[INF]", content);
    }

    [Fact]
    public void Entries_below_minimum_level_are_skipped()
    {
        using var provider = new FileLoggerProvider(_dir, LogLevel.Warning, retentionDays: 7);
        var logger = provider.CreateLogger("X");

        logger.LogInformation("ignored");
        logger.LogWarning("kept");

        var files = Directory.GetFiles(_dir);
        Assert.Single(files);
        var content = File.ReadAllText(files[0]);
        Assert.DoesNotContain("ignored", content);
        Assert.Contains("kept", content);
    }

    [Fact]
    public void PruneOldFiles_deletes_files_older_than_retention()
    {
        Directory.CreateDirectory(_dir);
        var keep   = Path.Combine(_dir, FileLoggerProvider.FileNameFor(DateTime.UtcNow));
        var prune  = Path.Combine(_dir, FileLoggerProvider.FileNameFor(DateTime.UtcNow.AddDays(-30)));
        var unrelated = Path.Combine(_dir, "notes.txt");
        File.WriteAllText(keep, "k");
        File.WriteAllText(prune, "p");
        File.WriteAllText(unrelated, "u");

        var removed = FileLoggerProvider.PruneOldFiles(_dir, retentionDays: 7);

        Assert.Equal(1, removed);
        Assert.True(File.Exists(keep));
        Assert.False(File.Exists(prune));
        Assert.True(File.Exists(unrelated)); // untouched: doesn't match log pattern
    }

    [Theory]
    [InlineData("traylight-2026-01-01.log", true,  "2026-01-01")]
    [InlineData("traylight-2026-13-01.log", false, "")]
    [InlineData("trace.log",                false, "")]
    [InlineData("traylight-NOPE.log",       false, "")]
    public void TryParseDate_handles_expected_inputs(string name, bool expected, string isoDate)
    {
        var ok = FileLoggerProvider.TryParseDate(name, out var date);
        Assert.Equal(expected, ok);
        if (expected)
            Assert.Equal(isoDate, date.ToString("yyyy-MM-dd"));
    }
}
