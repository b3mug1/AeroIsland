using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AeroIsland.Core;
using AeroIsland.Windows;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Microsoft.Win32;
using Brushes = System.Windows.Media.Brushes;
using Brush = System.Windows.Media.Brush;
using Orientation = System.Windows.Controls.Orientation;

namespace AeroIsland.App;

public partial class MainWindow
{
    private LiveActivity? _liveActivity;
    private readonly DispatcherTimer _audioPoll = new() { Interval = TimeSpan.FromMilliseconds(750) };
    private (float Level, bool Muted)? _lastAudioLevel;
    private readonly DispatcherTimer _liveActivityExpiry = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly Stopwatch _stopwatch = new();
    private readonly DispatcherTimer _stopwatchTick = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private TextBlock? _stopwatchLabel;
    private TextBlock? _stopwatchActivityLabel;

    private void InitializeLiveActivities()
    {
        _audioPoll.Tick += (_, _) =>
        {
            var current = AudioLevelReader.Read();
            if (current is { } audio && _lastAudioLevel is { } previous &&
                (Math.Abs(audio.Level - previous.Level) > 0.005 || audio.Muted != previous.Muted))
                ReportActivity(new LiveActivity(LiveActivityKind.Volume, audio.Muted ? "Muted" : "Volume",
                    audio.Muted ? "Sound off" : $"{audio.Level:P0}", audio.Muted ? 0 : audio.Level));
            _lastAudioLevel = current;
        };
        _liveActivityExpiry.Tick += (_, _) => DismissLiveActivity();
        _stopwatchTick.Tick += (_, _) =>
        {
            var time = _stopwatch.Elapsed.ToString(@"mm\:ss\.f");
            if (_stopwatchLabel is not null) _stopwatchLabel.Text = time;
            if (_stopwatchActivityLabel is not null) _stopwatchActivityLabel.Text = time;
        };
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.StatusChange) return;
        Dispatcher.InvokeAsync(() =>
        {
            var power = System.Windows.Forms.SystemInformation.PowerStatus;
            if (power.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery) ||
                power.BatteryLifePercent is < 0 or > 1) return;
            ReportActivity(new LiveActivity(LiveActivityKind.Battery,
                power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online ? "Charging" : "Battery",
                $"{power.BatteryLifePercent:P0}", power.BatteryLifePercent));
        });
    }

    /// <summary>Receives activity updates from application integrations on any thread.</summary>
    public void ReportActivity(LiveActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (!Dispatcher.CheckAccess()) { Dispatcher.InvokeAsync(() => ReportActivity(activity)); return; }
        if (!Enum.IsDefined(activity.Kind)) throw new ArgumentOutOfRangeException(nameof(activity));
        _liveActivity = activity.Validate();
        _liveActivityExpiry.Stop();
        if (activity.Kind is LiveActivityKind.Battery or LiveActivityKind.Volume or LiveActivityKind.Notification)
            _liveActivityExpiry.Start();
        if (_expanded)
        {
            if (_activePage == "Activity") ShowPage("Activity");
        }
        else
        {
            ShowCompact(Brushes.White);
            AnimateWindow(CompactWidth, CompactHeight, true);
        }
    }

    public void DismissLiveActivity()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.InvokeAsync(DismissLiveActivity); return; }
        _liveActivityExpiry.Stop();
        _liveActivity = null;
        if (_expanded && _activePage == "Activity") ShowPage("Island");
        else if (!_expanded)
        {
            ShowCompact(Brushes.White);
            AnimateWindow(CompactWidth, CompactHeight);
        }
    }

    private Brush LiveActivityColor() => _liveActivity?.Kind switch
    {
        LiveActivityKind.Battery => ActivityGreen(),
        LiveActivityKind.Microphone or LiveActivityKind.Stopwatch => ActivityOrange(),
        LiveActivityKind.ScreenRecording => new SolidColorBrush(Color.FromRgb(255, 69, 58)),
        _ => Brushes.White
    };

    private string LiveActivityGlyph() => _liveActivity?.Kind switch
    {
        LiveActivityKind.Battery => "\uE83F",
        LiveActivityKind.Volume => "\uE767",
        LiveActivityKind.Microphone => "\uE720",
        LiveActivityKind.ScreenRecording => "\uE7C8",
        LiveActivityKind.Download => "\uE896",
        LiveActivityKind.Stopwatch => "\uE916",
        _ => "\uE7F4"
    };

    private void BuildCompactLiveActivity(Grid grid)
    {
        grid.Children.Add(new TextBlock
        {
            Text = LiveActivityGlyph(), FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            Foreground = LiveActivityColor(), FontSize = 21, Margin = new Thickness(4, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        var title = new TextBlock
        {
            Text = _liveActivity?.Title, Foreground = Brushes.White, FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(title, 1); grid.Children.Add(title);
        var detail = new TextBlock
        {
            Text = _liveActivity?.Progress is { } progress ? $"{progress:P0}" : "●",
            Foreground = LiveActivityColor(), FontSize = 12, Margin = new Thickness(12, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        if (_liveActivity?.Kind == LiveActivityKind.Stopwatch)
        {
            detail.Text = _stopwatch.Elapsed.ToString(@"mm\:ss\.f");
            _stopwatchActivityLabel = detail;
        }
        Grid.SetColumn(detail, 2); grid.Children.Add(detail);
        AutomationProperties.SetLiveSetting(grid, AutomationLiveSetting.Polite);
    }

    private void ShowLiveActivity()
    {
        _page.Children.Add(new TextBlock
        {
            Text = LiveActivityGlyph(), FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            Foreground = LiveActivityColor(), FontSize = 28, Margin = new Thickness(0, 2, 0, 10)
        });
        _page.Children.Add(new TextBlock
        {
            Text = _liveActivity?.Title ?? "Activity ended", Foreground = Brushes.White,
            FontSize = 18, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis
        });
        var detail = new TextBlock
        {
            Text = _liveActivity?.Detail, Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 166)),
            FontSize = 13, Margin = new Thickness(0, 5, 0, 14), TextWrapping = TextWrapping.Wrap,
            MaxHeight = 42, TextTrimming = TextTrimming.CharacterEllipsis
        };
        if (_liveActivity?.Kind == LiveActivityKind.Stopwatch) _stopwatchActivityLabel = detail;
        _page.Children.Add(detail);
        if (_liveActivity?.Progress is { } progress)
        {
            var track = new Grid { Height = 5, Margin = new Thickness(0, 0, 0, 15) };
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, progress), GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - progress), GridUnitType.Star) });
            var background = new Border { CornerRadius = new CornerRadius(2.5), Background = new SolidColorBrush(Color.FromRgb(44, 44, 46)) };
            Grid.SetColumnSpan(background, 2); track.Children.Add(background);
            track.Children.Add(new Border { CornerRadius = new CornerRadius(2.5), Background = LiveActivityColor(), Visibility = progress == 0 ? Visibility.Hidden : Visibility.Visible });
            _page.Children.Add(track);
        }
        var done = Button("Dismiss", DismissLiveActivity);
        done.Foreground = Brushes.White;
        _page.Children.Add(done);
    }

    private void AddStopwatchControls()
    {
        Section("Stopwatch");
        _stopwatchLabel = Label(_stopwatch.Elapsed.ToString(@"mm\:ss\.f"), 24, true);
        _page.Children.Add(_stopwatchLabel);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Button(_stopwatch.IsRunning ? "Pause stopwatch" : "Start stopwatch", () =>
        {
            if (_stopwatch.IsRunning) { _stopwatch.Stop(); _stopwatchTick.Stop(); }
            else { _stopwatch.Start(); _stopwatchTick.Start(); }
            ReportActivity(new LiveActivity(LiveActivityKind.Stopwatch,
                _stopwatch.IsRunning ? "Stopwatch" : "Stopwatch paused", _stopwatch.Elapsed.ToString(@"mm\:ss\.f")));
            ShowPage("Focus");
        }));
        buttons.Children.Add(Button("Reset", () => { _stopwatch.Reset(); _stopwatchTick.Stop(); if (_liveActivity?.Kind == LiveActivityKind.Stopwatch) DismissLiveActivity(); ShowPage("Focus"); }));
        _page.Children.Add(buttons);
    }
}
