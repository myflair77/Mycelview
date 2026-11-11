using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PinShelf;
public partial class CardView
{
    private Point fileDragStart;
    private FileEntry? fileDragItem;
    private bool deferRowClick, toggleOnClick;
    private DateTime lastTransferScroll;
    private void FileDragMouseDown(object sender, MouseButtonEventArgs e)
    {
        TrackNameClick(e);
        if (FindParent<TextBox>(e.OriginalSource as DependencyObject) != null) return;
        fileDragItem = null; deferRowClick = false;
        if (e.ClickCount != 1 || Workspace.FileTasks.Busy || FindParent<DataGridRow>(e.OriginalSource as DependencyObject)?.Item is not FileEntry item) return;
        fileDragStart = e.GetPosition(FileList); fileDragItem = item;
        // Delay collapsing/toggling an already selected row until mouse-up, so a drag keeps the entire selection.
        if (FileList.SelectedItems.Contains(item) && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Control)
        {
            deferRowClick = true; toggleOnClick = Keyboard.Modifiers == ModifierKeys.Control;
            FileList.Focus(); Mouse.Capture(FileList, CaptureMode.SubTree); e.Handled = true;
        }
    }
    private void FileDragMouseUp(object sender, MouseButtonEventArgs e)
    {
        CompleteNameClick(e);
        var item = fileDragItem; fileDragItem = null;
        if (Mouse.Captured == FileList) Mouse.Capture(null);
        if (!deferRowClick || item == null) return;
        deferRowClick = false;
        if (FindParent<DataGridRow>(e.OriginalSource as DependencyObject)?.Item is not FileEntry released || released != item) return;
        if (toggleOnClick) FileList.SelectedItems.Remove(item);
        else if (FileList.SelectedItems.Count!=1 || !FileList.SelectedItems.Contains(item)) { FileList.SelectedItems.Clear(); FileList.SelectedItems.Add(item); }
        e.Handled = true;
    }
    private async void FileDragMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) { fileDragItem = null; deferRowClick = false; return; }
        if (fileDragItem == null || Workspace.FileTasks.Busy) return;
        var delta = e.GetPosition(FileList) - fileDragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        CancelPendingRename();
        var item = fileDragItem; fileDragItem = null; deferRowClick = false;
        if (Mouse.Captured == FileList) Mouse.Capture(null);
        if (!FileList.SelectedItems.Contains(item)) return;
        var payload = new CardFileDrag(Card.State.Id, FileList.SelectedItems.Cast<FileEntry>().Select(x => new CardDragEntry(x.FullPath, x.IsDirectory)));
        var host = Host; var result = DragDropEffects.None;
        try { e.Handled = true; result = DragDrop.DoDragDrop(FileList, payload.ToData(), DragDropEffects.Move | DragDropEffects.Copy); }
        catch (Exception error) { Workspace.Status = "파일 드래그 실패: " + error.Message; return; }
        finally { host.ClearFileTransferHints(); }
        // Do not display collision dialogs inside the native drag loop.
        if (result == DragDropEffects.None) { Workspace.Status = "파일 드래그를 취소했습니다. 원본은 변경하지 않았습니다."; return; }
        await payload.CompleteAsync(result, host.ViewModel.FileTasks);
    }
    private void FileTransferOver(object sender, DragEventArgs e)
    {
        if (CardFileDrag.Read(e.Data) is not { } payload) return;
        e.Handled = true;
        e.Effects = payload.Effect(Card.State.Id, Card.CurrentPath, e.KeyStates, e.AllowedEffects, Workspace.FileTasks.Busy);
        if (e.Effects == DragDropEffects.None) { ClearTransferHint(); Workspace.Status = "이 카드에는 놓을 수 없습니다. 다른 폴더 카드로 이동하세요."; return; }
        var copy = e.Effects == DragDropEffects.Copy;
        TransferHint.Text = $"{payload.Entries.Count}개 {(copy ? "복사" : "이동")} · {(copy ? "Ctrl을 놓으면 이동" : "Ctrl을 누르면 복사")}";
        TransferHint.ToolTip = Card.CurrentPath;
        TransferOverlay.Visibility = Visibility.Visible;
        Workspace.Status = $"{payload.Entries.Count}개 {(copy ? "복사" : "이동")} → {Card.CurrentPath} · 카드에 현재 열린 폴더로 작업 · Esc 취소";
        var scroll = FileContextMenus.Parent<ScrollViewer>(VisualTreeHelper.GetParent(this));
        if (scroll != null && (DateTime.UtcNow - lastTransferScroll).TotalMilliseconds >= 60)
        {
            var y = e.GetPosition(scroll).Y;
            if (y < 32) scroll.ScrollToVerticalOffset(scroll.VerticalOffset - 14);
            else if (y > scroll.ActualHeight - 32) scroll.ScrollToVerticalOffset(scroll.VerticalOffset + 14);
            lastTransferScroll = DateTime.UtcNow;
        }
    }
    private void FileTransferLeave(object sender, DragEventArgs e)
    {
        if (CardFileDrag.Read(e.Data) == null) return;
        var point = e.GetPosition(this);
        if (!new Rect(RenderSize).Contains(point)) ClearTransferHint();
    }
    private void FileTransferDrop(object sender, DragEventArgs e)
    {
        if (CardFileDrag.Read(e.Data) is not { } payload) return;
        e.Handled = true; ClearTransferHint();
        e.Effects = payload.Effect(Card.State.Id, Card.CurrentPath, e.KeyStates, e.AllowedEffects, Workspace.FileTasks.Busy);
        if (e.Effects != DragDropEffects.None) payload.Accepted = new(Card.CurrentPath, e.Effects == DragDropEffects.Copy);
    }
    internal void ClearTransferHint() => TransferOverlay.Visibility = Visibility.Collapsed;
}


