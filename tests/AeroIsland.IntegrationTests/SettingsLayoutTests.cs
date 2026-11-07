using System.Reflection;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AeroIsland.App;
using AeroIsland.Core;
using AeroIsland.Infrastructure;
using Xunit;

namespace AeroIsland.IntegrationTests;

public sealed class SettingsLayoutTests
{
    [Fact]
    public async Task Every_settings_section_builds_its_WPF_visual_tree()
    {
        var completion = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var storage = new LocalStorage(Path.Combine(Path.GetTempPath(), "AeroIsland.LayoutTests", Guid.NewGuid().ToString("N")));
                var previewDirectory = Environment.GetEnvironmentVariable("AEROISLAND_PREVIEW_DIR");
                var window = new MainWindow(storage, new AppSettings { ReducedMotion = previewDirectory is not null });
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(MainWindow).GetField("_expanded", flags)!.SetValue(window, true);
                typeof(MainWindow).GetMethod("ShowExpanded", flags)!.Invoke(window, null);
                foreach (var section in new[] { "General", "Appearance", "Media", "Clipboard", "Performance", "About" })
                {
                    typeof(MainWindow).GetField("_settingsSection", flags)!.SetValue(window, section);
                    typeof(MainWindow).GetMethod("ShowPage", flags)!.Invoke(window, ["Settings"]);
                    if (window.Content is not FrameworkElement content) throw new InvalidOperationException("Window content is missing.");
                    content.Measure(new Size(420, 540));
                    content.Arrange(new Rect(0, 0, 420, 540));
                    content.UpdateLayout();
                    SavePreview(content, $"Settings-{section}", previewDirectory);
                }
                foreach (var page in new[] { "Island", "Focus", "Music", "Clipboard" })
                {
                    typeof(MainWindow).GetMethod("ShowPage", flags)!.Invoke(window, [page]);
                    var content = (FrameworkElement)window.Content;
                    content.Measure(new Size(420, 540));
                    content.Arrange(new Rect(0, 0, 420, 540));
                    content.UpdateLayout();
                    SavePreview(content, page, previewDirectory);
                }
                var history = (ObservableCollection<HistoryItem>)typeof(MainWindow).GetField("_history", flags)!.GetValue(window)!;
                for (var index = 1; index <= 4; index++)
                    history.Add(new HistoryItem(Guid.NewGuid(), HistoryKind.Text, $"Item {index}", $"Item {index}", null,
                        DateTimeOffset.UtcNow.AddMinutes(-index), false, $"item-{index}"));
                typeof(MainWindow).GetField("_quickClipboardMode", flags)!.SetValue(window, true);
                typeof(MainWindow).GetMethod("ShowPage", flags)!.Invoke(window, ["Clipboard"]);
                var quick = (FrameworkElement)window.Content;
                quick.Measure(new Size(420, 540));
                quick.Arrange(new Rect(0, 0, 420, 540));
                quick.UpdateLayout();
                var historyHost = (System.Windows.Controls.StackPanel)typeof(MainWindow).GetField("_historyHost", flags)!.GetValue(window)!;
                if (historyHost.Children.Count != 3) throw new InvalidOperationException("Quick clipboard should show three recent items.");
                SavePreview(quick, "Clipboard-Quick", previewDirectory);
                typeof(MainWindow).GetMethod("RenderHistory", flags)!.Invoke(window, ["Item 4"]);
                if (historyHost.Children.Count != 1) throw new InvalidOperationException("Quick clipboard search should include older items.");
                typeof(MainWindow).GetField("_expanded", flags)!.SetValue(window, false);
                typeof(MainWindow).GetMethod("StartFocus", flags)!.Invoke(window, [5]);
                var compact = (FrameworkElement)window.Content;
                compact.Measure(new Size(220, 50));
                compact.Arrange(new Rect(0, 0, 220, 50));
                compact.UpdateLayout();
                SavePreview(compact, "Compact-Focus", previewDirectory, 220, 50);
                typeof(MainWindow).GetMethod("StopFocus", flags)!.Invoke(window, null);
                var media = new MediaSnapshot("sample.player", "A sample track", "Sample artist", "Album", true,
                    true, true, true, true, TimeSpan.FromSeconds(48), TimeSpan.FromSeconds(180), CreateArtwork());
                typeof(MainWindow).GetMethod("ShowMedia", flags)!.Invoke(window, [media]);
                compact.Measure(new Size(220, 50));
                compact.Arrange(new Rect(0, 0, 220, 50));
                compact.UpdateLayout();
                SavePreview(compact, "Compact-Music", previewDirectory, 220, 50);
                typeof(MainWindow).GetMethod("ShowMedia", flags)!.Invoke(window, [null]);
                typeof(MainWindow).GetMethod("ShowTransient", flags)!.Invoke(window, ["Copied to clipboard", "Clipboard"]);
                var contextPage = (string)typeof(MainWindow).GetProperty("ContextPage", flags)!.GetValue(window)!;
                if (contextPage != "Clipboard") throw new InvalidOperationException("Clipboard event should open clipboard controls.");
                compact.Measure(new Size(310, 58));
                compact.Arrange(new Rect(0, 0, 310, 58));
                compact.UpdateLayout();
                SavePreview(compact, "Compact-Event", previewDirectory, 310, 58);
                completion.TrySetResult(null);
            }
            catch (Exception error) { completion.TrySetResult(error); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        var failure = await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Null(failure);
    }

    private static void SavePreview(FrameworkElement content, string name, string? directory, int width = 420, int height = 540)
    {
        if (directory is null) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(stream);
    }

    private static byte[] CreateArtwork()
    {
        const int size = 16;
        var pixels = new byte[size * size * 4];
        for (var index = 0; index < size * size; index++)
        {
            pixels[index * 4] = (byte)(index % size < size / 2 ? 190 : 90);
            pixels[index * 4 + 1] = 110;
            pixels[index * 4 + 2] = 190;
            pixels[index * 4 + 3] = 255;
        }
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
