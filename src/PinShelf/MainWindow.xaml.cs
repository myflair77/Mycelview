using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PinShelf;
public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }
    internal FileContextMenus FileMenus { get; }
    internal FilePreviewController FilePreview { get; }
    public bool TestMode { get; set; }
    public MainWindow(MainViewModel vm)
    {
        InitializeComponent(); DataContext = ViewModel = vm; InitializeReordering(); InitializeWorkspaceDragging();
        FilePreview = new FilePreviewController(this);
        Closed += (_, _) => FilePreview.Dispose();
        vm.WorkspaceChanging += CloseCardEnlargement;
        vm.WorkspaceChanging += FilePreview.Close;
        Closing += (_, _) => CloseCardEnlargement();
        vm.WorkspaceChanging += CaptureWorkspaceLayout; vm.WorkspaceChanged += RestoreWorkspaceLayout;
        PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == Key.Escape && e.OriginalSource is TextBox { Name: "InlineNameEditor" }) return;
            if (e.Key == Key.Escape && FilePreview.IsOpen) { e.Handled = true; FilePreview.Close(); return; }
            if (e.Key == Key.Escape && enlargedCard != null) { e.Handled = true; CloseCardEnlargement(); return; }
            if ((e.Key == Key.Z || e.Key == Key.Y) && Keyboard.Modifiers == ModifierKeys.Control && FileContextMenus.Parent<System.Windows.Controls.Primitives.TextBoxBase>(e.OriginalSource as DependencyObject) == null)
            { e.Handled = true; if (e.Key == Key.Y) await ViewModel.FileTasks.RedoAsync(); else await ViewModel.FileTasks.UndoAsync(); }
        };
        Loaded += (_, _) => ApplySearchSort();
        SearchPopup.Opened += (_, _) => UpdateSearchPopupPlacement();
        SizeChanged += (_, _) => UpdateSearchPopupPlacement();
        LocationChanged += (_, _) => { SkinPicker.IsDropDownOpen = false; UpdateSearchPopupPlacement(); };
        StateChanged += (_, _) => UpdateSearchPopupPlacement();
        SearchBox.SizeChanged += (_, _) => Dispatcher.BeginInvoke(new Action(UpdateSearchPopupPlacement));
        FileMenus = new(vm) { PreviewPath = FilePreview.Show, UpdatePreview = path => FilePreview.Select(path), ClosePreview = FilePreview.Close, PreviewIsOpen = () => FilePreview.IsOpen };
        FileMenus.AttachGrid(ExplorerList, entry => _ = OpenExplorerEntry(entry), () => vm.Explorer.CurrentPath);
        FileMenus.AttachGrid(SearchResults, entry => _ = OpenExplorerEntry(entry));
        FileMenus.AttachList(FavoritesList, item => { var card = (CardViewModel)item; return new(card.CurrentPath, card.IsFolder, card); });
        FileMenus.AttachList(RecentFilesList, item => new(((RecentFile)item).CurrentPath, false));
        FileMenus.AttachTree(FolderTree);
        Deactivated += (_, _) => { if (!TestMode) ViewModel.Search.HideResults(); };
        AddHandler(System.Windows.Controls.Primitives.Thumb.DragCompletedEvent, new System.Windows.Controls.Primitives.DragCompletedEventHandler((sender, e) =>
        { if (e.OriginalSource is GridSplitter) SavePaneLayout(); }), true);
    }
    private IndexManagementWindow? indexManagement;
    private void OpenIndexManagement(object sender, RoutedEventArgs e)
    {
        if (indexManagement != null) { indexManagement.Activate(); return; }
        indexManagement = new IndexManagementWindow(this, ViewModel.Index);
        indexManagement.Closed += (_, _) => indexManagement = null;
        indexManagement.Show();
    }
    private Point dragStart;
    private void ReduceText(object sender, RoutedEventArgs e) { ViewModel.TextScalePercent--; ViewModel.Save(); }
    private void EnlargeText(object sender, RoutedEventArgs e) { ViewModel.TextScalePercent++; ViewModel.Save(); }
    private void ResetText(object sender, RoutedEventArgs e) { ViewModel.TextScalePercent = 100; ViewModel.Save(); }
    private async void UndoFileAction(object sender, RoutedEventArgs e) => await ViewModel.FileTasks.UndoAsync();
    private async void RedoFileAction(object sender, RoutedEventArgs e) => await ViewModel.FileTasks.RedoAsync();
    private void UndoHistorySettings(object sender, RoutedEventArgs e) => ViewModel.FileTasks.ShowHistorySettings();
    private string? dragPath;
    private static T? FindParent<T>(DependencyObject? item) where T : DependencyObject
    { while (item != null) { if (item is T match) return match; item = VisualTreeHelper.GetParent(item); } return null; }
    private async void TreeExpanded(object sender, RoutedEventArgs e)
    { if (e.OriginalSource is TreeViewItem { DataContext: FolderNode node }) await node.ExpandAsync(); }
    private async void TreeSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    { if (e.NewValue is FolderNode node && node.Path.Length > 0) await ViewModel.Explorer.NavigateAsync(node.Path); }
    private async void AddressKeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Enter) { e.Handled = true; await ViewModel.Explorer.NavigateAsync(ViewModel.Explorer.Address); } }
    private async void ExplorerBack(object sender, RoutedEventArgs e) => await ViewModel.Explorer.BackAsync();
    private async void ExplorerForward(object sender, RoutedEventArgs e) => await ViewModel.Explorer.ForwardAsync();
    private async void ExplorerUp(object sender, RoutedEventArgs e)
    { if (!string.IsNullOrEmpty(ViewModel.Explorer.CurrentPath)) await ViewModel.Explorer.UpAsync(); }
    private async void ExplorerRefresh(object sender, RoutedEventArgs e) => await ViewModel.Explorer.NavigateAsync(ViewModel.Explorer.CurrentPath);
    private void RegisterCurrentFolder(object sender, RoutedEventArgs e)
    { if (!string.IsNullOrEmpty(ViewModel.Explorer.CurrentPath)) ViewModel.AddPath(ViewModel.Explorer.CurrentPath); }
    private void RegisterSelection(object sender, RoutedEventArgs e)
    {
        var selected = ExplorerList.SelectedItems.Cast<FileEntry>().ToArray();
        if (selected.Length == 0) { ViewModel.Status = "왼쪽 폴더 내용에서 등록할 파일·폴더를 선택하세요."; return; }
        foreach (var item in selected) ViewModel.AddPath(item.FullPath);
    }
    private async void ExplorerOpen(object sender, MouseButtonEventArgs e)
    { if (FindParent<DataGridRow>(e.OriginalSource as DependencyObject)?.Item is FileEntry item) await OpenExplorerEntry(item); }
    private async void ExplorerKeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Enter && ExplorerList.SelectedItem is FileEntry item) { e.Handled = true; await OpenExplorerEntry(item); } }
    private async Task OpenExplorerEntry(FileEntry item)
    {
        if (item.IsDirectory) await ViewModel.Explorer.NavigateAsync(item.FullPath);
        else { try { FileService.Open(item.FullPath); } catch (Exception e) { ViewModel.Status = "파일 열기 실패: " + e.Message; } }
    }
    private void ExplorerMouseDown(object sender, MouseButtonEventArgs e)
    { dragStart = e.GetPosition(this); dragPath = (FindParent<DataGridRow>(e.OriginalSource as DependencyObject)?.Item as FileEntry)?.FullPath; }
    private void TreeMouseDown(object sender, MouseButtonEventArgs e)
    { dragStart = e.GetPosition(this); dragPath = (FindParent<TreeViewItem>(e.OriginalSource as DependencyObject)?.DataContext as FolderNode)?.Path; }
    private bool ShouldDrag(MouseEventArgs e) => e.LeftButton == MouseButtonState.Pressed && !string.IsNullOrEmpty(dragPath) &&
        (Math.Abs(e.GetPosition(this).X - dragStart.X) >= SystemParameters.MinimumHorizontalDragDistance || Math.Abs(e.GetPosition(this).Y - dragStart.Y) >= SystemParameters.MinimumVerticalDragDistance);
    private void ExplorerMouseMove(object sender, MouseEventArgs e)
    {
        if (!ShouldDrag(e)) return;
        var paths = ExplorerList.SelectedItems.Cast<FileEntry>().Select(x => x.FullPath).ToArray();
        if (!paths.Contains(dragPath)) paths = [dragPath!];
        StartRegistrationDrag(ExplorerList, paths); e.Handled = true;
    }
    private void TreeMouseMove(object sender, MouseEventArgs e)
    { if (ShouldDrag(e)) { StartRegistrationDrag(FolderTree, [dragPath!]); e.Handled = true; } }
    private void StartRegistrationDrag(DependencyObject source, string[] paths)
    {
        dragPath = null;
        ViewModel.Status = "등록할 영역에 놓으세요. 폴더는 2열 카드, 파일은 1열 목록으로 등록합니다.";
        DragDrop.DoDragDrop(source, new DataObject(DataFormats.FileDrop, paths), DragDropEffects.Link);
    }
    private void FavoriteOpen(object sender, MouseButtonEventArgs e)
    {
        if (FindParent<TextBox>(e.OriginalSource as DependencyObject) != null || FindParent<Button>(e.OriginalSource as DependencyObject) != null) return;
        if (FindParent<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is CardViewModel card) card.OpenExternal.Execute(null);
    }
    private void FavoriteKeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Enter && FindParent<TextBox>(e.OriginalSource as DependencyObject) == null && FavoritesList.SelectedItem is CardViewModel card) { card.OpenExternal.Execute(null); e.Handled = true; } }
    private void FavoriteMenu(object sender, RoutedEventArgs e)
    { var button = (Button)sender; button.ContextMenu.DataContext = button.DataContext; button.ContextMenu.PlacementTarget = button; button.ContextMenu.IsOpen = true; }
    private void RemoveFavorite(object sender, RoutedEventArgs e)
    { if (((MenuItem)sender).DataContext is CardViewModel card) ViewModel.Remove(card); }
    private void ReconnectFavorite(object sender, RoutedEventArgs e)
    {
        if (((MenuItem)sender).DataContext is not CardViewModel card) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "즐겨찾기 파일 재연결" };
        if (dialog.ShowDialog() == true) card.Reconnect(dialog.FileName);
    }
    private void WorkspaceDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetDataPresent(CardFileDrag.Format)) { e.Effects = DragDropEffects.None; ViewModel.Status = "파일을 이동·복사할 다른 폴더 카드에 놓으세요. Ctrl: 복사 · Esc: 취소"; return; }
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
        ViewModel.Status = "즐겨찾기 등록 — 원본 파일을 이동하지 않습니다.";
    }
    internal void ClearFileTransferHints()
    {
        foreach (var item in FolderCards.Items)
            if (FolderCards.ItemContainerGenerator.ContainerFromItem(item) is DependencyObject container)
                CardView.FindChild<CardView>(container)?.ClearTransferHint();
    }
    internal void RegisterDroppedPaths(IDataObject data)
    { if (data.GetDataPresent(CardFileDrag.Format)) return; if (data.GetData(DataFormats.FileDrop) is string[] paths) foreach (var p in paths) ViewModel.AddPath(p); }
    private void WorkspaceDrop(object sender, DragEventArgs e) { RegisterDroppedPaths(e.Data); e.Handled = true; }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (ViewModel.SettingsBusy || ViewModel.WorkspaceBusy) { e.Cancel = true; return; }
        if (ViewModel.FileTasks.Busy) { e.Cancel = true; ViewModel.FileTasks.Cancel.Execute(null); return; }
        if (!TestMode) CaptureWorkspaceLayout();
        if (!TestMode && !ViewModel.Save() && MessageBox.Show("설정을 저장하지 못했습니다. 저장하지 않고 종료할까요?", "PinShelf", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) e.Cancel = true;
        if (!e.Cancel) ViewModel.Dispose(); base.OnClosing(e);
    }
}





