using System.Globalization;
using System.Windows;
using System.Windows.Controls;
namespace Mycelview;
public partial class MainWindow
{
    private EdgeActivationService? edgeActivation;
    private void ShowEdgeActivationSettings(object sender, RoutedEventArgs e)
    {
        var saved = ViewModel.EdgeActivation;
        var dialog = new Window { Owner = this, Icon = Icon, Title = "화면 가장자리 호출 설정", Width = 440, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        dialog.SetResourceReference(BackgroundProperty, "SurfaceBrush"); dialog.SetResourceReference(ForegroundProperty, "TextBrush");
        var panel = new StackPanel { Margin = new Thickness(20) }; dialog.MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height - 40); dialog.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var enabled = new CheckBox { Content = "화면 가장자리에서 Mycelview 표시", IsChecked = saved.Enabled }; panel.Children.Add(enabled);
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
            rangeHint.Text = (vertical ? "슬라이더 왼쪽은 화면 위, 오른쪽은 화면 아래입니다." : "슬라이더는 화면의 왼쪽에서 오른쪽 방향입니다.") + $" 선택: {start:P0}~{end:P0}";
        }
        rangeStart.ValueChanged += (_, _) => { if (adjustingRange) return; adjustingRange = true; if (rangeStart.Value >= rangeEnd.Value) rangeEnd.Value = rangeStart.Value + 1; adjustingRange = false; UpdateRange(); };
        rangeEnd.ValueChanged += (_, _) => { if (adjustingRange) return; adjustingRange = true; if (rangeEnd.Value <= rangeStart.Value) rangeStart.Value = rangeEnd.Value - 1; adjustingRange = false; UpdateRange(); };
        edge.SelectionChanged += (_, _) => UpdateRange(); rangeMode.SelectionChanged += (_, _) => UpdateRange(); UpdateRange();
        Label("파란 표시 두께 (2~30px · 기본 10px)");
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
        Label("대기 시간 (초 · 0.1~5 · 0.1초씩 조절)");
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
        Label("작동 모니터");
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
        Label("반응 구간은 은은한 파란색으로 표시됩니다. 기본값: 가장 왼쪽 모니터의 왼쪽 경계 아래쪽 절반(50~100%)에서 0.2초 대기. 마우스를 경계에서 떼었다가 다시 가져가면 재호출합니다. 파일을 드래그하는 동안에는 호출하지 않습니다.");
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap }; panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,12,0,0) }; panel.Children.Add(buttons);
        buttons.Children.Add(new Button { Content = "취소", IsCancel = true }); var save = new Button { Content = "저장", IsDefault = true }; buttons.Children.Add(save);
        save.Click += (_, _) =>
        {
            int start = rangeMode.SelectedIndex == 0 ? 0 : (int)Math.Round(rangeStart.Value);
            int end = rangeMode.SelectedIndex == 0 ? 100 : (int)Math.Round(rangeEnd.Value);
            var previous = ViewModel.EdgeActivation;
            ViewModel.EdgeActivation = new() { Enabled = enabled.IsChecked == true, Edge = edges[Math.Max(0, edge.SelectedIndex)], DelayMs = delayMs, ThicknessPixels = thicknessPixels, StartPercent = start, EndPercent = end, Monitor = (string?)monitor.SelectedValue ?? "Automatic" };
            if (!ViewModel.Save()) { ViewModel.EdgeActivation = previous; error.Text = "설정을 저장하지 못했습니다. 다시 시도하세요."; return; }
            dialog.DialogResult = true;
        };
        DismissSettingsOnOutsideClick.Attach(dialog);
        dialog.ShowDialog();
    }
}
