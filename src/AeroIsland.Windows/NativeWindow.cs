using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using AeroIsland.Core;
using Point = System.Windows.Point;

namespace AeroIsland.Windows;

public sealed class NativeWindow : IDisposable
{
    private const int WmClipboardUpdate = 0x031D;
    private const int WmHotkey = 0x0312;
    private const int WmNcHitTest = 0x0084;
    private const int WmDpiChanged = 0x02E0;
    private const int HotkeyId = 0xA310;
    private readonly Window _window;
    private HwndSource? _source;
    private IntPtr _handle;
    public event EventHandler? ClipboardUpdated;
    public event EventHandler? HotkeyPressed;
    public event EventHandler? DpiChanged;
    public bool HotkeyAvailable { get; private set; }
    public double CornerRadius { get; set; } = 22;

    public NativeWindow(Window window) => _window = window;

    public void Start(AppSettings settings)
    {
        _handle = new WindowInteropHelper(_window).Handle;
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WndProc);
        AddClipboardFormatListener(_handle);
        RegisterShortcut(settings.Hotkey);
    }

    public void RegisterShortcut(string shortcut)
    {
        if (_handle == IntPtr.Zero) return;
        UnregisterHotKey(_handle, HotkeyId);
        var modifier = shortcut.Contains("Shift", StringComparison.Ordinal) ? 0x0002u | 0x0004u : 0x0002u | 0x0001u;
        HotkeyAvailable = RegisterHotKey(_handle, HotkeyId, modifier | 0x4000u, 0x20);
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmNcHitTest && _source?.CompositionTarget is { } target)
        {
            var screenX = (short)(lParam.ToInt64() & 0xffff);
            var screenY = (short)((lParam.ToInt64() >> 16) & 0xffff);
            var origin = _window.PointToScreen(new Point(0, 0));
            var local = target.TransformFromDevice.Transform(new Point(screenX - origin.X, screenY - origin.Y));
            var radius = CornerRadius;
            var width = _window.ActualWidth;
            var height = _window.ActualHeight;
            var inside = local.X >= 0 && local.Y >= 0 && local.X <= width && local.Y <= height;
            if (inside && local.X < radius && local.Y < radius)
                inside = Math.Pow(local.X - radius, 2) + Math.Pow(local.Y - radius, 2) <= radius * radius;
            if (inside && local.X > width - radius && local.Y < radius)
                inside = Math.Pow(local.X - width + radius, 2) + Math.Pow(local.Y - radius, 2) <= radius * radius;
            if (inside && local.X < radius && local.Y > height - radius)
                inside = Math.Pow(local.X - radius, 2) + Math.Pow(local.Y - height + radius, 2) <= radius * radius;
            if (inside && local.X > width - radius && local.Y > height - radius)
                inside = Math.Pow(local.X - width + radius, 2) + Math.Pow(local.Y - height + radius, 2) <= radius * radius;
            if (!inside) { handled = true; return new IntPtr(-1); }
        }
        if (message == WmClipboardUpdate) ClipboardUpdated?.Invoke(this, EventArgs.Empty);
        if (message == WmDpiChanged) DpiChanged?.Invoke(this, EventArgs.Empty);
        if (message == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            RemoveClipboardFormatListener(_handle);
            UnregisterHotKey(_handle, HotkeyId);
        }
        _source?.RemoveHook(WndProc);
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}

public static class WindowPlacement
{
    public static void AtEdgeCenter(Window window, string monitorDeviceName, ScreenEdge edge)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        var screen = screens.FirstOrDefault(s => s.DeviceName == monitorDeviceName) ??
            screens.FirstOrDefault(s => s.Primary) ?? screens[0];
        var source = PresentationSource.FromVisual(window);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var area = screen.WorkingArea;
        var topLeft = transform.Transform(new Point(area.Left, area.Top));
        var bottomRight = transform.Transform(new Point(area.Right, area.Bottom));
        window.Left = Math.Clamp((topLeft.X + bottomRight.X - window.Width) / 2, topLeft.X,
            Math.Max(topLeft.X, bottomRight.X - window.Width));
        window.Top = edge == ScreenEdge.Top ? topLeft.Y + 8 : Math.Max(topLeft.Y, bottomRight.Y - window.Height - 8);
    }
}
