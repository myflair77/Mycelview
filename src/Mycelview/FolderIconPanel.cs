using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Mycelview;

// Realize only the visible icon cells, even for directories with many thousands of files.
internal sealed class FolderIconPanel : VirtualizingPanel, IScrollInfo
{
    internal double CellWidth = 120, CellHeight = 100;
    internal bool ColumnFlow;
    internal string GroupMode="";
    Rect[] groupedCells=[];
    readonly List<(double Y,string Label)> headers=[];
    bool groupsDirty=true;
    string lastGroup="";
    int lastLanes;
    double lastCellWidth,lastCellHeight,groupedHeight;
    internal int GroupCount=>headers.Count;
    void BuildGroups()
    {
        if(GroupMode==""){groupedCells=[];headers.Clear();lastGroup="";return;}
        if(!groupsDirty&&lastGroup==GroupMode&&lastLanes==lanes&&lastCellWidth==CellWidth&&lastCellHeight==CellHeight&&groupedCells.Length==Count)return;
        groupsDirty=false;lastGroup=GroupMode;lastLanes=lanes;lastCellWidth=CellWidth;lastCellHeight=CellHeight;
        groupedCells=new Rect[Count];headers.Clear();
        var owner=ItemsControl.GetItemsOwner(this)!;
        double y=0;int start=0;
        while(start<Count)
        {
            string label=FolderGrouping.Key((FileEntry)owner.Items[start],GroupMode).Label;
            int end=start+1;
            while(end<Count&&FolderGrouping.Key((FileEntry)owner.Items[end],GroupMode).Label==label)end++;
            headers.Add((y,$"{label} ({end-start:N0})"));y+=30;
            for(int i=start;i<end;i++)groupedCells[i]=new Rect((i-start)%lanes*CellWidth,y+(i-start)/lanes*CellHeight,CellWidth,CellHeight);
            y+=Math.Ceiling((end-start)/(double)lanes)*CellHeight+8;start=end;
        }
        groupedHeight=y;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if(GroupMode=="")return;
        var owner=ItemsControl.GetItemsOwner(this);
        var color=owner?.Foreground??Brushes.DimGray;
        dc.PushClip(new RectangleGeometry(new Rect(viewport)));
        foreach(var header in headers)
        {
            double y=header.Y-offset.Y;
            if(y+30<0||y>viewport.Height)continue;
            var text=new FormattedText(header.Label,System.Globalization.CultureInfo.CurrentCulture,FlowDirection.LeftToRight,
                new Typeface(owner?.FontFamily??new FontFamily("Segoe UI"),FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal),
                owner?.FontSize??12,color,VisualTreeHelper.GetDpi(this).PixelsPerDip){MaxTextWidth=Math.Max(1,viewport.Width-12),Trimming=TextTrimming.CharacterEllipsis};
            dc.DrawText(text,new Point(6,y+5));
            dc.DrawLine(new Pen(Brushes.LightGray,1),new Point(6,y+28),new Point(Math.Max(6,viewport.Width-6),y+28));
        }
        dc.Pop();
    }
    int lanes = 1;
    internal int Lanes => lanes;
    internal void Reveal(int index) => BringIndexIntoView(index);
    Size extent, viewport;
    Point offset;
    int Count => ItemsControl.GetItemsOwner(this)?.Items.Count ?? 0;
    internal Rect Cell(int index) => GroupMode!="" && index>=0 && index<groupedCells.Length ? groupedCells[index] : ColumnFlow
        ? new Rect(index / lanes * CellWidth, index % lanes * CellHeight, CellWidth, CellHeight)
        : new Rect(index % lanes * CellWidth, index / lanes * CellHeight, CellWidth, CellHeight);
    internal IEnumerable<int> Intersecting(Rect rectangle)
    {
        if(GroupMode!="")
        {
            for(int i=0;i<groupedCells.Length;i++)
            {
                if(groupedCells[i].Top>rectangle.Bottom)break;
                if(groupedCells[i].IntersectsWith(rectangle))yield return i;
            }
            yield break;
        }
        int firstLine=Math.Max(0,(int)Math.Floor((ColumnFlow?rectangle.Left:rectangle.Top)/(ColumnFlow?CellWidth:CellHeight)));
        int lastLine=Math.Min((Count+lanes-1)/lanes-1,(int)Math.Floor((ColumnFlow?rectangle.Right:rectangle.Bottom)/(ColumnFlow?CellWidth:CellHeight)));
        int firstLane=Math.Max(0,(int)Math.Floor((ColumnFlow?rectangle.Top:rectangle.Left)/(ColumnFlow?CellHeight:CellWidth)));
        int lastLane=Math.Min(lanes-1,(int)Math.Floor((ColumnFlow?rectangle.Bottom:rectangle.Right)/(ColumnFlow?CellHeight:CellWidth)));
        for(int line=firstLine;line<=lastLine;line++)for(int lane=firstLane;lane<=lastLane;lane++)
        {
            int index=line*lanes+lane;
            if(index<Count && rectangle.IntersectsWith(Cell(index)))yield return index;
        }
    }    protected override Size MeasureOverride(Size available)
    {
        viewport = new Size(double.IsInfinity(available.Width) ? 600 : Math.Max(1, available.Width), double.IsInfinity(available.Height) ? 400 : Math.Max(1, available.Height));
        lanes = Math.Max(1, (int)((ColumnFlow ? viewport.Height : viewport.Width) / (ColumnFlow ? CellHeight : CellWidth)));
        BuildGroups();
        int lines = (int)Math.Ceiling(Count / (double)lanes);
        extent = ColumnFlow ? new Size(Math.Max(viewport.Width, lines * CellWidth), viewport.Height) : new Size(viewport.Width, Math.Max(viewport.Height, GroupMode!=""?groupedHeight:lines * CellHeight));
        offset.X = Math.Clamp(offset.X, 0, Math.Max(0, extent.Width - viewport.Width));
        offset.Y = Math.Clamp(offset.Y, 0, Math.Max(0, extent.Height - viewport.Height));
        ScrollOwner?.InvalidateScrollInfo();
        int first = Math.Max(0, (int)((ColumnFlow ? offset.X : offset.Y) / (ColumnFlow ? CellWidth : CellHeight)) * lanes);
        int last = Math.Min(Count - 1, first + ((int)Math.Ceiling((ColumnFlow ? viewport.Width : viewport.Height) / (ColumnFlow ? CellWidth : CellHeight)) + 2) * lanes - 1);
        if(GroupMode!="")
        {
            first=0;while(first<Count&&groupedCells[first].Bottom<offset.Y)first++;
            last=first-1;while(last+1<Count&&groupedCells[last+1].Top<=offset.Y+viewport.Height+CellHeight)last++;
        }
        InvalidateVisual();
        _ = InternalChildren; // Initialize the items host before acquiring its generator.
        var generator = ItemContainerGenerator;
        for (int i = InternalChildren.Count - 1; i >= 0; i--)
        {
            int item = generator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            if (item < first || item > last) { generator.Remove(new GeneratorPosition(i, 0), 1); RemoveInternalChildRange(i, 1); }
        }
        if (last >= first)
        {
            var position = generator.GeneratorPositionFromIndex(first);
            int childIndex = position.Offset == 0 ? position.Index : position.Index + 1;
            using (generator.StartAt(position, GeneratorDirection.Forward, true))
                for (int i = first; i <= last; i++, childIndex++)
                {
                    var child = (UIElement)generator.GenerateNext(out bool created);
                    if (created)
                    {
                        if (childIndex >= InternalChildren.Count) AddInternalChild(child); else InsertInternalChild(childIndex, child);
                        generator.PrepareItemContainer(child);
                    }
                    child.Measure(new Size(CellWidth, CellHeight));
                }
        }
        return viewport;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            int index = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            var rect = Cell(index); rect.Offset(-offset.X, -offset.Y);
            InternalChildren[i].Arrange(rect);
        }
        return finalSize;
    }
    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        base.OnItemsChanged(sender, args);groupsDirty=true;
        if(args.Action==System.Collections.Specialized.NotifyCollectionChangedAction.Reset) RemoveInternalChildRange(0,InternalChildren.Count);
        if (args.Action is System.Collections.Specialized.NotifyCollectionChangedAction.Remove or System.Collections.Specialized.NotifyCollectionChangedAction.Replace or System.Collections.Specialized.NotifyCollectionChangedAction.Move)
            RemoveInternalChildRange(args.Position.Index, args.ItemUICount);
        InvalidateMeasure();
    }
    protected override void BringIndexIntoView(int index)
    {
        if (index < 0 || index >= Count) return;
        var r = Cell(index);
        if (ColumnFlow) SetHorizontalOffset(r.Left < offset.X ? r.Left : Math.Max(offset.X, r.Right - viewport.Width));
        else SetVerticalOffset(r.Top < offset.Y ? r.Top : Math.Max(offset.Y, r.Bottom - viewport.Height));
    }
    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        var child = visual as DependencyObject;
        while (child != null && VisualTreeHelper.GetParent(child) != this) child = VisualTreeHelper.GetParent(child);
        if (child is UIElement element)
        {
            int index = InternalChildren.IndexOf(element);
            if (index >= 0) BringIndexIntoView(ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(index, 0)));
        }
        return rectangle;
    }
    public void SetHorizontalOffset(double value) { offset.X = Math.Clamp(value, 0, Math.Max(0, extent.Width - viewport.Width)); InvalidateMeasure(); ScrollOwner?.InvalidateScrollInfo(); }
    public void SetVerticalOffset(double value) { offset.Y = Math.Clamp(value, 0, Math.Max(0, extent.Height - viewport.Height)); InvalidateMeasure(); ScrollOwner?.InvalidateScrollInfo(); }
    public void LineUp() => SetVerticalOffset(offset.Y - CellHeight);
    public void LineDown() => SetVerticalOffset(offset.Y + CellHeight);
    public void LineLeft() => SetHorizontalOffset(offset.X - CellWidth);
    public void LineRight() => SetHorizontalOffset(offset.X + CellWidth);
    public void MouseWheelUp() { if (ColumnFlow) LineLeft(); else SetVerticalOffset(offset.Y - CellHeight * 3); }
    public void MouseWheelDown() { if (ColumnFlow) LineRight(); else SetVerticalOffset(offset.Y + CellHeight * 3); }
    public void MouseWheelLeft() => LineLeft();
    public void MouseWheelRight() => LineRight();
    public void PageUp() => SetVerticalOffset(offset.Y - viewport.Height);
    public void PageDown() => SetVerticalOffset(offset.Y + viewport.Height);
    public void PageLeft() => SetHorizontalOffset(offset.X - viewport.Width);
    public void PageRight() => SetHorizontalOffset(offset.X + viewport.Width);
    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; }
    public double ExtentWidth => extent.Width;
    public double ExtentHeight => extent.Height;
    public double ViewportWidth => viewport.Width;
    public double ViewportHeight => viewport.Height;
    public double HorizontalOffset => offset.X;
    public double VerticalOffset => offset.Y;
    public ScrollViewer? ScrollOwner { get; set; }
}
