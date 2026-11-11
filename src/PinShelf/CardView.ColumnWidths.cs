using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
namespace PinShelf;
public partial class CardView
{
    private Dictionary<DataGridColumn,double>? resizeStart;
    private void InitializeColumnWidths()
    {
        FileList.AddHandler(Thumb.DragStartedEvent,new DragStartedEventHandler((sender,e)=>
        {
            if(FindParent<DataGridColumnHeader>(e.OriginalSource as DependencyObject)==null)return;
            resizeStart=FileList.Columns.ToDictionary(c=>c,c=>c.ActualWidth);
        }),true);
        FileList.AddHandler(Thumb.DragCompletedEvent,new DragCompletedEventHandler((sender,e)=>
        {
            if(resizeStart==null)return;
            if(!e.Canceled && DataContext is CardViewModel card && (Window.GetWindow(this) as MainWindow)?.IsCardEnlarged(this)!=true)
            {
                card.State.ColumnWidths ??= new();
                foreach(var column in FileList.Columns)
                    if(resizeStart.TryGetValue(column,out var before) && Math.Abs(before-column.ActualWidth)>.5 && column.Width.IsAbsolute)
                        card.State.ColumnWidths[column.SortMemberPath]=column.ActualWidth;
                Workspace.Changed();
            }
            resizeStart=null;
        }),true);
    }
    private double TextWidth(string text)
    {
        var formatted=new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface(FileList.FontFamily,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal),FileList.FontSize,Brushes.Black,VisualTreeHelper.GetDpi(this).PixelsPerDip);
        return Math.Ceiling(formatted.WidthIncludingTrailingWhitespace)+12;
    }
    internal void ApplyColumnWidths()
    {
        if(DataContext is not CardViewModel card || (Window.GetWindow(this) as MainWindow)?.IsCardEnlarged(this)==true)return;
        card.State.ColumnWidths ??= new();
        foreach(var column in FileList.Columns)
        {
            if(card.State.ColumnWidths.TryGetValue(column.SortMemberPath,out double saved) && double.IsFinite(saved) && saved>=20 && saved<=3000)
            {column.Width=new DataGridLength(saved);continue;}
            column.Width=column.SortMemberPath switch
            {
                "Name"=>new DataGridLength(1,DataGridLengthUnitType.Star),
                "Modified"=>new DataGridLength(Math.Max(TextWidth("수정한 날짜"),TextWidth("26.11.10 23:59"))),
                "Length"=>new DataGridLength(Math.Max(TextWidth("크기"),card.Entries.Select(x=>x.Size).Distinct().Select(TextWidth).DefaultIfEmpty(TextWidth("—")).Max())),
                "Type"=>new DataGridLength(95*TextScale.Factor),
                "Extension"=>new DataGridLength(65*TextScale.Factor),
                _=>DataGridLength.Auto
            };
        }
    }
}
