using System.Windows;
using System.Windows.Controls;
namespace Mycelview;
public sealed partial class FileTasks
{
    internal void ShowHistorySettings()
    {
        if (Busy || history == null) return;
        var window = new Window { Owner = Owner, Title = "실행 취소 기록 관리", Width = 460, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Icon = Owner.Icon };
        var panel = new StackPanel { Margin = new Thickness(20) }; window.Content = panel;
        panel.Children.Add(new TextBlock { Text = $"실행 취소 {history.Data.Undo.Count}개 · 다시 실행 {history.Data.Redo.Count}개\n보관 용량 {history.Usage / 1048576d:F1} MB", Margin = new Thickness(0,0,0,12) });
        panel.Children.Add(new TextBlock { Text = "최대 보관 용량 (MB)" });
        var limit = new TextBox { Text = history.Data.LimitMB.ToString() }; panel.Children.Add(limit);
        panel.Children.Add(new TextBlock { Text = "보관 기간 (일)" });
        var days = new TextBox { Text = history.Data.Days.ToString() }; panel.Children.Add(days);
        panel.Children.Add(new TextBlock { Text = "시작 시와 파일 작업 시 오래된 기록부터 자동 정리합니다.\n정리한 기록은 되돌릴 수 없습니다. 실제 파일은 삭제하지 않습니다.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,12,0,12) });
        var apply = new Button { Content = "적용 및 정리" }; panel.Children.Add(apply);
        apply.Click += (_, _) => {
            if (!int.TryParse(limit.Text, out var mb) || mb < 1 || mb > 1048576 || !int.TryParse(days.Text, out var age) || age < 1 || age > 3650) { MessageBox.Show(window, "용량 1~1048576 MB, 기간 1~3650일을 입력하세요."); return; }
            try { history.Data.LimitMB = mb; history.Data.Days = age; history.Save(); history.Cleanup(); NotifyUndo(); window.Close(); }
            catch (Exception ex) { MessageBox.Show(window, ex.Message); }
        };
        window.ShowDialog();
    }
}
