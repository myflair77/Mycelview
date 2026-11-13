using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Mycelview;

public partial class MainWindow
{
    private Grid? enlargedFileSection, fileSectionParent, fileSectionPlaceholder;
    private Canvas? fileSectionLayer;
    private Border? fileSectionFrame;
    private Thickness fileSectionMargin;
    private Size fileSectionSize;
    private DispatcherTimer? fileSectionMonitor;
    private DateTime fileSectionOpened;
    private bool FileSectionPinned(bool recent) => recent
        ? ViewModel.ActiveWorkspace?.RecentEnlargementPinned == true
        : ViewModel.ActiveWorkspace?.FavoritesEnlargementPinned == true;

    private void FileSectionHover(object sender, MouseEventArgs e)
    {
        if (TestMode || sender is not Border hotspot || Mouse.LeftButton == MouseButtonState.Pressed || Mouse.Captured != null) return;
        bool recent = (string)hotspot.Tag == "Recent";
        if (enlargedFileSection == (recent ? RecentSection : FavoritesSection)) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (hotspot.IsMouseOver && Mouse.LeftButton != MouseButtonState.Pressed && Mouse.Captured == null)
                OpenFileSectionEnlargement(recent);
        }));
    }

    private void FileSectionPinClick(object sender, MouseButtonEventArgs e)
    {
        bool recent = (string)((Border)sender).Tag == "Recent";
        if (enlargedFileSection != (recent ? RecentSection : FavoritesSection)) OpenFileSectionEnlargement(recent);
        if (enlargedFileSection == (recent ? RecentSection : FavoritesSection)) ToggleFileSectionPin(recent);
        e.Handled = true;
    }

    internal void ToggleFileSectionPin(bool recent)
    {
        if (ViewModel.ActiveWorkspace is not { } tab) return;
        if (recent) tab.RecentEnlargementPinned = !tab.RecentEnlargementPinned;
        else tab.FavoritesEnlargementPinned = !tab.FavoritesEnlargementPinned;
        UpdateFileSectionPins(); ViewModel.Changed(); ViewModel.Save();
    }

    private void UpdateFileSectionPins()
    {
        FavoritesEnlargementPin.Opacity = FileSectionPinned(false) ? 1 : .25;
        RecentEnlargementPin.Opacity = FileSectionPinned(true) ? 1 : .25;
        foreach (bool recent in new[] { false, true })
        {
            var section = recent ? RecentSection : FavoritesSection;
            var hotspot = recent ? RecentEnlargeHotspot : FavoritesEnlargeHotspot;
            var icon = recent ? RecentEnlargeIcon : FavoritesEnlargeIcon;
            bool enlarged = section == enlargedFileSection;
            icon.Data = Geometry.Parse(enlarged
                ? "M1,1 L6,6 M2,6 L6,6 L6,2 M15,15 L10,10 M14,10 L10,10 L10,14 M15,1 L10,6 M14,6 L10,6 L10,2 M1,15 L6,10 M2,10 L6,10 L6,14"
                : "M6,6 L1,1 M1,5 L1,1 L5,1 M10,10 L15,15 M11,15 L15,15 L15,11 M10,6 L15,1 M11,1 L15,1 L15,5 M6,10 L1,15 M1,11 L1,15 L5,15");
            hotspot.ToolTip = enlarged ? (FileSectionPinned(recent) ? "확대 고정 중 · 클릭하면 핀 해제 · Esc로 닫기" : "클릭하면 확대 고정 · 목록 밖으로 나가면 닫기") : "마우스를 올리면 목록 확대 · 클릭하면 고정";
        }
    }

    internal void OpenFileSectionEnlargement(bool recent)
    {
        var section = recent ? RecentSection : FavoritesSection;
        if (ViewModel.Enlargement.Mode == "C" || enlargedFileSection == section) return;
        CloseCardEnlargement();
        ViewModel.Search.HideResults();
        fileSectionParent = (Grid)section.Parent;
        fileSectionSize = new Size(section.ActualWidth, section.ActualHeight);
        fileSectionMargin = section.Margin;
        // Keep bindings alive while moving between the dock and floating frame.
        section.DataContext = ViewModel;
        fileSectionPlaceholder = new Grid();
        Grid.SetRow(fileSectionPlaceholder, Grid.GetRow(section));
        fileSectionParent.Children.Remove(section);
        fileSectionParent.Children.Add(fileSectionPlaceholder);
        enlargedFileSection = section;
        section.Margin = new Thickness(0);
        fileSectionFrame = new Border { Padding = new Thickness(10), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = section };
        fileSectionFrame.AllowDrop = true;
        fileSectionFrame.DragOver += WorkspaceDragOver;
        fileSectionFrame.Drop += WorkspaceDrop;
        fileSectionFrame.SetResourceReference(BackgroundProperty, "CanvasBrush");
        fileSectionFrame.SetResourceReference(BorderBrushProperty, "AccentBrush");
        fileSectionLayer = new Canvas();
        fileSectionLayer.Children.Add(fileSectionFrame); WindowLayers.Children.Add(fileSectionLayer);
        UpdateLayout(); ResizeFileSection(null, null!); UpdateFileSectionPins();
        SizeChanged += ResizeFileSection; Deactivated += DeactivateFileSection;
        fileSectionOpened = DateTime.UtcNow;
        fileSectionMonitor ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        fileSectionMonitor.Tick -= MonitorFileSection; fileSectionMonitor.Tick += MonitorFileSection; fileSectionMonitor.Start();
    }

    private void ResizeFileSection(object? sender, SizeChangedEventArgs e)
    {
        if (fileSectionFrame == null || fileSectionPlaceholder == null) return;
        var settings = ViewModel.Enlargement;
        double width = Math.Min(Math.Max(fileSectionSize.Width + 22, settings.FixedSize ? settings.Width : fileSectionSize.Width * settings.Percent / 100), Math.Max(100, WindowLayers.ActualWidth - 24));
        double height = Math.Min(Math.Max(fileSectionSize.Height + 22, settings.FixedSize ? settings.Height : fileSectionSize.Height * settings.Percent / 100), Math.Max(100, WindowLayers.ActualHeight - 24));
        var point = fileSectionPlaceholder.TranslatePoint(new Point(), WindowLayers);
        fileSectionFrame.Width = width; fileSectionFrame.Height = height;
        Canvas.SetLeft(fileSectionFrame, Math.Clamp(point.X + fileSectionPlaceholder.ActualWidth - width + 11, 12, Math.Max(12, WindowLayers.ActualWidth - width - 12)));
        Canvas.SetTop(fileSectionFrame, Math.Clamp(point.Y + fileSectionMargin.Top - 11, 12, Math.Max(12, WindowLayers.ActualHeight - height - 12)));
    }

    private void MonitorFileSection(object? sender, EventArgs e)
    {
        if (enlargedFileSection == null || FileSectionPinned(enlargedFileSection == RecentSection) || DateTime.UtcNow - fileSectionOpened < TimeSpan.FromMilliseconds(200)) return;
        if (fileSectionFrame!.IsMouseOver || NativeShellMenu.IsOpen || ContextMenuIsOpen(enlargedFileSection) || Mouse.Captured != null || Mouse.LeftButton == MouseButtonState.Pressed || ViewModel.FileTasks.Busy) return;
        CloseFileSectionEnlargement();
    }

    private void DeactivateFileSection(object? sender, EventArgs e)
    {
        if (enlargedFileSection != null && FileSectionPinned(enlargedFileSection == RecentSection)) return;
        if (!TestMode && !ViewModel.FileTasks.Busy && Mouse.LeftButton != MouseButtonState.Pressed) CloseFileSectionEnlargement();
    }

    internal void CloseFileSectionEnlargement()
    {
        if (enlargedFileSection == null) return;
        fileSectionMonitor?.Stop(); SizeChanged -= ResizeFileSection; Deactivated -= DeactivateFileSection;
        var section = enlargedFileSection;
        fileSectionFrame!.Child = null; WindowLayers.Children.Remove(fileSectionLayer!);
        fileSectionParent!.Children.Remove(fileSectionPlaceholder!);
        section.Margin = fileSectionMargin; fileSectionParent.Children.Add(section);
        enlargedFileSection = null; fileSectionFrame = null; fileSectionLayer = null; fileSectionPlaceholder = null; fileSectionParent = null;
        UpdateFileSectionPins();
    }
}
