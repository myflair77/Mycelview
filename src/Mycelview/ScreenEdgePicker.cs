using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
namespace Mycelview;
internal sealed record EdgePreviewZone(string Key,string Name,EdgeActivationSettings Region);
internal static class ScreenEdgePicker
{
    internal static (string Edge,int Percent) Position(double x,double y,double width,double height,string? fixedEdge=null)
    {
        var edge=fixedEdge??new[]{("Left",x),("Right",width-x),("Top",y),("Bottom",height-y)}.OrderBy(p=>p.Item2).First().Item1;
        return(edge,Math.Clamp((int)Math.Round((edge is "Left" or "Right"?y/Math.Max(1,height):x/Math.Max(1,width))*100),0,100));
    }
    internal static Brush ZoneBrush(int index)
    {
        if(index==0)return Brushes.DodgerBlue;
        double h=(index*137.508+20)%360,s=0.68,v=0.72,c=v*s,x=c*(1-Math.Abs((h/60)%2-1)),m=v-c;
        (double r,double g,double b)=h switch{<60=>(c,x,0d),<120=>(x,c,0d),<180=>(0d,c,x),<240=>(0d,x,c),<300=>(x,0d,c),_=>(c,0d,x)};
        var brush=new SolidColorBrush(Color.FromRgb((byte)((r+m)*255),(byte)((g+m)*255),(byte)((b+m)*255)));brush.Freeze();return brush;
    }
    internal static (int Start,int End) ResizeRange(int start,int end,int value,int endpoint,int anchor=0)
    {
        value=Math.Clamp(value,0,100);
        if(endpoint<0)return(Math.Clamp(value,0,end-1),end);
        if(endpoint>0)return(start,Math.Clamp(value,start+1,100));
        int a=Math.Min(99,Math.Min(anchor,value));return(a,Math.Max(a+1,Math.Max(anchor,value)));
    }
    internal static double DistanceToSegment(Point p,Point a,Point b)
    {
        var ab=b-a;double t=ab.LengthSquared==0?0:Math.Clamp(Vector.Multiply(p-a,ab)/ab.LengthSquared,0,1);
        return(p-(a+ab*t)).Length;
    }
    internal static (int Start,int End) MoveRange(int start,int end,int delta,IEnumerable<(int Start,int End)> obstacles)
    {
        int length=end-start,min=0,max=100-length;
        foreach(var obstacle in obstacles){
            if(obstacle.End<=start)min=Math.Max(min,obstacle.End);
            else if(obstacle.Start>=end)max=Math.Min(max,obstacle.Start-length);
            else return(start,end);
        }
        int moved=Math.Clamp(start+delta,min,Math.Max(min,max));return(moved,moved+length);
    }
    internal static List<Brush> RegionColors(List<EdgePreviewZone> zones,List<EdgeMonitor> monitors)
    {
        Brush[] palette=[Brushes.DodgerBlue,Brushes.Gold,Brushes.Crimson,Brushes.ForestGreen];
        var result=Enumerable.Repeat<Brush>(Brushes.DodgerBlue,zones.Count).ToList();
        var ordered=Enumerable.Range(0,zones.Count).GroupBy(i=>{
            var r=zones[i].Region;return (EdgeActivationService.SelectMonitor(monitors,r.Monitor,r.Edge)?.Device??r.Monitor)+"|"+r.Edge;
        });
        var sequence=ordered.SelectMany(group=>group.OrderBy(i=>zones[i].Region.StartPercent).ThenBy(i=>zones[i].Region.EndPercent).ThenBy(i=>zones[i].Key)).ToList();
        var neighbors=Enumerable.Range(0,zones.Count).Select(_=>new HashSet<int>()).ToArray();
        foreach(var group in ordered){
            var indices=group.OrderBy(i=>zones[i].Region.StartPercent).ThenBy(i=>zones[i].Region.EndPercent).ThenBy(i=>zones[i].Key).ToArray();
            for(int n=0;n<indices.Length;n++){
                result[indices[n]]=palette[n%4];
                if(n>0){neighbors[indices[n]].Add(indices[n-1]);neighbors[indices[n-1]].Add(indices[n]);}
            }
        }
        Rect? Segment(int index){
            var r=zones[index].Region;
            var m=EdgeActivationService.SelectMonitor(monitors,r.Monitor,r.Edge);
            if(m==null)return null;
            bool vertical=r.Edge is "Left" or "Right";
            double start=(vertical?m.Bottom-m.Top:m.Right-m.Left)*r.StartPercent/100d;
            double end=(vertical?m.Bottom-m.Top:m.Right-m.Left)*r.EndPercent/100d;
            return vertical?new Rect(r.Edge=="Left"?m.Left:m.Right,m.Top+start,0,end-start)
                :new Rect(m.Left+start,r.Edge=="Top"?m.Top:m.Bottom,end-start,0);
        }
        var segments=Enumerable.Range(0,zones.Count).Select(Segment).ToArray();
        for(int i=0;i<zones.Count;i++)for(int j=0;j<i;j++){
            if(segments[i] is Rect a&&segments[j] is Rect b&&a.IntersectsWith(b)){
                neighbors[i].Add(j);neighbors[j].Add(i);
            }
        }
        var assigned=new HashSet<int>();
        foreach(int index in sequence){
            var used=neighbors[index].Where(assigned.Contains).Select(i=>result[i].ToString()).ToHashSet();
            if(used.Contains(result[index].ToString()))
                result[index]=palette.FirstOrDefault(p=>!used.Contains(p.ToString()))
                    ?? Enumerable.Range(1,zones.Count+1).Select(ZoneBrush).First(p=>!used.Contains(p.ToString()));
            assigned.Add(index);
        }
        return result;
    }    internal static List<EdgePreviewZone> Zones(EdgeActivationSettings basic,IEnumerable<AppWindowZone> apps) => new[]{new EdgePreviewZone("Mycelview","Mycelview",basic)}.Concat(apps.Select(z=>new EdgePreviewZone(z.Id.ToString(),z.Name,z.Region(basic)))).ToList();
    internal static FrameworkElement Create(Func<EdgeActivationSettings> current,Action<string,string,int,int> apply,Func<List<EdgePreviewZone>>? allZones=null,string currentKey="Mycelview",Action<string>? selectZone=null,Action<string,string,string,int,int>? applyZone=null)
    {
        var root=new StackPanel{Margin=new Thickness(0,10,0,0)};
        var heading=new DockPanel();
        var refresh=new Button{Content="↻",ToolTip="현재 모니터 배치를 다시 읽습니다.",Padding=new Thickness(5)};
        DockPanel.SetDock(refresh,Dock.Right);heading.Children.Add(refresh);
        heading.Children.Add(new TextBlock{Text="화면 배치에서 구간 선택",VerticalAlignment=VerticalAlignment.Center});
        root.Children.Add(heading);
        var canvas=new Canvas{Height=180,Background=Brushes.Transparent,ClipToBounds=true,ToolTip="기존 구간이나 이름을 클릭해 편집 대상을 선택하세요. 가운데를 드래그하면 길이를 유지해 이동하고, 양 끝 점을 드래그하면 범위가 바뀝니다. 빈 가장자리를 드래그하면 새 범위를 지정합니다. 색상과 번호는 아래 이름표에 대응합니다. 굵은 선과 양 끝 점은 현재 편집 중인 구간입니다. 저장을 눌러야 적용됩니다."};
        var border=new Border{Child=canvas,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(6),Margin=new Thickness(0,5,0,0)};
        border.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");root.Children.Add(border);
        var legend=new StackPanel{Margin=new Thickness(0,5,0,0)};root.Children.Add(legend);
        var monitors=EdgeActivationService.GetMonitors();
        string dragKey=currentKey;
        var regions=new List<(EdgeMonitor Monitor,Rect Rect)>();
        var segments=new List<(EdgePreviewZone Zone,EdgeMonitor Monitor,Rect Rect,Point Start,Point End)>();string previous="";
        void Draw()
        {
            canvas.Children.Clear();regions.Clear();segments.Clear();legend.Children.Clear();
            if(monitors.Count==0||canvas.ActualWidth<1)return;
            var settings=current();var zones=allZones?.Invoke()??[new EdgePreviewZone(currentKey,"선택 구간",settings)];var colors=RegionColors(zones,monitors);int left=monitors.Min(m=>m.Left),top=monitors.Min(m=>m.Top);
            double w=monitors.Max(m=>m.Right)-left,h=monitors.Max(m=>m.Bottom)-top;
            double scale=Math.Min(Math.Max(1,canvas.ActualWidth-32)/Math.Max(1,w),148/Math.Max(1,h));
            double ox=(canvas.ActualWidth-w*scale)/2,oy=(180-h*scale)/2;
            var selected=EdgeActivationService.SelectMonitor(monitors,settings.Monitor,settings.Edge);int index=0;
            foreach(var monitor in monitors){
                var rect=new Rect(ox+(monitor.Left-left)*scale,oy+(monitor.Top-top)*scale,(monitor.Right-monitor.Left)*scale,(monitor.Bottom-monitor.Top)*scale);
                regions.Add((monitor,rect));index++;
                var screen=new Border{Width=rect.Width,Height=rect.Height,Background=new SolidColorBrush(Color.FromRgb(235,240,246)),BorderBrush=Brushes.SlateGray,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(3),Child=new TextBlock{Text=$"{index}"+(monitor.Primary?" · 주 화면":""),Foreground=Brushes.DarkSlateGray,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center},IsHitTestVisible=false};
                Canvas.SetLeft(screen,rect.Left);Canvas.SetTop(screen,rect.Top);canvas.Children.Add(screen);
                var occupied=new List<(string Edge,int Start,int End,int Lane)>();
                for(int zi=0;zi<zones.Count;zi++){
                    var zone=zones[zi];var r=zone.Region;
                    if(EdgeActivationService.SelectMonitor(monitors,r.Monitor,r.Edge)?.Device!=monitor.Device)continue;
                    int lane=0;
                    while(occupied.Any(o=>o.Edge==r.Edge&&o.Lane==lane&&o.Start<r.EndPercent&&r.StartPercent<o.End))lane++;
                    occupied.Add((r.Edge,r.StartPercent,r.EndPercent,lane));
                    bool active=zone.Key==currentKey,vertical=r.Edge is "Left" or "Right";
                    var brush=colors[zi];
                    double inset=lane*7+2;
                    double a=(vertical?rect.Height:rect.Width)*r.StartPercent/100,b=(vertical?rect.Height:rect.Width)*r.EndPercent/100;
                    double x=r.Edge=="Right"?rect.Right-inset:rect.Left+inset,y=r.Edge=="Bottom"?rect.Bottom-inset:rect.Top+inset;
                    var p1=vertical?new Point(x,rect.Top+a):new Point(rect.Left+a,y);var p2=vertical?new Point(x,rect.Top+b):new Point(rect.Left+b,y);
                    segments.Add((zone,monitor,rect,p1,p2));
                    canvas.Children.Add(new Line{X1=p1.X,Y1=p1.Y,X2=p2.X,Y2=p2.Y,Stroke=brush,StrokeThickness=active?6:4,Opacity=active?1:0.8,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,IsHitTestVisible=false});
                    if(active)foreach(var p in new[]{p1,p2}){var dot=new Ellipse{Width=9,Height=9,Fill=Brushes.White,Stroke=brush,StrokeThickness=2,IsHitTestVisible=false};Canvas.SetLeft(dot,p.X-4.5);Canvas.SetTop(dot,p.Y-4.5);canvas.Children.Add(dot);}
                    var badge=new Border{Background=brush,CornerRadius=new CornerRadius(3),Padding=new Thickness(3,0,3,0),Child=new TextBlock{Text=(zi+1).ToString(),Foreground=brush==Brushes.Gold?Brushes.Black:Brushes.White,FontSize=10},IsHitTestVisible=false};
                    Canvas.SetLeft(badge,Math.Clamp((p1.X+p2.X)/2+5,0,Math.Max(0,canvas.ActualWidth-20)));Canvas.SetTop(badge,Math.Clamp((p1.Y+p2.Y)/2-7,0,164));canvas.Children.Add(badge);
                }
            }
            for(int i=0;i<zones.Count;i++){
                var z=zones[i];var monitor=EdgeActivationService.SelectMonitor(monitors,z.Region.Monitor,z.Region.Edge);
                string direction=z.Region.Edge switch{"Right"=>"오른쪽","Top"=>"위쪽","Bottom"=>"아래쪽",_=>"왼쪽"};
                var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,2,0,2)};
                row.Children.Add(new Border{Width=18,Height=16,Background=colors[i],CornerRadius=new CornerRadius(3),Margin=new Thickness(0,0,6,0),Child=new TextBlock{Text=(i+1).ToString(),Foreground=colors[i]==Brushes.Gold?Brushes.Black:Brushes.White,TextAlignment=TextAlignment.Center}});
                var text=new TextBlock{Text=z.Name+(z.Key==currentKey?" · 편집 중":"")+$"  ·  {(monitor==null?"연결 안 됨":$"화면 {monitors.IndexOf(monitor)+1}")} {direction} {z.Region.StartPercent}~{z.Region.EndPercent}%",TextWrapping=TextWrapping.Wrap,MaxWidth=380,FontWeight=z.Key==currentKey?FontWeights.SemiBold:FontWeights.Normal};
                text.SetResourceReference(TextBlock.ForegroundProperty,"TextBrush");row.Children.Add(text);row.Cursor=Cursors.Hand;row.Background=Brushes.Transparent;row.MouseLeftButtonDown+=(_,e)=>{selectZone?.Invoke(z.Key);e.Handled=true;};legend.Children.Add(row);
            }
        }
        EdgeMonitor? dragging=null;Rect dragRect=default;string dragEdge="Left";int anchor=0;int endpoint=0;int originalStart=0,originalEnd=100;
        void Apply(Point p){
            if(dragging==null)return;
            int value=Position(p.X-dragRect.X,p.Y-dragRect.Y,dragRect.Width,dragRect.Height,dragEdge).Percent;
            var others=(allZones?.Invoke()??[]).Where(z=>z.Key!=dragKey && z.Key!="Mycelview" && z.Region.Edge==dragEdge && EdgeActivationService.SelectMonitor(monitors,z.Region.Monitor,z.Region.Edge)?.Device==dragging.Device).Select(z=>(Start:z.Region.StartPercent,End:z.Region.EndPercent)).ToList();
            var range=endpoint==2?MoveRange(originalStart,originalEnd,value-anchor,others):ResizeRange(originalStart,originalEnd,value,endpoint,anchor);
            if(others.Any(o=>range.Start<o.End&&o.Start<range.End))return;
            if(applyZone!=null)applyZone(dragKey,dragging.Device,dragEdge,range.Start,range.End);
            else apply(dragging.Device,dragEdge,range.Start,range.End);
            Draw();
        }
        canvas.MouseLeftButtonDown+=(_,e)=>{
            var p=e.GetPosition(canvas);
            var active=segments.Where(s=>s.Zone.Key==currentKey).FirstOrDefault(s=>(p-s.Start).Length<=9||(p-s.End).Length<=9);
            if(active.Zone!=null){
                dragging=active.Monitor;dragRect=active.Rect;dragEdge=active.Zone.Region.Edge;
                dragKey=active.Zone.Key;originalStart=active.Zone.Region.StartPercent;originalEnd=active.Zone.Region.EndPercent;
                endpoint=(p-active.Start).Length<=(p-active.End).Length?-1:1;
                canvas.CaptureMouse();e.Handled=true;return;
            }
            var existing=segments.OrderBy(s=>DistanceToSegment(p,s.Start,s.End)).FirstOrDefault();
            if(existing.Zone!=null && DistanceToSegment(p,existing.Start,existing.End)<=7){
                dragKey=existing.Zone.Key;dragging=existing.Monitor;dragRect=existing.Rect;dragEdge=existing.Zone.Region.Edge;
                originalStart=existing.Zone.Region.StartPercent;originalEnd=existing.Zone.Region.EndPercent;
                anchor=Position(p.X-dragRect.X,p.Y-dragRect.Y,dragRect.Width,dragRect.Height,dragEdge).Percent;endpoint=2;
                canvas.CaptureMouse();canvas.Cursor=Cursors.SizeAll;e.Handled=true;return;
            }
            endpoint=0;dragKey=currentKey;
            var hit=regions.LastOrDefault(r=>r.Rect.Contains(p));if(hit.Monitor==null)return;
            dragging=hit.Monitor;dragRect=hit.Rect;var position=Position(p.X-dragRect.X,p.Y-dragRect.Y,dragRect.Width,dragRect.Height);
            dragEdge=position.Edge;anchor=position.Percent;canvas.CaptureMouse();Apply(p);e.Handled=true;
        };
        canvas.MouseMove+=(_,e)=>{if(dragging!=null)Apply(e.GetPosition(canvas));};
        canvas.MouseLeftButtonUp+=(_,_)=>{bool select=dragging!=null;dragging=null;canvas.ReleaseMouseCapture();canvas.Cursor=Cursors.Arrow;if(select)selectZone?.Invoke(dragKey);};
        canvas.LostMouseCapture+=(_,_)=>{dragging=null;canvas.Cursor=Cursors.Arrow;};
        refresh.Click+=(_,_)=>{monitors=EdgeActivationService.GetMonitors();Draw();};
        canvas.SizeChanged+=(_,_)=>Draw();
        var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(150)};
        timer.Tick+=(_,_)=>{var s=current();string key=string.Join("|",(allZones?.Invoke()??[new EdgePreviewZone(currentKey,"선택 구간",s)]).Select(z=>$"{z.Key}:{z.Name}:{z.Region.Monitor}:{z.Region.Edge}:{z.Region.StartPercent}:{z.Region.EndPercent}"));if(key!=previous){previous=key;Draw();}};
        root.Loaded+=(_,_)=>{Draw();timer.Start();};root.Unloaded+=(_,_)=>timer.Stop();
        return root;
    }
}
