using AeroIsland.Core;
using AeroIsland.Infrastructure;
using Xunit;

namespace AeroIsland.UnitTests;

public sealed class CoreTests
{
    [Fact]
    public void Settings_are_clamped_and_nonfinite_values_recovered()
    {
        var settings = new AppSettings { CapsuleWidth = 999, Opacity = double.NaN, MaxHistoryItems = -4, AccentColor = "red" }.Validate();
        Assert.Equal(450, settings.CapsuleWidth);
        Assert.Equal(1, settings.Opacity);
        Assert.Equal(10, settings.MaxHistoryItems);
        Assert.Equal("#B9A6FF", settings.AccentColor);
    }

    [Fact]
    public void Previous_defaults_migrate_without_replacing_custom_accent()
    {
        var previous = new AppSettings
        {
            SchemaVersion = 1,
            AccentColor = "#82B8AA",
            CornerRadius = 22,
            AnimationDurationMs = 240
        }.Validate();
        Assert.Equal(2, previous.SchemaVersion);
        Assert.Equal("#B9A6FF", previous.AccentColor);
        Assert.Equal(28, previous.CornerRadius);
        Assert.Equal(340, previous.AnimationDurationMs);
        Assert.Equal("#FF6633", new AppSettings
        {
            SchemaVersion = 1,
            AccentColor = "#FF6633"
        }.Validate().AccentColor);
    }

    [Fact]
    public void History_deduplicates_and_expires_unpinned_items()
    {
        var now = DateTimeOffset.UtcNow;
        var entries = new[]
        {
            new HistoryItem(Guid.NewGuid(), HistoryKind.Text, "new", "new", null, now, false, "same"),
            new HistoryItem(Guid.NewGuid(), HistoryKind.Text, "old duplicate", "old", null, now.AddMinutes(-1), false, "same"),
            new HistoryItem(Guid.NewGuid(), HistoryKind.Text, "expired", "expired", null, now.AddDays(-30), false, "expired"),
            new HistoryItem(Guid.NewGuid(), HistoryKind.Text, "pinned", "pinned", null, now.AddDays(-30), true, "pinned")
        };
        var result = HistoryRules.Normalize(entries, new AppSettings(), now);
        Assert.Equal(2, result.Count);
        Assert.Contains(result, x => x.Preview == "pinned");
    }

    [Fact]
    public void Private_key_markers_are_filtered()
    {
        Assert.True(HistoryRules.ShouldSkipText("-----BEGIN PRIVATE KEY-----"));
        Assert.False(HistoryRules.ShouldSkipText("normal note"));
    }

    [Fact]
    public void Restored_clipboard_item_is_ignored_once()
    {
        var feedback = new ClipboardFeedbackGuard();
        feedback.MarkRestored("abc");
        Assert.False(feedback.ShouldIgnore("other"));
        Assert.True(feedback.ShouldIgnore("abc"));
        Assert.False(feedback.ShouldIgnore("abc"));
    }

    [Fact]
    public async Task Corrupt_settings_recover_to_defaults()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AeroIsland.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new LocalStorage(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "settings.json"), "not json");
            Assert.False((await storage.LoadSettingsAsync()).ClipboardEnabled);
            await storage.SaveSettingsAsync(new AppSettings { CapsuleWidth = 320 });
            Assert.Equal(320, (await storage.LoadSettingsAsync()).CapsuleWidth);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
