using System.Windows;
using System.Windows.Controls;
namespace Mycelview;
public sealed class FolderGridPanel : Panel
{
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(nameof(Columns),typeof(int),typeof(FolderGridPanel),new FrameworkPropertyMetadata(2,FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));
    public int Columns { get => Math.Clamp((int)GetValue(ColumnsProperty),1,5); set => SetValue(ColumnsProperty,value); }
    private readonly ReorderAnimation animation = new();
    internal CardViewModel? ExpandedCard { get; set; }
    internal static readonly DependencyProperty ExpandedWidthProperty=DependencyProperty.Register(nameof(ExpandedWidth),typeof(double),typeof(FolderGridPanel),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsMeasure));
    internal double ExpandedWidth { get => (double)GetValue(ExpandedWidthProperty); set => SetValue(ExpandedWidthProperty,value); }
    private int ExpandedInPair(int i) => Enumerable.Range(i,Math.Min(Columns,InternalChildren.Count-i)).FirstOrDefault(j=>InternalChildren[j] is FrameworkElement f && ReferenceEquals(f.DataContext,ExpandedCard) && ExpandedCard!=null,-1);
    private double CellWidth(double width,int expanded,int index,int count)
    {
        if(expanded<0) return width/Columns;
        if(count==1) return width;
        var large=Math.Clamp(ExpandedWidth,width/Columns,Math.Max(width/Columns,width-(count-1)*Math.Min(100,width/Columns)));
        return index==expanded ? large : (width-large)/(count-1);
    }
    protected override Size MeasureOverride(Size available)
    {
        var width=double.IsInfinity(available.Width)?800:available.Width;double height=0;
        for(int i=0;i<InternalChildren.Count;i+=Columns)
        {
            int count=Math.Min(Columns,InternalChildren.Count-i), expanded=ExpandedInPair(i);double row=0;
            for(int j=i;j<i+count;j++){var child=InternalChildren[j];child.Measure(new Size(CellWidth(width,expanded,j,count),double.PositiveInfinity));row=Math.Max(row,child.DesiredSize.Height);}
            height+=row;
        }
        return new Size(width,height);
    }
    protected override Size ArrangeOverride(Size size)
    {
        double y=0;
        for(int i=0;i<InternalChildren.Count;i+=Columns)
        {
            int count=Math.Min(Columns,InternalChildren.Count-i),expanded=ExpandedInPair(i);double x=0,row=0;
            for(int j=i;j<i+count;j++)
            {
                var child=InternalChildren[j];double width=CellWidth(size.Width,expanded,j,count);
                child.Arrange(new Rect(x,y,width,child.DesiredSize.Height));x+=width;row=Math.Max(row,child.DesiredSize.Height);
            }
            y+=row;
        }
        animation.Arrange(InternalChildren);return size;
    }
}


