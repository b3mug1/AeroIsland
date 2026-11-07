using AeroIsland.Core;
using Windows.Media.Control;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace AeroIsland.Windows;

public sealed class MediaService : IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private string? _selectedId;
    private string? _artKey;
    private byte[]? _cachedArt;
    private int _generation;
    public int MaxArtworkBytes { get; set; } = 2 * 1024 * 1024;
    public bool TimelineUpdatesEnabled { get; set; }
    public event EventHandler<MediaSnapshot?>? Changed;
    public IReadOnlyList<string> SessionIds => _manager?.GetSessions().Select(s => s.SourceAppUserModelId).ToArray() ?? [];

    public async Task StartAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.SessionsChanged += OnSessionsChanged;
            Select();
        }
        catch (Exception e) when (e is UnauthorizedAccessException or NotSupportedException or System.Runtime.InteropServices.COMException)
        {
            Changed?.Invoke(this, null);
        }
    }

    public void ChooseSession(string? id) { _selectedId = id; Select(); }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) => Select();

    private void Select()
    {
        GlobalSystemMediaTransportControlsSession? next;
        try
        {
            next = _manager?.GetSessions().FirstOrDefault(s => s.SourceAppUserModelId == _selectedId)
                ?? _manager?.GetCurrentSession();
        }
        catch (System.Runtime.InteropServices.COMException) { Changed?.Invoke(this, null); return; }
        if (!ReferenceEquals(next, _session))
        {
            Detach();
            _session = next;
            if (next is not null)
            {
                next.MediaPropertiesChanged += OnMediaChanged;
                next.PlaybackInfoChanged += OnPlaybackChanged;
                next.TimelinePropertiesChanged += OnTimelineChanged;
            }
        }
        _ = RefreshAsync();
    }

    private void OnMediaChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) => _ = RefreshAsync();
    private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => _ = RefreshAsync();
    private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
    {
        if (TimelineUpdatesEnabled) _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var session = _session;
        var generation = Interlocked.Increment(ref _generation);
        if (session is null) { Changed?.Invoke(this, null); return; }
        try
        {
            var metadata = await session.TryGetMediaPropertiesAsync();
            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            var artKey = session.SourceAppUserModelId + "\0" + metadata.Title + "\0" + metadata.Artist;
            byte[]? art = _artKey == artKey ? _cachedArt : null;
            if (_artKey != artKey && metadata.Thumbnail is not null)
            {
                using var stream = await metadata.Thumbnail.OpenReadAsync();
                if (stream.Size <= (ulong)MaxArtworkBytes)
                {
                    using var reader = new DataReader(stream);
                    var length = await reader.LoadAsync((uint)stream.Size);
                    art = new byte[length];
                    reader.ReadBytes(art);
                }
            }
            if (generation != Volatile.Read(ref _generation)) return;
            _artKey = artKey;
            _cachedArt = art;
            var controls = playback.Controls;
            Changed?.Invoke(this, new MediaSnapshot(session.SourceAppUserModelId, metadata.Title ?? "",
                metadata.Artist ?? "", metadata.AlbumTitle ?? "", playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                controls.IsPlayPauseToggleEnabled, controls.IsPreviousEnabled, controls.IsNextEnabled,
                controls.IsPlaybackPositionEnabled, timeline.Position, timeline.EndTime - timeline.StartTime, art));
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            if (generation == Volatile.Read(ref _generation)) Changed?.Invoke(this, null);
        }
    }

    public async Task ToggleAsync()
    {
        var session = _session;
        if (session?.GetPlaybackInfo().Controls.IsPlayPauseToggleEnabled == true) await session.TryTogglePlayPauseAsync();
    }
    public async Task PreviousAsync()
    {
        var session = _session;
        if (session?.GetPlaybackInfo().Controls.IsPreviousEnabled == true) await session.TrySkipPreviousAsync();
    }
    public async Task NextAsync()
    {
        var session = _session;
        if (session?.GetPlaybackInfo().Controls.IsNextEnabled == true) await session.TrySkipNextAsync();
    }
    public async Task SeekAsync(TimeSpan position)
    {
        var session = _session;
        if (session?.GetPlaybackInfo().Controls.IsPlaybackPositionEnabled == true)
            await session.TryChangePlaybackPositionAsync(position.Ticks);
    }

    private void Detach()
    {
        if (_session is null) return;
        _session.MediaPropertiesChanged -= OnMediaChanged;
        _session.PlaybackInfoChanged -= OnPlaybackChanged;
        _session.TimelinePropertiesChanged -= OnTimelineChanged;
    }
    public void Dispose()
    {
        Detach();
        if (_manager is not null) _manager.SessionsChanged -= OnSessionsChanged;
        _manager = null;
        _session = null;
    }
}
