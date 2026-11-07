using System.Text.Json;
using AeroIsland.Core;

namespace AeroIsland.Infrastructure;

public sealed class LocalStorage
{
    private readonly string _directory;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    public string DirectoryPath => _directory;
    public LocalStorage(string? directory = null)
    {
        _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AeroIsland");
        Directory.CreateDirectory(_directory);
        Directory.CreateDirectory(Path.Combine(_directory, "images"));
    }

    public async Task<AppSettings> LoadSettingsAsync()
    {
        try
        {
            await using var stream = File.OpenRead(Path.Combine(_directory, "settings.json"));
            return (await JsonSerializer.DeserializeAsync<AppSettings>(stream, _json) ?? new AppSettings()).Validate();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public Task SaveSettingsAsync(AppSettings settings) => SaveAtomicAsync("settings.json", settings.Validate());

    public async Task<IReadOnlyList<HistoryItem>> LoadHistoryAsync(AppSettings settings)
    {
        try
        {
            await using var stream = File.OpenRead(Path.Combine(_directory, "history.json"));
            if (stream.Length > 16 * 1024 * 1024) return [];
            var items = await JsonSerializer.DeserializeAsync<List<HistoryItem>>(stream, _json) ?? [];
            var imageRoot = Path.GetFullPath(Path.Combine(_directory, "images")) + Path.DirectorySeparatorChar;
            var safe = items.Where(x => x.Text is null || x.Text.Length <= settings.MaxTextLength)
                .Where(x => x.Preview is not null && x.Fingerprint is not null)
                .Where(x => x.ImageFile is null || SafeImagePath(x.ImageFile, imageRoot));
            return HistoryRules.Normalize(safe, settings, DateTimeOffset.UtcNow);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public Task SaveHistoryAsync(IEnumerable<HistoryItem> items, AppSettings settings) =>
        SaveAtomicAsync("history.json", HistoryRules.Normalize(items, settings, DateTimeOffset.UtcNow));

    public string NewImagePath() => Path.Combine(_directory, "images", $"{Guid.NewGuid():N}.png");

    private static bool SafeImagePath(string path, string root)
    {
        try { return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    public void PruneImages(IEnumerable<HistoryItem> items)
    {
        var kept = items.Where(x => x.ImageFile is not null).Select(x => x.ImageFile!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var imageDirectory = Path.Combine(_directory, "images");
        foreach (var file in Directory.EnumerateFiles(imageDirectory, "*.png"))
        {
            if (!kept.Contains(file))
            {
                try { File.Delete(file); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private async Task SaveAtomicAsync<T>(string name, T value)
    {
        await _writeGate.WaitAsync();
        try
        {
            var target = Path.Combine(_directory, name);
            var temporary = target + ".tmp";
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, _json);
                await stream.FlushAsync();
            }
            File.Move(temporary, target, true);
        }
        finally { _writeGate.Release(); }
    }
}
