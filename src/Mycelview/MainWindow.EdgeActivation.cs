using System.Globalization;
using System.Windows;
using System.Windows.Controls;
namespace Mycelview;
public partial class MainWindow
{
    private EdgeActivationService? edgeActivation;
    private AppEdgeSwitcher? appEdgeSwitcher;
    private void ShowEdgeActivationSettings(object sender, RoutedEventArgs e) => OpenEdgeActivationSettings();
    private void OpenEdgeActivationSettings(LauncherFavorite? favorite = null) => CreateEdgeActivationDialog(favorite).ShowDialog();
    internal Window CreateEdgeActivationDialog(LauncherFavorite? favorite = null)
    {
        var saved = ViewModel.EdgeActivation.Normalize();
        if(favorite != null) LauncherEdgeSetup.Prepare(saved,favorite);
        var dialog = new Window { Owner = this, Icon = Icon, Title = "엣지콜(edgecall) 설정", Width = 540, Height = Math.Min(820, SystemParameters.WorkArea.Height - 40), MinHeight = 400, MinWidth = 500, SizeToContent = SizeToContent.Manual, ResizeMode = ResizeMode.CanResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        dialog.SetResourceReference(BackgroundProperty, "SurfaceBrush"); dialog.SetResourceReference(ForegroundProperty, "TextBrush");
        var panel = new StackPanel { Margin = new Thickness(20) }; dialog.MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height - 40); dialog.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var enabled = new CheckBox { Content = "엣지콜(edgecall) 사용", ToolTip = "엣지콜은 지정한 화면 가장자리에 마우스를 두면 Mycelview를 앞으로 가져오거나 앱의 열린 창을 미리보기로 보여주는 기능입니다.", IsChecked = saved.Enabled }; panel.Children.Add(enabled);
        void Label(string value) => panel.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,12,0,5) });

        Label("작동 위치");
        string[] edges = ["Left", "Right", "Top", "Bottom"];
        var edge = new ComboBox { ItemsSource = new[] { "왼쪽", "오른쪽", "위쪽", "아래쪽" }, SelectedIndex = Array.IndexOf(edges, saved.Edge) }; panel.Children.Add(edge);
        Label("작동 범위");
        var rangeMode = new ComboBox { ItemsSource = new[] { "경계 전체", "일부 구간 지정" }, SelectedIndex = saved.StartPercent == 0 && saved.EndPercent == 100 ? 0 : 1 }; panel.Children.Add(rangeMode);
        var rangeRow = new StackPanel { Margin = new Thickness(0,6,0,0) };
        var rangeStart = new Slider { Minimum = 0, Maximum = 99, Value = saved.StartPercent, TickFrequency = 1, IsSnapToTickEnabled = true, IsMoveToPointEnabled = true };
        var rangeEnd = new Slider { Minimum = 1, Maximum = 100, Value = saved.EndPercent, TickFrequency = 1, IsSnapToTickEnabled = true, IsMoveToPointEnabled = true };
        rangeRow.Children.Add(new TextBlock { Text = "시작 위치" }); rangeRow.Children.Add(rangeStart);
        rangeRow.Children.Add(new TextBlock { Text = "끝 위치" }); rangeRow.Children.Add(rangeEnd); panel.Children.Add(rangeRow);
        var preview = new Canvas { Width = 220, Height = 90, Background = System.Windows.Media.Brushes.WhiteSmoke, Margin = new Thickness(0,8,0,0), HorizontalAlignment = HorizontalAlignment.Center };
        var segment = new System.Windows.Shapes.Rectangle { Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(70,155,255)), RadiusX = 3, RadiusY = 3 };
        preview.Children.Add(segment); panel.Children.Add(preview);
        var rangeHint = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,4,0,0) }; rangeHint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); panel.Children.Add(rangeHint);
        int thicknessPixels = saved.ThicknessPixels;
        bool adjustingRange = false;
        void UpdateRange()
        {
            rangeRow.IsEnabled = rangeMode.SelectedIndex == 1;
            double start = rangeMode.SelectedIndex == 0 ? 0 : rangeStart.Value / 100, end = rangeMode.SelectedIndex == 0 ? 1 : rangeEnd.Value / 100;
            bool vertical = edge.SelectedIndex < 2;
            double thickness = thicknessPixels;
            segment.Width = vertical ? thickness : preview.Width * (end - start); segment.Height = vertical ? preview.Height * (end - start) : thickness;
            Canvas.SetLeft(segment, vertical ? (edge.SelectedIndex == 1 ? preview.Width - thickness : 0) : preview.Width * start);
            Canvas.SetTop(segment, vertical ? preview.Height * start : (edge.SelectedIndex == 3 ? preview.Height - thickness : 0));
            rangeHint.Text = $"반응 구간  {start:P0} ~ {end:P0}";
            rangeStart.ToolTip=rangeEnd.ToolTip=vertical?"화면 위에서 아래 방향으로 시작·끝 위치를 조절합니다.":"화면 왼쪽에서 오른쪽 방향으로 시작·끝 위치를 조절합니다.";
        }
        rangeStart.ValueChanged += (_, _) => { if (adjustingRange) return; adjustingRange = true; if (rangeStart.Value >= rangeEnd.Value) rangeEnd.Value = rangeStart.Value + 1; adjustingRange = false; UpdateRange(); };
        rangeEnd.ValueChanged += (_, _) => { if (adjustingRange) return; adjustingRange = true; if (rangeEnd.Value <= rangeStart.Value) rangeStart.Value = rangeEnd.Value - 1; adjustingRange = false; UpdateRange(); };
        edge.SelectionChanged += (_, _) => UpdateRange(); rangeMode.SelectionChanged += (_, _) => UpdateRange(); UpdateRange();
        int commonStart=panel.Children.Count;
        Label("표시 두께");
        var thicknessRow = new StackPanel { Orientation = Orientation.Horizontal };
        var thicknessValue = new TextBlock { Width = 70, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
        var thicknessDown = new Button { Content = "▼", ToolTip = "1px 감소" };
        var thicknessUp = new Button { Content = "▲", ToolTip = "1px 증가" };
        System.Windows.Automation.AutomationProperties.SetName(thicknessDown, "표시 두께 감소");
        System.Windows.Automation.AutomationProperties.SetName(thicknessUp, "표시 두께 증가");
        void ChangeThickness(int change)
        {
            thicknessPixels = Math.Clamp(thicknessPixels + change, 2, 30);
            thicknessValue.Text = $"{thicknessPixels}px";
            thicknessDown.IsEnabled = thicknessPixels > 2; thicknessUp.IsEnabled = thicknessPixels < 30;
            UpdateRange();
        }
        thicknessDown.Click += (_, _) => ChangeThickness(-1); thicknessUp.Click += (_, _) => ChangeThickness(1);
        thicknessRow.Children.Add(thicknessDown); thicknessRow.Children.Add(thicknessValue); thicknessRow.Children.Add(thicknessUp); panel.Children.Add(thicknessRow); ChangeThickness(0);
        Label("불투명도");
        var opacityRow=new DockPanel();
        var opacityValue=new TextBlock{Width=65,VerticalAlignment=VerticalAlignment.Center,TextAlignment=TextAlignment.Right};
        DockPanel.SetDock(opacityValue,Dock.Right);opacityRow.Children.Add(opacityValue);
        var opacityReset=new Button{Content="기본값",ToolTip="기본 불투명도 35%로 되돌립니다."};
        DockPanel.SetDock(opacityReset,Dock.Right);opacityRow.Children.Add(opacityReset);
        var opacity=new Slider{Minimum=0,Maximum=100,Value=saved.OpacityPercent,TickFrequency=1,SmallChange=1,LargeChange=10,IsMoveToPointEnabled=true,ToolTip="0%는 완전 투명, 100%는 완전 불투명입니다. 표시 농도만 바뀌며 호출 기능은 유지됩니다."};
        System.Windows.Automation.AutomationProperties.SetName(opacity,"반응 구간 불투명도");
        opacityRow.Children.Add(opacity);panel.Children.Add(opacityRow);
        void UpdateOpacity(){opacityValue.Text=$"{opacity.Value:0.#}%";}
        opacity.ValueChanged+=(_,_)=>UpdateOpacity();opacityReset.Click+=(_,_)=>opacity.Value=EdgeActivationSettings.DefaultOpacityPercent;UpdateOpacity();
        Label("대기 시간");
        int delayMs = saved.DelayMs;
        var delayRow = new StackPanel { Orientation = Orientation.Horizontal };
        var delay = new TextBox { Text = (delayMs / 1000.0).ToString("0.0", CultureInfo.CurrentCulture), IsReadOnly = true, Width = 90, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "위·아래 화살표로 대기 시간 조절" };
        var arrows = new StackPanel();
        var increase = new System.Windows.Controls.Primitives.RepeatButton { Content = "▲", Width = 28, Height = 18, Padding = new Thickness(0), Margin = new Thickness(2,0,0,0), ToolTip = "0.1초 증가" };
        var decrease = new System.Windows.Controls.Primitives.RepeatButton { Content = "▼", Width = 28, Height = 18, Padding = new Thickness(0), Margin = new Thickness(2,0,0,0), ToolTip = "0.1초 감소" };
        System.Windows.Automation.AutomationProperties.SetName(increase, "대기 시간 증가");
        System.Windows.Automation.AutomationProperties.SetName(decrease, "대기 시간 감소");
        void UpdateDelay(int change)
        {
            delayMs = Math.Clamp(delayMs + change, 100, 5000);
            delay.Text = (delayMs / 1000.0).ToString("0.0", CultureInfo.CurrentCulture);
            increase.IsEnabled = delayMs < 5000; decrease.IsEnabled = delayMs > 100;
        }
        increase.Click += (_, _) => UpdateDelay(100); decrease.Click += (_, _) => UpdateDelay(-100);
        delay.PreviewKeyDown += (_, key) => { if (key.Key is System.Windows.Input.Key.Up or System.Windows.Input.Key.Down) { UpdateDelay(key.Key == System.Windows.Input.Key.Up ? 100 : -100); key.Handled = true; } };
        arrows.Children.Add(increase); arrows.Children.Add(decrease); delayRow.Children.Add(delay); delayRow.Children.Add(arrows); panel.Children.Add(delayRow); UpdateDelay(0);
        int monitorStart=panel.Children.Count;
        Label("모니터");
        var monitor = new ComboBox { DisplayMemberPath = "Value", SelectedValuePath = "Key" }; panel.Children.Add(monitor);
        void UpdateMonitors()
        {
            string selected = monitor.SelectedValue as string ?? saved.Monitor;
            if (selected is "Leftmost" or "Rightmost" or "Topmost" or "Bottommost") selected = "Automatic";
            string direction = new[] { "왼쪽", "오른쪽", "위쪽", "아래쪽" }[Math.Max(0, edge.SelectedIndex)];
            var choices = new List<KeyValuePair<string,string>> { new("Automatic", $"가장 {direction} 모니터 (자동 · 기본값)") };
            choices.AddRange(EdgeActivationService.GetMonitors().OrderBy(m => m.Left).Select(m => new KeyValuePair<string,string>(m.Device, m.Label)));
            if (!choices.Any(c => c.Key == selected)) choices.Add(new(selected, selected + " (연결되지 않음)"));
            monitor.ItemsSource = choices; monitor.SelectedValue = selected;
        }
        edge.SelectionChanged += (_, _) => UpdateMonitors(); UpdateMonitors();
        int helpStart=panel.Children.Count;
        int appStart=panel.Children.Count;
        EdgeActivationSettings BasicRegion() => new(){Enabled=enabled.IsChecked==true,Edge=edges[edge.SelectedIndex],Monitor=(string?)monitor.SelectedValue??"Automatic",StartPercent=rangeMode.SelectedIndex==0?0:(int)rangeStart.Value,EndPercent=rangeMode.SelectedIndex==0?100:(int)rangeEnd.Value};
        Action<string> navigate=_=>{};
        Action<string,string,string,int,int> updateZone=(_,_,_,_,_)=>{};
        var appZones = AddAppZoneEditor(panel, saved, BasicRegion,key=>navigate(key),out var selectApp,out var updateApp,(key,device,side,a,b)=>updateZone(key,device,side,a,b));
        updateZone=(key,device,side,a,b)=>{
            if(key!="Mycelview"){updateApp(key,device,side,a,b);return;}
            edge.SelectedIndex=Array.IndexOf(edges,side);monitor.SelectedValue=device;rangeMode.SelectedIndex=1;
            adjustingRange=true;rangeStart.Value=a;rangeEnd.Value=b;adjustingRange=false;UpdateRange();
        };
        int appEnd=panel.Children.Count;
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap }; panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,12,0,0) }; panel.Children.Add(buttons);
        buttons.Children.Add(new Button { Content = "취소", IsCancel = true }); var save = new Button { Content = "저장", IsDefault = true }; buttons.Children.Add(save);
        save.Click += (_, _) =>
        {
            int start = rangeMode.SelectedIndex == 0 ? 0 : (int)Math.Round(rangeStart.Value);
            int end = rangeMode.SelectedIndex == 0 ? 100 : (int)Math.Round(rangeEnd.Value);
            if (!ValidateAppZones(appZones, out var zoneError)) { error.Text=zoneError; return; }
            var previous = ViewModel.EdgeActivation;
            ViewModel.EdgeActivation = new() { AppZones = appZones, Enabled = enabled.IsChecked == true, Edge = edges[Math.Max(0, edge.SelectedIndex)], DelayMs = delayMs, ThicknessPixels = thicknessPixels, OpacityPercent = opacity.Value, StartPercent = start, EndPercent = end, Monitor = (string?)monitor.SelectedValue ?? "Automatic" };
            if (!ViewModel.Save()) { ViewModel.EdgeActivation = previous; error.Text = "설정을 저장하지 못했습니다. 다시 시도하세요."; return; }
            dialog.DialogResult = true;
        };
        DismissSettingsOnOutsideClick.Attach(dialog);
        var children=panel.Children.Cast<UIElement>().ToList();
        panel.Children.Clear();
        var basic=new StackPanel();
        foreach(var child in children.Skip(1).Take(commonStart-1).Concat(children.Skip(monitorStart).Take(helpStart-monitorStart)))basic.Children.Add(child);
        basic.Children.Remove(preview);
        basic.Children.Insert(0,ScreenEdgePicker.Create(()=>new EdgeActivationSettings{Edge=edges[edge.SelectedIndex],Monitor=(string?)monitor.SelectedValue??"Automatic",StartPercent=rangeMode.SelectedIndex==0?0:(int)rangeStart.Value,EndPercent=rangeMode.SelectedIndex==0?100:(int)rangeEnd.Value},(device,side,a,b)=>{
            edge.SelectedIndex=Array.IndexOf(edges,side);monitor.SelectedValue=device;rangeMode.SelectedIndex=1;
            adjustingRange=true;rangeStart.Value=a;rangeEnd.Value=b;adjustingRange=false;UpdateRange();
        },()=>ScreenEdgePicker.Zones(BasicRegion(),appZones),"Mycelview",key=>navigate(key),(key,device,side,a,b)=>updateZone(key,device,side,a,b)));
        var common=new StackPanel();
        foreach(var child in children.Skip(commonStart).Take(monitorStart-commonStart))common.Children.Add(child);
        var apps=new StackPanel();
        foreach(var child in children.Skip(appStart).Take(appEnd-appStart))apps.Children.Add(child);
        var tabs=new TabControl{Margin=new Thickness(0,12,0,0)};
        void Tab(string title,StackPanel content,string hint){
            tabs.Items.Add(new TabItem{Header=title,ToolTip=hint,Content=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(12,6,12,10)}});
        }
        Tab("Mycelview 호출",basic,"화면 가장자리에서 Mycelview 창을 앞으로 가져올 위치를 지정합니다.");
        Tab("브라우저·사이트·앱별 선택",apps,"앱마다 반응 구간을 지정하고 열린 창의 미리보기에서 전환합니다. 한 창의 탭은 하나로 취급합니다.");
        Tab("반응 설정",common,"Mycelview 호출과 앱별 창 선택에 공통으로 적용됩니다.");
        navigate=key=>{if(key=="Mycelview")tabs.SelectedIndex=0;else{selectApp(key);tabs.SelectedIndex=1;}};
        tabs.SelectedIndex=favorite==null?0:1;
        var root=new DockPanel{Margin=new Thickness(18)};
        DockPanel.SetDock(enabled,Dock.Top);root.Children.Add(enabled);
        DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
        DockPanel.SetDock(error,Dock.Bottom);root.Children.Add(error);
        root.Children.Add(tabs);dialog.Content=root;
        dialog.SetResourceReference(FontSizeProperty,"FontSize11");
        enabled.ToolTip="전체 가장자리 호출 기능을 켜거나 끕니다. 파일을 드래그하는 동안에는 반응하지 않습니다.";
        edge.ToolTip="마우스를 둘 화면 가장자리를 선택합니다.";
        rangeMode.ToolTip="전체 경계 또는 슬라이더로 지정한 일부 구간에서만 반응하도록 설정합니다.";
        monitor.ToolTip="자동 선택은 방향에 따라 가장 왼쪽·오른쪽·위쪽·아래쪽 모니터를 사용합니다.";
        preview.ToolTip="파란 부분이 현재 선택한 반응 구간입니다.";
        thicknessValue.ToolTip=thicknessRow.ToolTip="2~30px, 1px씩 조절합니다. 기본값은 10px입니다.";
        delay.ToolTip=delayRow.ToolTip="가장자리에 머무르는 시간입니다. 0.1~5초, 0.1초씩 조절하며 기본값은 0.1초입니다.";
        return dialog;
    }
}
