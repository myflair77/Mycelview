using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
namespace PinShelf;
public partial class MainWindow
{


    private const string WorkspaceDragFormat = "PinShelf.WorkspaceTab";
    private void CaptureWorkspaceLayout()
    {
        SavePaneLayout();
        if (ViewModel.ActiveWorkspace is { } tab)
        {
            tab.FolderScroll = FolderWorkspaceScroll.VerticalOffset;
            tab.FavoriteScroll = CardView.FindChild<ScrollViewer>(FavoritesList)?.VerticalOffset ?? 0;
        }
        ViewModel.Search.HideResults();
    }
    private void RestoreWorkspaceLayout()
    {
        RestorePaneLayout();
        var tab = ViewModel.ActiveWorkspace;
        Dispatcher.BeginInvoke(() =>
        {
            if (tab == null || tab != ViewModel.ActiveWorkspace) return;
            FolderWorkspaceScroll.ScrollToVerticalOffset(tab.FolderScroll);
            CardView.FindChild<ScrollViewer>(FavoritesList)?.ScrollToVerticalOffset(tab.FavoriteScroll);
        }, DispatcherPriority.Loaded);
    }
    private static WorkspaceTab? TabFrom(object sender) => (sender as FrameworkElement)?.DataContext as WorkspaceTab;
    private async Task WorkspaceAction(Func<Task> action)
    { try { await action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "작업공간", MessageBoxButton.OK, MessageBoxImage.Information); } }
    private string? WorkspaceName(string title, string initial)
    {
        var dialog = new Window { Owner = this, Title = title, Icon = Icon, Width = 400, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, FontSize = FontSize };
        dialog.SetResourceReference(BackgroundProperty, "SurfaceBrush"); dialog.SetResourceReference(ForegroundProperty, "TextBrush");
        var panel = new StackPanel { Margin = new Thickness(18) }; dialog.Content = panel;
        panel.Children.Add(new TextBlock { Text = "탭마다 폴더·즐겨찾기·메모를 따로 저장합니다.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,12) });
        panel.Children.Add(new TextBlock { Text = "업무 탭 이름", Margin = new Thickness(0,0,0,6) });
        var text = new TextBox { Text = initial, MaxLength = 60 }; panel.Children.Add(text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,12,0,0) }; panel.Children.Add(buttons);
        var cancel = new Button { Content = "취소", IsCancel = true }; buttons.Children.Add(cancel);
        var ok = new Button { Content = "확인", IsDefault = true }; buttons.Children.Add(ok); ok.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(text.Text)) dialog.DialogResult = true; };
        dialog.Loaded += (_, _) => { text.Focus(); text.SelectAll(); };
        return dialog.ShowDialog() == true ? text.Text : null;
    }
    private bool addingWorkspace;
    private async void AddWorkspace(object sender, RoutedEventArgs e)
    {
        if(addingWorkspace)return;
        addingWorkspace=true;
        try
        {
            int number=1;
            while(ViewModel.Workspaces.Any(tab=>string.Equals(tab.Name,$"업무{number}",StringComparison.OrdinalIgnoreCase)))number++;
            await WorkspaceAction(async()=>await ViewModel.CreateWorkspaceAsync($"업무{number}"));
            await Dispatcher.InvokeAsync(()=> {UpdateLayout();AddWorkspaceButton.BringIntoView();},System.Windows.Threading.DispatcherPriority.Loaded);
        }
        finally {addingWorkspace=false;}
    }
    private async void SelectWorkspace(object sender, RoutedEventArgs e)
    { if (TabFrom(sender) is { } tab) await WorkspaceAction(() => ViewModel.SwitchWorkspaceAsync(tab.Id)); }
    private async void RenameWorkspace(object sender, RoutedEventArgs e)
    { if (TabFrom(sender) is not { } tab) return; var name = WorkspaceName("작업공간 이름 변경", tab.Name); if (name != null) await WorkspaceAction(() => { ViewModel.RenameWorkspace(tab.Id, name); return Task.CompletedTask; }); }
    private async void DuplicateWorkspace(object sender, RoutedEventArgs e)
    { if (TabFrom(sender) is { } tab) await WorkspaceAction(async () => await ViewModel.DuplicateWorkspaceAsync(tab.Id)); }
    private async void DeleteWorkspace(object sender, RoutedEventArgs e)
    {
        if (TabFrom(sender) is not { } tab) return;
        if (MessageBox.Show(this, $"‘{tab.Name}’ 작업공간을 삭제할까요?\n카드·즐겨찾기·메모 구성이 삭제됩니다. 원본 파일은 그대로 남습니다.\n삭제 전에 모든 작업공간 설정을 자동 백업합니다.", "작업공간 삭제", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            await WorkspaceAction(() => ViewModel.DeleteWorkspaceAsync(tab.Id));
    }
    private void WorkspaceTabMenu(object sender, RoutedEventArgs e)
    {
        var border = FindParent<Border>((DependencyObject)sender);
        if (border?.ContextMenu is not { } menu) return;
        menu.DataContext = border.DataContext; menu.PlacementTarget = border; menu.IsOpen = true;
    }
    private void WorkspaceTabDragStart(object sender, MouseButtonEventArgs e) => StartSmoothTabDrag(sender,e);
    private void WorkspaceTabDragMove(object sender, MouseEventArgs e) { }
    private void WorkspaceTabDragOver(object sender, DragEventArgs e)
    { e.Effects = e.Data.GetDataPresent(WorkspaceDragFormat) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; }
    private async void WorkspaceTabDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(WorkspaceDragFormat) is not Guid source || TabFrom(sender) is not { } target) return;
        var after = e.GetPosition((IInputElement)sender).X > ((FrameworkElement)sender).ActualWidth / 2;
        await WorkspaceAction(() => { ViewModel.ReorderWorkspace(source, target.Id, after); return Task.CompletedTask; });
    }
}


