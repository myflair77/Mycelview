using System.Windows;
using System.Windows.Input;

namespace Mycelview;
public partial class MainWindow
{
    private static double Safe(double value, double min, double max, double fallback) => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
    private void WorkspaceMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        if (TryScrollFolderWorkspace(e.GetPosition(PaneGrid), e.OriginalSource as DependencyObject, e.Delta))
            if (e.RoutedEvent != null) e.Handled = true;
    }
    internal bool TryScrollFolderWorkspace(Point point, DependencyObject? source, int delta)
    {
        if (!ViewModel.ExplorerCollapsed || delta == 0) return false;
        double right = PaneGrid.ColumnDefinitions.Take(3).Sum(column => column.ActualWidth);
        // Include the collapsed explorer strip and grid margins below the tab row.
        if (point.X < 0 || point.X >= right || point.Y < PaneGrid.RowDefinitions[0].ActualHeight || point.Y >= PaneGrid.ActualHeight)
            return false;
        // Preserve every card's own wheel behaviour, including its header, editor and list.
        if (FileContextMenus.Parent<CardView>(source) != null || FileContextMenus.Parent<ViewportCardHost>(source) != null)
            return false;
        int lines = SystemParameters.WheelScrollLines;
        double distance = lines < 0 ? FolderWorkspaceScroll.ViewportHeight : lines * 16;
        FolderWorkspaceScroll.ScrollToVerticalOffset(FolderWorkspaceScroll.VerticalOffset - delta / 120.0 * distance);
        return true;
    }
    internal void RestorePaneLayout()
    {
        UpdateLayout(); var layout = ViewModel.Layout;
        double left = Safe(ViewModel.ExplorerWidth, 210, 480, 280), right = Safe(ViewModel.FavoritesWidth, 220, 500, 265);
        var available = Math.Max(430, ActualWidth - 630);
        if (left + right > available) { left = Math.Min(left, Math.Max(210, available - 220)); right = Math.Max(220, available - left); }
        ExplorerColumn.MinWidth = 210; ExplorerColumn.Width = new GridLength(left); FavoritesColumn.Width = new GridLength(right);
        ExplorerTreeRow.Height = new GridLength(Safe(ViewModel.ExplorerTreeHeight, 60, Math.Max(60, ActualHeight - 430), 160));
        ApplyExplorerVisibility(left);
        var ratio = Safe(ViewModel.FavoritesRatio, 0.1, 0.9, 0.5);
        FavoritesRow.Height = new GridLength(ratio, GridUnitType.Star); RecentRow.Height = new GridLength(1 - ratio, GridUnitType.Star);
    }
    internal void ToggleExplorer(object sender, RoutedEventArgs e)
    {
        SavePaneLayout();
        ViewModel.ExplorerCollapsed = !ViewModel.ExplorerCollapsed;
        ApplyExplorerVisibility();
        ViewModel.Changed();
        if (e.RoutedEvent != null) e.Handled = true;
    }
    private void ApplyExplorerVisibility(double? expandedWidth = null)
    {
        bool collapsed = ViewModel.ExplorerCollapsed;
        // Offset the card host's 5 DIP inset so cards align with the collapsed pane boundary.
        FolderWorkspaceArea.Margin = new Thickness(collapsed ? -5 : 10, 0, 10, 0);
        WorkspaceTabScroll.Margin = new Thickness(collapsed ? 0 : 15, 0, 12, 0);
        ExplorerPane.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        ExplorerSplitter.IsEnabled = !collapsed;
        ExpandExplorerButton.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        ExplorerColumn.MinWidth = collapsed ? 22.5 : 210;
        ExplorerColumn.Width = new GridLength(collapsed ? 22.5 : expandedWidth ?? Safe(ViewModel.ExplorerWidth, 210, 480, 280));
    }
    internal void SavePaneLayout()
    {
        var layout = ViewModel.Layout;
        if (!ViewModel.ExplorerCollapsed) ViewModel.ExplorerWidth = ExplorerColumn.ActualWidth; ViewModel.FavoritesWidth = FavoritesColumn.ActualWidth;
        if (!ViewModel.ExplorerCollapsed) ViewModel.ExplorerTreeHeight = ExplorerTreeRow.ActualHeight;
        var total = FavoritesRow.ActualHeight + RecentRow.ActualHeight;
        if (total > 0) ViewModel.FavoritesRatio = FavoritesRow.ActualHeight / total;
        ViewModel.Changed();
    }
}
