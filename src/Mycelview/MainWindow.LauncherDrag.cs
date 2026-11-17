using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Mycelview;
public partial class MainWindow
{
    const string LauncherOrderFormat="Mycelview.LauncherOrder";
    sealed record LauncherOrderDrag(string? Group,Guid? Item);
    Point launcherPress;
    FrameworkElement? launcherSource;
    bool launcherDragging;
    OrderLine? launcherLine;
    AdornerLayer? launcherLayer;
    private void LauncherOrderDown(object sender,MouseButtonEventArgs e)
    {
        if(e.ChangedButton!=MouseButton.Left)return;
        launcherSource=sender as FrameworkElement;
        launcherPress=e.GetPosition(this);
    }
    private void LauncherOrderMove(object sender,MouseEventArgs e)
    {
        if(launcherSource!=sender||e.LeftButton!=MouseButtonState.Pressed||launcherDragging)return;
        var p=e.GetPosition(this);
        if(Math.Abs(p.X-launcherPress.X)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y-launcherPress.Y)<SystemParameters.MinimumVerticalDragDistance)return;
        var source=launcherSource;
        LauncherOrderDrag? payload=source.DataContext switch{LauncherGroup g=>new(g.Kind,null),LauncherFavorite item=>new(null,item.Id),_=>null};
        if(payload==null)return;
        e.Handled=true;launcherDragging=true;launcherSource=null;
        Mouse.Capture(null);
        FrameworkElement visual=source;
        if(payload.Group!=null){
            for(var parent=VisualTreeHelper.GetParent(source);parent!=null;parent=VisualTreeHelper.GetParent(parent))
                if(parent is Border {DataContext:LauncherGroup} border){visual=border;break;}
        }
        var layer=AdornerLayer.GetAdornerLayer(WindowLayers);
        var ghost=new LauncherGhost(WindowLayers,visual){IsHitTestVisible=false};
        layer?.Add(ghost);
        var oldOpacity=visual.Opacity;visual.Opacity=0.3;
        void Follow(object s,GiveFeedbackEventArgs args){
            if(AppEdgeSwitcher.GetCursorPos(out var point))ghost.Move(PointFromScreen(new Point(point.X,point.Y)));
            args.UseDefaultCursors=true;
        }
        source.GiveFeedback+=Follow;
        try{DragDrop.DoDragDrop(source,new DataObject(LauncherOrderFormat,payload),DragDropEffects.Move);}
        finally{source.GiveFeedback-=Follow;layer?.Remove(ghost);visual.Opacity=oldOpacity;ClearLauncherLine();Dispatcher.BeginInvoke(new Action(()=>launcherDragging=false));}
    }
    private void LauncherOrderUp(object sender,MouseButtonEventArgs e){launcherSource=null;}
    void ClearLauncherLine(){if(launcherLine!=null)launcherLayer?.Remove(launcherLine);launcherLine=null;launcherLayer=null;}
    private void LauncherOrderLeave(object sender,DragEventArgs e){ClearLauncherLine();}
    (FrameworkElement? Element,string? Group,LauncherFavorite? Item,bool After) LauncherOrderTarget(DragEventArgs e,LauncherOrderDrag drag)
    {
        FrameworkElement? itemElement=null,groupElement=null;
        for(var current=e.OriginalSource as DependencyObject;current!=null;current=VisualTreeHelper.GetParent(current)){
            if(current is Button {DataContext:LauncherFavorite} button && itemElement==null)itemElement=button;
            if(current is Border {DataContext:LauncherGroup} border){groupElement=border;break;}
        }
        if(groupElement?.DataContext is not LauncherGroup group)return default;
        if(drag.Group!=null)return(groupElement,group.Kind,null,e.GetPosition(groupElement).X>=groupElement.ActualWidth/2);
        var moving=ViewModel.LauncherFavorites.FirstOrDefault(x=>x.Id==drag.Item);
        if(moving==null||moving.Kind!=group.Kind)return default;
        if(itemElement?.DataContext is LauncherFavorite item)return(itemElement,group.Kind,item,e.GetPosition(itemElement).X>=itemElement.ActualWidth/2);
        return(groupElement,group.Kind,group.Items.LastOrDefault(),true);
    }
    bool HandleLauncherOrder(DragEventArgs e,bool drop)
    {
        if(e.Data.GetData(LauncherOrderFormat) is not LauncherOrderDrag drag)return false;
        e.Handled=true;e.Effects=DragDropEffects.None;
        var target=LauncherOrderTarget(e,drag);ClearLauncherLine();
        if(target.Element==null)return true;
        e.Effects=DragDropEffects.Move;
        if(drop){
            bool changed=drag.Group!=null?ViewModel.InsertLauncherGroup(drag.Group,target.Group!,target.After):
                target.Item!=null && ViewModel.InsertLauncherItem(drag.Item!.Value,target.Item.Id,target.After);
            if(changed)ViewModel.Save();
        }else{
            launcherLayer=AdornerLayer.GetAdornerLayer(target.Element);
            if(launcherLayer!=null){launcherLine=new OrderLine(target.Element,target.After);launcherLayer.Add(launcherLine);}
            var scroll=FileContextMenus.Parent<ScrollViewer>(target.Element);
            if(scroll!=null){double x=e.GetPosition(scroll).X;if(x<28)scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset-16);else if(x>scroll.ActualWidth-28)scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset+16);}
        }
        return true;
    }
    sealed class LauncherGhost : Adorner
    {
        readonly BitmapSource snapshot;
        readonly double width,height;
        Point position;
        internal LauncherGhost(UIElement root,FrameworkElement source):base(root)
        {
            width=Math.Max(1,source.ActualWidth);height=Math.Max(1,source.ActualHeight);
            var dpi=VisualTreeHelper.GetDpi(source);
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(width*dpi.DpiScaleX),(int)Math.Ceiling(height*dpi.DpiScaleY),96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);
            bitmap.Render(source);bitmap.Freeze();snapshot=bitmap;
        }
        internal void Move(Point point){position=new(point.X+12,point.Y+12);InvalidateVisual();}
        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(45,0,0,0)),null,new Rect(position.X+3,position.Y+4,width,height),7,7);
            dc.PushOpacity(0.94);dc.DrawImage(snapshot,new Rect(position.X,position.Y,width,height));dc.Pop();
        }
    }
    sealed class OrderLine(FrameworkElement element,bool after):Adorner(element)
    {
        protected override void OnRender(DrawingContext dc)
        {
            IsHitTestVisible=false;
            double x=after?AdornedElement.RenderSize.Width:0;
            dc.DrawLine(new Pen(Brushes.DodgerBlue,2),new Point(x,0),new Point(x,AdornedElement.RenderSize.Height));
        }
    }
}

