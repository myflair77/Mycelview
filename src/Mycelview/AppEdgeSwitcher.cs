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
    readonly Dictionary<Guid,List<IntPtr>> windowOrders=[];
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
            marker.Update(EdgeActivationService.SelectMonitor(monitors,zone.Monitor,zone.Edge),zone.Region(settings),EdgeRegionPalette.Get(settings,monitors,zone.Id.ToString()));
        }
        if(!owner.IsEnabled){Close();dwell.Reset();return;}
        if(GetAsyncKeyState(1)<0||GetAsyncKeyState(2)<0){dwell.Reset();return;}
        Matches(settings,monitors,out var active);
        if(active==null&&settings.SwitchMode=="Click"&&GetCursorPos(out var cursor)&&EdgeActivationService.SelectMonitor(monitors,settings.Monitor,settings.Edge) is {} ownMonitor&&EdgeActivationService.InRange(ownMonitor,settings,cursor.X,cursor.Y))
            active=new AppWindowZone{Id=Guid.Empty,Name="Mycelview",AppPath=Environment.ProcessPath!,Edge=settings.Edge,Monitor=settings.Monitor,StartPercent=settings.StartPercent,EndPercent=settings.EndPercent,SwitchMode="Click"};
        if(picker!=null && Environment.TickCount64-shown>650 && active==null && !picker.ContainsCursor())Close();
        if(dwell.Update(active?.Id.ToString(),Environment.TickCount64,settings.DelayMs) && active!=null){
            var monitor=EdgeActivationService.SelectMonitor(monitors,active.Monitor,active.Edge);
            if(monitor==null)return;
            Close();
            var listed=AppWindows.List(string.IsNullOrWhiteSpace(active.WindowAppPath)?AppWindows.WindowExecutable(active.AppPath):active.WindowAppPath);
            if(active.Id==Guid.Empty)listed=[new AppOpenWindow(new WindowInteropHelper(owner).Handle,"Mycelview",Environment.ProcessPath!)];
            if(active.SwitchMode=="Mouse"&&listed.Count==1&&BrowserTabsBridge.Current?.ForWindow(listed[0])==null){AppWindows.Activate(listed[0].Handle);return;}
            if(windowOrders.TryGetValue(active.Id,out var order))listed=listed.OrderBy(w=>{int index=order.IndexOf(w.Handle);return index<0?int.MaxValue:index;}).ToList();
            windowOrders[active.Id]=listed.Select(w=>w.Handle).ToList();
            foreach(var id in windowOrders.Keys.Where(id=>!zones.Any(z=>z.Id==id)).ToArray())windowOrders.Remove(id);
            var next=new WindowPicker(active,listed,monitor,items=>windowOrders[active.Id]=items.Select(w=>w.Handle).ToList());
            picker=next;
            next.Closed+=(_,_)=>{if(ReferenceEquals(picker,next))picker=null;};
            next.Show();shown=Environment.TickCount64;
        }
    }
    void Close(){var previous=picker;picker=null;previous?.Dismiss();}
    public void Dispose(){timer.Stop();timer.Tick-=Tick;Close();foreach(var marker in markers.Values)marker.Dispose();markers.Clear();}
    [StructLayout(LayoutKind.Sequential)] internal struct Point{public int X,Y;}
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
}

internal sealed class WindowPicker : Window
{
    readonly List<IntPtr> thumbnails=[];
    Border contentFrame=null!;
    double contentScale=1;
    internal static double FitScale(double width,double height,double availableWidth,double availableHeight)=>Math.Min(1,Math.Min(availableWidth/Math.Max(1,width),availableHeight/Math.Max(1,height)));
    readonly List<AppOpenWindow> windows;
    readonly System.Windows.Controls.Primitives.UniformGrid cards=new();
    readonly TextBlock pageLabel=new(){VerticalAlignment=VerticalAlignment.Center};
    readonly AppWindowZone zone;
    readonly EdgeMonitor monitor;
    readonly DispatcherTimer refresh=new(){Interval=TimeSpan.FromMilliseconds(250)};
    readonly Action<List<AppOpenWindow>>? orderChanged;
    readonly Action<IntPtr> activateWindow;
    IntPtr lastHover;
    internal void HoverWindow(IntPtr handle){
        if(zone.SwitchMode!="Mouse"||dragging||IsDismissed||lastHover==handle)return;
        if(System.Windows.Input.Mouse.LeftButton==System.Windows.Input.MouseButtonState.Pressed||System.Windows.Input.Mouse.RightButton==System.Windows.Input.MouseButtonState.Pressed)return;
        lastHover=handle;activateWindow(handle);
    }
    bool dragging;
    const string DragFormat="Mycelview.Edgecall.Window";
    long lastPageDrag;
    Border? movingCard;
    readonly Dictionary<FrameworkElement,System.Windows.Point> motionSlots=[];
    System.Windows.Vector grabOffset;
    IntPtr movingHandle;
    int motionIndex=-1;
    void BeginMotion(IntPtr handle,System.Windows.Point pointer)
    {
        movingHandle=handle;
        movingCard=cards.Children.OfType<Border>().FirstOrDefault(c=>c.Tag is IntPtr id&&id==handle);
        if(movingCard==null)return;
        motionSlots.Clear();
        foreach(var card in cards.Children.OfType<FrameworkElement>())motionSlots[card]=card.TranslatePoint(new System.Windows.Point(),cards);
        grabOffset=pointer-movingCard.TranslatePoint(new System.Windows.Point(),this);
        var movingSlot=slots.FindIndex(s=>s.Handle==handle);
        if(movingSlot>=0){var slot=slots[movingSlot];slots.RemoveAt(movingSlot);slots.Add(slot);Release();}
        movingCard.IsHitTestVisible=false;
        Panel.SetZIndex(movingCard,10);
        CompositionTarget.Rendering+=MotionFrame;
    }
    void MotionFrame(object? sender,EventArgs e)
    {
        if(movingCard==null||IsDismissed||!AppEdgeSwitcher.GetCursorPos(out var cursor))return;
        var pointer=PointFromScreen(new System.Windows.Point(cursor.X,cursor.Y));
        var home=movingCard.TranslatePoint(new System.Windows.Point(),this);
        var transform=movingCard.RenderTransform as TranslateTransform;
        var basePoint=new System.Windows.Point(home.X-(transform?.X??0)*contentScale,home.Y-(transform?.Y??0)*contentScale);
        movingCard.RenderTransform=new TranslateTransform((pointer.X-grabOffset.X-basePoint.X)/contentScale,(pointer.Y-grabOffset.Y-basePoint.Y)/contentScale);
        var local=TranslatePoint(pointer,cards);
        var order=motionSlots.Keys.ToList();
        int destination=order.FindIndex(c=>new System.Windows.Rect(motionSlots[c],c.RenderSize).Contains(local));
        if(destination>=0&&destination!=motionIndex){
            motionIndex=destination;
            var remaining=order.Where(c=>c!=movingCard).ToList();remaining.Insert(destination,movingCard);
            var locations=motionSlots.Values.ToArray();
            for(int i=0;i<remaining.Count;i++){
                var card=remaining[i];if(card==movingCard)continue;
                var origin=motionSlots[card];var previous=card.RenderTransform as TranslateTransform;
                var move=new TranslateTransform(previous?.X??0,previous?.Y??0);card.RenderTransform=move;
                var duration=TimeSpan.FromMilliseconds(160);
                move.BeginAnimation(TranslateTransform.XProperty,new System.Windows.Media.Animation.DoubleAnimation(locations[i].X-origin.X,duration));
                move.BeginAnimation(TranslateTransform.YProperty,new System.Windows.Media.Animation.DoubleAnimation(locations[i].Y-origin.Y,duration));
            }
        }
        UpdateThumbnails();
    }
    void EndMotion(bool commit)
    {
        CompositionTarget.Rendering-=MotionFrame;
        if(commit&&movingCard!=null&&motionIndex>=0){
            var order=motionSlots.Keys.ToList();
            var target=(IntPtr)order[motionIndex].Tag;
            int original=order.IndexOf(movingCard);
            if(MoveWindow(windows,movingHandle,target,motionIndex>original))orderChanged?.Invoke(windows);
        }
        foreach(var card in motionSlots.Keys){card.RenderTransform=Transform.Identity;card.IsHitTestVisible=true;Panel.SetZIndex(card,0);}
        motionSlots.Clear();movingCard=null;motionIndex=-1;
    }
    internal const int PageSize=20;
    internal static int PageCount(int count)=>Math.Max(1,(count+PageSize-1)/PageSize);
    long browserRevision=-1;
    int page;
    internal static bool MoveWindow(List<AppOpenWindow> items,IntPtr source,IntPtr target,bool after)
    {
        int from=items.FindIndex(w=>w.Handle==source),to=items.FindIndex(w=>w.Handle==target);
        if(from<0||to<0||from==to)return false;
        var item=items[from];items.RemoveAt(from);
        to=items.FindIndex(w=>w.Handle==target)+(after?1:0);
        items.Insert(to,item);return true;
    }
    bool closing,closed;
    internal bool IsDismissed => closing || closed;
    AppEdgeSwitcher.Point anchor;
    internal int ThumbnailCount => thumbnails.Count;
    internal WindowPicker(AppWindowZone zone,List<AppOpenWindow> windows,EdgeMonitor monitor,Action<List<AppOpenWindow>>? orderChanged=null,Action<IntPtr>? activate=null)
    {
        this.zone=zone;this.windows=windows;this.monitor=EdgeActivationService.WorkArea(monitor);this.orderChanged=orderChanged;activateWindow=activate??(handle=>{AppWindows.Activate(handle);});
        AppEdgeSwitcher.GetCursorPos(out anchor);
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;ShowActivated=false;Topmost=true;
        Width=540;SizeToContent=SizeToContent.Manual;
        Background=Brushes.White;Foreground=Brushes.Black;
        SetResourceReference(FontSizeProperty,"FontSize11");
        var root=new StackPanel{Margin=new Thickness(12)};
        var heading=new DockPanel{Margin=new Thickness(0,0,0,10)};
        var close=new Button{Content="닫기"};DockPanel.SetDock(close,Dock.Right);heading.Children.Add(close);close.Click+=(_,_)=>Dismiss();
        heading.Children.Add(new TextBlock{Text=zone.Name+" · 열린 창 "+windows.Count+"개",FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});
        root.Children.Add(heading);root.Children.Add(cards);
        cards.AllowDrop=true;
        cards.PreviewDragOver+=(_,e)=>{if(dragging&&movingCard!=null){e.Effects=DragDropEffects.Move;e.Handled=true;}};
        cards.PreviewDrop+=(_,e)=>{if(dragging&&movingCard!=null){e.Effects=DragDropEffects.Move;e.Handled=true;}};
        var navigation=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center};
        var prev=new Button{Content="◀",ToolTip="이전 창 목록"};var next=new Button{Content="▶",ToolTip="다음 창 목록"};
        prev.Click+=(_,_)=>{if(page>0){page--;Build();}};
        next.Click+=(_,_)=>{if((page+1)*PageSize<windows.Count){page++;Build();}};
        void PageDrag(Button control,int delta){
            control.AllowDrop=true;
            control.DragOver+=(_,e)=>{
                if(!dragging||!e.Data.GetDataPresent(DragFormat))return;
                e.Effects=DragDropEffects.Move;e.Handled=true;
                if(Environment.TickCount64-lastPageDrag<650)return;
                int destination=Math.Clamp(page+delta,0,Math.Max(0,(windows.Count-1)/PageSize));
                if(destination!=page){EndMotion(false);page=destination;lastPageDrag=Environment.TickCount64;Build();}
            };
        }
        PageDrag(prev,-1);PageDrag(next,1);
        navigation.Children.Add(prev);navigation.Children.Add(pageLabel);navigation.Children.Add(next);root.Children.Add(navigation);
        contentFrame=new Border{BorderBrush=Brushes.LightSkyBlue,BorderThickness=new Thickness(1),Child=root};Content=new Viewbox{Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,Child=contentFrame};
        Loaded+=(_,_)=>{Build();Place();refresh.Start();};
        refresh.Tick+=(_,_)=>{if(IsDismissed||dragging)return;var revision=BrowserTabsBridge.Current?.Revision??0;if(revision!=browserRevision){browserRevision=revision;Build();}if(windows.RemoveAll(w=>!AppWindows.Exists(w.Handle))>0){page=Math.Min(page,Math.Max(0,(windows.Count-1)/PageSize));Build();}};

        Deactivated+=(_,_)=>{if(!dragging&&zone.SwitchMode!="Mouse")Dismiss();};
        PreviewKeyDown+=(_,e)=>{if(!dragging&&e.Key==System.Windows.Input.Key.Escape)Dismiss();};
    }
    internal void Dismiss()
    {
        if(closing || closed)return;
        closing=true;
        Close();
    }
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        closing=true;
        base.OnClosing(e);
        if(e.Cancel)closing=false;
    }
    protected override void OnClosed(EventArgs e)
    {
        closed=true;EndMotion(false);refresh.Stop();Release();slots.Clear();
        base.OnClosed(e);
    }
    protected override void OnDpiChanged(DpiScale oldDpi,DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi,newDpi);
        if(IsLoaded&&!IsDismissed)Dispatcher.BeginInvoke(new Action(()=>{if(!IsDismissed)Build();}));
    }
    void Place()
    {
        if(IsDismissed)return;
        UpdateLayout();
        var hwnd=new WindowInteropHelper(this).Handle;
        var scale=PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice??Matrix.Identity;
        int width=(int)Math.Ceiling(Width*scale.M11),height=(int)Math.Ceiling(Height*scale.M22);
        int x=zone.Edge=="Right"?monitor.Right-width-12:monitor.Left+12;
        int y=zone.Edge=="Bottom"?monitor.Bottom-height-12:monitor.Top+12;
        if(zone.Edge is "Left" or "Right")y=anchor.Y-height/2;
        else x=anchor.X-width/2;
        x=Math.Clamp(x,monitor.Left,Math.Max(monitor.Left,monitor.Right-width));
        y=Math.Clamp(y,monitor.Top,Math.Max(monitor.Top,monitor.Bottom-height));
        SetWindowPos(hwnd,new IntPtr(-1),x,y,width,height,0x10);
        UpdateLayout();
        UpdateThumbnails();
    }
    readonly List<(IntPtr Handle,Border Preview)> slots=[];
    void Build()
    {
        if(IsDismissed)return;
        Release();cards.Children.Clear();slots.Clear();
        var dpi=PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice??Matrix.Identity;
        int count=Math.Min(PageSize,Math.Max(1,windows.Count-page*PageSize));
        int columns=Math.Min(5,count);cards.Columns=columns;
        int rows=(int)Math.Ceiling(count/(double)columns);
        double availableWidth=(monitor.Right-monitor.Left)/dpi.M11-32;
        double availableHeight=(monitor.Bottom-monitor.Top)/dpi.M22-40;
        double cardWidth=250;
        double previewHeight=126;
        contentFrame.Height=double.NaN;contentFrame.Width=columns*(cardWidth+4)+26;
        pageLabel.Text=$" {page+1} / {PageCount(windows.Count)} ";
        if(windows.Count==0)cards.Children.Add(new TextBlock{Text="이 앱의 열린 창이 없습니다.",Margin=new Thickness(12)});
        foreach(var item in windows.Skip(page*PageSize).Take(PageSize))
        {
            var panel=new StackPanel();
            var preview=new Border{Width=Math.Max(40,cardWidth-20),Height=previewHeight,Background=Brushes.WhiteSmoke,Child=new TextBlock{Text="미리보기를 사용할 수 없는 창\n(클릭하여 전환)",TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
            panel.Children.Add(preview);
            panel.Children.Add(new TextBlock{Text=item.Title,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(2,6,2,2)});
            var button=new Button{Content=panel,Width=cardWidth-6,Padding=new Thickness(4),Margin=new Thickness(1),Background=Brushes.Transparent,BorderBrush=Brushes.Transparent,ToolTip=item.Title};
            button.Click+=(_,_)=>{if(!dragging){Dismiss();activateWindow(item.Handle);}};
            button.MouseEnter+=(_,_)=>HoverWindow(item.Handle);
            button.MouseLeave+=(_,_)=>lastHover=IntPtr.Zero;
            System.Windows.Point? start=null;
            button.PreviewMouseLeftButtonDown+=(_,e)=>start=e.GetPosition(this);
            button.PreviewMouseLeftButtonUp+=(_,_)=>start=null;
            button.PreviewMouseMove+=(_,e)=>{
                if(start is not {} origin||e.LeftButton!=System.Windows.Input.MouseButtonState.Pressed||dragging)return;
                var point=e.GetPosition(this);
                if(Math.Abs(point.X-origin.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(point.Y-origin.Y)<SystemParameters.MinimumVerticalDragDistance)return;
                start=null;dragging=true;e.Handled=true;button.ReleaseMouseCapture();
                BeginMotion(item.Handle,origin);
                try{var effect=DragDrop.DoDragDrop(button,new DataObject(DragFormat,item.Handle),DragDropEffects.Move);EndMotion(effect==DragDropEffects.Move);}
                finally{EndMotion(false);dragging=false;if(!IsDismissed)Build();}
            };
            button.AllowDrop=true;
            var normalBorder=button.BorderBrush;
            button.DragOver+=(_,e)=>{
                if(!dragging||!e.Data.GetDataPresent(DragFormat))return;
                e.Handled=true;e.Effects=DragDropEffects.Move;
                button.BorderBrush=Brushes.DodgerBlue;
                button.BorderThickness=e.GetPosition(button).X<button.ActualWidth/2?new Thickness(3,1,1,1):new Thickness(1,1,3,1);
            };
            button.DragLeave+=(_,_)=>{button.BorderBrush=normalBorder;button.BorderThickness=new Thickness(1);};
            button.Drop+=(_,e)=>{
                if(!dragging||e.Data.GetData(DragFormat) is not IntPtr source)return;
                e.Handled=true;
                if(MoveWindow(windows,source,item.Handle,e.GetPosition(button).X>=button.ActualWidth/2))orderChanged?.Invoke(windows);
            };
            button.ToolTip=item.Title+"\n드래그하여 순서 변경 · 클릭하여 창 전환";
            var tile=new Grid();
            tile.RowDefinitions.Add(new RowDefinition{Height=new GridLength(26)});
            tile.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            Grid.SetRow(button,1);tile.Children.Add(button);
            var closeWindow=new Button{Content="×",Width=24,Height=24,Padding=new Thickness(0),Margin=new Thickness(0,5,5,0),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,ToolTip="이 창 닫기",Background=Brushes.White};
            System.Windows.Automation.AutomationProperties.SetName(closeWindow,"창 닫기 · "+item.Title);
            closeWindow.Click+=(_,e)=>{e.Handled=true;if(!AppWindows.RequestClose(item))closeWindow.ToolTip="창을 닫을 수 없습니다. 해당 앱에서 직접 닫아주세요.";};
            tile.Children.Add(new TextBlock{Text="창 미리보기",Foreground=Brushes.SlateGray,FontSize=11,Margin=new Thickness(9,5,30,0),VerticalAlignment=VerticalAlignment.Center});
            tile.Children.Add(closeWindow);
            if(BrowserTabsBridge.BrowserKind(item.AppPath)!=null){
                tile.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
                var tabList=BrowserTabList(item);Grid.SetRow(tabList,2);tile.Children.Add(tabList);
            }
            var card=new Border{Tag=item.Handle,Width=cardWidth,Margin=new Thickness(2,3,2,3),CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(1),BorderBrush=Brushes.LightSteelBlue,Background=Brushes.AliceBlue,Child=tile};
            card.MouseEnter+=(_,_)=>card.BorderBrush=Brushes.DodgerBlue;
            card.MouseLeave+=(_,_)=>card.BorderBrush=Brushes.LightSteelBlue;
            cards.Children.Add(card);slots.Add((item.Handle,preview));
        }
        contentFrame.Measure(new System.Windows.Size(contentFrame.Width,double.PositiveInfinity));
        contentScale=FitScale(contentFrame.DesiredSize.Width,contentFrame.DesiredSize.Height,availableWidth,availableHeight);
        contentFrame.Height=contentFrame.DesiredSize.Height;
        Width=Math.Ceiling(contentFrame.Width*contentScale);Height=Math.Ceiling(contentFrame.Height*contentScale);
        UpdateLayout();Place();
    }
    FrameworkElement BrowserTabList(AppOpenWindow item)
    {
        var bridge=BrowserTabsBridge.Current;
        var data=bridge?.ForWindow(item);
        if(data==null){
            bool connected=bridge?.Connected(BrowserTabsBridge.BrowserKind(item.AppPath)!)==true;
            return new TextBlock{Text=connected?"탭 연결 대기 · 이 창을 한 번 선택하세요":"탭 목록 · Mycelview 탭 연결 확장 설치 필요",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.SlateGray,Margin=new Thickness(8,4,8,8),FontSize=11};
        }
        var panel=new StackPanel{Margin=new Thickness(5,2,5,6)};
        var label=new TextBlock{Text=$"탭 {data.Tabs.Count}개 · 클릭하여 전환",Foreground=Brushes.SlateGray,Margin=new Thickness(3,1,3,4),FontSize=11};
        panel.Children.Add(label);
        var list=new ListBox{ItemsSource=data.Tabs,Height=Math.Min(104,Math.Max(26,data.Tabs.Count*26)),BorderThickness=new Thickness(0),Background=Brushes.Transparent,HorizontalContentAlignment=HorizontalAlignment.Stretch};
        ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);
        VirtualizingPanel.SetIsVirtualizing(list,true);VirtualizingPanel.SetVirtualizationMode(list,VirtualizationMode.Recycling);
        var template=new DataTemplate(typeof(BrowserTabItem));
        var button=new FrameworkElementFactory(typeof(Button));button.SetValue(Button.PaddingProperty,new Thickness(3,2,3,2));button.SetValue(Button.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch);
        button.SetBinding(Button.ToolTipProperty,new System.Windows.Data.Binding("Title"));
        button.AddHandler(Button.ClickEvent,new RoutedEventHandler(async(sender,e)=>{
            e.Handled=true;if(dragging||sender is not FrameworkElement{DataContext:BrowserTabItem tab})return;
            // Only an explicit tab click changes the browser's active tab.
            if(await bridge!.Activate(data,tab.Id)){Dismiss();activateWindow(item.Handle);}
            else if(!IsDismissed)label.Text="탭 연결이 바뀌었습니다. 다시 선택하세요.";
        }));
        var row=new FrameworkElementFactory(typeof(DockPanel));
        var icon=new FrameworkElementFactory(typeof(Image));icon.SetValue(Image.WidthProperty,16d);icon.SetValue(Image.HeightProperty,16d);icon.SetValue(Image.MarginProperty,new Thickness(0,0,5,0));
        icon.SetBinding(Image.SourceProperty,new System.Windows.Data.Binding("Icon"){TargetNullValue=ShellIcons.ForPath(item.AppPath,false)});row.AppendChild(icon);
        var title=new FrameworkElementFactory(typeof(TextBlock));title.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Title"));title.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);title.SetValue(TextBlock.FontSizeProperty,11d);
        row.AppendChild(title);button.AppendChild(row);template.VisualTree=button;list.ItemTemplate=template;
        panel.Children.Add(list);return panel;
    }
    void UpdateThumbnails()
    {
        if(IsDismissed)return;
        var dest=new WindowInteropHelper(this).Handle;
        var transform=PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice??Matrix.Identity;
        if(thumbnails.Count!=slots.Count){
            Release();
            foreach(var slot in slots){DwmRegisterThumbnail(dest,slot.Handle,out var handle);thumbnails.Add(handle);}
        }
        for(int index=0;index<slots.Count;index++){
            var (source,preview)=slots[index];var thumb=thumbnails[index];
            if(thumb==IntPtr.Zero){preview.Child.Visibility=Visibility.Visible;continue;}
            var bounds=preview.TransformToAncestor(this).TransformBounds(new System.Windows.Rect(0,0,preview.ActualWidth,preview.ActualHeight));
            var p=transform.Transform(bounds.TopLeft);
            var w=bounds.Width*transform.M11;var h=bounds.Height*transform.M22;
            if(DwmQueryThumbnailSourceSize(thumb,out var size)==0 && size.Width>0 && size.Height>0){
                var ratio=Math.Min(w/size.Width,h/size.Height);
                p.X+=(w-size.Width*ratio)/2;p.Y+=(h-size.Height*ratio)/2;
                w=size.Width*ratio;h=size.Height*ratio;
            }
            var props=new ThumbnailProperties{Flags=1|4|8|16,Destination=new Rect{Left=(int)p.X,Top=(int)p.Y,Right=(int)(p.X+w),Bottom=(int)(p.Y+h)},Opacity=255,Visible=true,ClientOnly=false};
            preview.Child.Visibility=DwmUpdateThumbnailProperties(thumb,ref props)==0?Visibility.Hidden:Visibility.Visible;
        }
    }
    void Release(){foreach(var thumb in thumbnails)DwmUnregisterThumbnail(thumb);thumbnails.Clear();}
    internal bool ContainsCursor()
    {
        return !IsDismissed && AppEdgeSwitcher.GetCursorPos(out var p)&&GetWindowRect(new WindowInteropHelper(this).Handle,out var r)&&p.X>=r.Left-16&&p.X<=r.Right+16&&p.Y>=r.Top-16&&p.Y<=r.Bottom+16;
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
