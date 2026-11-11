using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PinShelf;

// Only transforms move during a gesture. Collection.Move happens once on release.
// Hit testing uses the original layout slots, never animated visual bounds.
internal sealed class ReorderDragSession
{
    private sealed record Slot(int Index, CardViewModel Card, FrameworkElement Element, Rect Bounds, TranslateTransform Transform);
    private readonly MainWindow window;
    private readonly ItemsControl control;
    private readonly CardViewModel source;
    private readonly int from;
    private readonly List<Slot> slots = [];
    private readonly AdornerLayer layer;
    private readonly DragGhost ghost;
    private readonly Vector grab;
    private readonly ScrollViewer? scroll;
    private int target;
    private Point pointer;
    private Rect viewport;
    internal int TargetIndex => target;
    internal int PreviewChanges { get; private set; }
    internal Point GhostPosition => ghost.Position;

    internal ReorderDragSession(MainWindow window, ItemsControl control, CardViewModel source, Point start, Vector? grabOverride = null)
    {
        this.window = window; this.control = control; this.source = source;
        from = target = control.Items.IndexOf(source);
        window.UpdateLayout();
        var container = (FrameworkElement)control.ItemContainerGenerator.ContainerFromItem(source);
        var root = (UIElement)window.Content;
        layer = AdornerLayer.GetAdornerLayer(root) ?? throw new InvalidOperationException("Drag adorner layer is unavailable.");
        var origin = container.TranslatePoint(new Point(), root);
        grab = grabOverride ?? (window.TranslatePoint(start, root) - origin);
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(container.ActualWidth)), Math.Max(1, (int)Math.Ceiling(container.ActualHeight)), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(container); bitmap.Freeze();
        ghost = new DragGhost(root, bitmap, container.RenderSize) { IsHitTestVisible = false, Position = origin };
        scroll = FindScroll(control);
        CaptureSlots();
        layer.Add(ghost);
        source.DragOpacity = 0.16;
    }
    private static ScrollViewer? FindScroll(DependencyObject item)
    {
        if (item is ListBox) return Descendant<ScrollViewer>(item);
        for (var p = VisualTreeHelper.GetParent(item); p != null; p = VisualTreeHelper.GetParent(p))
            if (p is ScrollViewer scroll) return scroll;
        return null;
    }
    private static T? Descendant<T>(DependencyObject node) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is T value) return value;
            if (Descendant<T>(child) is { } nested) return nested;
        }
        return null;
    }
    private void CaptureSlots()
    {
        slots.Clear();
        for (int i = 0; i < control.Items.Count; i++)
        {
            if (control.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement child) continue;
            var parent = (UIElement)VisualTreeHelper.GetParent(child);
            var rect = LayoutInformation.GetLayoutSlot(child);
            rect.Location = parent.TranslatePoint(rect.Location, window);
            var transform = child.RenderTransform as TranslateTransform ?? new TranslateTransform();
            child.RenderTransform = transform;
            slots.Add(new(i, (CardViewModel)control.Items[i], child, rect, transform));
        }
        UIElement surface = scroll is null ? control : (UIElement?)Descendant<ScrollContentPresenter>(scroll) ?? scroll;
        viewport = new Rect(surface.TranslatePoint(new Point(), window), surface.RenderSize);
    }
    internal bool Move(Point point)
    {
        pointer = point;
        ghost.Position = window.TranslatePoint(point, ghost.AdornedElement) - grab;
        ghost.InvalidateVisual();
        if (!viewport.Contains(point)) return false;
        var candidate = slots.FirstOrDefault(s => s.Bounds.Contains(point));
        if (candidate == null || candidate.Index == target) return true;
        // A small dead band at each slot edge prevents oscillation at a boundary.
        var inside = candidate.Bounds; inside.Inflate(-Math.Min(8, inside.Width / 4), -Math.Min(6, inside.Height / 4));
        if (!inside.Contains(point)) return true;
        target = candidate.Index; PreviewChanges++;
        Preview();
        return true;
    }
    private void Preview()
    {
        var ordered = slots.OrderBy(s => s.Index == from ? target : from < target && s.Index > from && s.Index <= target ? s.Index - 1 : from > target && s.Index >= target && s.Index < from ? s.Index + 1 : s.Index).ToArray();
        double y = slots.Count == 0 ? 0 : slots[0].Bounds.Top;
        for (int i = 0; i < ordered.Length;)
        {
            var count = Math.Min(source.IsFolder ? window.ViewModel.FolderColumns : window.ViewModel.FavoriteColumns, ordered.Length - i);
            double height = 0;
            for (int col = 0; col < count; col++)
            {
                var slot = ordered[i + col];
                var x = slots[col].Bounds.Left;
                var t = slot.Transform;
                ReorderAnimation.Move(t, t.X, t.Y, x - slot.Bounds.Left, y - slot.Bounds.Top);
                height = Math.Max(height, slot.Bounds.Height);
            }
            y += height; i += count;
        }
    }
    internal void ScrollAtEdge()
    {
        if (scroll == null || !viewport.Contains(pointer)) return;
        var edge = Math.Min(36, viewport.Height / 4);
        double direction = pointer.Y < viewport.Top + edge ? -1 : pointer.Y > viewport.Bottom - edge ? 1 : 0;
        if (direction == 0) return;
        var before = scroll.VerticalOffset;
        if ((direction < 0 && before <= 0) || (direction > 0 && before >= scroll.ScrollableHeight)) return;
        var pixels = scroll.CanContentScroll && !source.IsFolder ? 1 : 12;
        // Restore transforms before a virtualized panel recycles any containers.
        foreach (var slot in slots) Reset(slot.Transform);
        scroll.ScrollToVerticalOffset(before + direction * pixels);
        window.UpdateLayout(); CaptureSlots(); Preview(); Move(pointer);
    }
    private static void Reset(TranslateTransform transform)
    {
        transform.BeginAnimation(TranslateTransform.XProperty, null); transform.X = 0;
        transform.BeginAnimation(TranslateTransform.YProperty, null); transform.Y = 0;
    }
    internal bool Finish(bool commit)
    {
        bool changed = false;
        try
        {
            var dragged = slots.FirstOrDefault(s => s.Card == source);
            if (dragged != null)
            {
                var point = ghost.AdornedElement.TranslatePoint(ghost.Position, window);
                Reset(dragged.Transform);
                dragged.Transform.X = point.X - dragged.Bounds.Left;
                dragged.Transform.Y = point.Y - dragged.Bounds.Top;
            }
            if (commit && target != from)
            {
                var destination = (CardViewModel)control.Items[target];
                changed = window.ViewModel.Reorder(source.State.Id, destination.State.Id, target > from);
                window.UpdateLayout();
            }
            else
                foreach (var slot in slots)
                    ReorderAnimation.Move(slot.Transform, slot.Transform.X, slot.Transform.Y, 0, 0);
            return changed;
        }
        finally { source.DragOpacity = 1; layer.Remove(ghost); }
    }
    private sealed class DragGhost(UIElement owner, ImageSource image, Size size) : Adorner(owner)
    {
        internal Point Position { get; set; }
        protected override void OnRender(DrawingContext dc)
        {
            var rect = new Rect(Position, size);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(28, 0, 0, 0)), null, new Rect(Position + new Vector(3, 5), size), 7, 7);
            dc.PushOpacity(0.94); dc.DrawImage(image, rect); dc.Pop();
        }
    }
}


