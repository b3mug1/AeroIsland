using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using AeroIsland.Core;
using AeroIsland.Infrastructure;
using AeroIsland.Windows;
using Forms = System.Windows.Forms;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using NativeWindow = AeroIsland.Windows.NativeWindow;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Orientation = System.Windows.Controls.Orientation;
using Image = System.Windows.Controls.Image;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using CheckBox = System.Windows.Controls.CheckBox;
using Cursors = System.Windows.Input.Cursors;
using ColorConverter = System.Windows.Media.ColorConverter;
using Colors = System.Windows.Media.Colors;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using FontFamily = System.Windows.Media.FontFamily;

namespace AeroIsland.App;

public partial class MainWindow : Window
{
    private readonly LocalStorage _storage;
    private readonly ClipboardCapture _capture;
    private readonly MediaService _media = new();
    private NativeWindow? _native;
    private Forms.NotifyIcon? _tray;
    private AppSettings _settings;
    private readonly ObservableCollection<HistoryItem> _history = [];
    private readonly Border _surface = new();
    private readonly Grid _root = new();
    private readonly Dictionary<string, Button> _navButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _settingsCategoryButtons = new(StringComparer.Ordinal);
    private Slider? _timeline;
    private BitmapSource? _artworkSource;
    private readonly StackPanel _page = new();
    private ScrollViewer? _pageScroll;
    private Border? _tabsShell;
    private readonly TextBlock _privacy = new();
    private readonly DispatcherTimer _hover = new();
    private readonly DispatcherTimer _fullscreen = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _timelineTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private MediaSnapshot? _snapshot;
    private bool _expanded;
    private bool _exiting;
    private bool _hiddenToTray;
    private bool _fullscreenActive;
    private string _activePage = "Island";
    private string _settingsSection = "General";

    public MainWindow(LocalStorage storage, AppSettings settings)
    {
        InitializeComponent();
        _storage = storage;
        _settings = settings;
        _media.MaxArtworkBytes = settings.ArtworkCacheMiB * 1024 * 1024;
        _capture = new ClipboardCapture(storage);
        InitializeActivity();
        BuildShell();
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += OnClosed;
        Deactivated += (_, _) => { if (_expanded) Collapse(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Collapse(); e.Handled = true; } };
        MouseEnter += (_, _) => { if (!_expanded && _settings.HoverToExpand) { _hover.Interval = TimeSpan.FromMilliseconds(_settings.HoverDelayMs); _hover.Start(); } };
        MouseLeave += (_, _) => _hover.Stop();
        SizeChanged += (_, _) => Place();
        _hover.Tick += (_, _) => { _hover.Stop(); Open(); };
        _fullscreen.Tick += (_, _) => UpdateFullscreen();
        _timelineTimer.Tick += (_, _) => { if (_snapshot?.IsPlaying == true && _timeline is { } slider && slider.Value < slider.Maximum) slider.Value += 1; };
        _media.Changed += (_, state) => Dispatcher.InvokeAsync(() => ShowMedia(state));
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Place();
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        _fullscreen.Start();
        CreateTray();
        foreach (var item in await _storage.LoadHistoryAsync(_settings)) _history.Add(item);
        await _media.StartAsync();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _native = new NativeWindow(this) { CornerRadius = _settings.CornerRadius };
        _native.Start(_settings);
        _native.ClipboardUpdated += OnClipboardUpdated;
        _native.HotkeyPressed += (_, _) => Open();
        _native.ClipboardHotkeyPressed += (_, _) => OpenQuickClipboard();
        _native.DpiChanged += (_, _) => Dispatcher.BeginInvoke((Action)Place);
    }

    private async void OnClipboardUpdated(object? sender, EventArgs e)
    {
        try
        {
            var item = await _capture.CaptureAsync(_settings, CancellationToken.None);
            if (item is null) return;
            if (_history.Any(x => x.Fingerprint == item.Fingerprint))
            {
                if (item.ImageFile is { } duplicate) File.Delete(duplicate);
                return;
            }
            _history.Insert(0, item);
            await PersistHistoryAsync();
            if (_activePage == "Clipboard" && _expanded) ShowPage("Clipboard");
            ShowTransient("Saved to history", "Clipboard");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }
    }

    private async Task PersistHistoryAsync()
    {
        var normalized = HistoryRules.Normalize(_history, _settings, DateTimeOffset.UtcNow);
        var ids = normalized.Select(x => x.Id).ToHashSet();
        foreach (var old in _history.Where(x => !ids.Contains(x.Id)).ToArray()) _history.Remove(old);
        long imageBytes = 0;
        foreach (var item in _history.Where(x => x.ImageFile is not null)
                     .OrderByDescending(x => x.Pinned).ThenByDescending(x => x.CreatedUtc).ToArray())
        {
            var file = item.ImageFile!;
            if (!File.Exists(file)) { _history.Remove(item); continue; }
            imageBytes += new FileInfo(file).Length;
            if (imageBytes > _settings.MaxImageCacheMiB * 1024L * 1024L)
            {
                imageBytes -= new FileInfo(file).Length;
                _history.Remove(item);
            }
        }
        await _storage.SaveHistoryAsync(_history, _settings);
        _storage.PruneImages(_history);
    }

    private void BuildShell()
    {
        var dark = _settings.Theme != IslandTheme.Light;
        _surface.Background = new SolidColorBrush(dark ? Color.FromRgb(9, 9, 13) : Color.FromRgb(250, 249, 253));
        Resources["AccentBrush"] = Accent();
        Resources["OutlineBrush"] = Outline();
        Resources["PanelBrush"] = _surface.Background;
        _surface.CornerRadius = new CornerRadius(_settings.CornerRadius);
        _surface.BorderBrush = Outline();
        _surface.BorderThickness = new Thickness(1);
        _surface.Opacity = _expanded ? 1 : _settings.Opacity;
        _surface.ClipToBounds = true;
        _surface.Child = _root;
        _root.Margin = new Thickness(18, 7, 18, 8);
        _root.ClipToBounds = true;
        Content = _surface;
        if (_expanded)
        {
            ShowExpanded();
            AnimateWindow(420 * _settings.Scale, PageHeight(_activePage) * _settings.Scale);
        }
        else
        {
            Width = _settings.CapsuleWidth * _settings.Scale;
            Height = 50 * _settings.Scale;
            ShowCompact(ForegroundBrush());
        }
    }

    private void ShowCompact(Brush foreground, bool animate = true)
    {
        if (_pageScroll is not null) _pageScroll.Content = null;
        _root.Children.Clear();
        _root.RowDefinitions.Clear();
        _navButtons.Clear();
        _tabsShell = null;
        _compactRing = null;
        _compactClock = null;
        _focusRing = null;
        _focusClock = null;
        _statusText = null;
        var mode = CurrentActivity;
        var grid = new Grid { Cursor = Cursors.Hand };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(37) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        FrameworkElement mark;
        if (mode is CompactActivity.Focus or CompactActivity.Music)
        {
            var display = new Grid { Width = 31, Height = 31, VerticalAlignment = VerticalAlignment.Center };
            var progress = mode == CompactActivity.Focus
                ? _focusTimer.Progress(DateTimeOffset.UtcNow)
                : _snapshot is { Duration: { Ticks: > 0 } } media
                    ? Math.Clamp(media.Position.TotalMilliseconds / media.Duration.TotalMilliseconds, 0, 1) : 0;
            display.Children.Add(CreateCompactRing(progress));
            if (mode == CompactActivity.Music && _artworkSource is not null)
            {
                display.Children.Add(new Border
                {
                    Width = 22,
                    Height = 22,
                    CornerRadius = new CornerRadius(11),
                    Background = new ImageBrush(_artworkSource) { Stretch = Stretch.UniformToFill },
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            else display.Children.Add(new TextBlock
            {
                Text = mode == CompactActivity.Focus ? "●" : "♫",
                Foreground = Accent(),
                FontSize = mode == CompactActivity.Focus ? 12 : 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            mark = display;
        }
        else if (mode == CompactActivity.Event)
            mark = new Border
            {
                Width = 27,
                Height = 27,
                CornerRadius = new CornerRadius(14),
                Background = AccentSoft(),
                Child = new TextBlock
                {
                    Text = "✓", Foreground = Accent(), FontSize = 15,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
        else mark = BrandMark(27);
        Grid.SetColumn(mark, 0);
        grid.Children.Add(mark);
        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock
        {
            Text = mode switch
            {
                CompactActivity.Event => _transientMessage ?? "AeroIsland",
                CompactActivity.Focus => FormatRemaining(_focusTimer.Remaining(DateTimeOffset.UtcNow)),
                CompactActivity.Music => _snapshot?.Title is { Length: > 0 } track ? track : "Now playing",
                _ => "AeroIsland"
            },
            Foreground = foreground,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = Math.Max(80, Width - 100)
        };
        if (mode == CompactActivity.Focus) _compactClock = title;
        titleStack.Children.Add(title);
        titleStack.Children.Add(new TextBlock
        {
            Text = mode switch
            {
                CompactActivity.Event => "AEROISLAND",
                CompactActivity.Focus => _focusTimer.Phase == FocusTimerPhase.Paused ? "FOCUS PAUSED" : "FOCUS SESSION",
                CompactActivity.Music => _snapshot?.Artist.ToUpperInvariant() ?? "MEDIA",
                _ => "READY WHEN YOU ARE"
            },
            Foreground = Muted(),
            FontSize = 8,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = Math.Max(80, Width - 100)
        });
        Grid.SetColumn(titleStack, 1);
        grid.Children.Add(titleStack);
        var activity = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (mode == CompactActivity.Focus)
        {
            var toggle = Button(_focusTimer.Phase == FocusTimerPhase.Running ? "Ⅱ" : "▶", ToggleFocus);
            toggle.Width = 28; toggle.Height = 28;
            toggle.Padding = new Thickness(0); toggle.Margin = new Thickness(0);
            toggle.Background = AccentSoft(); toggle.Foreground = Accent();
            AutomationProperties.SetName(toggle, _focusTimer.Phase == FocusTimerPhase.Running ? "Pause focus timer" : "Resume focus timer");
            activity.Children.Add(toggle);
        }
        else if (mode == CompactActivity.Music && _snapshot?.CanPlayPause == true)
        {
            var toggle = Button("Ⅱ", async () => await SafeMediaAsync(_media.ToggleAsync));
            toggle.Width = 28; toggle.Height = 28;
            toggle.Padding = new Thickness(0); toggle.Margin = new Thickness(0);
            toggle.Background = AccentSoft(); toggle.Foreground = Accent();
            AutomationProperties.SetName(toggle, "Pause music");
            activity.Children.Add(toggle);
        }
        else
        {
        var dot = new Border
        {
            Width = 6,
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Background = Accent(),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        if (_snapshot?.IsPlaying == true && MotionEnabled)
        {
            dot.BeginAnimation(OpacityProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(680))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        }
        activity.Children.Add(dot);
        var chevron = new TextBlock
        {
            Text = "⌄",
            Foreground = Accent(),
            FontSize = 18,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, -5, 0, 0)
        };
        activity.Children.Add(chevron);
        }
        Grid.SetColumn(activity, 2);
        grid.Children.Add(activity);
        _root.Children.Add(grid);
        grid.MouseLeftButtonUp += (_, e) => { if (!IsInsideButton(e.OriginalSource as DependencyObject)) Open(); };
        AutomationProperties.SetName(grid, "AeroIsland. Open controls");
        if (animate) FadeIn(_root, 0);
        else { _root.BeginAnimation(OpacityProperty, null); _root.Opacity = 1; }
        UpdateActivityVisuals();
    }

    public void Open()
    {
        _hiddenToTray = false;
        if (!_expanded)
        {
            _activePage = ContextPage;
            _requestedPage = null;
            _transientTimer.Stop();
            _transientMessage = null;
            _transientPage = null;
            _expanded = true;
            _surface.Opacity = 1;
            _surface.Effect = new DropShadowEffect
            {
                BlurRadius = 24,
                ShadowDepth = 8,
                Direction = 270,
                Opacity = _settings.Theme == IslandTheme.Light ? 0.16 : 0.33,
                Color = Colors.Black
            };
            ShowExpanded();
            AnimateWindow(420 * _settings.Scale, PageHeight(_activePage) * _settings.Scale, spring: true);
        }
        Show();
        Activate();
    }

    private void Collapse()
    {
        if (!_expanded) return;
        _expanded = false;
        _surface.Opacity = _settings.Opacity;
        _media.TimelineUpdatesEnabled = false;
        _timelineTimer.Stop();
        ShowCompact(_settings.Theme == IslandTheme.Light ? Brushes.Black : Brushes.White);
        AnimateWindow((_transientMessage is null ? _settings.CapsuleWidth : Math.Max(_settings.CapsuleWidth, 310)) * _settings.Scale,
            (_transientMessage is null ? 50 : 58) * _settings.Scale);
        _surface.Effect = null;
    }

    private bool MotionEnabled => _settings.AnimationsEnabled && !_settings.ReducedMotion &&
        _settings.AnimationDurationMs > 0 && !(_fullscreenActive && _settings.FullscreenBehavior == FullscreenBehavior.PauseAnimations);

    private static double PageHeight(string page) => page switch
    {
        "Island" => 490,
        "Focus" => 454,
        "Music" => 390,
        "Clipboard" => 530,
        "Settings" => 540,
        _ => 416
    };

    private void AnimateWindow(double targetWidth, double targetHeight, bool spring = false)
    {
        if (!MotionEnabled)
        {
            BeginAnimation(WidthProperty, null);
            BeginAnimation(HeightProperty, null);
            Width = targetWidth;
            Height = targetHeight;
            Place();
            return;
        }
        var duration = TimeSpan.FromMilliseconds(_settings.AnimationDurationMs * (spring ? 1.1 : 1));
        IEasingFunction easing = new SineEase { EasingMode = EasingMode.EaseInOut };
        BeginAnimation(WidthProperty, new DoubleAnimation(targetWidth, duration) { EasingFunction = easing }, HandoffBehavior.SnapshotAndReplace);
        BeginAnimation(HeightProperty, new DoubleAnimation(targetHeight, duration) { EasingFunction = easing }, HandoffBehavior.SnapshotAndReplace);
    }

    private void FadeIn(UIElement element, double offset, int delayMs = 35)
    {
        element.BeginAnimation(OpacityProperty, null);
        if (!MotionEnabled) { element.Opacity = 1; return; }
        element.Opacity = 0;
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1,
            TimeSpan.FromMilliseconds(Math.Min(340, _settings.AnimationDurationMs)))
        { BeginTime = TimeSpan.FromMilliseconds(delayMs), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        if (offset == 0) return;
        var transform = new TranslateTransform(0, offset);
        element.RenderTransform = transform;
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offset, 0,
            TimeSpan.FromMilliseconds(Math.Min(340, _settings.AnimationDurationMs)))
        { BeginTime = TimeSpan.FromMilliseconds(delayMs), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void ShowExpanded()
    {
        _compactRing = null;
        _compactClock = null;
        if (_pageScroll is not null) _pageScroll.Content = null;
        _root.Children.Clear();
        _root.RowDefinitions.Clear();
        _navButtons.Clear();
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition());
        var header = new Grid { Margin = new Thickness(0, 4, 0, 17) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(41) });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var mark = BrandMark(32);
        Grid.SetColumn(mark, 0);
        header.Children.Add(mark);
        var brand = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(new TextBlock { Text = "AEROISLAND", Foreground = ForegroundBrush(), FontSize = 12, FontWeight = FontWeights.Bold });
        brand.Children.Add(new TextBlock { Text = "DESKTOP COMPANION", Foreground = Muted(), FontSize = 8, FontWeight = FontWeights.SemiBold });
        Grid.SetColumn(brand, 1);
        header.Children.Add(brand);
        var status = new Border
        {
            Background = Surface2(),
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var statusRow = new StackPanel { Orientation = Orientation.Horizontal };
        statusRow.Children.Add(new Border
        {
            Width = 6,
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Background = Accent(),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        _statusText = new TextBlock { Text = "READY", Foreground = Muted(), FontSize = 9, FontWeight = FontWeights.Bold };
        statusRow.Children.Add(_statusText);
        status.Child = statusRow;
        Grid.SetColumn(status, 2);
        header.Children.Add(status);
        var close = Button("×", Collapse);
        close.Width = 30;
        close.Height = 30;
        close.Padding = new Thickness(0);
        close.FontSize = 18;
        close.Margin = new Thickness(0);
        Grid.SetColumn(close, 3);
        header.Children.Add(close);
        _root.Children.Add(header);
        var tabs = new Grid();
        for (var index = 0; index < 5; index++) tabs.ColumnDefinitions.Add(new ColumnDefinition());
        var names = new[] { "Island", "Focus", "Music", "Clipboard", "Settings" };
        for (var index = 0; index < names.Length; index++)
        {
            var name = names[index];
            var tab = Button(name, () => ShowPage(name));
            tab.Margin = new Thickness(3);
            tab.Padding = new Thickness(5, 8, 5, 8);
            tab.FontSize = 10;
            tab.BorderThickness = new Thickness(0);
            Grid.SetColumn(tab, index);
            tabs.Children.Add(tab);
            _navButtons[name] = tab;
        }
        var tabsShell = new Border
        {
            Background = Surface2(),
            CornerRadius = new CornerRadius(14),
            Child = tabs,
            Margin = new Thickness(0, 0, 0, 17),
            ClipToBounds = true
        };
        _tabsShell = tabsShell;
        Grid.SetRow(tabsShell, 1);
        _root.Children.Add(tabsShell);
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.Transparent
        };
        _page.Children.Clear();
        scroll.Content = _page;
        _pageScroll = scroll;
        Grid.SetRow(scroll, 2);
        _root.Children.Add(scroll);
        ShowPage(_activePage);
        FadeIn(_root, 6, 20);
    }

    private void ShowPage(string page)
    {
        var changed = _activePage != page;
        _activePage = page;
        if (page != "Clipboard") _quickClipboardMode = false;
        if (_tabsShell is not null) _tabsShell.Visibility = _quickClipboardMode ? Visibility.Collapsed : Visibility.Visible;
        _media.TimelineUpdatesEnabled = _expanded && page == "Music";
        if (page != "Music") { _timelineTimer.Stop(); _timeline = null; }
        _focusRing = null;
        _focusClock = null;
        foreach (var (name, tab) in _navButtons)
        {
            var accent = ((SolidColorBrush)Accent()).Color;
            var target = name == page ? Color.FromArgb(52, accent.R, accent.G, accent.B) : Colors.Transparent;
            if (tab.Background is SolidColorBrush brush)
            {
                if (MotionEnabled)
                    brush.BeginAnimation(SolidColorBrush.ColorProperty,
                        new ColorAnimation(target, TimeSpan.FromMilliseconds(300))
                        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                else brush.Color = target;
            }
            else tab.Background = new SolidColorBrush(target);
            tab.Foreground = name == page ? Accent() : Muted();
        }
        _page.Children.Clear();
        if (!_expanded) return;
        switch (page)
        {
            case "Island": ShowIsland(); break;
            case "Focus": ShowFocus(); break;
            case "Music": ShowMusic(); break;
            case "Clipboard": ShowClipboard(); break;
            case "Settings": ShowSettings(); break;
        }
        if (changed)
        {
            _pageScroll?.ScrollToTop();
            AnimateWindow(420 * _settings.Scale, PageHeight(page) * _settings.Scale);
            FadeIn(_page, 9);
        }
    }

    private void ShowIsland()
    {
        var hero = new Grid { Margin = new Thickness(2, 0, 2, 18) };
        hero.ColumnDefinitions.Add(new ColumnDefinition());
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var copy = new StackPanel();
        copy.Children.Add(new TextBlock
        {
            Text = DateTime.Now.ToString("ddd, dd MMM").ToUpperInvariant(),
            Foreground = Accent(),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        copy.Children.Add(new TextBlock
        {
            Text = "Everything in reach.",
            Foreground = ForegroundBrush(),
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        });
        copy.Children.Add(new TextBlock
        {
            Text = "Your essentials, one gesture away.",
            Foreground = Muted(),
            FontSize = 11
        });
        hero.Children.Add(copy);
        var clock = new Border
        {
            Background = AccentSoft(),
            BorderBrush = Outline(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(15),
            Padding = new Thickness(11, 9, 11, 9),
            VerticalAlignment = VerticalAlignment.Center
        };
        clock.Child = new TextBlock
        {
            Text = DateTime.Now.ToString("HH:mm"),
            Foreground = Accent(),
            FontSize = 18,
            FontWeight = FontWeights.SemiBold
        };
        Grid.SetColumn(clock, 1);
        hero.Children.Add(clock);
        _page.Children.Add(hero);
        FadeIn(hero, 10, 55);

        _page.Children.Add(Card("♪", "NOW PLAYING", _snapshot?.Title is { Length: > 0 } title ? title : "Nothing playing",
            _snapshot?.Artist is { Length: > 0 } artist ? artist : "Compatible players appear here", () => ShowPage("Music")));
        _page.Children.Add(Card("▣", "CLIPBOARD",
            _settings.ClipboardEnabled ? (_settings.ClipboardPaused ? "History paused" : $"{_history.Count} items saved") : "History is off",
            _settings.ClipboardEnabled ? "Stored only on this device" : "Enable it whenever you need it",
            () => ShowPage("Clipboard")));
        _page.Children.Insert(2, Card("◷", "FOCUS TIMER",
            _focusTimer.Phase == FocusTimerPhase.Idle ? "Start a focus session" : "Focus session active",
            _focusTimer.Phase == FocusTimerPhase.Paused ? "Paused" : "5, 25 or 50 minutes", () => ShowPage("Focus")));
        FadeIn(_page.Children[^3], 11, 100);
        FadeIn(_page.Children[^2], 11, 150);
        FadeIn(_page.Children[^1], 11, 200);
        var footer = new Grid { Margin = new Thickness(3, 13, 3, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(new TextBlock
        {
            Text = _settings.Hotkey.Replace("+", " + ") + "   TO OPEN",
            Foreground = Muted(),
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        var local = new TextBlock
        {
            Text = "LOCAL · PRIVATE",
            Foreground = Accent(),
            FontSize = 9,
            FontWeight = FontWeights.SemiBold
        };
        Grid.SetColumn(local, 1);
        footer.Children.Add(local);
        _page.Children.Add(footer);
        FadeIn(footer, 6, 250);
    }

    private void ShowMusic()
    {
        PageHeading("MEDIA CONTROL", "Your music", "Playback, without leaving your flow.");
        if (_snapshot is null)
        {
            _timelineTimer.Stop();
            _timeline = null;
            var empty = new StackPanel { Margin = new Thickness(20, 18, 20, 18), HorizontalAlignment = HorizontalAlignment.Center };
            empty.Children.Add(new TextBlock
            {
                Text = "♪",
                Foreground = Accent(),
                FontSize = 36,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            });
            empty.Children.Add(new TextBlock
            {
                Text = "Nothing playing yet",
                Foreground = ForegroundBrush(),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            empty.Children.Add(new TextBlock
            {
                Text = "Open a player with a Windows media session.",
                Foreground = Muted(),
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 0)
            });
            _page.Children.Add(new Border
            {
                Child = empty,
                Background = Surface2(),
                BorderBrush = Outline(),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Margin = new Thickness(0, 10, 0, 0)
            });
            return;
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 10) };
        var artwork = new Border
        {
            Width = 72,
            Height = 72,
            CornerRadius = new CornerRadius(17),
            Margin = new Thickness(0, 0, 14, 0),
            Background = _artworkSource is null ? AccentSoft() : new ImageBrush(_artworkSource) { Stretch = Stretch.UniformToFill }
        };
        if (_artworkSource is null)
            artwork.Child = new TextBlock
            {
                Text = "♫", FontSize = 31, Foreground = Accent(),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
        RenderOptions.SetBitmapScalingMode(artwork, BitmapScalingMode.HighQuality);
        row.Children.Add(artwork);
        var track = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        track.Children.Add(new TextBlock { Text = _snapshot.Title, FontWeight = FontWeights.SemiBold, Foreground = ForegroundBrush(), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 240 });
        track.Children.Add(new TextBlock { Text = _snapshot.Artist, Foreground = Muted(), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 240 });
        row.Children.Add(track);
        _page.Children.Add(row);
        FadeIn(row, 8, 40);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var previous = Button("⏮", async () => await SafeMediaAsync(_media.PreviousAsync)); previous.IsEnabled = _snapshot.CanPrevious;
        var play = Button(_snapshot.IsPlaying ? "⏸" : "▶", async () => await SafeMediaAsync(_media.ToggleAsync)); play.IsEnabled = _snapshot.CanPlayPause;
        play.Background = Accent(); play.Foreground = AccentInk();
        var next = Button("⏭", async () => await SafeMediaAsync(_media.NextAsync)); next.IsEnabled = _snapshot.CanNext;
        foreach (var button in new[] { previous, play, next }) { button.Margin = new Thickness(5); controls.Children.Add(button); }
        _page.Children.Add(controls);
        var timeline = new Slider
        {
            Minimum = 0,
            Maximum = Math.Max(1, _snapshot.Duration.TotalSeconds),
            IsEnabled = _snapshot.CanSeek,
            Style = (Style)FindResource("IslandSlider")
        };
        timeline.Value = Math.Clamp(_snapshot.Position.TotalSeconds, 0, timeline.Maximum);
        timeline.PreviewMouseLeftButtonUp += async (_, _) => await SafeMediaAsync(() => _media.SeekAsync(TimeSpan.FromSeconds(timeline.Value)));
        _timeline = timeline;
        if (_settings.ShowTimeline) _page.Children.Add(timeline);
        ChoiceField("Media source", _media.SessionIds, _snapshot.SessionId,
            id => { _media.ChooseSession(id); return Task.CompletedTask; });
        if (_snapshot.IsPlaying && _settings.ShowTimeline) _timelineTimer.Start();
        else _timelineTimer.Stop();
    }

    private async Task SafeMediaAsync(Func<Task> command)
    {
        try { await command(); } catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException) { }
    }

    private void ShowMedia(MediaSnapshot? state)
    {
        var previous = _snapshot;
        var trackChanged = previous?.SessionId != state?.SessionId || previous?.Title != state?.Title ||
            previous?.Artist != state?.Artist;
        var visualChanged = trackChanged || previous?.IsPlaying != state?.IsPlaying ||
            previous?.Duration != state?.Duration || !ReferenceEquals(previous?.Artwork, state?.Artwork);
        _mediaSampleUtc = DateTimeOffset.UtcNow;
        _snapshot = state;
        if (state?.Artwork is { Length: > 0 } bytes && _settings.ShowArtwork)
        {
            try
            {
                using var stream = new MemoryStream(bytes);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 96;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                _artworkSource = bitmap;
            }
            catch (Exception e) when (e is IOException or NotSupportedException) { _artworkSource = null; }
        }
        else _artworkSource = null;
        if (!_expanded && visualChanged) ShowCompact(ForegroundBrush(), trackChanged);
        else if (_expanded && visualChanged && _activePage is "Music" or "Island") ShowPage(_activePage);
        else if (_expanded && _activePage == "Music" && _timeline is { } slider && state is not null)
        {
            slider.Maximum = Math.Max(1, state.Duration.TotalSeconds);
            slider.Value = Math.Clamp(state.Position.TotalSeconds, 0, slider.Maximum);
        }
        RefreshActivityTimer();
    }

    private void ShowClipboard()
    {
        PageHeading("YOUR HISTORY", _quickClipboardMode ? "Quick clipboard" : "Clipboard",
            _quickClipboardMode ? "Three recent items. Search to see more." : "Save what matters. Keep it on this device.");
        _privacy.Text = _settings.ClipboardEnabled ? (_settings.ClipboardPaused ? "History paused" : "History on · stored on this device") : "History off · enable it to start saving";
        _privacy.Foreground = _settings.ClipboardEnabled && !_settings.ClipboardPaused ? Accent() : Muted();
        _privacy.Margin = new Thickness(0, 4, 0, 12);
        if (!_quickClipboardMode) _page.Children.Add(_privacy);
        var actions = new WrapPanel();
        actions.Children.Add(Button(_settings.ClipboardEnabled ? "Turn off" : "Enable history", async () => { _settings = _settings with { ClipboardEnabled = !_settings.ClipboardEnabled }; await SaveSettingsAsync(); ShowPage("Clipboard"); }));
        actions.Children.Add(Button(_settings.ClipboardPaused ? "Resume" : "Pause", async () => { _settings = _settings with { ClipboardPaused = !_settings.ClipboardPaused }; await SaveSettingsAsync(); ShowPage("Clipboard"); }));
        actions.Children.Add(Button("Clear unpinned", async () => { foreach (var x in _history.Where(x => !x.Pinned).ToArray()) _history.Remove(x); await PersistHistoryAsync(); ShowPage("Clipboard"); }));
        actions.Children.Add(Button("Clear all", async () => { _history.Clear(); await PersistHistoryAsync(); ShowPage("Clipboard"); }));
        if (!_quickClipboardMode) _page.Children.Add(actions);
        else if (!_settings.ClipboardEnabled)
            _page.Children.Add(Button("Enable history", async () =>
            {
                _settings = _settings with { ClipboardEnabled = true };
                await SaveSettingsAsync();
                ShowPage("Clipboard");
            }));
        if (_quickClipboardMode)
        {
            _page.Children.Add(Button("Show all history", () => { _quickClipboardMode = false; ShowPage("Clipboard"); }));
        }
        var search = new TextBox
        {
            Margin = new Thickness(0),
            Style = (Style)FindResource("IslandTextBox"),
            Background = Surface2(),
            Foreground = ForegroundBrush(),
            BorderBrush = Outline(),
            ToolTip = "Search history"
        };
        AutomationProperties.SetName(search, "Search clipboard history");
        var searchShell = new Grid { Margin = new Thickness(0, _quickClipboardMode ? 4 : 12, 0, 10) };
        searchShell.Children.Add(search);
        var placeholder = new TextBlock
        {
            Text = "Search history",
            Foreground = Muted(),
            FontSize = 12,
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        searchShell.Children.Add(placeholder);
        search.TextChanged += (_, _) => { placeholder.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; RenderHistory(search.Text); };
        _page.Children.Add(searchShell);
        _historyHost = new StackPanel();
        _page.Children.Add(_historyHost);
        RenderHistory("");
    }

    private StackPanel? _historyHost;
    private void RenderHistory(string query)
    {
        if (_historyHost is null) return;
        _historyHost.Children.Clear();
        var normalizedQuery = query.Trim();
        var matches = _history.Where(x => x.Preview.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase));
        if (_quickClipboardMode && normalizedQuery.Length == 0) matches = matches.Take(3);
        foreach (var item in matches)
        {
            var card = new StackPanel();
            card.Children.Add(new TextBlock
            {
                Text = item.Preview,
                Foreground = ForegroundBrush(),
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 0, 3)
            });
            if (!_quickClipboardMode) card.Children.Add(Label(item.CreatedUtc.ToLocalTime().ToString("g"), 10));
            var actions = new WrapPanel();
            actions.Children.Add(Button("Copy", () =>
            {
                try { _capture.Restore(item); ShowTransient("Copied to clipboard", "Clipboard"); }
                catch (Exception e) when (e is IOException or System.Runtime.InteropServices.ExternalException) { }
            }));
            actions.Children.Add(Button(item.Pinned ? "Unpin" : "Pin", async () => { var index = _history.IndexOf(item); if (index >= 0) _history[index] = item with { Pinned = !item.Pinned }; await PersistHistoryAsync(); ShowPage("Clipboard"); }));
            if (!_quickClipboardMode)
                actions.Children.Add(Button("Delete", async () => { _history.Remove(item); await PersistHistoryAsync(); ShowPage("Clipboard"); }));
            card.Children.Add(actions);
            _historyHost.Children.Add(new Border
            {
                Child = card,
                Background = Surface2(),
                BorderBrush = Outline(),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 8, 10, 4),
                Margin = new Thickness(0, 0, 0, 7)
            });
        }
        if (_historyHost.Children.Count == 0) _historyHost.Children.Add(Label("No saved items", 12));
    }

    private void ShowSettings()
    {
        PageHeading("MAKE IT YOURS", "Settings", "AeroIsland, tuned to your desktop.");
        var categories = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        _settingsCategoryButtons.Clear();
        foreach (var section in new[] { "General", "Appearance", "Media", "Clipboard", "Performance", "About" })
        {
            var category = Button(section, () =>
            {
                _settingsSection = section;
                ShowPage("Settings");
                _pageScroll?.ScrollToTop();
                FadeIn(_page, 6);
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    if (_settingsCategoryButtons.TryGetValue(section, out var current)) current.Focus();
                }));
            });
            category.FontSize = 10;
            category.Padding = new Thickness(10, 6, 10, 6);
            category.Margin = new Thickness(0, 0, 5, 5);
            category.Background = section == _settingsSection ? Accent() : Surface2();
            category.Foreground = section == _settingsSection ? AccentInk() : Muted();
            categories.Children.Add(category);
            _settingsCategoryButtons[section] = category;
        }
        _page.Children.Add(categories);
        Section(_settingsSection);
        if (_settingsSection == "General")
        {
            Toggle("Start with Windows", _settings.StartWithWindows, async value => { _settings = _settings with { StartWithWindows = value }; StartupRegistration.Set(value); await SaveSettingsAsync(); }, "Launch automatically after sign-in");
            Toggle("Minimize to tray", _settings.MinimizeToTray, async value => { _settings = _settings with { MinimizeToTray = value }; await SaveSettingsAsync(); }, "Keep AeroIsland available after closing");
            Toggle("Hover to expand", _settings.HoverToExpand, async value => { _settings = _settings with { HoverToExpand = value }; await SaveSettingsAsync(); }, "Open the panel when your pointer rests on it");
            ChoiceField("Keyboard shortcut", new[] { "Ctrl+Alt+Space", "Ctrl+Shift+Space" }, _settings.Hotkey,
                async value => { _settings = _settings with { Hotkey = value }; _native?.RegisterShortcut(value); await SaveSettingsAsync(); });
            if (_native?.HotkeyAvailable == false) _page.Children.Add(Label("Shortcut unavailable. Another app may already use it.", 11));
            _page.Children.Add(Label("Quick clipboard: Ctrl+Alt+V", 11));
            if (_native?.ClipboardHotkeyAvailable == false) _page.Children.Add(Label("Quick clipboard shortcut unavailable. Another app may already use it.", 11));
            var screens = System.Windows.Forms.Screen.AllScreens;
            var monitors = screens.Select(x => x.DeviceName).ToArray();
            var selectedMonitor = monitors.Contains(_settings.MonitorDeviceName) ? _settings.MonitorDeviceName :
                System.Windows.Forms.Screen.PrimaryScreen?.DeviceName ?? monitors.FirstOrDefault() ?? "";
            ChoiceField("Monitor", monitors, selectedMonitor,
                async value => { _settings = _settings with { MonitorDeviceName = value }; await SaveSettingsAsync(); Place(); },
                value =>
                {
                    var index = Array.IndexOf(monitors, value);
                    var screen = index >= 0 ? screens[index] : null;
                    return screen is null ? "Display unavailable" :
                        $"Display {index + 1} · {screen.Bounds.Width} × {screen.Bounds.Height}{(screen.Primary ? " · Primary" : "")}";
                });
            ChoiceField("Screen edge", Enum.GetValues<ScreenEdge>(), _settings.ScreenEdge,
                async value => { _settings = _settings with { ScreenEdge = value }; await SaveSettingsAsync(); Place(); });
            ChoiceField("Fullscreen behavior", Enum.GetValues<FullscreenBehavior>(), _settings.FullscreenBehavior,
                async value => { _settings = _settings with { FullscreenBehavior = value }; await SaveSettingsAsync(); },
                value => value switch
                {
                    FullscreenBehavior.Hide => "Hide over fullscreen apps",
                    FullscreenBehavior.PauseAnimations => "Pause animation",
                    _ => "Stay visible"
                });
        }
        if (_settingsSection == "Appearance")
        {
            Toggle("Light theme", _settings.Theme == IslandTheme.Light, async value =>
            {
                _settings = _settings with { Theme = value ? IslandTheme.Light : IslandTheme.Dark };
                await SaveSettingsAsync();
                BuildShell();
            });
            Toggle("Reduced motion", _settings.ReducedMotion, async value => { _settings = _settings with { ReducedMotion = value }; await SaveSettingsAsync(); });
            Toggle("Animations", _settings.AnimationsEnabled, async value => { _settings = _settings with { AnimationsEnabled = value }; await SaveSettingsAsync(); }, "Smooth opening and page transitions");
            SliderSetting("Scale", _settings.Scale, 0.75, 1.5, async value => { _settings = _settings with { Scale = value }; AnimateWindow(420 * value, PageHeight(_activePage) * value); await SaveSettingsAsync(); });
            SliderSetting("Capsule width", _settings.CapsuleWidth, 150, 450, async value => { _settings = _settings with { CapsuleWidth = (int)value }; await SaveSettingsAsync(); });
            SliderSetting("Capsule opacity", _settings.Opacity, 0.65, 1, async value => { _settings = _settings with { Opacity = value }; await SaveSettingsAsync(); });
            SliderSetting("Corner radius", _settings.CornerRadius, 8, 36, async value => { _settings = _settings with { CornerRadius = (int)value }; _surface.CornerRadius = new CornerRadius(value); if (_native is not null) _native.CornerRadius = value; await SaveSettingsAsync(); });
            SliderSetting("Animation ms", _settings.AnimationDurationMs, 0, 700, async value => { _settings = _settings with { AnimationDurationMs = (int)value }; await SaveSettingsAsync(); });
            _page.Children.Add(Label("Accent color · #RRGGBB", 11));
            var accent = new TextBox
            {
                Text = _settings.AccentColor,
                Style = (Style)FindResource("IslandTextBox"),
                Background = Surface2(),
                Foreground = ForegroundBrush(),
                BorderBrush = Outline(),
                Margin = new Thickness(0, 4, 0, 8)
            };
            accent.LostFocus += async (_, _) =>
            {
                _settings = (_settings with { AccentColor = accent.Text }).Validate();
                accent.Text = _settings.AccentColor;
                Resources["AccentBrush"] = Accent();
                await SaveSettingsAsync();
            };
            _page.Children.Add(accent);
        }
        if (_settingsSection == "Media")
        {
            Toggle("Show artwork", _settings.ShowArtwork, async value => { _settings = _settings with { ShowArtwork = value }; await SaveSettingsAsync(); });
            Toggle("Show timeline", _settings.ShowTimeline, async value => { _settings = _settings with { ShowTimeline = value }; await SaveSettingsAsync(); });
            SliderSetting("Artwork cache MiB", _settings.ArtworkCacheMiB, 1, 64, async value => { _settings = _settings with { ArtworkCacheMiB = (int)value }; _media.MaxArtworkBytes = _settings.ArtworkCacheMiB * 1024 * 1024; await SaveSettingsAsync(); });
        }
        if (_settingsSection == "Clipboard")
        {
            _page.Children.Add(Label($"Limit: {_settings.MaxHistoryItems} items · {_settings.RetentionDays} days", 12));
            SliderSetting("History items", _settings.MaxHistoryItems, 10, 500, async value => { _settings = _settings with { MaxHistoryItems = (int)value }; await SaveSettingsAsync(); await PersistHistoryAsync(); });
            SliderSetting("Retention days", _settings.RetentionDays, 1, 90, async value => { _settings = _settings with { RetentionDays = (int)value }; await SaveSettingsAsync(); await PersistHistoryAsync(); });
            SliderSetting("Max text length", _settings.MaxTextLength, 100, 100000, async value => { _settings = _settings with { MaxTextLength = (int)value }; await SaveSettingsAsync(); });
            SliderSetting("Max image dimension", _settings.MaxImageDimension, 128, 2048, async value => { _settings = _settings with { MaxImageDimension = (int)value }; await SaveSettingsAsync(); });
            SliderSetting("Image cache MiB", _settings.MaxImageCacheMiB, 4, 256, async value => { _settings = _settings with { MaxImageCacheMiB = (int)value }; await SaveSettingsAsync(); await PersistHistoryAsync(); });
        }
        if (_settingsSection == "Performance")
        {
            Button? export = null;
            Toggle("Local diagnostics", _settings.DiagnosticsEnabled, async value =>
            {
                _settings = _settings with { DiagnosticsEnabled = value };
                if (export is not null) export.IsEnabled = value;
                await SaveSettingsAsync();
            });
            export = Button("Export process snapshot", ExportDiagnostics);
            export.IsEnabled = _settings.DiagnosticsEnabled;
            _page.Children.Add(export);
        }
        if (_settingsSection == "About")
        {
            _page.Children.Add(Label("AeroIsland 0.2.0", 12));
            _page.Children.Add(Label(_storage.DirectoryPath, 10));
            _page.Children.Add(Label("No telemetry. Clipboard data stays on this device.", 11));
        }
    }

    private void Toggle(string label, bool value, Func<bool, Task> changed, string? description = null)
    {
        var button = new Button { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        StyleButton(button);
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.Margin = new Thickness(0, 0, 0, 8);
        button.Padding = new Thickness(12, 10, 12, 10);
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = ForegroundBrush(),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold
        });
        if (description is not null)
            copy.Children.Add(new TextBlock
            {
                Text = description,
                Foreground = Muted(),
                FontSize = 10,
                Margin = new Thickness(0, 3, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
        row.Children.Add(copy);
        var thumb = new System.Windows.Shapes.Ellipse
        {
            Width = 16,
            Height = 16,
            Fill = value ? AccentInk() : ForegroundBrush(),
            Margin = new Thickness(3),
            HorizontalAlignment = value ? HorizontalAlignment.Right : HorizontalAlignment.Left
        };
        var track = new Border
        {
            Width = 42,
            Height = 23,
            CornerRadius = new CornerRadius(12),
            Background = value ? Accent() : Outline(),
            Child = thumb,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(track, 1);
        row.Children.Add(track);
        button.Content = row;
        AutomationProperties.SetName(button, $"{label}: {(value ? "on" : "off")}");
        button.Click += async (_, _) =>
        {
            value = !value;
            track.Background = value ? Accent() : Outline();
            thumb.Fill = value ? AccentInk() : ForegroundBrush();
            thumb.HorizontalAlignment = value ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            AutomationProperties.SetName(button, $"{label}: {(value ? "on" : "off")}");
            await changed(value);
        };
        _page.Children.Add(button);
    }

    private void ChoiceField<T>(string label, IReadOnlyList<T> options, T selected, Func<T, Task> changed,
        Func<T, string>? format = null)
        where T : notnull
    {
        string Display(T value) => format?.Invoke(value) ?? value.ToString() ?? "";
        _page.Children.Add(Label(label, 11));
        var button = new Button { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        StyleButton(button);
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.Margin = new Thickness(0, 4, 0, 10);
        button.Padding = new Thickness(12, 10, 12, 10);
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var valueText = new TextBlock
        {
            Text = Display(selected),
            Foreground = ForegroundBrush(),
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        row.Children.Add(valueText);
        var chevron = new TextBlock
        {
            Text = "⌄",
            Foreground = Accent(),
            FontSize = 16,
            Margin = new Thickness(12, -4, 0, 0)
        };
        Grid.SetColumn(chevron, 1);
        row.Children.Add(chevron);
        button.Content = row;
        AutomationProperties.SetName(button, $"{label}: {Display(selected)}");
        var choices = new StackPanel { Margin = new Thickness(4) };
        var choicesHost = new Border
        {
            Child = choices,
            Background = Surface2(),
            BorderBrush = Outline(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, -4, 0, 9)
        };
        foreach (var option in options)
        {
            var item = new Button { Content = Display(option), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            StyleButton(item);
            item.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            item.Background = EqualityComparer<T>.Default.Equals(option, selected) ? AccentSoft() : Brushes.Transparent;
            item.BorderThickness = new Thickness(0);
            item.Margin = new Thickness(0, 0, 0, 2);
            item.Click += async (_, _) =>
            {
                choicesHost.Visibility = Visibility.Collapsed;
                valueText.Text = Display(option);
                AutomationProperties.SetName(button, $"{label}: {Display(option)}");
                button.Focus();
                await changed(option);
            };
            choices.Children.Add(item);
        }
        button.IsEnabled = options.Count > 0;
        button.Click += (_, _) =>
        {
            choicesHost.Visibility = choicesHost.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            if (choicesHost.Visibility == Visibility.Visible)
                Dispatcher.BeginInvoke((Action)(() => choicesHost.BringIntoView()));
        };
        _page.Children.Add(button);
        _page.Children.Add(choicesHost);
    }

    private void PageHeading(string kicker, string title, string subtitle)
    {
        _page.Children.Add(new TextBlock
        {
            Text = kicker,
            Foreground = Accent(),
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(2, 0, 0, 5)
        });
        _page.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = ForegroundBrush(),
            FontSize = 23,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 3)
        });
        _page.Children.Add(new TextBlock
        {
            Text = subtitle,
            Foreground = Muted(),
            FontSize = 11,
            Margin = new Thickness(1, 0, 0, 14)
        });
    }

    private void Section(string name)
    {
        _page.Children.Add(Spacer(14));
        _page.Children.Add(new TextBlock
        {
            Text = name.ToUpperInvariant(),
            Foreground = Accent(),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(2, 0, 0, 7)
        });
        _page.Children.Add(new Border { Height = 1, Background = Outline(), Margin = new Thickness(0, 0, 0, 5) });
    }
    private void SliderSetting(string label, double value, double minimum, double maximum, Func<double, Task> changed)
    {
        var wrapper = new StackPanel { Margin = new Thickness(12, 9, 12, 8) };
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = ForegroundBrush(),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold
        });
        var current = new TextBlock { Text = value.ToString("0.##"), Foreground = Accent(), FontSize = 11 };
        Grid.SetColumn(current, 1);
        header.Children.Add(current);
        wrapper.Children.Add(header);
        var slider = new Slider
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = value,
            Margin = new Thickness(0, 9, 0, 0),
            SmallChange = (maximum - minimum) / 100,
            Style = (Style)FindResource("IslandSlider")
        };
        slider.ValueChanged += (_, _) => current.Text = slider.Value.ToString("0.##");
        slider.PreviewMouseLeftButtonUp += async (_, _) => await changed(slider.Value);
        slider.KeyUp += async (_, e) => { if (e.Key is Key.Left or Key.Right or Key.Home or Key.End) await changed(slider.Value); };
        wrapper.Children.Add(slider);
        _page.Children.Add(new Border
        {
            Child = wrapper,
            Background = Surface2(),
            BorderBrush = Outline(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Margin = new Thickness(0, 0, 0, 8)
        });
    }
    private static FrameworkElement Spacer(double height) => new Border { Height = height };
    private TextBlock Label(string text, double size, bool strong = false) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = strong ? ForegroundBrush() : Muted(),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 2, 0, 2)
    };
    private Border BrandMark(double size)
    {
        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size * 0.34),
            Background = AccentSoft(),
            BorderBrush = Accent(),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "A",
                Foreground = Accent(),
                FontSize = size * 0.47,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -2, 0, 0)
            }
        };
    }

    private Border Card(string icon, string eyebrow, string title, string subtitle, Action click)
    {
        var content = new Grid { Margin = new Thickness(13, 11, 13, 11) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        content.ColumnDefinitions.Add(new ColumnDefinition());
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        var iconBox = new Border
        {
            Width = 39,
            Height = 39,
            Background = AccentSoft(),
            CornerRadius = new CornerRadius(12),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = icon,
                FontFamily = new FontFamily("Segoe UI Symbol"),
                Foreground = Accent(),
                FontSize = 21,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        content.Children.Add(iconBox);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = eyebrow,
            Foreground = Accent(),
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 3)
        });
        text.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = ForegroundBrush(),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        text.Children.Add(new TextBlock
        {
            Text = subtitle,
            Foreground = Muted(),
            FontSize = 10,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0)
        });
        Grid.SetColumn(text, 1);
        content.Children.Add(text);
        var arrow = new TextBlock
        {
            Text = "›",
            Foreground = Muted(),
            FontSize = 22,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetColumn(arrow, 2);
        content.Children.Add(arrow);
        var border = new Border
        {
            Background = Surface2(),
            BorderBrush = Outline(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Child = content,
            Margin = new Thickness(0, 0, 0, 9),
            Cursor = Cursors.Hand
        };
        border.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
        border.RenderTransform = new ScaleTransform(1, 1);
        border.MouseLeftButtonUp += (_, _) => click();
        border.MouseEnter += (_, _) => AnimateCard(border, true);
        border.MouseLeave += (_, _) => AnimateCard(border, false);
        AutomationProperties.SetName(border, $"{eyebrow}: {title}. Open details");
        return border;
    }

    private void AnimateCard(Border card, bool hovered)
    {
        var target = hovered ? (_settings.Theme == IslandTheme.Light ? Color.FromRgb(235, 231, 247) : Color.FromRgb(37, 34, 49))
            : (_settings.Theme == IslandTheme.Light ? Color.FromRgb(242, 239, 249) : Color.FromRgb(24, 23, 31));
        if (card.Background is not SolidColorBrush brush) return;
        if (!MotionEnabled) { brush.Color = target; return; }
        brush.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(target, TimeSpan.FromMilliseconds(300)));
        if (card.RenderTransform is ScaleTransform scale)
        {
            var animation = new DoubleAnimation(hovered ? 1.018 : 1, TimeSpan.FromMilliseconds(300))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }
    }
    private Button Button(string text, Action click)
    {
        var button = new Button { Content = text };
        StyleButton(button);
        button.Click += (_, _) => click();
        return button;
    }
    private void StyleButton(Button button)
    {
        button.Style = (Style)FindResource("IslandButton");
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.Background = Surface2(); button.Foreground = ForegroundBrush();
        button.BorderBrush = Outline(); button.BorderThickness = new Thickness(1);
        button.Padding = new Thickness(11, 7, 11, 7); button.Margin = new Thickness(0, 0, 6, 6);
        button.Cursor = Cursors.Hand;
        AutomationProperties.SetName(button, button.Content?.ToString() ?? "Action");
    }
    private Brush Accent() { try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(_settings.AccentColor)); } catch { return new SolidColorBrush(Color.FromRgb(185, 166, 255)); } }
    private Brush AccentSoft()
    {
        var accent = ((SolidColorBrush)Accent()).Color;
        return new SolidColorBrush(Color.FromArgb(_settings.Theme == IslandTheme.Light ? (byte)34 : (byte)43,
            accent.R, accent.G, accent.B));
    }
    private Brush AccentInk()
    {
        var color = ((SolidColorBrush)Accent()).Color;
        var luminance = 0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B;
        return luminance > 150 ? new SolidColorBrush(Color.FromRgb(15, 14, 21)) : Brushes.White;
    }
    private Brush Outline() => new SolidColorBrush(_settings.Theme == IslandTheme.Light
        ? Color.FromRgb(219, 213, 231) : Color.FromRgb(48, 46, 57));
    private Brush Surface2() => new SolidColorBrush(_settings.Theme == IslandTheme.Light
        ? Color.FromRgb(242, 239, 249) : Color.FromRgb(24, 23, 31));
    private Brush ForegroundBrush() => new SolidColorBrush(_settings.Theme == IslandTheme.Light
        ? Color.FromRgb(32, 29, 42) : Color.FromRgb(247, 245, 252));
    private Brush Muted() => new SolidColorBrush(_settings.Theme == IslandTheme.Light
        ? Color.FromRgb(105, 100, 117) : Color.FromRgb(157, 153, 171));

    private async Task SaveSettingsAsync() => await _storage.SaveSettingsAsync(_settings);

    private void ExportDiagnostics()
    {
        var dialog = new SaveFileDialog { Filter = "JSON file|*.json", FileName = "aeroisland-diagnostics.json" };
        if (dialog.ShowDialog(this) != true) return;
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var snapshot = new
        {
            Utc = DateTimeOffset.UtcNow,
            PrivateMiB = process.PrivateMemorySize64 / 1048576.0,
            WorkingMiB = process.WorkingSet64 / 1048576.0,
            ManagedMiB = GC.GetTotalMemory(false) / 1048576.0,
            Threads = process.Threads.Count,
            Handles = process.HandleCount,
            Gc0 = GC.CollectionCount(0),
            Gc1 = GC.CollectionCount(1),
            Gc2 = GC.CollectionCount(2)
        };
        File.WriteAllText(dialog.FileName, System.Text.Json.JsonSerializer.Serialize(snapshot, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private void CreateTray()
    {
        _tray = new Forms.NotifyIcon { Text = "AeroIsland", Icon = System.Drawing.SystemIcons.Application, Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => Dispatcher.Invoke(Open));
        menu.Items.Add("Settings", null, (_, _) => Dispatcher.Invoke(() => { Open(); ShowPage("Settings"); }));
        menu.Items.Add("Pause Clipboard History", null, async (_, _) => { _settings = _settings with { ClipboardPaused = !_settings.ClipboardPaused }; await SaveSettingsAsync(); });
        menu.Items.Add("Start with Windows", null, async (_, _) => { _settings = _settings with { StartWithWindows = !_settings.StartWithWindows }; StartupRegistration.Set(_settings.StartWithWindows); await SaveSettingsAsync(); });
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(() => { _exiting = true; Close(); Application.Current.Shutdown(); }));
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(Open);
    }

    private void Place() => WindowPlacement.AtEdgeCenter(this, _settings.MonitorDeviceName, _settings.ScreenEdge);
    private void OnDisplayChanged(object? sender, EventArgs e) => Dispatcher.Invoke(Place);
    private void UpdateFullscreen()
    {
        if (_hiddenToTray) return;
        if (_settings.FullscreenBehavior == FullscreenBehavior.RemainVisible) { if (!IsVisible) Show(); return; }
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == new System.Windows.Interop.WindowInteropHelper(this).Handle) return;
        GetWindowThreadProcessId(foreground, out var processId);
        if (processId == Environment.ProcessId) return;
        if (!GetWindowRect(foreground, out var rect)) return;
        var bounds = Forms.Screen.FromHandle(foreground).Bounds;
        var fullscreen = rect.Left <= bounds.Left + 2 && rect.Top <= bounds.Top + 2 && rect.Right >= bounds.Right - 2 && rect.Bottom >= bounds.Bottom - 2;
        if (fullscreen && !_fullscreenActive && _settings.FullscreenBehavior == FullscreenBehavior.PauseAnimations)
        {
            var currentHeight = ActualHeight;
            BeginAnimation(HeightProperty, null);
            Height = currentHeight;
        }
        _fullscreenActive = fullscreen;
        if (_settings.FullscreenBehavior == FullscreenBehavior.Hide)
        {
            if (fullscreen && IsVisible) Hide();
            else if (!fullscreen && !IsVisible) Show();
        }
        else if (fullscreen) _timelineTimer.Stop();
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int processId);

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_exiting && _settings.MinimizeToTray) { e.Cancel = true; _hiddenToTray = true; Hide(); }
        base.OnClosing(e);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        _hover.Stop(); _fullscreen.Stop(); _timelineTimer.Stop(); _activityTimer.Stop(); _transientTimer.Stop();
        _native?.Dispose(); _media.Dispose();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        if (!_exiting) Application.Current.Shutdown();
    }
}
