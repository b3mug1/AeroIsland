using System.Reflection;
using System.IO;
using System.Threading;
using System.Windows;
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
                var window = new MainWindow(storage, new AppSettings());
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
                }
                completion.TrySetResult(null);
            }
            catch (Exception error) { completion.TrySetResult(error); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        var failure = await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Null(failure);
    }
}
