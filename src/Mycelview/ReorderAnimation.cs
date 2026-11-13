using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Mycelview;

// Remember identity, rather than container identity: recycling may replace containers.
internal sealed class ReorderAnimation
{
    private Dictionary<Guid, (Point Point, TranslateTransform Transform)> positions = [];
    private Guid[] order = [];
    public void Arrange(UIElementCollection children)
    {
        var items = children.OfType<FrameworkElement>()
            .Where(x => x.DataContext is CardViewModel).ToArray();
        var nextOrder = items.Select(x => ((CardViewModel)x.DataContext).State.Id).ToArray();
        bool reordered = order.Length == nextOrder.Length && !order.SequenceEqual(nextOrder)
            && order.ToHashSet().SetEquals(nextOrder);
        var next = new Dictionary<Guid, (Point, TranslateTransform)>();
        foreach (var child in items)
        {
            var id = ((CardViewModel)child.DataContext).State.Id;
            var slot = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(child).Location;
            var transform = child.RenderTransform as TranslateTransform ?? new TranslateTransform();
            child.RenderTransform = transform;
            next[id] = (slot, transform);
            if (!reordered || !positions.TryGetValue(id, out var old)) continue;
            var offset = old.Point - slot;
            Move(transform, offset.X + old.Transform.X, offset.Y + old.Transform.Y, 0, 0);
        }
        positions = next; order = nextOrder;
    }
    internal static void Move(TranslateTransform transform, double fromX, double fromY, double toX, double toY)
    {
        Animate(transform, TranslateTransform.XProperty, fromX, toX);
        Animate(transform, TranslateTransform.YProperty, fromY, toY);
    }
    private static void Animate(TranslateTransform transform, DependencyProperty property, double from, double to)
    {
        transform.BeginAnimation(property, null);
        transform.SetValue(property, to);
        if (SystemParameters.ClientAreaAnimation && Math.Abs(from - to) > 0.1)
            transform.BeginAnimation(property, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(180))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }
}

public sealed class SmoothFavoritesPanel : VirtualizingStackPanel
{
    private readonly ReorderAnimation animation = new();
    protected override Size ArrangeOverride(Size size)
    {
        var result = base.ArrangeOverride(size);
        animation.Arrange(InternalChildren);
        return result;
    }
}
