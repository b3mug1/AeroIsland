namespace AeroIsland.Core;

public enum LiveActivityKind { Battery, Volume, Microphone, ScreenRecording, Download, Notification, Stopwatch }

/// <summary>Presentation data supplied by a system adapter or an in-app operation.</summary>
public sealed record LiveActivity(LiveActivityKind Kind, string Title, string Detail, double? Progress = null)
{
    public LiveActivity Validate() => this with
    {
        Title = (Title ?? "")[..Math.Min(Title?.Length ?? 0, 160)],
        Detail = (Detail ?? "")[..Math.Min(Detail?.Length ?? 0, 300)],
        Progress = Progress is { } value && double.IsFinite(value) ? Math.Clamp(value, 0, 1) : null
    };
}
