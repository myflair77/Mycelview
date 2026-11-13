using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Mycelview;
public partial class MainWindow
{
    private sealed record TabSlot(WorkspaceTab Tab,FrameworkElement Element,double Left,double Width,TranslateTransform Motion);
    private List<TabSlot>? tabSlots;
    private int tabFrom,tabTarget;
    private Point tabStart;
    private bool tabMoved,tabInside;
    private void InitializeWorkspaceDragging()
    {
        PreviewMouseMove+=MoveSmoothTabDrag;
        PreviewMouseLeftButtonUp+=(_,e)=>{if(tabSlots!=null){FinishSmoothTabDrag(tabMoved&&tabInside);e.Handled=true;}};
        PreviewKeyDown+=(_,e)=>{if(tabSlots!=null&&e.Key==Key.Escape){FinishSmoothTabDrag(false);e.Handled=true;}};
        LostMouseCapture+=(_,_)=>{if(tabSlots!=null&&Mouse.Captured!=this)FinishSmoothTabDrag(false);};
        Deactivated+=(_,_)=>FinishSmoothTabDrag(false);
        Closing+=(_,_)=>FinishSmoothTabDrag(false);
        SizeChanged+=(_,_)=>FinishSmoothTabDrag(false);
    }
    private void StartSmoothTabDrag(object sender,MouseButtonEventArgs e)
    {
        if(TabFrom(sender) is not {} tab || ViewModel.FileTasks.Busy || ViewModel.WorkspaceBusy)return;
        FinishSmoothTabDrag(false);CloseCardEnlargement();UpdateLayout();
        var slots=new List<TabSlot>();
        foreach(var item in ViewModel.Workspaces)
        {
            if(WorkspaceTabs.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement element)return;
            var motion=new TranslateTransform();element.RenderTransform=motion;
            slots.Add(new(item,element,element.TranslatePoint(new Point(),this).X,element.ActualWidth,motion));
        }
        tabFrom=tabTarget=slots.FindIndex(x=>x.Tab==tab);tabStart=e.GetPosition(this);tabMoved=false;tabInside=true;tabSlots=slots;
        if(!Mouse.Capture(this,CaptureMode.Element)){tabSlots=null;return;}
        e.Handled=true;
    }
    private void MoveSmoothTabDrag(object sender,MouseEventArgs e)
    {
        if(tabSlots is not {} slots)return;
        if(e.LeftButton!=MouseButtonState.Pressed){FinishSmoothTabDrag(false);return;}
        var point=e.GetPosition(this);double dx=point.X-tabStart.X;
        if(!tabMoved && Math.Abs(dx)<SystemParameters.MinimumHorizontalDragDistance)return;
        tabMoved=true;
        var viewport=new Rect(WorkspaceTabScroll.TranslatePoint(new Point(),this),WorkspaceTabScroll.RenderSize);viewport.Inflate(0,15);tabInside=viewport.Contains(point);
        int next=tabFrom;
        for(int i=0;i<slots.Count;i++)
        {
            if(i<tabFrom && point.X<slots[i].Left+slots[i].Width/2){next=i;break;}
            if(i>tabFrom && point.X>slots[i].Left+slots[i].Width/2)next=i;
        }
        if(tabInside&&next!=tabTarget)
        {
            tabTarget=next;var ordered=slots.ToList();var source=ordered[tabFrom];ordered.RemoveAt(tabFrom);ordered.Insert(tabTarget,source);
            double x=slots[0].Left;
            foreach(var item in ordered){if(item!=source)ReorderAnimation.Move(item.Motion,item.Motion.X,0,x-item.Left,0);x+=item.Width;}
        }
        var dragged=slots[tabFrom];Panel.SetZIndex(dragged.Element,10);dragged.Element.Opacity=.85;dragged.Motion.X=dx;e.Handled=true;
    }
    private async void FinishSmoothTabDrag(bool commit)
    {
        if(tabSlots is not {} slots)return;
        int from=tabFrom,to=tabTarget;tabSlots=null;
        foreach(var item in slots){item.Motion.BeginAnimation(TranslateTransform.XProperty,null);item.Motion.X=0;item.Element.Opacity=1;Panel.SetZIndex(item.Element,0);}
        if(Mouse.Captured==this)Mouse.Capture(null);
        if(commit&&from!=to)await WorkspaceAction(()=>{ViewModel.ReorderWorkspace(slots[from].Tab.Id,slots[to].Tab.Id,to>from);return Task.CompletedTask;});
    }
}
