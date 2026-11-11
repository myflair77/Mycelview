using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PinShelf;
public partial class MainWindow
{
    private string searchSortProperty = "Name";
    private bool searchSortDescending;
    private void UpdateSearchPopupPlacement()
    {
        if (!IsLoaded || !SearchPopup.IsOpen || WindowState == WindowState.Minimized) return;
        ResizeSearchResults();
        // WPF Popup uses its own HWND and does not automatically follow owner moves.
        // Changing an offset invalidates placement against the current SearchBox position.
        var offset = SearchPopup.HorizontalOffset;
        SearchPopup.SetCurrentValue(System.Windows.Controls.Primitives.Popup.HorizontalOffsetProperty, offset + 0.01);
        SearchPopup.SetCurrentValue(System.Windows.Controls.Primitives.Popup.HorizontalOffsetProperty, offset);
    }
    private void SortSearchResults(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        searchSortDescending = searchSortProperty == e.Column.SortMemberPath && !searchSortDescending;
        searchSortProperty = e.Column.SortMemberPath;
        ApplySearchSort();
    }
    private void ApplySearchSort()
    {
        if (System.Windows.Data.CollectionViewSource.GetDefaultView(SearchResults.ItemsSource) is System.Windows.Data.ListCollectionView view)
            view.CustomSort = new FileEntryComparer(searchSortProperty, searchSortDescending);
        foreach (var column in SearchResults.Columns)
            column.SortDirection = column.SortMemberPath == searchSortProperty ? (searchSortDescending ? System.ComponentModel.ListSortDirection.Descending : System.ComponentModel.ListSortDirection.Ascending) : null;
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MonitorRect { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MonitorDetails { public int Size; public MonitorRect Monitor, Work; public uint Flags; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct ScreenPoint { public int X, Y; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(ScreenPoint point, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorDetails details);
    private void ResizeSearchResults()
    {
        if (!IsLoaded) return;
        var point = SearchBox.PointToScreen(new Point(0, SearchBox.ActualHeight));
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleY;
        var details = new MonitorDetails { Size = System.Runtime.InteropServices.Marshal.SizeOf<MonitorDetails>() };
        double available = Math.Max(0, ActualHeight - SearchBox.TranslatePoint(new Point(0, SearchBox.ActualHeight), this).Y - 32);
        if (GetMonitorInfo(MonitorFromPoint(new ScreenPoint { X = (int)point.X, Y = (int)point.Y }, 2), ref details))
            available = Math.Max(0, Math.Min((details.Work.Bottom - point.Y) / dpi - 34, (details.Work.Bottom - details.Work.Top) / dpi * .75 - 22));
        SearchResultsLayout.Height = Math.Min(300 * 1.5, available);
    }
    private void SearchOutsideClick(object sender, MouseButtonEventArgs e)
    {
        if (FileContextMenus.Parent<ContextMenu>(e.OriginalSource as DependencyObject) != null) return;
        var current = e.OriginalSource as DependencyObject;
        while (current != null)
        {
            if (current == SearchArea || current == SearchResultsPanel) return;
            current = current is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D ? System.Windows.Media.VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }
        ViewModel.Search.HideResults();
    }
    private void SearchBoxClicked(object sender, MouseButtonEventArgs e) => ViewModel.Search.ShowResults();
    private async void SearchKeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Enter) { e.Handled = true; await ViewModel.Search.RunAsync(); } }
    private void RegisterSearchSelection(object sender, RoutedEventArgs e)
    {
        var entries = SearchResults.SelectedItems.Cast<FileEntry>().ToArray();
        if (entries.Length == 0) { ViewModel.Status = "검색 결과에서 등록할 항목을 선택하세요."; return; }
        foreach (var entry in entries) ViewModel.AddPath(entry.FullPath);
    }
    private async void SearchOpen(object sender, MouseButtonEventArgs e)
    { if (FindParent<DataGridRow>(e.OriginalSource as DependencyObject)?.Item is FileEntry entry) await OpenExplorerEntry(entry); }
    private async void SearchResultKeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Enter && SearchResults.SelectedItem is FileEntry entry) { e.Handled = true; await OpenExplorerEntry(entry); } }
    private void SearchMouseDown(object sender, MouseButtonEventArgs e)
    { dragStart = e.GetPosition(this); dragPath = (FindParent<DataGridRow>(e.OriginalSource as DependencyObject)?.Item as FileEntry)?.FullPath; }
    private void SearchMouseMove(object sender, MouseEventArgs e)
    {
        if (!ShouldDrag(e)) return;
        var paths = SearchResults.SelectedItems.Cast<FileEntry>().Select(x => x.FullPath).ToArray();
        if (!paths.Contains(dragPath)) paths = [dragPath!];
        StartRegistrationDrag(SearchResults, paths); e.Handled = true;
    }
    private void SearchLocation(object sender, RoutedEventArgs e)
    {
        if (SearchResults.SelectedItem is not FileEntry entry) { ViewModel.Status = "위치를 열 검색 결과를 선택하세요."; return; }
        try { FileService.Open(Path.GetDirectoryName(entry.FullPath) ?? entry.FullPath); }
        catch (Exception error) { ViewModel.Status = "위치를 열 수 없습니다: " + error.Message; }
    }
}
