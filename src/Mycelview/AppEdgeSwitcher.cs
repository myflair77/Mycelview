using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
namespace Mycelview;

internal sealed class AppEdgeSwitcher : IDisposable
{
    readonly MainWindow owner;
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(50)};
    readonly EdgeDwell dwell=new();
    readonly Dictionary<Guid,EdgeActivationMarker> markers=[];
    List<EdgeMonitor> monitors=[];
    long refreshed,shown;
    WindowPicker? picker;
    EdgeActivationSettings? configuration;
    internal AppEdgeSwitcher(MainWindow owner){this.owner=owner;timer.Tick+=Tick;timer.Start();}
    internal static bool Matches(EdgeActivationSettings settings,List<EdgeMonitor> monitors,out AppWindowZone? zone)
    {
        zone=null;if(!GetCursorPos(out var p))return false;
        zone=(settings.AppZones??[]).FirstOrDefault(z=>EdgeActivationService.SelectMonitor(monitors,z.Monitor,z.Edge) is {} m && EdgeActivationService.InRange(m,z.Region(settings),p.X,p.Y));
        return zone!=null;
    }
    void Tick(object? sender,EventArgs e)
    {
        var settings=owner.ViewModel.EdgeActivation;

        if(!ReferenceEquals(configuration,settings)){configuration=settings;Close();dwell.Reset();}
        if(Environment.TickCount64-refreshed>2000||monitors.Count==0){monitors=EdgeActivationService.GetMonitors();refreshed=Environment.TickCount64;}
        var zones=settings.AppZones??[];
        foreach(var id in markers.Keys.Where(id=>!settings.Enabled||!zones.Any(z=>z.Id==id)).ToArray()){markers[id].Dispose();markers.Remove(id);}
        if(!settings.Enabled||owner.TestMode){Close();dwell.Reset();return;}
        foreach(var zone in zones){
            if(!markers.TryGetValue(zone.Id,out var marker))markers[zone.Id]=marker=new();
            marker.Update(EdgeActivationService.SelectMonitor(monitors,zone.Monitor,zone.Edge),zone.Region(settings));
        }
        if(!owner.IsEnabled){Close();dwell.Reset();return;}
        if(GetAsyncKeyState(1)<0||GetAsyncKeyState(2)<0){dwell.Reset();return;}
        Matches(settings,monitors,out var active);
        if(picker!=null && Environment.TickCount64-shown>650 && active==null && !picker.ContainsCursor())Close();
        if(dwell.Update(active?.Id.ToString(),Environment.TickCount64,settings.DelayMs) && active!=null){
            var monitor=EdgeActivationService.SelectMonitor(monitors,active.Monitor,active.Edge);
            if(monitor==null)return;
            Close();picker=new WindowPicker(active,AppWindows.List(active.AppPath),monitor);
            picker.Show();shown=Environment.TickCount64;
        }
    }
    void Close(){picker?.Close();picker=null;}
    public void Dispose(){timer.Stop();timer.Tick-=Tick;Close();foreach(var marker in markers.Values)marker.Dispose();markers.Clear();}
    [StructLayout(LayoutKind.Sequential)] internal struct Point{public int X,Y;}
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
}

internal sealed class WindowPicker : Window
{
    readonly List<IntPtr> thumbnails=[];
    readonly List<AppOpenWindow> windows;
    readonly WrapPanel cards=new();
    readonly TextBlock pageLabel=new(){VerticalAlignment=VerticalAlignment.Center};
    readonly AppWindowZone zone;
    readonly EdgeMonitor monitor;
    int page;
    AppEdgeSwitcher.Point anchor;
    internal int ThumbnailCount => thumbnails.Count;
    internal WindowPicker(AppWindowZone zone,List<AppOpenWindow> windows,EdgeMonitor monitor)
    {
        this.zone=zone;this.windows=windows;this.monitor=monitor;
        AppEdgeSwitcher.GetCursorPos(out anchor);
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;ShowActivated=false;Topmost=true;
        Width=540;SizeToContent=SizeToContent.Height;
        Background=Brushes.White;Foreground=Brushes.Black;
        SetResourceReference(FontSizeProperty,"FontSize11");
        var root=new StackPanel{Margin=new Thickness(12)};
        var heading=new DockPanel{Margin=new Thickness(0,0,0,10)};
        var close=new Button{Content="닫기"};DockPanel.SetDock(close,Dock.Right);heading.Children.Add(close);close.Click+=(_,_)=>Close();
        heading.Children.Add(new TextBlock{Text=zone.Name+" · 열린 창 "+windows.Count+"개",FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});
        root.Children.Add(heading);root.Children.Add(cards);
        var navigation=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center};
        var prev=new Button{Content="◀",ToolTip="이전 창 목록"};var next=new Button{Content="▶",ToolTip="다음 창 목록"};
        prev.Click+=(_,_)=>{if(page>0){page--;Build();}};
        next.Click+=(_,_)=>{if((page+1)*4<windows.Count){page++;Build();}};
        navigation.Children.Add(prev);navigation.Children.Add(pageLabel);navigation.Children.Add(next);root.Children.Add(navigation);
        Content=new Border{BorderBrush=Brushes.LightSkyBlue,BorderThickness=new Thickness(1),Child=root};
        Loaded+=(_,_)=>{Build();Place();};
        Closed+=(_,_)=>Release();
        Deactivated+=(_,_)=>Close();
        PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape)Close();};
    }
    void Place()
    {
        UpdateLayout();
        var hwnd=new WindowInteropHelper(this).Handle;
        var scale=PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice??Matrix.Identity;
        int width=(int)(ActualWidth*scale.M11),height=(int)(ActualHeight*scale.M22);
        int x=zone.Edge=="Right"?monitor.Right-width-12:monitor.Left+12;
        int y=zone.Edge=="Bottom"?monitor.Bottom-height-12:monitor.Top+12;
        if(zone.Edge is "Left" or "Right")y=anchor.Y-height/2;
        else x=anchor.X-width/2;
        x=Math.Clamp(x,monitor.Left,Math.Max(monitor.Left,monitor.Right-width));
        y=Math.Clamp(y,monitor.Top,Math.Max(monitor.Top,monitor.Bottom-height));
        SetWindowPos(hwnd,new IntPtr(-1),x,y,0,0,0x11);
        UpdateThumbnails();
    }
    readonly List<(IntPtr Handle,Border Preview)> slots=[];
    void Build()
    {
        Release();cards.Children.Clear();slots.Clear();
        pageLabel.Text=$" {page+1} / {Math.Max(1,(windows.Count+3)/4)} ";
        if(windows.Count==0)cards.Children.Add(new TextBlock{Text="이 앱의 열린 창이 없습니다.",Margin=new Thickness(12)});
        foreach(var item in windows.Skip(page*4).Take(4))
        {
            var panel=new StackPanel();
            var preview=new Border{Width=238,Height=126,Background=Brushes.WhiteSmoke,Child=new TextBlock{Text="미리보기를 사용할 수 없는 창\n(클릭하여 전환)",TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
            panel.Children.Add(preview);
            panel.Children.Add(new TextBlock{Text=item.Title,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(2,6,2,2)});
            var button=new Button{Content=panel,Width=250,Padding=new Thickness(4),Margin=new Thickness(2),ToolTip=item.Title};
            button.Click+=(_,_)=>{Close();AppWindows.Activate(item.Handle);};
            cards.Children.Add(button);slots.Add((item.Handle,preview));
        }
        UpdateLayout();Place();
    }
    void UpdateThumbnails()
    {
        Release();
        var dest=new WindowInteropHelper(this).Handle;
        var transform=PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice??Matrix.Identity;
        foreach(var (source,preview) in slots){
            if(DwmRegisterThumbnail(dest,source,out var thumb)!=0)continue;
            thumbnails.Add(thumb);
            var p=transform.Transform(preview.TranslatePoint(new System.Windows.Point(),this));
            var w=preview.ActualWidth*transform.M11;var h=preview.ActualHeight*transform.M22;
            if(DwmQueryThumbnailSourceSize(thumb,out var size)==0 && size.Width>0 && size.Height>0){
                var ratio=Math.Min(w/size.Width,h/size.Height);
                p.X+=(w-size.Width*ratio)/2;p.Y+=(h-size.Height*ratio)/2;
                w=size.Width*ratio;h=size.Height*ratio;
            }
            var props=new ThumbnailProperties{Flags=1|4|8|16,Destination=new Rect{Left=(int)p.X,Top=(int)p.Y,Right=(int)(p.X+w),Bottom=(int)(p.Y+h)},Opacity=255,Visible=true,ClientOnly=false};
            DwmUpdateThumbnailProperties(thumb,ref props);
        }
    }
    void Release(){foreach(var thumb in thumbnails)DwmUnregisterThumbnail(thumb);thumbnails.Clear();}
    internal bool ContainsCursor()
    {
        return AppEdgeSwitcher.GetCursorPos(out var p)&&GetWindowRect(new WindowInteropHelper(this).Handle,out var r)&&p.X>=r.Left-16&&p.X<=r.Right+16&&p.Y>=r.Top-16&&p.Y<=r.Bottom+16;
    }
    [StructLayout(LayoutKind.Sequential)] struct Size{public int Width,Height;}
    [DllImport("dwmapi.dll")]static extern int DwmQueryThumbnailSourceSize(IntPtr thumbnail,out Size size);
    [StructLayout(LayoutKind.Sequential)] struct Rect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] struct ThumbnailProperties{public uint Flags;public Rect Destination,Source;public byte Opacity;[MarshalAs(UnmanagedType.Bool)]public bool Visible;[MarshalAs(UnmanagedType.Bool)]public bool ClientOnly;}
    [DllImport("dwmapi.dll")]static extern int DwmRegisterThumbnail(IntPtr dest,IntPtr source,out IntPtr thumbnail);
    [DllImport("dwmapi.dll")]static extern int DwmUnregisterThumbnail(IntPtr thumbnail);
    [DllImport("dwmapi.dll")]static extern int DwmUpdateThumbnailProperties(IntPtr thumbnail,ref ThumbnailProperties properties);
    [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
    [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int w,int h,uint flags);
}



