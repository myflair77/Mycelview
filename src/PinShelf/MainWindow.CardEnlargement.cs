using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
namespace PinShelf;

public partial class MainWindow
{
    private CardView? enlargedCard, hoverCandidate;
    private ViewportCardHost? enlargedSlot;
    private DataGridLength[]? originalColumnWidths;
    private Canvas? enlargementLayer;
    private Border? enlargementPanel;
    private FolderGridPanel? pushPanel;
    private Size baseCardSize;
    private bool automaticEnlargement;
    private int hoverVersion;
    private DateTime animationUntil;
    private Point? lastHoverPoint;
    private DispatcherTimer? enlargementMonitor;
    internal bool IsCardEnlarged(CardView view) => enlargedCard == view;
    internal void QueueCardEnlargement(CardView view)
    {
        if (view.ReorderGrip.IsMouseOver) { CancelQueuedEnlargement(view); return; }
        if (TestMode || enlargedCard == view || ViewModel.Enlargement.Mode == "C") return;
        var point=Mouse.GetPosition(WindowLayers);
        if(enlargedCard!=null && lastHoverPoint is Point previous && (point-previous).Length<2) return;
        hoverCandidate = view; int version = ++hoverVersion;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (version != hoverVersion || !view.IsLoaded || !view.EnlargeHotspot.IsMouseOver || view.ReorderGrip.IsMouseOver || Mouse.LeftButton == MouseButtonState.Pressed || Mouse.Captured != null || reorderCard != null) return;
            hoverCandidate = null;
            lastHoverPoint=Mouse.GetPosition(WindowLayers);
            OpenCardEnlargement(view, true);
        }));
    }
    internal void CancelQueuedEnlargement(CardView view) { if (hoverCandidate == view) { hoverVersion++; hoverCandidate = null; } }    internal void ToggleCardEnlargement(CardView view)
    {
        if (enlargedCard == view) { CloseCardEnlargement(); return; }
        OpenCardEnlargement(view, false);
    }
    internal void OpenCardEnlargement(CardView view, bool automatic)
    {
        if (ViewModel.Enlargement.Mode == "C") return;
        CloseCardEnlargement();
        var slot = FindParent<ViewportCardHost>(view);
        if (slot == null || view.DataContext is not CardViewModel card) return;
        ViewModel.Search.HideResults();
        enlargedCard = view; enlargedSlot = slot; slot.Enlarged = true; automaticEnlargement = automatic;
        baseCardSize = new Size(slot.ActualWidth, card.State.Height);
        originalColumnWidths = view.FileList.Columns.Select(c => c.Width).ToArray();
        foreach(var column in view.FileList.Columns.Skip(1)) column.Width=new DataGridLength(column.ActualWidth);
        view.FileList.Columns[0].Width=new DataGridLength(1,DataGridLengthUnitType.Star);
        view.DataContext = card;
        view.ClearValue(HeightProperty); view.Height = double.NaN;
        view.CollapseButton.Visibility = Visibility.Collapsed;
        view.SetEnlargementIndicator(true);
        view.FolderBody.Visibility = Visibility.Visible;
        if (ViewModel.Enlargement.Mode == "B")
        {
            pushPanel = FindParent<FolderGridPanel>(slot);
            if (pushPanel == null) { CloseCardEnlargement(); return; }
            slot.ClearValue(HeightProperty);
            pushPanel.ExpandedCard = card;
        }
        else
        {
            slot.Content = null;
            enlargementLayer = new Canvas();
            enlargementPanel = new Border { Padding = new Thickness(2), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = view };
            enlargementPanel.SetResourceReference(BackgroundProperty,"SurfaceBrush");
            enlargementPanel.SetResourceReference(BorderBrushProperty,"AccentBrush");
            enlargementLayer.Children.Add(enlargementPanel); WindowLayers.Children.Add(enlargementLayer);
        }
        SizeChanged += ResizeEnlargement;
        ViewModel.Cards.CollectionChanged += EnlargedCardsChanged;
        Deactivated += DeactivateEnlargement;
        ResizeEnlargement(null, null!);
        AnimateEnlargement(view);
        if (!automatic) view.FileList.Focus();
        enlargementMonitor ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        enlargementMonitor.Tick -= MonitorEnlargement; enlargementMonitor.Tick += MonitorEnlargement;
        animationUntil = DateTime.UtcNow.AddMilliseconds(160); enlargementMonitor.Start();
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => { if (enlargedCard == view) ResizeEnlargedColumns(view.FileList, null!); }));
    }
    private void AnimateEnlargement(CardView view)
    {
        if (TestMode || !SystemParameters.ClientAreaAnimation) return;

        var duration=TimeSpan.FromMilliseconds(140);
        System.Windows.Media.Animation.DoubleAnimation Motion(double from,double to)=>new(from,to,duration) { EasingFunction=new System.Windows.Media.Animation.CubicEase { EasingMode=System.Windows.Media.Animation.EasingMode.EaseOut },FillBehavior=System.Windows.Media.Animation.FillBehavior.Stop };
        if(pushPanel!=null && enlargedSlot!=null)
        {pushPanel.BeginAnimation(FolderGridPanel.ExpandedWidthProperty,Motion(baseCardSize.Width+10,pushPanel.ExpandedWidth));enlargedSlot.BeginAnimation(HeightProperty,Motion(baseCardSize.Height,enlargedSlot.Height));}
        else if(enlargementPanel!=null)
        {enlargementPanel.BeginAnimation(WidthProperty,Motion(baseCardSize.Width,enlargementPanel.Width));enlargementPanel.BeginAnimation(HeightProperty,Motion(baseCardSize.Height,enlargementPanel.Height));}
    }    private void DeactivateEnlargement(object? sender, EventArgs e)
    {
        // Owned preview/dialogs may take activation without cancelling a file operation.
        if (!TestMode && !ViewModel.FileTasks.Busy && Mouse.LeftButton != MouseButtonState.Pressed && enlargedCard?.NotePopup.IsOpen != true) CloseCardEnlargement();
    }
    private void MonitorEnlargement(object? sender, EventArgs e)
    {
        if (enlargedCard == null) return;
        if (!automaticEnlargement || DateTime.UtcNow < animationUntil) return;
        if (NativeShellMenu.IsOpen || enlargedCard.IsMouseOver || enlargedCard.IsRenaming || enlargedCard.NotePopup.IsOpen || ContextMenuIsOpen(enlargedCard) || Mouse.LeftButton == MouseButtonState.Pressed || Mouse.Captured != null || ViewModel.FileTasks.Busy)
        { return; }
        CloseCardEnlargement();
    }
    private static bool ContextMenuIsOpen(DependencyObject root)
    {
        if (root is FrameworkElement { ContextMenu.IsOpen: true }) return true;
        for (int i=0; i<VisualTreeHelper.GetChildrenCount(root); i++) if (ContextMenuIsOpen(VisualTreeHelper.GetChild(root,i))) return true;
        return false;
    }
    private void ResizeEnlargement(object? sender, SizeChangedEventArgs e)
    {
        if (enlargedSlot == null || enlargedCard == null) return;
        var settings = ViewModel.Enlargement;
        double width = Math.Max(baseCardSize.Width, settings.FixedSize ? settings.Width : baseCardSize.Width * settings.Percent / 100);
        double height = settings.FixedSize ? settings.Height : baseCardSize.Height * settings.Percent / 100;
        height = Math.Min(height, Math.Max(180, WindowLayers.ActualHeight-24));
        if (pushPanel != null)
        {
            width = Math.Min(width, Math.Max(100, pushPanel.ActualWidth-10));
            pushPanel.ExpandedWidth = width + 10;
            enlargedSlot.Height = height;
            pushPanel.InvalidateMeasure();
        }
        else if (enlargementPanel != null && enlargementLayer != null)
        {
            // The floating frame adds 6 DIP of border/padding around the card.
            width = Math.Min(Math.Max(width, baseCardSize.Width + 6), Math.Max(100,WindowLayers.ActualWidth-24));
            var point = enlargedSlot.TranslatePoint(new Point(),WindowLayers);
            enlargementPanel.Width = width; enlargementPanel.Height = height;
            Canvas.SetLeft(enlargementPanel,Math.Clamp(point.X,12,Math.Max(12,WindowLayers.ActualWidth-width-12)));
            Canvas.SetTop(enlargementPanel,Math.Clamp(point.Y,12,Math.Max(12,WindowLayers.ActualHeight-height-12)));
        }
    }
    private void ResizeEnlargedColumns(object sender, SizeChangedEventArgs e)
    {
        if (enlargedCard == null) return;
        enlargedCard.FileList.Columns[0].Width=new DataGridLength(1,DataGridLengthUnitType.Star);
    }
    private void EnlargedCardsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    { if(enlargedCard?.DataContext is CardViewModel card && !ViewModel.Cards.Contains(card)) CloseCardEnlargement(); }
    internal void CloseCardEnlargement()
    {
        hoverVersion++; hoverCandidate = null;
        if(enlargedCard==null) return;
        var view=enlargedCard; var slot=enlargedSlot!; var card=(CardViewModel)view.DataContext;
        bool automatic = automaticEnlargement;
        enlargementMonitor?.Stop();
        SizeChanged-=ResizeEnlargement; Deactivated-=DeactivateEnlargement; ViewModel.Cards.CollectionChanged-=EnlargedCardsChanged;
        view.FileList.SizeChanged-=ResizeEnlargedColumns;
        if(originalColumnWidths!=null) for(int i=0;i<originalColumnWidths.Length;i++) view.FileList.Columns[i].Width=originalColumnWidths[i];
        originalColumnWidths=null; view.NotePopup.IsOpen=false;
        if(enlargementPanel!=null) enlargementPanel.Child=null;
        if(enlargementLayer!=null) WindowLayers.Children.Remove(enlargementLayer);
        slot.BeginAnimation(HeightProperty,null);
        if(pushPanel!=null) { pushPanel.BeginAnimation(FolderGridPanel.ExpandedWidthProperty,null); pushPanel.ExpandedCard=null; pushPanel.InvalidateMeasure(); }
        slot.SetBinding(HeightProperty,new Binding(nameof(CardViewModel.DisplayHeight)));
        view.CollapseButton.Visibility=Visibility.Visible; view.SetEnlargementIndicator(false);
        view.SetBinding(HeightProperty,new Binding(nameof(CardViewModel.DisplayHeight)));
        view.FolderBody.SetBinding(VisibilityProperty,new Binding(nameof(CardViewModel.Expanded)){Converter=new BooleanToVisibilityConverter()});
        enlargedCard=null; enlargedSlot=null; enlargementLayer=null; enlargementPanel=null; pushPanel=null; slot.Enlarged=false;
        if(slot.IsLoaded && ViewModel.Cards.Contains(card)) { slot.Content=view; if(!automatic) view.FileList.Focus(); }
    }
}











