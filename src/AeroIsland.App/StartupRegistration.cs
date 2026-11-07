using Microsoft.Win32;

namespace AeroIsland.App;

internal static class StartupRegistration
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
        if (enabled) key?.SetValue("AeroIsland", $"\"{Environment.ProcessPath}\"");
        else key?.DeleteValue("AeroIsland", throwOnMissingValue: false);
    }
}
