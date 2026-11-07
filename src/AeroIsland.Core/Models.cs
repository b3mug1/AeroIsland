namespace AeroIsland.Core;

public enum IslandTheme { System, Dark, Light }
public enum FullscreenBehavior { Hide, PauseAnimations, RemainVisible }
public enum ScreenEdge { Top, Bottom }
public enum HistoryKind { Text, Url, Image }

public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 2;
    public bool ClipboardEnabled { get; init; }
    public bool ClipboardPaused { get; init; }
    public bool StartWithWindows { get; init; }
    public bool MinimizeToTray { get; init; } = true;
    public bool HoverToExpand { get; init; }
    public int HoverDelayMs { get; init; } = 400;
    public string Hotkey { get; init; } = "Ctrl+Alt+Space";
    public string MonitorDeviceName { get; init; } = "";
    public ScreenEdge ScreenEdge { get; init; } = ScreenEdge.Top;
    public IslandTheme Theme { get; init; } = IslandTheme.Dark;
    public string AccentColor { get; init; } = "#B9A6FF";
    public int CapsuleWidth { get; init; } = 220;
    public double Scale { get; init; } = 1;
    public int CornerRadius { get; init; } = 28;
    public double Opacity { get; init; } = 1;
    public bool AnimationsEnabled { get; init; } = true;
    public bool ReducedMotion { get; init; }
    public int AnimationDurationMs { get; init; } = 340;
    public bool ShowArtwork { get; init; } = true;
    public bool ShowTimeline { get; init; } = true;
    public int ArtworkCacheMiB { get; init; } = 8;
    public int MaxHistoryItems { get; init; } = 100;
    public int MaxTextLength { get; init; } = 10_000;
    public int MaxImageDimension { get; init; } = 1024;
    public int MaxImageCacheMiB { get; init; } = 32;
    public int RetentionDays { get; init; } = 7;
    public bool DiagnosticsEnabled { get; init; }
    public FullscreenBehavior FullscreenBehavior { get; init; } = FullscreenBehavior.Hide;

    public AppSettings Validate() => this with
    {
        SchemaVersion = 2,
        HoverDelayMs = Math.Clamp(HoverDelayMs, 100, 2000),
        CapsuleWidth = Math.Clamp(CapsuleWidth, 150, 450),
        Scale = double.IsFinite(Scale) ? Math.Clamp(Scale, 0.75, 1.5) : 1,
        CornerRadius = Math.Clamp(SchemaVersion < 2 && CornerRadius == 22 ? 28 : CornerRadius, 8, 36),
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0.65, 1) : 1,
        AnimationDurationMs = Math.Clamp(SchemaVersion < 2 && AnimationDurationMs == 240 ? 340 : AnimationDurationMs, 0, 500),
        ArtworkCacheMiB = Math.Clamp(ArtworkCacheMiB, 1, 64),
        MaxHistoryItems = Math.Clamp(MaxHistoryItems, 10, 500),
        MaxTextLength = Math.Clamp(MaxTextLength, 100, 100_000),
        MaxImageDimension = Math.Clamp(MaxImageDimension, 128, 2048),
        MaxImageCacheMiB = Math.Clamp(MaxImageCacheMiB, 4, 256),
        RetentionDays = Math.Clamp(RetentionDays, 1, 90),
        AccentColor = System.Text.RegularExpressions.Regex.IsMatch(AccentColor ?? "", "^#[0-9A-Fa-f]{6}$")
            ? (SchemaVersion < 2 && string.Equals(AccentColor, "#82B8AA", StringComparison.OrdinalIgnoreCase) ? "#B9A6FF" : AccentColor!)
            : "#B9A6FF",
        Hotkey = Hotkey is "Ctrl+Alt+Space" or "Ctrl+Shift+Space" ? Hotkey : "Ctrl+Alt+Space",
        Theme = Enum.IsDefined(Theme) ? Theme : IslandTheme.Dark,
        FullscreenBehavior = Enum.IsDefined(FullscreenBehavior) ? FullscreenBehavior : FullscreenBehavior.Hide,
        ScreenEdge = Enum.IsDefined(ScreenEdge) ? ScreenEdge : ScreenEdge.Top
    };
}

public sealed record HistoryItem(Guid Id, HistoryKind Kind, string Preview, string? Text, string? ImageFile,
    DateTimeOffset CreatedUtc, bool Pinned, string Fingerprint);

public sealed record MediaSnapshot(string SessionId, string Title, string Artist, string Album, bool IsPlaying,
    bool CanPlayPause, bool CanPrevious, bool CanNext, bool CanSeek, TimeSpan Position, TimeSpan Duration,
    byte[]? Artwork);

public static class HistoryRules
{
    public static IReadOnlyList<HistoryItem> Normalize(IEnumerable<HistoryItem> source, AppSettings settings, DateTimeOffset now)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return source.Where(x => x.Pinned || now - x.CreatedUtc < TimeSpan.FromDays(settings.RetentionDays))
            .OrderByDescending(x => x.CreatedUtc)
            .Where(x => seen.Add(x.Fingerprint))
            .Take(settings.MaxHistoryItems).ToArray();
    }

    public static string TextFingerprint(string value) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));

    public static bool ShouldSkipText(string value) => string.IsNullOrWhiteSpace(value) ||
        value.Contains("BEGIN PRIVATE KEY", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("BEGIN OPENSSH PRIVATE KEY", StringComparison.OrdinalIgnoreCase);
}

public sealed class ClipboardFeedbackGuard
{
    private string? _fingerprint;
    public void MarkRestored(string fingerprint) => _fingerprint = fingerprint;
    public bool ShouldIgnore(string fingerprint)
    {
        if (_fingerprint != fingerprint) return false;
        _fingerprint = null;
        return true;
    }
}
