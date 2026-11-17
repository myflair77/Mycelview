using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Mycelview;
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
        if (!FileList.SelectedItems.Contains(item) || FileList.SelectedItems.Cast<FileEntry>().Any(x => x.IsShellItem)) return;
        var payload = new CardFileDrag(Card.State.Id, FileList.SelectedItems.Cast<FileEntry>().Select(x => new CardDragEntry(x.FullPath, x.IsDirectory)));
        var host = Host; var result = DragDropEffects.None;
        try { e.Handled = true; result = DragDrop.DoDragDrop(FileList, payload.ToData(), DragDropEffects.Move | DragDropEffects.Copy); }
        catch (Exception error) { Workspace.Status = "파일 드래그 실패: " + error.Message; return; }
        finally { host.ClearFileTransferHints(); }
        // Do not display collision dialogs inside the native drag loop.
        if (result == DragDropEffects.None) { Workspace.Status = "파일 드래그를 취소했습니다. 원본은 변경하지 않았습니다."; return; }
        await payload.CompleteAsync(result, host.ViewModel.FileTasks);
    }
    private string TransferDestination(DragEventArgs e)
    {
        var source=e.OriginalSource as DependencyObject;
        var row=FileContextMenus.Parent<DataGridRow>(source);
        var icon=FileContextMenus.Parent<ListBoxItem>(source);
        var entry=row?.Item as FileEntry ?? icon?.DataContext as FileEntry;
        return entry is { IsDirectory:true, IsShellItem:false } ? entry.FullPath : Card.CurrentPath;
    }
    private void FileTransferOver(object sender, DragEventArgs e)
    {
        var destination=TransferDestination(e);
        if (CardFileDrag.Read(e.Data) is not { } payload)
        {
            if(!e.Data.GetDataPresent(DataFormats.FileDrop))return;
            e.Handled=true;
            var paths=ExternalFileDrop.Paths(e.Data);
            e.Effects=ExternalFileDrop.Effect(paths,destination,e.KeyStates,e.AllowedEffects,Workspace.FileTasks.Busy);
            if(e.Effects==DragDropEffects.None){ClearTransferHint();return;}
            TransferHint.Text=$"{paths.Length}개 {(e.Effects==DragDropEffects.Copy?"복사":"이동")} · Ctrl: 복사 · Shift: 이동";
            TransferHint.ToolTip=destination;TransferHint.Text+=" → "+System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(destination));TransferOverlay.Visibility=Visibility.Visible;
            Workspace.Status=TransferHint.Text+" → "+destination;
            return;
        }
        e.Handled = true;
        e.Effects = payload.Effect(Card.State.Id, destination, e.KeyStates, e.AllowedEffects, Workspace.FileTasks.Busy);
        if (e.Effects == DragDropEffects.None) { ClearTransferHint(); Workspace.Status = "이 위치에는 놓을 수 없습니다."; return; }
        var copy = e.Effects == DragDropEffects.Copy;
        TransferHint.Text = $"{payload.Entries.Count}개 {(copy ? "복사" : "이동")} · {(copy ? "Ctrl을 놓으면 이동" : "Ctrl을 누르면 복사")}";
        TransferHint.ToolTip = destination;
        TransferHint.Text += " → " + System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(destination));
        TransferOverlay.Visibility = Visibility.Visible;
        Workspace.Status = $"{payload.Entries.Count}개 {(copy ? "복사" : "이동")} → {destination} · Esc 취소";
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
        if (CardFileDrag.Read(e.Data) == null && !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var point = e.GetPosition(this);
        if (!new Rect(RenderSize).Contains(point)) ClearTransferHint();
    }
    private async void FileTransferDrop(object sender, DragEventArgs e)
    {
        var destination=TransferDestination(e);
        if (CardFileDrag.Read(e.Data) is not { } payload)
        {
            if(!e.Data.GetDataPresent(DataFormats.FileDrop))return;
            e.Handled=true;
            var paths=ExternalFileDrop.Paths(e.Data);
            e.Effects=ExternalFileDrop.Effect(paths,destination,e.KeyStates,e.AllowedEffects,Workspace.FileTasks.Busy);
            if(e.Effects==DragDropEffects.None){ClearTransferHint();return;}
            var copy=e.Effects==DragDropEffects.Copy;
            ClearTransferHint();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            await Workspace.FileTasks.TransferAsync(paths,destination,copy);
            return;
        }
        e.Handled = true; ClearTransferHint();
        e.Effects = payload.Effect(Card.State.Id, destination, e.KeyStates, e.AllowedEffects, Workspace.FileTasks.Busy);
        if (e.Effects != DragDropEffects.None) payload.Accepted = new(destination, e.Effects == DragDropEffects.Copy);
    }
    internal void ClearTransferHint() => TransferOverlay.Visibility = Visibility.Collapsed;
}


