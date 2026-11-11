using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace PinShelf;
public partial class MainWindow
{
    private Point reorderStart;
    private Vector? reorderGrab;
    private CardViewModel? reorderCard;
    private ReorderDragSession? reorderSession;
    private readonly DispatcherTimer reorderScroll = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private bool reorderValid;
    private void InitializeReordering()
    {
        PreviewMouseMove += ReorderMouseMove;
        PreviewMouseLeftButtonUp += (_, e) => { if (reorderCard != null) { EndReorder(reorderValid); e.Handled = true; } };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && reorderCard != null) { EndReorder(false); e.Handled = true; } };
        LostMouseCapture += (_, _) => { if (reorderCard != null && Mouse.Captured != this) EndReorder(false); };
        Deactivated += (_, _) => EndReorder(false);
        Closing += (_, _) => EndReorder(false);
        reorderScroll.Tick += (_, _) => reorderSession?.ScrollAtEdge();
    }
    internal void ReorderStart(object sender, MouseButtonEventArgs e) => ReorderStart(sender,e,null);
    internal void ReorderStart(object sender, MouseButtonEventArgs e, Vector? grab)
    {
        if (((FrameworkElement)sender).DataContext is not CardViewModel card) return;
        EndReorder(false);
        reorderGrab=grab;
        reorderStart = e.GetPosition(this); reorderCard = card; reorderValid = false;
        if (!Mouse.Capture(this, CaptureMode.Element)) { reorderCard = null; return; }
        e.Handled = true;
    }
    private void ReorderMouseMove(object sender, MouseEventArgs e)
    {
        if (reorderCard == null) return;
        if (e.LeftButton != MouseButtonState.Pressed) { EndReorder(false); return; }
        var point = e.GetPosition(this); var delta = point - reorderStart;
        if (reorderSession == null)
        {
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            reorderSession = new(this, reorderCard.IsFolder ? FolderCards : FavoritesList, reorderCard, reorderStart, reorderGrab);
            Cursor = Cursors.SizeAll; reorderScroll.Start();
        }
        reorderValid = reorderSession.Move(point);
        e.Handled = true;
    }
    private void EndReorder(bool commit)
    {
        var session = reorderSession; reorderSession = null; reorderCard = null;
        reorderScroll.Stop(); ClearValue(CursorProperty);
        if (Mouse.Captured == this) Mouse.Capture(null);
        if (session?.Finish(commit) == true) ViewModel.Status = "표시 순서를 변경했습니다.";
    }
    private void ShowColumnsMenu(object sender, RoutedEventArgs e)
    { var button = (Button)sender; button.ContextMenu.DataContext = ViewModel; button.ContextMenu.PlacementTarget = button; button.ContextMenu.IsOpen = true; }
}


