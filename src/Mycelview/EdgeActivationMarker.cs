using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
namespace Mycelview;
// A passive edge marker: never accepts activation, clicks or taskbar presence.
internal sealed class EdgeActivationMarker : IDisposable
{
    private Window? marker;
    private string? lastBounds;
    internal static (int X, int Y, int Width, int Height) Bounds(EdgeMonitor monitor, EdgeActivationSettings settings)
    {
        bool vertical = settings.Edge is "Left" or "Right";
        int length = vertical ? monitor.Bottom - monitor.Top : monitor.Right - monitor.Left;
        int start = (int)Math.Round(length * settings.StartPercent / 100.0);
        int end = (int)Math.Round(length * settings.EndPercent / 100.0);
        int thickness = Math.Clamp(settings.ThicknessPixels, 2, 30);
        return vertical
            ? (settings.Edge == "Right" ? monitor.Right - thickness : monitor.Left, monitor.Top + start, thickness, Math.Max(1, end - start))
            : (monitor.Left + start, settings.Edge == "Bottom" ? monitor.Bottom - thickness : monitor.Top, Math.Max(1, end - start), thickness);
    }
    internal void Update(EdgeMonitor? monitor, EdgeActivationSettings settings)
    {
        if (!settings.Enabled || monitor == null) { marker?.Hide(); lastBounds = null; return; }
        var bounds = Bounds(monitor, settings); string key = bounds.ToString();
        if (key == lastBounds && marker?.IsVisible == true) return;
        if (marker == null)
        {
            marker = new Window { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, AllowsTransparency = true,
                Background = Brushes.Transparent, ShowActivated = false, ShowInTaskbar = false, Topmost = true,
                Width = 4, Height = 4, Left = -10000, Top = -10000, IsHitTestVisible = false, Focusable = false,
                Content = new Border { Background = new SolidColorBrush(Color.FromArgb(90, 70, 155, 255)), CornerRadius = new CornerRadius(2), IsHitTestVisible = false } };
            var handle = new WindowInteropHelper(marker).EnsureHandle();
            int style = GetWindowLong(handle, -20);
            SetWindowLong(handle, -20, style | 0x20 | 0x80 | 0x08000000); // transparent, tool window, no activate
        }
        marker.Show();
        SetWindowPos(new WindowInteropHelper(marker).Handle, new IntPtr(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x10 | 0x40);
        lastBounds = key;
    }
    public void Dispose() { marker?.Close(); marker = null; }
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
