using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
namespace Mycelview;
internal static class AppZoneTests
{
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint="SetWindowLongPtrW")] static extern IntPtr SetOwner(IntPtr hwnd,int index,IntPtr value);
    internal static void Run(string output)
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);}
        EdgeActivationTests.Run();
        Check(WindowPicker.FitScale(1000,800,500,600)==0.5&&WindowPicker.FitScale(1000,800,1200,400)==0.5&&WindowPicker.FitScale(1000,800,2000,1600)==1,"Preview scales uniformly to either dimension without enlarging");
        Check(WindowPicker.PageSize==20&&WindowPicker.PageCount(0)==1&&WindowPicker.PageCount(20)==1&&WindowPicker.PageCount(21)==2&&WindowPicker.PageCount(40)==2&&WindowPicker.PageCount(41)==3,"Twenty previews per page at boundaries");
        Check(CardViewModel.FolderHeading(@"C:\Work",@"C:\Work\Child")=="> Child","Folder heading descendant");
        Check(CardViewModel.FolderHeading(@"C:\Work\Child",@"C:\Work")=="< Work","Folder heading ancestor");
        Check(CardViewModel.FolderHeading(@"C:\Work",@"C:\Workshop")=="— Workshop","Folder heading respects directory boundaries");
        Check(CardViewModel.FolderHeading(@"C:\Work",@"c:\work\")=="","Folder heading omits duplicate current folder");
        Check(CardViewModel.FolderHeading(@"C:\Work",@"C:\")==@"< C:\","Folder heading handles drive root");
        var reordered=Enumerable.Range(1,6).Select(i=>new AppOpenWindow(new IntPtr(i),"Window "+i,"fixture.exe")).ToList();
        Check(WindowPicker.MoveWindow(reordered,new IntPtr(1),new IntPtr(3),true),"Drag moves item after target");
        Check(reordered.Select(w=>w.Handle.ToInt64()).SequenceEqual(new long[]{2,3,1,4,5,6}),"Forward reorder");
        Check(WindowPicker.MoveWindow(reordered,new IntPtr(6),new IntPtr(2),false),"Drag across pages");
        Check(reordered.Select(w=>w.Handle.ToInt64()).SequenceEqual(new long[]{6,2,3,1,4,5}),"Backward reorder");
        Check(!WindowPicker.MoveWindow(reordered,new IntPtr(6),new IntPtr(6),false),"Self drop ignored");
        Check(!WindowPicker.MoveWindow(reordered,new IntPtr(999),new IntPtr(6),false)&&reordered.Count==6,"Stale drag ignored");
        Check(ScreenEdgePicker.Position(0,540,1920,1080)==("Left",50),"Physical left edge midpoint");
        Check(ScreenEdgePicker.Position(1919,270,1920,1080)==("Right",25),"Physical right edge quarter");
        Check(ScreenEdgePicker.Position(960,0,1920,1080)==("Top",50),"Horizontal midpoint");
        Check(ScreenEdgePicker.Position(4000,-50,1920,1080,"Left")==("Left",0),"Clamp outside drag");
        Check(ScreenEdgePicker.Position(0,270,960,540)==("Left",50),"DPI-independent ratios");
        var exe=Environment.ProcessPath!;
        var messengerLauncher=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"AtMessenger7","Launcher.exe");
        if(File.Exists(messengerLauncher)){
            Check(AppWindows.WindowExecutable(messengerLauncher)!.EndsWith("AtMessengerMobileEdition.exe"),"Messenger launcher resolves to window process");
            var messengerWindows=AppWindows.List(AppWindows.WindowExecutable(messengerLauncher));
            File.WriteAllText(Path.Combine(output,"messenger-windows.txt"),"Detected windows: "+messengerWindows.Count);
        }
        Check(AppWindows.WindowExecutable(exe)==exe,"Ordinary apps retain exact process matching");
        var launcher=Path.Combine(output,"launcher.exe");
        var runtime=Path.Combine(output,"runtime.exe");
        var helper=Path.Combine(output,"helper.exe");
        var samples=new List<AppOpenWindow>{new(new IntPtr(10),"Runtime",runtime)};
        Check(AppWindows.MatchWindowExecutable(launcher,samples)==runtime,"Generic launcher matches unique sibling window process");
        samples.Add(new(new IntPtr(11),"Helper",helper));
        Check(AppWindows.MatchWindowExecutable(launcher,samples)==launcher,"Ambiguous runtime requires explicit window selection");
        Check(AppWindows.MatchWindowExecutable(runtime,samples)==runtime,"Exact process wins");
        var linked=new AppWindowZone{Name="Linked",AppPath=exe,WindowAppPath=runtime};
        var roundtrip=JsonSerializer.Deserialize<AppWindowZone>(JsonSerializer.Serialize(linked))!;
        Check(roundtrip.WindowAppPath==runtime&&linked.Copy().WindowAppPath==runtime,"Window connection persists and clones");
        Check(JsonSerializer.Deserialize<AppWindowZone>("{}")!.WindowAppPath=="","Legacy window connection defaults to automatic");
        Check(new EdgeActivationSettings().SwitchMode=="Mouse"&&new AppWindowZone().SwitchMode=="Mouse","All new regions default to hover switching");
        var modeState=new EdgeActivationSettings{SwitchMode="Click",AppZones=[new(){AppPath=exe,SwitchMode="Mouse"}]};
        var modes=JsonSerializer.Deserialize<EdgeActivationSettings>(JsonSerializer.Serialize(modeState))!.Normalize();
        Check(modes.SwitchMode=="Click"&&modes.AppZones[0].SwitchMode=="Mouse","Per-region switch modes persist");
        var hoverCalls=new List<IntPtr>();
        var modeMonitor=EdgeActivationService.GetMonitors()[0];
        var hoverPicker=new WindowPicker(new(){SwitchMode="Mouse"},[],modeMonitor,null,h=>hoverCalls.Add(h));
        hoverPicker.HoverWindow(new IntPtr(10));hoverPicker.HoverWindow(new IntPtr(10));hoverPicker.HoverWindow(new IntPtr(11));
        Check(hoverCalls.SequenceEqual(new[]{new IntPtr(10),new IntPtr(11)}),"Hover switches each newly targeted window once without clicking");
        hoverPicker.Dismiss();hoverPicker.HoverWindow(new IntPtr(12));
        Check(hoverCalls.Count==2,"Dismissed preview never activates");
        var clickPicker=new WindowPicker(new(){SwitchMode="Click"},[],modeMonitor,null,h=>hoverCalls.Add(h));
        clickPicker.HoverWindow(new IntPtr(12));clickPicker.Dismiss();
        Check(hoverCalls.Count==2,"Click mode ignores hover");
        var a=new AppWindowZone{Name="Test",AppPath=exe,StartPercent=0,EndPercent=25};
        var b=new AppWindowZone{Name="Test2",AppPath=exe,StartPercent=25,EndPercent=50};
        Check(MainWindow.ValidateAppZones([a,b],out _),"Adjacent zones allowed");
        b.StartPercent=20;Check(!MainWindow.ValidateAppZones([a,b],out _),"Overlap rejected");b.StartPercent=25;
        var settings=new EdgeActivationSettings{AppZones=[a,b]};
        var previewZones=ScreenEdgePicker.Zones(settings,settings.AppZones);
        Check(previewZones.Count==3&&previewZones[0].Name=="Mycelview"&&previewZones[2].Name=="Test2","Preview includes Mycelview and every app");
        Check(previewZones.Select(z=>z.Key).Distinct().Count()==3,"Distinct region identifiers");
        Check(Enumerable.Range(0,10).Select(i=>ScreenEdgePicker.ZoneBrush(i).ToString()).Distinct().Count()==10,"Distinct region colors");
        var map=(System.Windows.Controls.StackPanel)ScreenEdgePicker.Create(()=>settings,(_,_,_,_)=>{},()=>previewZones);
        map.Measure(new Size(480,500));map.Arrange(new Rect(0,0,480,500));map.UpdateLayout();
        var mapCanvas=(System.Windows.Controls.Canvas)((System.Windows.Controls.Border)map.Children[1]).Child;
        var legend=(System.Windows.Controls.StackPanel)map.Children[2];
        Check(mapCanvas.Children.OfType<System.Windows.Shapes.Line>().Count()==3,"All assigned ranges drawn");
        Check(legend.Children.Count==3,"Each range has a name legend");
        var legendText=legend.Children.OfType<System.Windows.Controls.StackPanel>().SelectMany(p=>p.Children.OfType<System.Windows.Controls.TextBlock>()).Select(t=>t.Text).ToList();
        Check(legendText.Any(t=>t.Contains("Mycelview")&&t.Contains("편집 중"))&&legendText.Any(t=>t.Contains("Test2")),"Current region and app names visible");
        Check(ScreenEdgePicker.ResizeRange(20,60,35,-1)==(35,60),"Left endpoint keeps end fixed");
        Check(ScreenEdgePicker.ResizeRange(20,60,80,1)==(20,80),"Right endpoint keeps start fixed");
        Check(ScreenEdgePicker.ResizeRange(20,60,90,-1)==(59,60),"Endpoint cannot cross opposite end");
        Check(ScreenEdgePicker.ResizeRange(20,60,10,1)==(20,21),"Minimum width preserved");
        Check(ScreenEdgePicker.DistanceToSegment(new Point(5,50),new Point(0,0),new Point(0,100))==5,"Segment click hit test");
        Check(ScreenEdgePicker.MoveRange(30,50,10,[])==(40,60),"Body drag preserves length");
        Check(ScreenEdgePicker.MoveRange(30,50,80,[])==(80,100),"Body stops at screen end");
        Check(ScreenEdgePicker.MoveRange(30,50,-80,[])==(0,20),"Body stops at screen start");
        Check(ScreenEdgePicker.MoveRange(30,50,80,[(60,80)])==(40,60),"Body cannot cross right neighbor");
        Check(ScreenEdgePicker.MoveRange(30,50,-80,[(0,20)])==(20,40),"Body cannot cross left neighbor");
        Check(ScreenEdgePicker.MoveRange(30,50,0,[(0,20),(60,80)])==(30,50),"Click without motion preserves range");
        var cornerMonitor=new EdgeMonitor("corner",-1920,-1080,0,0,true);
        foreach(var corner in new[]{("Left",0,"Top",0),("Right",0,"Top",75),("Left",75,"Bottom",0),("Right",75,"Bottom",75)}){
            var cornerZones=new List<EdgePreviewZone>{
                new("a","a",new(){Edge=corner.Item1,StartPercent=corner.Item2,EndPercent=corner.Item2+25}),
                new("b","b",new(){Edge=corner.Item3,StartPercent=corner.Item4,EndPercent=corner.Item4+25})};
            var cornerColors=ScreenEdgePicker.RegionColors(cornerZones,[cornerMonitor]);
            Check(cornerColors[0].ToString()!=cornerColors[1].ToString(),"Different colors at corner "+corner);
        }
        var colors=ScreenEdgePicker.RegionColors(previewZones,EdgeActivationService.GetMonitors());
        Check(colors[1].ToString()!=colors[2].ToString(),"Adjacent app ranges have different colors");
        var spatial=Enumerable.Range(0,5).Select(i=>new EdgePreviewZone(i.ToString(),i.ToString(),new EdgeActivationSettings{StartPercent=i*20,EndPercent=(i+1)*20})).Reverse().ToList();
        var orderedColors=ScreenEdgePicker.RegionColors(spatial,EdgeActivationService.GetMonitors());
        Check(orderedColors[4]==System.Windows.Media.Brushes.DodgerBlue&&orderedColors[3]==System.Windows.Media.Brushes.Gold&&orderedColors[2]==System.Windows.Media.Brushes.Crimson&&orderedColors[1]==System.Windows.Media.Brushes.ForestGreen&&orderedColors[0]==System.Windows.Media.Brushes.DodgerBlue,"Spatial blue yellow red green cycle");
        var physicalMonitors=EdgeActivationService.GetMonitors();
        var overlapping=new EdgeActivationSettings{StartPercent=50,EndPercent=100,AppZones=[new(){StartPercent=60,EndPercent=80}]};
        Check(EdgeActivationService.VisibleMarkerRanges(overlapping,physicalMonitors).SequenceEqual(new[]{(50,60),(80,100)}),"Mycelview marker does not blend over app colors");
        var palette=ScreenEdgePicker.RegionColors(previewZones,physicalMonitors);
        foreach(var entry in previewZones.Select((zone,index)=>(zone,index))){
            var color=EdgeRegionPalette.Get(settings,physicalMonitors,entry.zone.Key);
            Check(color==((System.Windows.Media.SolidColorBrush)palette[entry.index]).Color,"Desktop colors equal diagram colors");
        }
        using(var markerTest=new EdgeActivationMarker()){
            var offscreen=new EdgeMonitor("fixture",-32000,-32000,-31000,-31000,false);
            markerTest.Update(offscreen,new EdgeActivationSettings(),System.Windows.Media.Colors.Gold);
            Check(markerTest.DisplayColor is {} gold&&gold.R==255&&gold.G==215&&gold.B==0,"Marker uses assigned yellow hue");
            markerTest.Update(offscreen,new EdgeActivationSettings(),System.Windows.Media.Colors.Crimson);
            Check(markerTest.DisplayColor is {} red&&red.R==220&&red.G==20&&red.B==60&&red.A==89,"Color changes without changing bounds and default opacity preserved");
            markerTest.Update(offscreen,new EdgeActivationSettings{OpacityPercent=100},System.Windows.Media.Colors.Crimson);
            Check(markerTest.DisplayColor?.A==255,"Fully opaque update at same bounds");
            markerTest.Update(offscreen,new EdgeActivationSettings{OpacityPercent=10},System.Windows.Media.Colors.Crimson);
            Check(markerTest.DisplayColor?.A==26,"More transparent update");
            markerTest.Update(offscreen,new EdgeActivationSettings{OpacityPercent=0},System.Windows.Media.Colors.Crimson);
            Check(markerTest.DisplayColor?.A==0,"Fully transparent supported");
        }
        var opacityState=StateStore.Parse(JsonSerializer.Serialize(new WorkspaceState{EdgeActivation=new(){OpacityPercent=72}}));
        Check(opacityState.EdgeActivation.Normalize().OpacityPercent==72,"Opacity survives save and load");
        Check(new AppWindowZone().Region(opacityState.EdgeActivation).OpacityPercent==72,"App markers inherit global opacity");
        Check(new EdgeActivationSettings{OpacityPercent=200}.Normalize().OpacityPercent==100,"Opacity clamped");
        Check(StateStore.Parse("{}").EdgeActivation.OpacityPercent==EdgeActivationSettings.DefaultOpacityPercent,"Legacy settings keep previous opacity");
        var copy=settings.Normalize();copy.AppZones[0].Name="Copy";
        Check(settings.AppZones[0].Name=="Test","Dialog clone cannot mutate stored zones");
        var restored=StateStore.Parse(JsonSerializer.Serialize(new WorkspaceState{EdgeActivation=settings}));
        Check(restored.EdgeActivation.AppZones.Count==2&&restored.EdgeActivation.AppZones[1].AppPath==exe,"Global zone persistence");
        var monitor=new EdgeMonitor("test",-1920,0,0,1080,false);
        Check(EdgeActivationService.InRange(monitor,a.Region(settings),-1920,100),"First app zone");
        Check(!EdgeActivationService.InRange(monitor,a.Region(settings),-1920,500),"Different app zone");
        using(var vm=new MainViewModel(new StateStore(Path.Combine(output,"dialog-state")))){
            vm.EdgeActivation=settings;
            var owner=new MainWindow(vm){TestMode=true,Opacity=0,ShowActivated=false,ShowInTaskbar=false};owner.Show();
            var dialog=owner.CreateEdgeActivationDialog();
            Check(dialog.ResizeMode==ResizeMode.CanResize&&dialog.Height==Math.Min(820,SystemParameters.WorkArea.Height-40),"Edgecall dialog is taller and resizable");
            var root=(System.Windows.Controls.DockPanel)dialog.Content;
            var tabs=root.Children.OfType<System.Windows.Controls.TabControl>().Single();
            Check(tabs.Items.Count==3,"Three simple setting categories");
            Check(root.Children.OfType<System.Windows.Controls.CheckBox>().Single().ToolTip!=null,"Enable help is a tooltip");
            var appTab=(System.Windows.Controls.TabItem)tabs.Items[1];
            var appPanel=(System.Windows.Controls.StackPanel)((System.Windows.Controls.ScrollViewer)appTab.Content).Content;
            var selector=appPanel.Children.OfType<System.Windows.Controls.ComboBox>().Single();
            Check(selector.Items.Count==2,"Registered apps selectable");
            selector.SelectedIndex=0;
            var rows=appPanel.Children.OfType<System.Windows.Controls.StackPanel>().Single();
            Check(rows.Children.OfType<System.Windows.Controls.Border>().Count(x=>x.Visibility==Visibility.Visible)==1,"Only selected app expanded");
            Check(settings.AppZones[0].Name=="Test","Editor changes isolated from saved settings");
            tabs.SelectedIndex=0;
            root.Measure(new Size(500,550));root.Arrange(new Rect(0,0,500,550));root.UpdateLayout();
            var basicContent=(System.Windows.Controls.StackPanel)((System.Windows.Controls.ScrollViewer)((System.Windows.Controls.TabItem)tabs.Items[0]).Content).Content;
            var basicMap=(System.Windows.Controls.StackPanel)basicContent.Children[0];
            var basicLegend=(System.Windows.Controls.StackPanel)basicMap.Children[2];
            basicLegend.Children[1].RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0,System.Windows.Input.MouseButton.Left){RoutedEvent=UIElement.MouseLeftButtonDownEvent});
            Check(tabs.SelectedIndex==1 && ((AppWindowZone)selector.SelectedItem).Id==a.Id,"Click region name selects matching app editor");
            tabs.SelectedIndex=0;root.UpdateLayout();
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(500,550,96,96,System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(root);var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using(var stream=File.Create(Path.Combine(output,"settings.png")))png.Save(stream);
            dialog.Close();owner.Close();
        }
        var first=new Window{Title="Mycelview edge fixture 1",Width=300,Height=200,Left=-30000,Top=-30000,ShowActivated=false,ShowInTaskbar=false};
        var second=new Window{Title="Mycelview edge fixture 2",Width=300,Height=200,Left=-30000,Top=-30000,ShowActivated=false,ShowInTaskbar=false};
        WindowPicker? picker=null;
        try{
            first.Show();second.Show();
            var h1=new WindowInteropHelper(first).Handle;var h2=new WindowInteropHelper(second).Handle;
            SetOwner(h1,-8,IntPtr.Zero);SetOwner(h2,-8,IntPtr.Zero);
            var windows=AppWindows.List(exe,true);
            Check(windows.Any(x=>x.Handle==h1)&&windows.Any(x=>x.Handle==h2),"Enumerate two native windows for exact app");
            Check(AppWindows.List(Path.Combine(output,"different.exe"),true).Count==0,"Exclude different application");
            Check(!AppWindows.List(exe).Any(x=>x.Handle==h1),"Exclude Mycelview itself");
            picker=new WindowPicker(a,windows.Where(x=>x.Handle==h1||x.Handle==h2).ToList(),EdgeActivationService.GetMonitors()[0]){Opacity=0,ShowActivated=false};
            picker.Show();picker.UpdateLayout();
            Check(picker.ThumbnailCount==2,"Native DWM thumbnails registered");
            var beginMotion=typeof(WindowPicker).GetMethod("BeginMotion",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            var endMotion=typeof(WindowPicker).GetMethod("EndMotion",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            var movingField=typeof(WindowPicker).GetField("movingCard",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            beginMotion.Invoke(picker,[h1,new Point(20,20)]);
            var moving=(System.Windows.Controls.Border)movingField.GetValue(picker)!;
            Check(!moving.IsHitTestVisible&&System.Windows.Controls.Panel.GetZIndex(moving)==10,"Dragged card lifted above neighbors");
            endMotion.Invoke(picker,[false]);
            Check(moving.IsHitTestVisible&&moving.RenderTransform==System.Windows.Media.Transform.Identity&&movingField.GetValue(picker)==null,"Canceled drag restores card and releases animation state");
            var twenty=new WindowPicker(a,Enumerable.Range(0,20).Select(i=>new AppOpenWindow(h1,"Preview "+i,exe)).ToList(),EdgeActivationService.GetMonitors()[0]){Opacity=0,ShowActivated=false};
            twenty.Show();twenty.UpdateLayout();
            var cardGrid=(System.Windows.Controls.Primitives.UniformGrid)typeof(WindowPicker).GetField("cards",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(twenty)!;
            Check(cardGrid.Columns==5&&cardGrid.Children.Count==20,"Twenty previews use five columns and four rows");
            var scale=System.Windows.PresentationSource.FromVisual(twenty)!.CompositionTarget.TransformToDevice;
            var screen=EdgeActivationService.GetMonitors()[0];
            Check(twenty.ActualWidth*scale.M11<=screen.Right-screen.Left&&twenty.ActualHeight*scale.M22<=screen.Bottom-screen.Top,"Twenty previews fit monitor bounds");
            foreach(var card in cardGrid.Children.OfType<FrameworkElement>()){
                var bounds=card.TransformToAncestor(twenty).TransformBounds(new Rect(0,0,card.ActualWidth,card.ActualHeight));
                Check(bounds.Left>=-1&&bounds.Top>=-1&&bounds.Right<=twenty.ActualWidth+1&&bounds.Bottom<=twenty.ActualHeight+1,"Every card fully inside preview window");
            }
            twenty.Dismiss();
            foreach(int count in new[]{1,4,5,12,20,21}){
                var compact=new WindowPicker(a,Enumerable.Range(0,count).Select(i=>new AppOpenWindow(h1,"Preview "+i,exe)).ToList(),new EdgeMonitor("test-small",-30000,-30000,-29200,-29400,false)){Opacity=0,ShowActivated=false};
                compact.Show();compact.UpdateLayout();
                var compactGrid=(System.Windows.Controls.Primitives.UniformGrid)typeof(WindowPicker).GetField("cards",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(compact)!;
                var compactScale=System.Windows.PresentationSource.FromVisual(compact)!.CompositionTarget.TransformToDevice;
                Check(compact.ActualWidth*compactScale.M11<=800&&compact.ActualHeight*compactScale.M22<=600,"Small monitor fit for "+count+" windows");
                foreach(var child in compactGrid.Children.OfType<FrameworkElement>()){
                    var rect=child.TransformToAncestor(compact).TransformBounds(new Rect(0,0,child.ActualWidth,child.ActualHeight));
                    Check(rect.Left>=-1&&rect.Top>=-1&&rect.Right<=compact.ActualWidth+1&&rect.Bottom<=compact.ActualHeight+1,"No clipped card for "+count+" windows");
                }
                compact.Dismiss();
            }
            int reentered=0;
            picker.Closing+=(_,_)=>{reentered++;picker.Dismiss();};
            picker.Dismiss();
            picker.Dismiss();
            Check(reentered==1&&picker.IsDismissed,"Reentrant close ignored during deactivation/closing");
            Check(picker.ThumbnailCount==0,"Thumbnail handles released");
            Check(!picker.ContainsCursor(),"Closed picker is never inspected as live");
            for(int cycle=0;cycle<12;cycle++){
                var repeated=new WindowPicker(a,windows.Where(x=>x.Handle==h1).ToList(),EdgeActivationService.GetMonitors()[0]){Opacity=0,ShowActivated=false};
                repeated.Show();
                repeated.Closing+=(_,_)=>repeated.Dismiss();
                repeated.Close();repeated.Dismiss();
                Check(repeated.IsDismissed&&repeated.ThumbnailCount==0,"Repeated open/external-close cycle releases resources");
            }
            void PumpClose(){
                var frame=new System.Windows.Threading.DispatcherFrame();
                var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};
                timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();
                System.Windows.Threading.Dispatcher.PushFrame(frame);
            }
            bool cancelClose=true;
            first.Closing+=(_,e)=>e.Cancel=cancelClose;
            var targetWindow=windows.Single(w=>w.Handle==h1);
            Check(AppWindows.RequestClose(targetWindow),"Native close request accepted");
            PumpClose();
            Check(AppWindows.Exists(h1)&&AppWindows.Exists(h2),"Canceled close preserves both windows");
            cancelClose=false;
            Check(AppWindows.RequestClose(targetWindow),"Second close request accepted");
            PumpClose();
            Check(!AppWindows.Exists(h1)&&AppWindows.Exists(h2),"Only selected window closed");
            Check(!AppWindows.RequestClose(targetWindow),"Stale close target rejected");
            second.Close();Check(!AppWindows.List(exe,true).Any(x=>x.Handle==h2),"Closed window removed");
            Check(!AppWindows.Activate(IntPtr.Zero),"Invalid activation rejected");
            File.WriteAllText(Path.Combine(output,"passed.txt"),"Zone ranges, overlap, persistence, cloned settings, native window enumeration, process matching, DWM registration and disposal passed. No user's window activated.");
        }finally{picker?.Close();first.Close();second.Close();}
    }
}
