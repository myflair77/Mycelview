using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.Win32;
namespace Mycelview;

internal sealed class IndexManagementWindow : Window
{
    private readonly LocalSearchIndex index;
    private readonly TextBlock state = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock stats = new() { Margin = new Thickness(0,6,0,8) };
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,8) };
    private readonly DataGrid errors = new() { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Extended, Height = 150, EnableRowVirtualization = true };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly List<Button> workButtons = [];
    private readonly Button cancel = new() { Content = "재구축 취소" };
    private string issueSignature = "";
    private bool executing;
    internal IndexManagementWindow(Window owner, LocalSearchIndex index)
    {
        Owner = owner; this.index = index; Title = "검색 인덱스 관리"; Icon = owner.Icon;
        Width = 850; Height = Math.Min(560, SystemParameters.WorkArea.Height - 40); MinWidth = 600; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "SurfaceBrush"); SetResourceReference(ForegroundProperty, "TextBrush"); SetResourceReference(FontSizeProperty, "FontSize12");
        var panel = new StackPanel { Margin = new Thickness(18) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(state); panel.Children.Add(stats);
        var actions = new WrapPanel(); panel.Children.Add(actions);
        Add(actions, "변경 사항 재확인", () => { index.RecheckChanges(); return Task.CompletedTask; });
        Add(actions, "전체 인덱스 재구축", () => index.RebuildAsync());
        cancel.Click += (_, _) => index.CancelRebuild(); actions.Children.Add(cancel);
        panel.Children.Add(new TextBlock { Text = "재구축 중에도 메인 창에서 기존 인덱스로 검색할 수 있습니다. 완료되면 새 인덱스로 전환합니다.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,6,0,12) });
        panel.Children.Add(message); panel.Children.Add(new Separator());
        panel.Children.Add(new TextBlock { Text = "확인하지 못한 경로 · 최대 200개 · Ctrl/Shift로 다중 선택", Margin = new Thickness(0,8,0,6) });
        errors.Columns.Add(new DataGridTextColumn { Header = "경로", Binding = new Binding(nameof(IndexIssue.Path)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        errors.Columns.Add(new DataGridTextColumn { Header = "이유", Binding = new Binding(nameof(IndexIssue.Reason)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        panel.Children.Add(errors); var retry = new WrapPanel(); panel.Children.Add(retry);
        Add(retry, "선택한 경로 재시도", () => { var paths = errors.SelectedItems.Cast<IndexIssue>().Select(x => x.Path).ToArray(); if (paths.Length == 0) throw new IOException("재시도할 경로를 선택하세요."); index.RetryErrors(paths); return Task.CompletedTask; });
        Add(retry, "오류 경로 모두 재시도", () => { index.RetryErrors(index.ErrorDetails.Select(x => x.Path)); return Task.CompletedTask; });
        timer.Tick += (_, _) => Refresh();
        DismissSettingsOnOutsideClick.Attach(this, modal: false);
        Closed += (_, _) => timer.Stop(); Loaded += (_, _) => { Refresh(); timer.Start(); };
    }
    private void Add(Panel panel, string label, Func<Task> action)
    {
        var button = new Button { Content = label }; panel.Children.Add(button); workButtons.Add(button);
        button.Click += async (_, _) =>
        {
            if (executing) return; executing = true; message.Text = ""; Refresh();
            try { await action(); message.Text = "요청을 처리했습니다. 위의 진행 상태와 오류 목록을 확인하세요."; }
            catch (OperationCanceledException) { message.Text = "재구축을 취소했습니다. 기존 인덱스로 계속 검색할 수 있습니다."; }
            catch (Exception ex) { message.Text = ex.Message; }
            finally { executing = false; Refresh(); }
        };
    }
    private void Refresh()
    {
        state.Text = index.Status;
        stats.Text = $"검색 가능한 항목 {index.EntryCount:N0}개 · 누적 확인 {index.ScannedCount:N0}개 · 용량 {index.StorageBytes / 1048576d:N1} MB\n마지막 갱신: {(index.LastUpdated?.ToString("yyyy-MM-dd HH:mm:ss") ?? "아직 완료되지 않음")}";
        foreach (var button in workButtons) button.IsEnabled = !executing && !index.IsRebuilding;
        cancel.IsEnabled = index.IsRebuilding;
        var details = index.ErrorDetails; var signature = string.Join("\n", details.Select(x => x.Path + x.Reason));
        if (signature != issueSignature) { var selected = errors.SelectedItems.Cast<IndexIssue>().Select(x => x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase); errors.ItemsSource = details; foreach (var item in details.Where(x => selected.Contains(x.Path))) errors.SelectedItems.Add(item); issueSignature = signature; }
    }
}
