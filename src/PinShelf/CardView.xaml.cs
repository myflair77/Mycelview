using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PinShelf;
public partial class CardView : UserControl
{
    private CardViewModel? subscribedCard;
    private bool restoringScroll;
    private bool contextMenusAttached;
    private bool notePinned;
    private MainWindow? noteHost;
    private Point? noteAnchor;
    private readonly DispatcherTimer noteCloseTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private int restoreVersion;
    private HashSet<string> selectedPaths = new(StringComparer.OrdinalIgnoreCase);
    private void PrepareListing() { restoringScroll = true; var card = DataContext as CardViewModel ?? subscribedCard; selectedPaths = card?.SelectedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? []; }
    private CardViewModel Card => (CardViewModel)DataContext;
    private MainViewModel Workspace => ((MainWindow)Window.GetWindow(this)).ViewModel;
    public CardView()
    {
        InitializeComponent();
        InitializeColumnWidths();
        InitializeInlineRename();
        FileList.SelectionChanged += (_, _) => { if (!restoringScroll && IsLoaded && DataContext is CardViewModel card) card.SelectedPaths = FileList.SelectedItems.OfType<FileEntry>().Select(x => x.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase); };
        NotePopup.Opened += (_, _) => { noteAnchor = null; UpdateNotePlacement(null, EventArgs.Empty); };
        NoteButton.LayoutUpdated += UpdateNotePlacement;
        noteCloseTimer.Tick += (_, _) => { noteCloseTimer.Stop(); if (!notePinned && !NoteButton.IsMouseOver && !NoteSurface.IsMouseOver) NotePopup.IsOpen = false; };
        Unloaded += (_, _) => { DetachNoteHost(); noteCloseTimer.Stop(); NotePopup.IsOpen = false; };
        Loaded += (_, _) =>
        {
            TextScale.Changed -= ResizeTextColumns; TextScale.Changed += ResizeTextColumns;
            ResizeTextColumns();
            DetachNoteHost(); noteHost = Host; noteHost.PreviewMouseDown += CloseNoteOutside; noteHost.Deactivated += CloseNoteOnDeactivate;
            noteHost.LocationChanged += UpdateNotePlacement;
            noteHost.SizeChanged += UpdateNotePlacement;
            noteHost.StateChanged += UpdateNotePlacement;
            if (!contextMenusAttached)
            {
                Host.FileMenus.AttachGrid(FileList, entry => Card.OpenEntry(entry), () => Card.CurrentPath);
                Host.FileMenus.AttachHeader(CardHeader, () => Card);
                contextMenusAttached = true;
            }
            foreach (var (column, property) in new[] { (ModifiedColumn, "ShowModified"), (SizeColumn, "ShowSize"), (TypeColumn, "ShowType"), (CreatedColumn, "ShowCreated"), (ExtensionColumn, "ShowExtension") })
                BindingOperations.SetBinding(column, DataGridColumn.VisibilityProperty, new Binding(property) { Source = Workspace, Converter = new BooleanToVisibilityConverter() });
            if (subscribedCard != null) { subscribedCard.ListingRefreshed -= RestoreList; subscribedCard.ListingUpdating -= PrepareListing; }
            subscribedCard = DataContext as CardViewModel;
            selectedPaths = subscribedCard?.SelectedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            if (subscribedCard != null) { subscribedCard.ListingRefreshed += RestoreList; subscribedCard.ListingUpdating += PrepareListing; }
        };
        Unloaded += (_, _) => { TextScale.Changed -= ResizeTextColumns; if (subscribedCard != null) { subscribedCard.ListingRefreshed -= RestoreList; subscribedCard.ListingUpdating -= PrepareListing; } subscribedCard = null; };
    }
    private void OpenEntry(object sender, MouseButtonEventArgs e)
    { CancelPendingRename(); if (FindParent<TextBox>(e.OriginalSource as DependencyObject)!=null) return; if (FindParent<DataGridRow>(e.OriginalSource as DependencyObject) is { Item: FileEntry entry }) Card.OpenEntry(entry); }
    private void ResizeTextColumns()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (!IsLoaded) return;
            ApplyColumnWidths();
        }));
    }
    private void DetachNoteHost()
    {
        if (noteHost == null) return;
        noteHost.PreviewMouseDown -= CloseNoteOutside;
        noteHost.Deactivated -= CloseNoteOnDeactivate;
        noteHost.LocationChanged -= UpdateNotePlacement;
        noteHost.SizeChanged -= UpdateNotePlacement;
        noteHost.StateChanged -= UpdateNotePlacement;
        noteHost = null;
    }
    private void CloseNoteOnDeactivate(object? sender, EventArgs e) => NotePopup.IsOpen = false;
    private void UpdateNotePlacement(object? sender, EventArgs e)
    {
        if (!IsLoaded || !NotePopup.IsOpen || noteHost?.WindowState == WindowState.Minimized) return;
        var anchor = NoteButton.PointToScreen(new Point());
        if (noteAnchor == anchor) return;
        noteAnchor = anchor;
        var offset = NotePopup.HorizontalOffset;
        NotePopup.SetCurrentValue(Popup.HorizontalOffsetProperty, offset + 0.01);
        NotePopup.SetCurrentValue(Popup.HorizontalOffsetProperty, offset);
    }
    private void CloseNoteOutside(object sender, MouseButtonEventArgs e)
    { if (!NotePopup.IsOpen) return; var source = e.OriginalSource as DependencyObject; bool inside = source == NoteButton || source == NoteSurface || source is Visual visual && (NoteButton.IsAncestorOf(visual) || NoteSurface.IsAncestorOf(visual)); if (!inside) NotePopup.IsOpen = false; }
    private void OpenNotePopup(object sender, MouseEventArgs e) { noteCloseTimer.Stop(); NotePopup.IsOpen = true; }
    private void LeaveNotePopup(object sender, MouseEventArgs e) { if (!notePinned) noteCloseTimer.Start(); }
    private void ToggleNotePopup(object sender, RoutedEventArgs e)
    {
        notePinned = true; noteCloseTimer.Stop(); NotePopup.IsOpen = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (NotePopup.IsOpen && notePinned) NoteEditor.Focus(); }));
    }
    private void EditNoteSurface(object sender, MouseButtonEventArgs e)
    {
        notePinned = true; noteCloseTimer.Stop();
        if (FindParent<TextBox>(e.OriginalSource as DependencyObject) != NoteEditor)
        {
            e.Handled = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (NotePopup.IsOpen && notePinned) NoteEditor.Focus(); }));
        }
    }
    private void PinNotePopup(object sender, KeyboardFocusChangedEventArgs e) { notePinned = true; noteCloseTimer.Stop(); }
    private void NotePopupClosed(object? sender, EventArgs e) { noteAnchor = null; notePinned = false; noteCloseTimer.Stop(); }
    private void EntryKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter && FileList.SelectedItem is FileEntry entry) { Card.OpenEntry(entry); e.Handled = true; } }
    private async void CreateFolder(object sender, RoutedEventArgs e)
    {
        if (Workspace.FileTasks.Busy) return;
        try { await Workspace.FileTasks.NewFolderAsync(Card.CurrentPath); }
        catch (Exception error) { Workspace.Status = "새 폴더 만들기 실패: " + error.Message; }
    }
    private void ShowMenu(object sender, RoutedEventArgs e) { var button = (Button)sender; button.ContextMenu.DataContext = Card; button.ContextMenu.PlacementTarget = button; button.ContextMenu.IsOpen = true; }
    private void Duplicate(object sender, RoutedEventArgs e) => Workspace.Duplicate(Card);
    private void Remove(object sender, RoutedEventArgs e) => Workspace.Remove(Card);
    private void Reconnect(object sender, RoutedEventArgs e)
    {
        if (Card.IsFolder) { var d = new OpenFolderDialog { Title = "새 폴더 경로로 재연결" }; if (d.ShowDialog() == true) Card.Reconnect(d.FolderName); }
        else { var d = new OpenFileDialog { Title = "새 파일 경로로 재연결" }; if (d.ShowDialog() == true) Card.Reconnect(d.FileName); }
    }
    private void SortEntries(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        Card.State.SortDescending = Card.State.SortProperty == e.Column.SortMemberPath && !Card.State.SortDescending;
        Card.State.SortProperty = e.Column.SortMemberPath;
        PrepareListing(); RestoreList(); Workspace.Save();
    }
    private void ApplySort()
    {
        var direction = Card.State.SortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        var property = Card.State.SortProperty is "Name" or "Modified" or "Length" or "Type" or "Created" or "Extension" ? Card.State.SortProperty : "Name";
        var view = CollectionViewSource.GetDefaultView(FileList.ItemsSource);
        if (view is ListCollectionView list) list.CustomSort = new FileEntryComparer(property, Card.State.SortDescending);
        foreach (var column in FileList.Columns) column.SortDirection = column.SortMemberPath == property ? direction : null;
    }
    private void ListLoaded(object sender, RoutedEventArgs e)
    { RestoreList(); ResizeTextColumns(); }
    private MainWindow Host => (MainWindow)Window.GetWindow(this);
    private void HeaderHoverEnter(object sender, MouseEventArgs e) => (Window.GetWindow(this) as MainWindow)?.QueueCardEnlargement(this);
    private void HeaderHoverLeave(object sender, MouseEventArgs e) => (Window.GetWindow(this) as MainWindow)?.CancelQueuedEnlargement(this);
    internal void SetEnlargementIndicator(bool enlarged)
    {
        EnlargeHotspotIcon.Data=Geometry.Parse(enlarged
            ? "M1,1 L6,6 M2,6 L6,6 L6,2 M15,15 L10,10 M14,10 L10,10 L10,14 M15,1 L10,6 M14,6 L10,6 L10,2 M1,15 L6,10 M2,10 L6,10 L6,14"
            : "M6,6 L1,1 M1,5 L1,1 L5,1 M10,10 L15,15 M11,15 L15,15 L15,11 M10,6 L15,1 M11,1 L15,1 L15,5 M6,10 L1,15 M1,11 L1,15 L5,15");
        EnlargeHotspotLabel.Text=enlarged?"확대 중":"확대";
        EnlargeHotspot.ToolTip=enlarged?"카드 밖으로 마우스를 옮기면 원래 크기로 돌아갑니다 · Esc로 닫기":"이 영역에 마우스를 올리면 카드가 확대됩니다";
    }
    private void CardReorderStart(object sender, MouseButtonEventArgs e) { var host=Host; host.CancelQueuedEnlargement(this); Vector? grab=null; if(host.IsCardEnlarged(this)) { var point=e.GetPosition(this); grab=new Vector(point.X+5,point.Y+5); host.CloseCardEnlargement(); host.UpdateLayout(); } host.ReorderStart(sender,e,grab); }
    private void RestoreList()
    {
        if (DataContext is not CardViewModel) return;
        restoringScroll = true;
        ResizeTextColumns();
        selectedPaths = Card.SelectedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        ApplySort();
        foreach (var entry in Card.Entries.Where(x => selectedPaths.Contains(x.FullPath))) if (!FileList.SelectedItems.Contains(entry)) FileList.SelectedItems.Add(entry);
        var saved = Card.State.ScrollOffset;
        restoringScroll = true; var version = ++restoreVersion;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (version != restoreVersion) return;
            FindChild<ScrollViewer>(FileList)?.ScrollToVerticalOffset(saved);
            FileList.UpdateLayout(); restoringScroll = false;
        }));
    }
    private void ListScrolled(object sender, ScrollChangedEventArgs e)
    { if (!restoringScroll && DataContext is CardViewModel card && card.Expanded && e.VerticalChange != 0 && e.ExtentHeight > 0 && e.ViewportHeight > 0 && IsLoaded) { card.State.ScrollOffset = e.VerticalOffset; Workspace.Changed(); } }
    public static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); if (child is T found) return found; if (FindChild<T>(child) is T nested) return nested; } return null; }
    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    { while (child != null) { if (child is T found) return found; child = VisualTreeHelper.GetParent(child); } return null; }
}















