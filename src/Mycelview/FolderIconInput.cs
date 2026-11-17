using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
namespace Mycelview;
internal sealed class FolderIconInput
{
    readonly ListBox list;
    readonly DispatcherTimer timer;
    Point start;
    FileEntry? drag;
    int keyboardAnchor=-1;
    bool selecting, deferred, toggle;
    HashSet<object> initial=[], beforeSelection=[];
    AdornerLayer? layer;
    Box? box;
    internal FolderIconInput(ListBox list)
    {
        this.list=list;
        timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(40)};
        timer.Tick+=(_,_)=>Update(true);
        list.PreviewMouseLeftButtonDown+=(_,e)=>
        {
            if(FileContextMenus.Parent<TextBox>(e.OriginalSource as DependencyObject)!=null)return;
            if(e.ClickCount!=1 || FileContextMenus.Parent<ScrollBar>(e.OriginalSource as DependencyObject)!=null)return;
            start=e.GetPosition(list);
            drag=FileContextMenus.Parent<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as FileEntry;
            if(drag!=null)
            {
                deferred=list.SelectedItems.Contains(drag) && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Control;
                toggle=Keyboard.Modifiers==ModifierKeys.Control;
                if(deferred){list.Focus();e.Handled=true;}
                return;
            }
            var panel=FolderViewModes.Find<FolderIconPanel>(list);
            layer=AdornerLayer.GetAdornerLayer(list);
            if(panel==null || layer==null)return;
            beforeSelection=list.SelectedItems.Cast<object>().ToHashSet();
            initial=(Keyboard.Modifiers&(ModifierKeys.Control|ModifierKeys.Shift))!=0?list.SelectedItems.Cast<object>().ToHashSet():[];
            start.Offset(panel.HorizontalOffset,panel.VerticalOffset);
            list.Focus();selecting=list.CaptureMouse();
            if(!selecting)return;
            box=new Box(list){IsHitTestVisible=false};layer.Add(box);timer.Start();e.Handled=true;
            Update(false);
        };
        list.PreviewMouseMove+=async(_,e)=>
        {
            if(selecting){Update(false);e.Handled=true;return;}
            if(drag==null || e.LeftButton!=MouseButtonState.Pressed)return;
            var delta=e.GetPosition(list)-start;
            if(Math.Abs(delta.X)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y)<SystemParameters.MinimumVerticalDragDistance)return;
            var item=drag;drag=null;deferred=false;
            if(Window.GetWindow(list) is not MainWindow host || list.DataContext is not CardViewModel card || host.ViewModel.FileTasks.Busy || item.IsShellItem)return;
            var entries=list.SelectedItems.OfType<FileEntry>().ToArray();
            if(entries.Any(x=>x.IsShellItem))return;
            var payload=new CardFileDrag(card.State.Id,entries.Select(x=>new CardDragEntry(x.FullPath,x.IsDirectory)));
            try
            {
                var effect=DragDrop.DoDragDrop(list,payload.ToData(),DragDropEffects.Copy|DragDropEffects.Move);
                await payload.CompleteAsync(effect,host.ViewModel.FileTasks);
            }
            catch(Exception error){host.ViewModel.Status="파일 드래그 실패: "+error.Message;}
            finally{host.ClearFileTransferHints();}
        };
        list.PreviewMouseLeftButtonUp+=(_,e)=>
        {
            if(selecting){Update(false);End();e.Handled=true;}
            else if(deferred && drag!=null)
            {
                if(toggle)list.SelectedItems.Remove(drag);else {list.SelectedItems.Clear();list.SelectedItems.Add(drag);}
                e.Handled=true;
            }
            drag=null;deferred=false;
        };
        list.LostMouseCapture+=(_,_)=>End();
        list.Unloaded+=(_,_)=>End();
        list.PreviewKeyDown+=(_,e)=>
        {
            if(FileContextMenus.Parent<TextBox>(e.OriginalSource as DependencyObject)!=null)return;
            if(e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End && FolderViewModes.Find<FolderIconPanel>(list) is {} panel && list.Items.Count>0)
            {
                var focused=FileContextMenus.Parent<ListBoxItem>(Keyboard.FocusedElement as DependencyObject);
                int current=focused==null?Math.Max(0,list.SelectedIndex):Math.Max(0,list.ItemContainerGenerator.IndexFromContainer(focused));
                int step=e.Key switch {Key.Left=>panel.ColumnFlow?-panel.Lanes:-1,Key.Right=>panel.ColumnFlow?panel.Lanes:1,Key.Up=>panel.ColumnFlow?-1:-panel.Lanes,Key.Down=>panel.ColumnFlow?1:panel.Lanes,_=>0};
                int next=e.Key==Key.Home?0:e.Key==Key.End?list.Items.Count-1:Math.Clamp(current+step,0,list.Items.Count-1);
                if((Keyboard.Modifiers&ModifierKeys.Shift)!=0)
                {
                    if(keyboardAnchor<0)keyboardAnchor=current;
                    list.SelectedItems.Clear();
                    for(int i=Math.Min(keyboardAnchor,next);i<=Math.Max(keyboardAnchor,next);i++)list.SelectedItems.Add(list.Items[i]);
                }
                else if((Keyboard.Modifiers&ModifierKeys.Control)==0) {keyboardAnchor=next;list.SelectedItem=list.Items[next];}
                panel.Reveal(next);list.UpdateLayout();
                (list.ItemContainerGenerator.ContainerFromIndex(next) as ListBoxItem)?.Focus();
                e.Handled=true;
            }
            if(e.Key==Key.Escape && selecting){list.SelectedItems.Clear();foreach(var item in beforeSelection)list.SelectedItems.Add(item);End();e.Handled=true;}
            if((Keyboard.Modifiers==ModifierKeys.Control && e.Key==Key.A)){list.SelectAll();e.Handled=true;}
        };
    }
    void Update(bool scroll)
    {
        if(!selecting || box==null || FolderViewModes.Find<FolderIconPanel>(list) is not {} panel)return;
        var point=Mouse.GetPosition(list);
        if(scroll)
        {
            if(panel.ColumnFlow){if(point.X<20)panel.LineLeft();else if(point.X>list.ActualWidth-20)panel.LineRight();}
            else {if(point.Y<20)panel.LineUp();else if(point.Y>list.ActualHeight-20)panel.LineDown();}
            list.UpdateLayout();
        }
        var current=point;current.Offset(panel.HorizontalOffset,panel.VerticalOffset);
        var rect=new Rect(start,current);
        var chosen=new HashSet<object>(initial);
        foreach(int i in panel.Intersecting(rect))chosen.Add(list.Items[i]);
        foreach(var item in list.SelectedItems.Cast<object>().ToArray())if(!chosen.Contains(item))list.SelectedItems.Remove(item);
        foreach(var item in chosen)if(!list.SelectedItems.Contains(item))list.SelectedItems.Add(item);
        rect.Offset(-panel.HorizontalOffset,-panel.VerticalOffset);
        box.Bounds=Rect.Intersect(rect,new Rect(0,0,list.ActualWidth,list.ActualHeight));box.InvalidateVisual();
    }
    void End()
    {
        selecting=false;timer.Stop();if(box!=null)layer?.Remove(box);box=null;layer=null;initial.Clear();beforeSelection.Clear();
        if(list.IsMouseCaptured)list.ReleaseMouseCapture();
        drag=null;deferred=false;
    }
    sealed class Box(UIElement owner):Adorner(owner)
    {
        internal Rect Bounds;
        protected override void OnRender(DrawingContext dc){if(!Bounds.IsEmpty)dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(40,0,120,215)),new Pen(Brushes.DodgerBlue,1),Bounds);}
    }
}
