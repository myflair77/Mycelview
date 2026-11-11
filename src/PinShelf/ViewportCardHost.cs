using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
namespace PinShelf;

// Keep light, fixed-size slots for stable two-column drag geometry; realize heavy views near the viewport only.
public sealed class ViewportCardHost : ContentControl
{
    internal bool Enlarged { get; set; }
    private ScrollViewer? scroll;
    private bool queued;
    private CardViewModel? card;
    public ViewportCardHost()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        SetBinding(HeightProperty, new Binding(nameof(CardViewModel.DisplayHeight)));
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        DataContextChanged += (_, _) => { if (IsLoaded) { Detach(); Attach(); } };
    }
    private void Attach()
    {
        card = DataContext as CardViewModel;
        for (var parent = VisualTreeHelper.GetParent(this); parent != null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is ScrollViewer found) { scroll = found; break; }
        if (scroll == null || card == null) return;
        scroll.ScrollChanged += Scrolled; scroll.SizeChanged += Resized;
        card.PropertyChanged += Changed;
        UpdateVisibility();
    }
    private void Detach()
    {
        if (scroll != null) { scroll.ScrollChanged -= Scrolled; scroll.SizeChanged -= Resized; }
        if (card != null) card.PropertyChanged -= Changed;
        scroll = null; Content = null; card?.SetViewportActive(false); card = null;
    }
    private void Scrolled(object sender, ScrollChangedEventArgs e) { if (e.OriginalSource == scroll) Schedule(); }
    private void Resized(object sender, SizeChangedEventArgs e) => Schedule();
    private void Changed(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(CardViewModel.DisplayHeight)) Schedule(); }
    private void Schedule()
    {
        if (queued) return; queued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { queued = false; UpdateVisibility(); }));
    }
    private void UpdateVisibility()
    {
        if (Enlarged || !IsLoaded || scroll == null || card == null) return;
        var top = TransformToAncestor(scroll).Transform(new Point()).Y;
        var buffer = Math.Max(100, card.DisplayHeight);
        var visible = top + ActualHeight >= -buffer && top <= scroll.ActualHeight + buffer;
        // Do not detach a focused editor or a captured drag while it owns interaction.
        if (Content is CardView view && (view.IsKeyboardFocusWithin || view.IsMouseCaptureWithin || view.NotePopup.IsOpen)) visible = true;
        if (visible)
        {
            if (Content == null) Content = new CardView();
            card.SetViewportActive(true);
        }
        else { Content = null; card.SetViewportActive(false); }
    }
}

