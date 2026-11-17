using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
namespace Mycelview;

public sealed class AppWindowZone
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string AppPath { get; set; } = "";
    public string Edge { get; set; } = "Left";
    public int StartPercent { get; set; }
    public int EndPercent { get; set; } = 25;
    public string Monitor { get; set; } = "Automatic";
    public EdgeActivationSettings Region(EdgeActivationSettings global) => new() { Enabled=true, Edge=Edge, StartPercent=StartPercent, EndPercent=EndPercent, Monitor=Monitor, DelayMs=global.DelayMs,ThicknessPixels=global.ThicknessPixels,OpacityPercent=global.OpacityPercent };
    public AppWindowZone Normalized()
    {
        var copy=Copy();
        var region=Region(new()).Normalize();
        copy.Edge=region.Edge;copy.StartPercent=region.StartPercent;copy.EndPercent=region.EndPercent;copy.Monitor=region.Monitor;
        if(copy.Id==Guid.Empty)copy.Id=Guid.NewGuid();
        if(string.IsNullOrWhiteSpace(copy.Name))copy.Name=Path.GetFileNameWithoutExtension(copy.AppPath);
        return copy;
    }
    public AppWindowZone Copy()=> (AppWindowZone)MemberwiseClone();
}
internal sealed record AppOpenWindow(IntPtr Handle,string Title,string AppPath)
{
    public string AppName => Path.GetFileNameWithoutExtension(AppPath);
}
internal static class AppWindows
{
    internal static List<AppOpenWindow> List(string? path=null,bool includeSelf=false)
    {
        var result=new List<AppOpenWindow>();
        EnumWindows((hwnd,_)=>{
            if(!IsWindowVisible(hwnd)||GetWindow(hwnd,4)!=IntPtr.Zero)return true;
            int style=GetWindowLong(hwnd,-20);if((style&0x80)!=0)return true;
            if(DwmGetWindowAttribute(hwnd,14,out int cloaked,4)==0 && cloaked!=0)return true;
            var title=new StringBuilder(1024);GetWindowText(hwnd,title,title.Capacity);if(title.Length==0)return true;
            GetWindowThreadProcessId(hwnd,out var pid);
            if(!includeSelf && pid==Environment.ProcessId)return true;
            try{
                using var process=Process.GetProcessById((int)pid);
                var app=process.MainModule?.FileName;
                if(app!=null&&(path==null||StringComparer.OrdinalIgnoreCase.Equals(app,path)))result.Add(new(hwnd,title.ToString(),app));
            }catch(Exception ex) when(ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException or NotSupportedException){}
            return true;
        },IntPtr.Zero);
        return result;
    }
    internal static bool Activate(IntPtr hwnd)
    {
        if(!IsWindow(hwnd))return false;
        if(IsIconic(hwnd))ShowWindow(hwnd,9);
        uint current=GetCurrentThreadId(),foreground=GetWindowThreadProcessId(GetForegroundWindow(),out _);
        bool attached=foreground!=0 && foreground!=current && AttachThreadInput(current,foreground,true);
        try{BringWindowToTop(hwnd);return SetForegroundWindow(hwnd);}
        finally{if(attached)AttachThreadInput(current,foreground,false);}
    }
    private delegate bool EnumProc(IntPtr hwnd,IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback,IntPtr data);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd,uint command);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd,int index);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int count);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd,int attribute,out int value,int size);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd,int command);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint a,uint b,bool attach);
}
