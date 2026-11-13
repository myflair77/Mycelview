using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
namespace Mycelview;
public sealed class EdgeActivationSettings
{
    public bool Enabled { get; set; } = true;
    public string Edge { get; set; } = "Left";
    public int DelayMs { get; set; } = 100;
    private int? thicknessPixels;
    public int ThicknessPixels { get => thicknessPixels ?? (ThicknessScale is int scale ? Math.Clamp(scale, 1, 5) * 4 : 10); set => thicknessPixels = value; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? ThicknessScale { get; set; } // Read legacy multiplier settings without changing their visible size.
    public int StartPercent { get; set; } = 50;
    public int EndPercent { get; set; } = 100;
    public string Monitor { get; set; } = "Automatic";
    public EdgeActivationSettings Normalize() => new() { Enabled = Enabled, ThicknessPixels = Math.Clamp(ThicknessPixels, 2, 30), StartPercent = Math.Clamp(StartPercent, 0, 99), EndPercent = Math.Clamp(EndPercent, Math.Clamp(StartPercent, 0, 99) + 1, 100), Edge = Edge is "Right" or "Top" or "Bottom" ? Edge : "Left", DelayMs = Math.Clamp(DelayMs, 100, 5000), Monitor = string.IsNullOrWhiteSpace(Monitor) || Monitor is "Leftmost" or "Rightmost" or "Topmost" or "Bottommost" ? "Automatic" : Monitor };
}
internal sealed record EdgeMonitor(string Device, int Left, int Top, int Right, int Bottom, bool Primary)
{
    public string Label => $"{Device} · {Right - Left}×{Bottom - Top}" + (Primary ? " (주 모니터)" : "");
}
internal sealed class EdgeDwell
{
    private string? active;
    private long entered;
    private bool fired;
    internal void Reset() { active = null; fired = false; }
    internal bool Update(string? zone, long now, int delay)
    {
        if (zone == null) { Reset(); return false; }
        if (active != zone) { active = zone; entered = now; fired = false; }
        if (fired || now - entered < delay) return false;
        fired = true; return true;
    }
}
internal sealed class EdgeActivationService : IDisposable
{
    private readonly MainWindow window;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly EdgeDwell dwell = new();
    private readonly EdgeActivationMarker marker = new();
    private List<EdgeMonitor> monitors = [];
    private long refreshed;
    private string configuration = "";
    internal EdgeActivationService(MainWindow window)
    {
        this.window = window; timer.Tick += Tick; timer.Start();
    }
    internal static EdgeMonitor? SelectMonitor(IEnumerable<EdgeMonitor> monitors, string choice, string edge = "Left")
    {
        if (choice == "Automatic") choice = edge switch { "Right" => "Rightmost", "Top" => "Topmost", "Bottom" => "Bottommost", _ => "Leftmost" };
        return choice switch
        {
            "Leftmost" => monitors.OrderBy(m => m.Left).ThenBy(m => m.Top).FirstOrDefault(),
            "Rightmost" => monitors.OrderByDescending(m => m.Right).ThenBy(m => m.Top).FirstOrDefault(),
            "Topmost" => monitors.OrderBy(m => m.Top).ThenBy(m => m.Left).FirstOrDefault(),
            "Bottommost" => monitors.OrderByDescending(m => m.Bottom).ThenBy(m => m.Left).FirstOrDefault(),
            _ => monitors.FirstOrDefault(m => m.Device == choice)
        };
    }
    internal static bool OnEdge(EdgeMonitor m, string edge, int x, int y) => x >= m.Left && x < m.Right && y >= m.Top && y < m.Bottom && edge switch
    {
        "Right" => x >= m.Right - 2, "Top" => y < m.Top + 2, "Bottom" => y >= m.Bottom - 2, _ => x < m.Left + 2
    };
    internal static bool InRange(EdgeMonitor m, EdgeActivationSettings settings, int x, int y)
    {
        if (!OnEdge(m, settings.Edge, x, y)) return false;
        double position = settings.Edge is "Left" or "Right"
            ? (y - m.Top) * 100.0 / Math.Max(1, m.Bottom - m.Top - 1)
            : (x - m.Left) * 100.0 / Math.Max(1, m.Right - m.Left - 1);
        return position >= settings.StartPercent && position <= settings.EndPercent;
    }
    private void Tick(object? sender, EventArgs e)
    {
        var s = window.ViewModel.EdgeActivation;
        string key = $"{s.Enabled}|{s.Edge}|{s.DelayMs}|{s.Monitor}|{s.StartPercent}|{s.EndPercent}";
        if (key != configuration) { configuration = key; dwell.Reset(); }
        if (!s.Enabled || window.TestMode) { marker.Update(null, s); dwell.Reset(); return; }
        long now = Environment.TickCount64;
        if (monitors.Count == 0 || now - refreshed > 2000) { monitors = GetMonitors(); refreshed = now; }
        var monitor = SelectMonitor(monitors, s.Monitor, s.Edge);
        marker.Update(monitor, s);
        if (!window.IsEnabled || GetAsyncKeyState(1) < 0 || GetAsyncKeyState(2) < 0) { dwell.Reset(); return; }
        string? zone = monitor != null && GetCursorPos(out var point) && InRange(monitor, s, point.X, point.Y) ? monitor.Device + s.Edge : null;
        if (dwell.Update(zone, now, s.DelayMs) && !window.IsActive) Reveal();
    }
    private void Reveal()
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (IsIconic(hwnd)) ShowWindow(hwnd, 9);
        window.Show();
        uint current = GetCurrentThreadId(), foreground = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        bool attached = foreground != 0 && foreground != current && AttachThreadInput(current, foreground, true);
        try { BringWindowToTop(hwnd); SetForegroundWindow(hwnd); window.Activate(); }
        finally { if (attached) AttachThreadInput(current, foreground, false); }
    }
    public void Dispose() { timer.Stop(); timer.Tick -= Tick; marker.Dispose(); }
    internal static List<EdgeMonitor> GetMonitors()
    {
        var result = new List<EdgeMonitor>();
        MonitorCallback callback = (IntPtr h, IntPtr dc, ref NativeRect bounds, IntPtr data) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), Device = "" };
            if (GetMonitorInfo(h, ref info)) result.Add(new(info.Device, info.Bounds.Left, info.Bounds.Top, info.Bounds.Right, info.Bounds.Bottom, (info.Flags & 1) != 0));
            return true;
        };
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero); return result;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfo
    { public int Size; public NativeRect Bounds, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
    private delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref NativeRect rect, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from, uint to, bool attach);
}
