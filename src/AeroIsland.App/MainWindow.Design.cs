using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using AeroIsland.Core;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using Path = System.Windows.Shapes.Path;

namespace AeroIsland.App;

public partial class MainWindow
{
    private double CompactWidth => (_liveActivity is not null ? Math.Max(260, _settings.CapsuleWidth)
        : _transientMessage is not null ? Math.Max(310, _settings.CapsuleWidth) : _settings.CapsuleWidth) * _settings.Scale;
    private double CompactHeight => (_liveActivity is not null || _transientMessage is not null ? 54 : 44) * _settings.Scale;

    private static Brush ActivityOrange() => new SolidColorBrush(Color.FromRgb(255, 159, 10));
    private static Brush ActivityGreen() => new SolidColorBrush(Color.FromRgb(48, 209, 88));

    private void UpdateIslandGeometry()
    {
        var radius = Math.Min(ActualHeight / 2, 40 * _settings.Scale);
        _surface.CornerRadius = new CornerRadius(radius);
        _surface.Clip = new RectangleGeometry(new System.Windows.Rect(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight)), radius, radius);
        if (_native is not null) _native.CornerRadius = radius;
    }

    private Border AlbumArtwork(double size) => new()
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.22),
        VerticalAlignment = VerticalAlignment.Center,
        Background = _artworkSource is null ? new SolidColorBrush(Color.FromRgb(28, 28, 30))
            : new ImageBrush(_artworkSource) { Stretch = Stretch.UniformToFill },
        Child = _artworkSource is null ? new TextBlock
        {
            Text = "♪", Foreground = Brushes.White, FontSize = size * 0.48,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        } : null
    };

    private FrameworkElement CreateWaveform(double width, double height)
    {
        var wave = new StackPanel
        {
            Orientation = Orientation.Horizontal, Width = width, Height = height,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
        };
        for (var i = 0; i < 5; i++)
        {
            var bar = new Border
            {
                Width = 2, Height = new[] { 7d, 15, 20, 11, 6 }[i],
                CornerRadius = new CornerRadius(1), Background = Brushes.White,
                Margin = new Thickness(1.5, 0, 1.5, 0), VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1)
            };
            if (MotionEnabled && _snapshot?.IsPlaying == true)
                ((ScaleTransform)bar.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty,
                    new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(340 + i * 83))
                    { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            wave.Children.Add(bar);
        }
        return wave;
    }

    private Button TransportButton(string name, string geometry, Action action, bool enabled = true, double size = 23)
    {
        var button = Button("", action);
        button.Content = new Path
        {
            Data = Geometry.Parse(geometry), Fill = Brushes.White,
            Stretch = Stretch.Uniform, Width = size, Height = size
        };
        button.Width = 54; button.Height = 44;
        button.Background = Brushes.Transparent; button.BorderThickness = new Thickness(0);
        button.Padding = new Thickness(12, 8, 12, 8); button.Margin = new Thickness(7, 0, 7, 0);
        button.IsEnabled = enabled;
        button.ToolTip = name;
        AutomationProperties.SetName(button, name);
        return button;
    }

    private void ShowMusic()
    {
        _timeline = null;
        _timelineTimer.Stop();
        var track = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) });
        track.ColumnDefinitions.Add(new ColumnDefinition());
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        track.Children.Add(AlbumArtwork(52));
        var metadata = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        metadata.Children.Add(new TextBlock
        {
            Text = _snapshot?.Title is { Length: > 0 } title ? title : "Nothing playing",
            Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        metadata.Children.Add(new TextBlock
        {
            Text = _snapshot?.Artist is { Length: > 0 } artist ? artist : "Open a music app to get started",
            Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 166)), FontSize = 13,
            Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(metadata, 1); track.Children.Add(metadata);
        var waveform = CreateWaveform(26, 23);
        Grid.SetColumn(waveform, 2); track.Children.Add(waveform);
        _page.Children.Add(track);

        var timeline = new Slider
        {
            Minimum = 0, Maximum = Math.Max(1, _snapshot?.Duration.TotalSeconds ?? 0),
            IsEnabled = _snapshot?.CanSeek == true, Style = (Style)FindResource("IslandSlider"),
            Value = Math.Clamp(_snapshot?.Position.TotalSeconds ?? 0, 0, Math.Max(1, _snapshot?.Duration.TotalSeconds ?? 0))
        };
        AutomationProperties.SetName(timeline, "Playback position");
        timeline.PreviewMouseLeftButtonUp += async (_, _) => await SafeMediaAsync(() => _media.SeekAsync(TimeSpan.FromSeconds(timeline.Value)));
        timeline.KeyUp += async (_, e) =>
        {
            if (e.Key is System.Windows.Input.Key.Left or System.Windows.Input.Key.Right or System.Windows.Input.Key.Home or System.Windows.Input.Key.End)
                await SafeMediaAsync(() => _media.SeekAsync(TimeSpan.FromSeconds(timeline.Value)));
        };
        _timeline = timeline;
        if (_settings.ShowTimeline)
        {
            _page.Children.Add(timeline);
            var times = new Grid { Margin = new Thickness(0, -2, 0, 6) };
            var elapsed = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 166)), FontSize = 10 };
            var remaining = new TextBlock { Foreground = elapsed.Foreground, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Right };
            void UpdateTimes()
            {
                elapsed.Text = MediaTime(timeline.Value);
                remaining.Text = "−" + MediaTime(Math.Max(0, (_snapshot?.Duration.TotalSeconds ?? 0) - timeline.Value));
            }
            timeline.ValueChanged += (_, _) => UpdateTimes();
            UpdateTimes(); times.Children.Add(elapsed); times.Children.Add(remaining);
            _page.Children.Add(times);
        }
        var controls = new Grid();
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        controls.ColumnDefinitions.Add(new ColumnDefinition());
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        var transport = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        transport.Children.Add(TransportButton("Previous track", "M 11,0 L 0,8 11,16 Z M 22,0 L 11,8 22,16 Z",
            async () => await SafeMediaAsync(_media.PreviousAsync), _snapshot?.CanPrevious == true));
        transport.Children.Add(TransportButton(_snapshot?.IsPlaying == true ? "Pause" : "Play",
            _snapshot?.IsPlaying == true ? "M 0,0 H 6 V 22 H 0 Z M 11,0 H 17 V 22 H 11 Z" : "M 0,0 L 18,11 0,22 Z",
            async () => await SafeMediaAsync(_media.ToggleAsync), _snapshot?.CanPlayPause == true, 27));
        transport.Children.Add(TransportButton("Next track", "M 0,0 L 11,8 0,16 Z M 11,0 L 22,8 11,16 Z",
            async () => await SafeMediaAsync(_media.NextAsync), _snapshot?.CanNext == true));
        Grid.SetColumn(transport, 1); controls.Children.Add(transport);
        var more = Button("···", () => ShowPage("Island"));
        more.Width = 30; more.Padding = new Thickness(0); more.Margin = new Thickness(0);
        more.FontSize = 18; more.Background = Brushes.Transparent; more.BorderThickness = new Thickness(0);
        AutomationProperties.SetName(more, "Open all activities");
        Grid.SetColumn(more, 2); controls.Children.Add(more);
        _page.Children.Add(controls);
        if (_media.SessionIds.Count > 1)
            ChoiceField("Media source", _media.SessionIds, _snapshot?.SessionId ?? "",
                id => { _media.ChooseSession(id); return Task.CompletedTask; });
        if (_snapshot?.IsPlaying == true && _settings.ShowTimeline) _timelineTimer.Start();
    }

    private static string MediaTime(double seconds)
    {
        var value = (int)Math.Max(0, seconds);
        return $"{value / 60}:{value % 60:00}";
    }
}

// A critically damped spring settles quickly without overshooting window bounds.
internal sealed class IslandSpringEase : EasingFunctionBase
{
    public IslandSpringEase() => EasingMode = EasingMode.EaseIn;
    protected override double EaseInCore(double t) => (1 - (1 + 8 * t) * Math.Exp(-8 * t)) / (1 - 9 * Math.Exp(-8));
    protected override Freezable CreateInstanceCore() => new IslandSpringEase();
}
