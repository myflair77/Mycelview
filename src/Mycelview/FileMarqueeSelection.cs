using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Mycelview;

// Owned only by its grid; no global events or timers survive an unload/capture loss.
internal sealed class FileMarqueeSelection
{
    readonly DataGrid grid;
    readonly DispatcherTimer timer;
    ScrollViewer? scroll;
    AdornerLayer? layer;
    SelectionAdorner? overlay;
    Point anchor;
    HashSet<object> initial = [];
    bool additive, active;
    Dictionary<object, double>? groupedOffsets;
    internal FileMarqueeSelection(DataGrid grid)
    {
        this.grid = grid;
        VirtualizingPanel.SetScrollUnit(grid, ScrollUnit.Pixel);
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Input, (_, _) => Update(true), grid.Dispatcher);
        timer.Stop();
        grid.PreviewMouseLeftButtonDown += Down;
        grid.PreviewMouseMove += (_, e) => { if (active) { Update(false); e.Handled = true; } };
        grid.PreviewMouseLeftButtonUp += (_, e) => { if (active) { Update(false); End(); e.Handled = true; } };
        grid.LostMouseCapture += (_, _) => End();
        grid.Unloaded += (_, _) => End();
        grid.PreviewKeyDown += (_, e) => { if (active && e.Key == Key.Escape) { Restore(); End(); e.Handled = true; } };
    }
    static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (Find<T>(child) is { } nested) return nested;
        }
        return null;
    }
    internal static IEnumerable<int> Intersections(Rect rect, double top, double height, double width, int count)
    {
        for (int i = Math.Max(0, (int)Math.Floor((rect.Top - top) / height)); i < count && top + i * height <= rect.Bottom; i++)
            if (rect.IntersectsWith(new Rect(0, top + i * height, Math.Max(0, width), height))) yield return i;
    }
    void Down(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        if (FileContextMenus.Parent<DataGridRow>(source) != null ||
            FileContextMenus.Parent<DataGridColumnHeader>(source) != null ||
            FileContextMenus.Parent<ScrollBar>(source) != null) return;
        scroll = Find<ScrollViewer>(grid);
        layer = AdornerLayer.GetAdornerLayer(grid);
        if (scroll == null || layer == null) return;
        groupedOffsets = null;
        if (grid.Items.Groups is { Count: > 0 } groups)
        {
            groupedOffsets = new();
            double offset = 0, headerHeight = 24;
            if (grid.GroupStyle.FirstOrDefault()?.HeaderTemplate?.LoadContent() is FrameworkElement header)
            {
                header.DataContext = groups[0];
                header.Measure(new Size(grid.ActualWidth, double.PositiveInfinity));
                headerHeight = header.DesiredSize.Height;
            }
            foreach (System.Windows.Data.CollectionViewGroup group in groups)
            {
                offset += headerHeight;
                foreach (var item in group.Items) { groupedOffsets[item] = offset; offset += RowHeight; }
            }
        }
        initial = grid.SelectedItems.Cast<object>().ToHashSet();
        additive = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;
        anchor = e.GetPosition(grid);
        anchor.Y += Offset();
        if (!additive) grid.SelectedItems.Clear();
        Keyboard.Focus(grid);
        active = grid.CaptureMouse();
        if (!active) return;
        overlay = new SelectionAdorner(grid) { IsHitTestVisible = false };
        layer.Add(overlay);
        timer.Start();
        e.Handled = true;
    }
    double RowHeight => double.IsNaN(grid.RowHeight) ? 26 : grid.RowHeight;
    double Offset() => scroll?.VerticalOffset ?? 0;
    void Restore()
    {
        grid.SelectedItems.Clear();
        foreach (var item in initial) if (grid.Items.Contains(item)) grid.SelectedItems.Add(item);
    }
    void Update(bool autoScroll)
    {
        if (!active || scroll == null || overlay == null) return;
        var point = Mouse.GetPosition(grid);
        if (autoScroll)
        {
            if (point.Y < 25) scroll.LineUp();
            else if (point.Y > grid.ActualHeight - 20) scroll.LineDown();
            grid.UpdateLayout();
        }
        var origin = new Point(anchor.X, anchor.Y - Offset());
        var rect = new Rect(origin, point);
        overlay.Bounds = Rect.Intersect(rect, new Rect(0, 0, grid.ActualWidth, grid.ActualHeight));
        overlay.InvalidateVisual();
        // The realized row gives the content origin even with recycled containers.
        var row = Find<DataGridRow>(grid);
        if (row == null) return;
        double top = row.TranslatePoint(new Point(), grid).Y - row.GetIndex() * RowHeight;
        var selected = additive ? new HashSet<object>(initial) : new HashSet<object>();
        if (groupedOffsets != null && groupedOffsets.TryGetValue(row.Item, out var rowOffset))
        {
            top = row.TranslatePoint(new Point(), grid).Y - rowOffset;
            foreach (var pair in groupedOffsets)
                if (rect.IntersectsWith(new Rect(0, top + pair.Value, Math.Max(0, grid.ActualWidth - 18), RowHeight)))
                    selected.Add(pair.Key);
        }
        else foreach (int i in Intersections(rect, top, RowHeight, grid.ActualWidth - 18, grid.Items.Count))
            selected.Add(grid.Items[i]);
        foreach (var item in grid.SelectedItems.Cast<object>().ToArray()) if (!selected.Contains(item)) grid.SelectedItems.Remove(item);
        foreach (var item in selected) if (!grid.SelectedItems.Contains(item)) grid.SelectedItems.Add(item);
    }
    void End()
    {
        if (!active) return;
        active = false;
        timer.Stop();
        if (overlay != null) layer?.Remove(overlay);
        overlay = null; layer = null; initial.Clear(); groupedOffsets = null;
        if (grid.IsMouseCaptured) grid.ReleaseMouseCapture();
    }
    sealed class SelectionAdorner(UIElement owner) : Adorner(owner)
    {
        internal Rect Bounds;
        protected override void OnRender(DrawingContext dc)
        {
            IsHitTestVisible = false;
            if (!Bounds.IsEmpty) dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 0, 120, 215)), new Pen(Brushes.DodgerBlue, 1), Bounds);
        }
    }
}
