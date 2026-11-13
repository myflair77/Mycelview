using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Mycelview;

// Modal owners do not receive WPF mouse events, so observe clicks in screen coordinates.
internal static class DismissSettingsOnOutsideClick
{
    internal static void Attach(Window dialog, bool modal = true)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        int[] buttons = [1, 2, 4];
        var down = new bool[buttons.Length];
        dialog.Loaded += (_, _) =>
        {
            for (int i = 0; i < buttons.Length; i++) down[i] = (GetAsyncKeyState(buttons[i]) & 0x8000) != 0;
            timer.Start();
        };
        dialog.Closed += (_, _) => timer.Stop();
        timer.Tick += (_, _) =>
        {
            bool clicked = false;
            for (int i = 0; i < buttons.Length; i++)
            {
                short state = GetAsyncKeyState(buttons[i]);
                bool pressed = (state & 0x8000) != 0;
                clicked |= !down[i] && (pressed || (state & 1) != 0);
                down[i] = pressed;
            }
            if (!clicked || !dialog.IsVisible || !GetCursorPos(out var point)) return;
            var target = GetAncestor(WindowFromPoint(point), 2);
            if (BelongsToDialog(dialog, target)) return;
            timer.Stop();
            if (modal) dialog.DialogResult = false; else dialog.Close();
        };
    }
    internal static bool BelongsToDialog(Window dialog, IntPtr target)
    {
            var handle = new WindowInteropHelper(dialog).Handle;
            // ComboBox dropdowns and other owned popups are part of the dialog.
            for (var owner = target; owner != IntPtr.Zero; owner = GetWindow(owner, 4))
                if (owner == handle) return true;
            return false;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
}
