using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AeroIsland.Core;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;

namespace AeroIsland.App;

public partial class MainWindow
{
    private readonly FocusTimer _focusTimer = new();
    private readonly DispatcherTimer _activityTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _transientTimer = new();
    private DateTimeOffset _mediaSampleUtc;
    private string? _transientMessage;
    private string? _transientPage;
    private string? _requestedPage;
    private bool _quickClipboardMode;
    private ProgressRing? _compactRing;
    private ProgressRing? _focusRing;
    private TextBlock? _compactClock;
    private TextBlock? _focusClock;
    private TextBlock? _statusText;

    private enum CompactActivity { Idle, Music, Focus, Event }

    private CompactActivity CurrentActivity => _transientMessage is not null ? CompactActivity.Event :
        _focusTimer.Phase != FocusTimerPhase.Idle ? CompactActivity.Focus :
        _snapshot?.IsPlaying == true ? CompactActivity.Music : CompactActivity.Idle;

    private string ContextPage => _requestedPage ?? _transientPage ??
        (_focusTimer.Phase != FocusTimerPhase.Idle ? "Focus" :
        _snapshot?.IsPlaying == true ? "Music" : "Island");

    private void InitializeActivity()
    {
        _activityTimer.Tick += (_, _) => UpdateActivity();
        _transientTimer.Tick += (_, _) =>
        {
            _transientTimer.Stop();
            _transientMessage = null;
            _transientPage = null;
            if (_expanded)
            {
                if (_statusText is not null) _statusText.Text = "READY";
            }
            else
            {
                ShowCompact(ForegroundBrush());
                AnimateWindow(_settings.CapsuleWidth * _settings.Scale, 50 * _settings.Scale);
            }
        };
    }

    private void OpenQuickClipboard()
    {
        var wasExpanded = _expanded;
        _quickClipboardMode = true;
        _requestedPage = "Clipboard";
        Open();
        if (wasExpanded)
        {
            _requestedPage = null;
            ShowPage("Clipboard");
        }
    }

    private void ShowTransient(string message, string page)
    {
        _transientMessage = message;
        _transientPage = page;
        _transientTimer.Stop();
        _transientTimer.Interval = TimeSpan.FromSeconds(2.4);
        _transientTimer.Start();
        if (_expanded)
        {
            if (_statusText is not null) _statusText.Text = message.ToUpperInvariant();
            return;
        }
        ShowCompact(ForegroundBrush());
        AnimateWindow(Math.Max(_settings.CapsuleWidth, 310) * _settings.Scale, 58 * _settings.Scale);
    }

    private void RefreshActivityTimer()
    {
        if (_focusTimer.Phase == FocusTimerPhase.Running || _snapshot?.IsPlaying == true) _activityTimer.Start();
        else _activityTimer.Stop();
        UpdateActivityVisuals();
    }

    private void UpdateActivity()
    {
        if (_focusTimer.CompleteIfElapsed(DateTimeOffset.UtcNow))
        {
            RefreshActivityTimer();
            if (_expanded && _activePage == "Focus") ShowPage("Focus");
            ShowTransient("Focus complete", "Focus");
            return;
        }
        UpdateActivityVisuals();
    }

    private void UpdateActivityVisuals()
    {
        var now = DateTimeOffset.UtcNow;
        if (_focusTimer.Phase != FocusTimerPhase.Idle)
        {
            var time = FormatRemaining(_focusTimer.Remaining(now));
            if (_compactClock is not null) _compactClock.Text = time;
            if (_focusClock is not null) _focusClock.Text = time;
            SetRingProgress(_focusRing, _focusTimer.Progress(now));
            if (CurrentActivity == CompactActivity.Focus) SetRingProgress(_compactRing, _focusTimer.Progress(now));
        }
        else if (_focusClock is not null) _focusClock.Text = "00:00";

        if (CurrentActivity == CompactActivity.Music && _snapshot is { Duration: { Ticks: > 0 } } media)
        {
            var position = media.Position + (media.IsPlaying ? now - _mediaSampleUtc : TimeSpan.Zero);
            SetRingProgress(_compactRing, position.TotalMilliseconds / media.Duration.TotalMilliseconds);
        }
    }

    private void SetRingProgress(ProgressRing? ring, double value)
    {
        if (ring is null) return;
        value = Math.Clamp(value, 0, 1);
        if (!MotionEnabled)
        {
            ring.BeginAnimation(ProgressRing.ProgressProperty, null);
            ring.Progress = value;
            return;
        }
        ring.BeginAnimation(ProgressRing.ProgressProperty,
            new DoubleAnimation(value, TimeSpan.FromMilliseconds(130)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } },
            HandoffBehavior.SnapshotAndReplace);
    }

    private static string FormatRemaining(TimeSpan time)
    {
        var seconds = Math.Max(0, (int)Math.Ceiling(time.TotalSeconds));
        return $"{seconds / 60:00}:{seconds % 60:00}";
    }

    private void StartFocus(int minutes)
    {
        _focusTimer.Start(TimeSpan.FromMinutes(minutes), DateTimeOffset.UtcNow);
        RefreshActivityTimer();
        if (_expanded) ShowPage("Focus");
        else ShowCompact(ForegroundBrush());
    }

    private void ToggleFocus()
    {
        if (_focusTimer.Phase == FocusTimerPhase.Running) _focusTimer.Pause(DateTimeOffset.UtcNow);
        else if (_focusTimer.Phase == FocusTimerPhase.Paused) _focusTimer.Resume(DateTimeOffset.UtcNow);
        RefreshActivityTimer();
        if (_expanded && _activePage == "Focus") ShowPage("Focus");
        else if (!_expanded) ShowCompact(ForegroundBrush());
    }

    private void StopFocus()
    {
        _focusTimer.Reset();
        RefreshActivityTimer();
        if (_expanded && _activePage == "Focus") ShowPage("Focus");
        else if (!_expanded) ShowCompact(ForegroundBrush());
    }

    private void ShowFocus()
    {
        PageHeading("STAY IN FLOW", "Focus timer", "A quiet space for one task at a time.");
        var timerPanel = new Grid { Height = 178, Margin = new Thickness(0, 0, 0, 8) };
        var ring = new ProgressRing
        {
            Width = 146,
            Height = 146,
            StrokeWidth = 6,
            TrackBrush = Outline(),
            ProgressBrush = Accent(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _focusRing = ring;
        timerPanel.Children.Add(ring);
        var clock = new TextBlock
        {
            Text = _focusTimer.Phase == FocusTimerPhase.Idle ? "00:00" : FormatRemaining(_focusTimer.Remaining(DateTimeOffset.UtcNow)),
            FontSize = 31,
            FontWeight = FontWeights.SemiBold,
            Foreground = ForegroundBrush(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _focusClock = clock;
        timerPanel.Children.Add(clock);
        _page.Children.Add(timerPanel);
        FadeIn(timerPanel, 8, 45);

        var presets = new Grid { Margin = new Thickness(0, 4, 0, 14) };
        for (var index = 0; index < 3; index++) presets.ColumnDefinitions.Add(new ColumnDefinition());
        var minutes = new[] { 5, 25, 50 };
        for (var index = 0; index < minutes.Length; index++)
        {
            var duration = minutes[index];
            var preset = Button($"{duration} min", () => StartFocus(duration));
            preset.Margin = new Thickness(3);
            preset.Background = _focusTimer.Duration == TimeSpan.FromMinutes(duration) ? AccentSoft() : Surface2();
            Grid.SetColumn(preset, index);
            presets.Children.Add(preset);
        }
        _page.Children.Add(presets);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        if (_focusTimer.Phase != FocusTimerPhase.Idle)
        {
            var toggle = Button(_focusTimer.Phase == FocusTimerPhase.Running ? "Pause" : "Resume", ToggleFocus);
            toggle.Background = Accent();
            toggle.Foreground = AccentInk();
            actions.Children.Add(toggle);
            actions.Children.Add(Button("End session", StopFocus));
        }
        _page.Children.Add(actions);
        _page.Children.Add(Label(_focusTimer.Phase == FocusTimerPhase.Paused ? "Paused. Resume whenever you are ready." :
            _focusTimer.Phase == FocusTimerPhase.Running ? "The timer keeps running while the panel is closed." :
            "Choose a duration to begin.", 11));
        UpdateActivityVisuals();
    }

    private ProgressRing CreateCompactRing(double progress)
    {
        var ring = new ProgressRing
        {
            Width = 31,
            Height = 31,
            StrokeWidth = 2.5,
            TrackBrush = Outline(),
            ProgressBrush = Accent(),
            Progress = progress,
            VerticalAlignment = VerticalAlignment.Center
        };
        _compactRing = ring;
        return ring;
    }

    private static bool IsInsideButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button) return true;
            source = source switch
            {
                Visual or System.Windows.Media.Media3D.Visual3D => VisualTreeHelper.GetParent(source),
                FrameworkContentElement content => content.Parent,
                _ => null
            };
        }
        return false;
    }
}
