using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
namespace Mycelview;
public partial class MainWindow
{
    internal static bool ValidateAppZones(List<AppWindowZone> zones,out string error)
    {
        error="";
        foreach(var z in zones){
            if(string.IsNullOrWhiteSpace(z.Name)||!File.Exists(z.AppPath)||!Path.GetExtension(z.AppPath).Equals(".exe",StringComparison.OrdinalIgnoreCase)){error="앱 구간의 실행 파일(.exe)을 선택하세요.";return false;}
            if(z.StartPercent<0||z.EndPercent>100||z.StartPercent>=z.EndPercent){error="앱 구간의 시작 위치는 끝 위치보다 앞이어야 합니다.";return false;}
        }
        var monitors=EdgeActivationService.GetMonitors();
        for(int i=0;i<zones.Count;i++)for(int j=i+1;j<zones.Count;j++){
            var a=zones[i];var b=zones[j];
            var am=EdgeActivationService.SelectMonitor(monitors,a.Monitor,a.Edge)?.Device??a.Monitor;
            var bm=EdgeActivationService.SelectMonitor(monitors,b.Monitor,b.Edge)?.Device??b.Monitor;
            if(a.Edge==b.Edge&&am==bm&&a.StartPercent<b.EndPercent&&b.StartPercent<a.EndPercent){error="같은 화면 가장자리에서 앱 구간이 겹칩니다. 슬라이더로 구간을 나눠주세요.";return false;}
        }
        return true;
    }
    private List<AppWindowZone> AddAppZoneEditor(StackPanel parent,EdgeActivationSettings saved,Func<EdgeActivationSettings>? basicRegion,Action<string> navigate,out Action<string> selectApp,out Action<string,string,string,int,int> updateApp,Action<string,string,string,int,int> updateZone)
    {
        var zones=(saved.AppZones??[]).Select(x=>x.Copy()).ToList();
        var updates=new Dictionary<string,Action<string,string,int,int>>();
        updateApp=(key,device,edge,a,b)=>{if(updates.TryGetValue(key,out var update))update(device,edge,a,b);};


        var selector=new ComboBox{DisplayMemberPath="Name",Margin=new Thickness(0,0,0,8),ToolTip="등록한 앱을 선택해 반응 구간을 수정합니다."};
        parent.Children.Add(selector);
        selectApp=key=>selector.SelectedItem=zones.FirstOrDefault(z=>z.Id.ToString()==key);
        var editors=new Dictionary<AppWindowZone,Border>();
        void RefreshSelection(AppWindowZone? selected){
            selector.ItemsSource=null;selector.ItemsSource=zones;selector.SelectedItem=selected??zones.FirstOrDefault();
        }
        selector.SelectionChanged+=(_,_)=>{
            foreach(var pair in editors)pair.Value.Visibility=ReferenceEquals(pair.Key,selector.SelectedItem)?Visibility.Visible:Visibility.Collapsed;
        };
        var rows=new StackPanel();parent.Children.Add(rows);
        var add=new Button{Content="＋ 앱 추가",HorizontalAlignment=HorizontalAlignment.Left,ToolTip="현재 열린 앱이나 즐겨찾기에 등록한 앱을 선택합니다. 앱별 구간은 기본 Mycelview 호출 구간보다 우선합니다."};parent.Children.Add(add);
        var apps=AppWindows.List().DistinctBy(x=>x.AppPath,StringComparer.OrdinalIgnoreCase)
            .Select(x=>new BrowserChoice(x.AppName,x.AppPath)).ToList();
        foreach(var browser in LauncherService.Browsers().Where(x=>x.Path.Length>0))
            if(!apps.Any(x=>StringComparer.OrdinalIgnoreCase.Equals(x.Path,browser.Path)))apps.Add(browser);
        foreach(var favorite in ViewModel.LauncherFavorites.Where(x=>x.Kind!="Website"&&Path.GetExtension(x.Target).Equals(".exe",StringComparison.OrdinalIgnoreCase)))
            if(!apps.Any(x=>StringComparer.OrdinalIgnoreCase.Equals(x.Path,favorite.Target)))apps.Add(new(favorite.Name,favorite.Target));
        void Row(AppWindowZone z)
        {
            var panel=new StackPanel{Margin=new Thickness(0,8,0,8)};
            var border=new Border{Child=panel,BorderThickness=new Thickness(1),Padding=new Thickness(8),Margin=new Thickness(0,0,0,8)};
            border.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");rows.Children.Add(border);editors[z]=border;border.Visibility=Visibility.Collapsed;
            var top=new DockPanel();panel.Children.Add(top);
            var remove=new Button{Content="제거"};DockPanel.SetDock(remove,Dock.Right);top.Children.Add(remove);
            remove.Click+=(_,_)=>{zones.Remove(z);rows.Children.Remove(border);editors.Remove(z);RefreshSelection(null);};
            var choices=apps.ToList();
            if(z.AppPath.Length>0&&!choices.Any(x=>StringComparer.OrdinalIgnoreCase.Equals(x.Path,z.AppPath)))choices.Add(new(z.Name,z.AppPath));
            var app=new ComboBox{ItemsSource=choices,DisplayMemberPath="Name",SelectedItem=choices.FirstOrDefault(x=>StringComparer.OrdinalIgnoreCase.Equals(x.Path,z.AppPath)),ToolTip="현재 열린 앱 또는 설치된 브라우저 선택"};
            top.Children.Add(app);
            app.SelectionChanged+=(_,_)=>{if(app.SelectedItem is BrowserChoice choice){z.Name=choice.Name;z.AppPath=choice.Path;selector.Items.Refresh();}};
            var browse=new Button{Content="파일에서 선택…",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,5,0,5)};
            panel.Children.Add(browse);browse.Click+=(_,_)=>{
                var pick=new OpenFileDialog{Filter="앱 실행 파일|*.exe",CheckFileExists=true};
                if(pick.ShowDialog(Window.GetWindow(parent))==true){var choice=new BrowserChoice(Path.GetFileNameWithoutExtension(pick.FileName),pick.FileName);choices.Add(choice);app.ItemsSource=null;app.ItemsSource=choices;app.SelectedItem=choice;}
            };
            string[] edges=["Left","Right","Top","Bottom"];
            var edge=new ComboBox{ItemsSource=new[]{"왼쪽 경계","오른쪽 경계","위쪽 경계","아래쪽 경계"},SelectedIndex=Math.Max(0,Array.IndexOf(edges,z.Edge))};panel.Children.Add(edge);
            var monitor=new ComboBox{DisplayMemberPath="Value",SelectedValuePath="Key",Margin=new Thickness(0,5,0,5)};panel.Children.Add(monitor);
            void Monitors(){
                var entries=new List<KeyValuePair<string,string>>{new("Automatic","가장 "+new[]{"왼쪽","오른쪽","위쪽","아래쪽"}[edge.SelectedIndex]+" 모니터 (자동)")};
                entries.AddRange(EdgeActivationService.GetMonitors().Select(m=>new KeyValuePair<string,string>(m.Device,m.Label)));
                if(!entries.Any(x=>x.Key==z.Monitor))entries.Add(new(z.Monitor,z.Monitor+" (연결되지 않음)"));
                monitor.ItemsSource=entries;monitor.SelectedValue=z.Monitor;
            }
            edge.SelectionChanged+=(_,_)=>{z.Edge=edges[edge.SelectedIndex];Monitors();};Monitors();
            monitor.SelectionChanged+=(_,_)=>{if(monitor.SelectedValue is string value)z.Monitor=value;};
            var hint=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,2)};panel.Children.Add(hint);
            var start=new Slider{Minimum=0,Maximum=99,Value=z.StartPercent,TickFrequency=1,IsSnapToTickEnabled=true,IsMoveToPointEnabled=true,ToolTip="구간 시작"};
            var end=new Slider{Minimum=1,Maximum=100,Value=z.EndPercent,TickFrequency=1,IsSnapToTickEnabled=true,IsMoveToPointEnabled=true,ToolTip="구간 끝"};
            panel.Children.Add(start);panel.Children.Add(end);
            bool updating=false;
            void Range(bool fromStart){
                if(updating)return;updating=true;
                if(start.Value>=end.Value){if(fromStart)end.Value=start.Value+1;else start.Value=end.Value-1;}
                z.StartPercent=(int)start.Value;z.EndPercent=(int)end.Value;
                hint.Text=$"반응 구간  {z.StartPercent}% ~ {z.EndPercent}%";
                start.ToolTip=end.ToolTip=z.Edge is "Left" or "Right"?"화면 위에서 아래 방향으로 시작·끝 위치를 조절합니다.":"화면 왼쪽에서 오른쪽 방향으로 시작·끝 위치를 조절합니다.";
                updating=false;
            }
            start.ValueChanged+=(_,_)=>Range(true);end.ValueChanged+=(_,_)=>Range(false);edge.SelectionChanged+=(_,_)=>Range(true);Range(true);
            Action<string,string,int,int> change=(device,side,a,b)=>{
                z.Monitor=device;z.Edge=side;edge.SelectedIndex=Array.IndexOf(edges,side);Monitors();monitor.SelectedValue=device;
                updating=true;start.Value=a;end.Value=b;updating=false;Range(true);
            };
            updates[z.Id.ToString()]=change;
            panel.Children.Add(ScreenEdgePicker.Create(()=>z.Region(saved),change,()=>ScreenEdgePicker.Zones(basicRegion?.Invoke()??saved,zones),z.Id.ToString(),navigate,updateZone));
        }
        foreach(var z in zones)Row(z);
        add.Click+=(_,_)=>{var z=new AppWindowZone{Name="새 앱"};zones.Add(z);Row(z);RefreshSelection(z);};
        RefreshSelection(zones.LastOrDefault());
        return zones;
    }
}
