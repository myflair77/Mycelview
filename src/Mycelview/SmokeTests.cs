using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Mycelview;

// Opt-in integration checks. Only writes beneath the explicit test directory.
internal static class SmokeTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public static async Task RunAsync(MainWindow window, string directory, bool verify)
    {
        var vm = window.ViewModel;
        var results = new List<string>();
        if (verify)
        {
            Check(vm.Enlargement.Mode == "A" && vm.Enlargement.FixedSize && vm.Enlargement.Width == 900 && vm.Enlargement.Height == 600 && vm.Enlargement.Percent == 200 && vm.Enlargement.DelayMs == 450, "Card enlargement preferences must survive process restart.");
            results.Add("PASS: enlargement mode, size and hover delay restored in separate process.");
            Check(vm.SelectedSkinId == "vercel", "Selected skin must survive restart.");
            Check(vm.TextScalePercent == 101 && Math.Abs(window.FontSize - 12.221) < .001, "Text zoom must survive process restart and apply to window text.");
            results.Add("PASS: one-percent text zoom restored in new process.");
            Check(((SolidColorBrush)window.Background).Color == Color.FromRgb(250, 250, 250), "Restored skin must be applied to the window.");
            results.Add("PASS: saved Vercel skin restored and applied in new process.");
            Check(vm.Folders.Single(x=>x.Title=="결산").State.ColumnWidths.GetValueOrDefault("Modified")==123 && vm.Folders.Single(x=>x.Title=="결산").State.ColumnWidths.GetValueOrDefault("Length")==66,"Custom metadata widths must survive restart.");
            Check(vm.Cards.Count == 4, "Restart must restore all four cards.");
            var c = vm.Folders.Single(x => x.State.InitialPath.EndsWith("예산"));
            Check(vm.Folders.Count == 2 && vm.Favorites.Count == 2, "Restart must classify folders and file favorites.");
            Check(c.State.Collapsed && c.Note == "카드 메모", "Collapse and note must survive restart.");
            Check(c.CurrentPath.EndsWith("하위 폴더") && c.State.InitialPath.EndsWith("예산"), "Initial/current paths must be independent.");
            Check(vm.Favorites[0].Note == "파일 메모 B" && vm.Favorites[1].Note == "파일 메모 A", "Favorite drag order and duplicate-file notes must persist.");
            Check(vm.Folders[0].Title == "결산" && vm.Folders[1].Title == "예산", "Folder drag order must persist.");
            Check(vm.FolderHeight == 420 && vm.Folders.All(x => x.Height == 420), "Common height must persist for every folder.");
            Check(!vm.ShowModified && !vm.ShowSize && vm.ShowType && vm.ShowCreated && vm.ShowExtension, "Column settings must persist.");
            results.Add("PASS: folder/file order, shared card height and optional columns restored in a new process.");
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
            var restoredView = AllCards(window).Single(v => ((CardViewModel)v.DataContext).Title == "결산");
            var restoredList = (System.Windows.Controls.DataGrid)restoredView.FindName("FileList");
            Check(CardView.FindChild<System.Windows.Controls.ScrollViewer>(restoredList)!.VerticalOffset > 0, "Restart must restore visible list scroll offset.");
            results.Add("PASS: new process restored paths, geometry, collapse, and independent notes.");
            results.Add("PASS: new process restored actual list scroll offset.");
            foreach (var cardView in AllCards(window))
            {
                var card = (CardViewModel)cardView.DataContext;
                var expectedProperty = card.Title == "결산" ? "Name" : "Modified";
                var expectedDescending = card.Title == "결산";
                Check(card.State.SortProperty == expectedProperty && card.State.SortDescending == expectedDescending, "Each card must restore its own sorting settings.");
                Check(cardView.FileList.Columns.Single(x => x.SortMemberPath == expectedProperty).SortDirection == (expectedDescending ? System.ComponentModel.ListSortDirection.Descending : System.ComponentModel.ListSortDirection.Ascending), "Restored header arrow must match saved direction.");
                var entries = cardView.FileList.Items.Cast<FileEntry>().ToArray();
                var comparer = new FileEntryComparer(expectedProperty, expectedDescending);
                Check(entries.Zip(entries.Skip(1)).All(pair => comparer.Compare(pair.First, pair.Second) <= 0), "Restored visible rows must use saved sort.");
            }
            results.Add("PASS: independent card sort column, direction, header arrows and visible order restored in a new process.");
            Check(vm.RecentFiles.Count == 2 && vm.RecentFiles[0].Title == "예산 2027.xlsx", "Recent history must survive process restart.");
            results.Add("PASS: recent-file history restored in a new process.");
            Check(vm.FileTasks.CanRedo, "Redo history must survive actual process restart.");
            await vm.FileTasks.RedoAsync();
            var undoSource = Path.Combine(directory, "undo-fixtures", "source");
            Check(File.Exists(Path.Combine(undoSource, "pipeline.txt")), "Redo after process restart must restore renamed file.");
            await vm.FileTasks.UndoAsync();
            Check(File.Exists(Path.Combine(undoSource, "a.txt")), "Undo after restart and redo must restore original.");
            results.Add("PASS: persistent file undo/redo in a separate process.");
            await IndexTests.VerifyAsync(directory);
            await IndexManagementTests.VerifyAsync(directory);
            await WorkspaceTabTests.VerifyAsync(directory, vm.SelectedSkinId, vm.TextScalePercent);
            results.Add("PASS: workspace tabs, independent state and toggle restored in separate process.");
            results.Add("PASS: index management settings and committed generation survive process restart.");
            Check(Math.Abs(window.ExplorerColumn.ActualWidth - 300) < 1 && Math.Abs(window.FavoritesColumn.ActualWidth - 280) < 1 && Math.Abs(window.ExplorerTreeRow.ActualHeight - 180) < 1, "Pane boundaries must restore in a new process.");
            Check(Math.Abs(window.FavoritesRow.ActualHeight / (window.FavoritesRow.ActualHeight + window.RecentRow.ActualHeight) - 0.6) < 0.01, "Right pane split ratio must restore.");
            results.Add("PASS: persisted index, offline reconciliation and pane boundaries restored in a new process.");
        }
        else
        {
            Check(vm.SelectedSkinId == SkinManager.DefaultId, "New workspace must default to Windows 11.");
            Check(new WorkspaceState().SkinId == SkinManager.DefaultId && SkinManager.Normalize("unknown") == SkinManager.DefaultId, "Old settings and unknown skin must fall back to Windows 11.");
            Check(vm.Cards.Count == 0, "Use a fresh test directory.");
            var root = Path.Combine(directory, "fixtures");
            var a = Path.Combine(root, "예산"); var b = Path.Combine(root, "결산"); var child = Path.Combine(a, "하위 폴더");
            Directory.CreateDirectory(child); Directory.CreateDirectory(b);
            var file = Path.Combine(a, "업무 메모.txt"); File.WriteAllText(file, "Mycelview 개발 전용 테스트 파일");
            for (int i = 0; i < 180; i++) File.WriteAllText(Path.Combine(b, $"테스트-{i:000}.txt"), "test");
            await vm.Explorer.NavigateAsync(root);
            var treeNode = new FolderNode(root, "테스트 루트");
            await treeNode.ExpandAsync();
            Check(treeNode.Children.Count == 2 && treeNode.Children.Any(n => n.Path == a), "Tree must lazily enumerate real child folders.");
            window.ExplorerList.SelectedItems.Add(vm.Explorer.Entries.Single(e => e.FullPath == a));
            window.ExplorerList.SelectedItems.Add(vm.Explorer.Entries.Single(e => e.FullPath == b));
            window.RegisterSelectionButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            await vm.Explorer.NavigateAsync(a);
            window.ExplorerList.SelectedItems.Add(vm.Explorer.Entries.Single(e => e.FullPath == file));
            window.RegisterSelectionButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            window.RegisterDroppedPaths(new DataObject(DataFormats.FileDrop, new[] { file }));
            Check(vm.Folders.Count == 2 && vm.Favorites.Count == 2, "Selection and drop must route by item kind.");
            await vm.Explorer.NavigateAsync(Path.Combine(root, "missing"));
            Check(vm.Explorer.Status.Contains("실패") && vm.Explorer.CurrentPath == a, "Explorer errors must retain current folder.");
            await vm.Explorer.NavigateAsync(a);
            results.Add("PASS: explorer multi-selection button, drop registration, automatic folder/file separation and missing path recovery.");
            await Task.WhenAll(vm.Cards.Select(c => c.RefreshAsync()));
            Check(vm.Cards.Count == 4 && vm.Cards[0].Entries.Count == 2 && vm.Cards[1].Entries.Count == 180, "Real filesystem listings mismatch.");
            results.Add("PASS: two folders and two file favorites with real listings (180 items).");
            var c = vm.Cards[0]; c.Navigate(child); await c.RefreshAsync();
            Check(c.CurrentPath == child && c.Title == "예산", "Independent navigation/title failed.");
            c.Back.Execute(null); await c.RefreshAsync(); Check(c.CurrentPath == a, "Back failed.");
            c.Forward.Execute(null); await c.RefreshAsync(); Check(c.CurrentPath == child, "Forward failed.");
            c.Home.Execute(null); await c.RefreshAsync(); Check(c.CurrentPath == a, "Home failed.");
            c.Navigate(child); c.Up.Execute(null); await c.RefreshAsync(); Check(c.CurrentPath == a, "Up failed.");
            results.Add("PASS: back, forward, home, up, fixed title and independent navigation.");
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
            var view = CardView.FindChild<CardView>(window)!;
            Check(view != null, "WPF card must render.");
            var folderViews = AllCards(window).ToArray();
            Check(folderViews.Length == 2 && window.FavoritesList.Items.Count == 2, "Files must render as list rows, not cards.");
            var firstPoint = folderViews[0].TranslatePoint(new Point(), window);
            var secondPoint = folderViews[1].TranslatePoint(new Point(), window);
            Check(Math.Abs(firstPoint.Y - secondPoint.Y) < 1 && secondPoint.X > firstPoint.X + folderViews[0].ActualWidth, "Folders must arrange in two columns without overlap.");
            vm.Duplicate(c);
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
            var thirdView = AllCards(window).Last();
            Check(thirdView.TranslatePoint(new Point(), window).Y >= firstPoint.Y + folderViews[0].ActualHeight, "Third folder must flow to next row.");
            vm.Remove(vm.Cards.Last());
            vm.FolderHeight = 380;
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
            var icon = vm.Favorites[0].IconImage;
            Check(icon != null && icon.IsFrozen && vm.Explorer.Entries.All(e => e.IconImage != null), "Shell icons must be available for favorites and explorer entries.");
            Check(ReferenceEquals(icon, ShellIcons.ForPath(file, false)), "File type icon cache must reuse immutable images.");
            Check(ShellIcons.ForPath("example.xlsx", false) != null && ShellIcons.ForPath("example.hwp", false) != null, "Registered office types must resolve an icon or fallback.");
            var iconPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Background = Brushes.White };
            foreach (var extension in new[] { ".hwp", ".xlsx", ".pdf", ".txt", ".py", ".unknown" })
            {
                var item = new System.Windows.Controls.StackPanel { Width = 70 };
                item.Children.Add(new System.Windows.Controls.Image { Source = ShellIcons.ForPath("sample" + extension, false), Width = 16, Height = 16 });
                item.Children.Add(new System.Windows.Controls.TextBlock { Text = extension, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brushes.Black });
                iconPanel.Children.Add(item);
            }
            iconPanel.Measure(new Size(420, 44)); iconPanel.Arrange(new Rect(0, 0, 420, 44));
            var iconsBitmap = new RenderTargetBitmap(420, 44, 96, 96, PixelFormats.Pbgra32); iconsBitmap.Render(iconPanel);
            var iconsEncoder = new PngBitmapEncoder(); iconsEncoder.Frames.Add(BitmapFrame.Create(iconsBitmap));
            using (var iconsFile = File.Create(Path.Combine(directory, "file-type-icons.png"))) iconsEncoder.Save(iconsFile);
            results.Add("PASS: two-column row flow, one-column file list, Shell type icons and cache.");
            var listCard = AllCards(window).Single(v => ReferenceEquals(v.DataContext, vm.Cards[1]));
            var list = (System.Windows.Controls.DataGrid)listCard.FindName("FileList");
            var scroll = CardView.FindChild<System.Windows.Controls.ScrollViewer>(list)!;
            scroll.ScrollToVerticalOffset(35);
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
            var offset = scroll.VerticalOffset;
            Check(offset > 0 && vm.Cards[1].State.ScrollOffset > 0, $"List scroll position must be captured: offset={offset}, saved={vm.Cards[1].State.ScrollOffset}, extent={scroll.ExtentHeight}, viewport={scroll.ViewportHeight}, height={list.ActualHeight}.");
            await vm.Cards[1].RefreshAsync();
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
            Check(Math.Abs(scroll.VerticalOffset - offset) < 1, "Refresh must restore list scroll position.");
            vm.Cards[1].SetCollapsed(true); vm.Cards[1].SetCollapsed(false);
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
            Check(Math.Abs(scroll.VerticalOffset - offset) < 1, "Collapse/expand must preserve list scroll position.");
            results.Add("PASS: actual DataGrid scroll capture, refresh restoration and collapse preservation.");
            c.Note = "카드 메모"; c.NoteVisible = true; c.SetCollapsed(true); Check(!c.Expanded && c.DisplayHeight >= ((GridLength)Application.Current.Resources["CardHeaderHeight"]).Value + 2 && c.DisplayHeight < c.Height, "Collapse must fit scaled title without retaining full card height.");
            c.SetCollapsed(false); Check(c.DisplayHeight == 380, "Expand must restore height.");
            vm.Cards[2].Note = "파일 메모 A"; vm.Cards[3].Note = "파일 메모 B";
            results.Add("PASS: automatic layout cards, collapse/expand, independent notes.");
            var watchFile = Path.Combine(a, "감시 확인.txt"); File.WriteAllText(watchFile, "watch");
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (!c.Entries.Any(x => x.FullPath == watchFile) && DateTime.UtcNow < deadline) await Task.Delay(100);
            Check(c.Entries.Any(x => x.FullPath == watchFile), "Watcher did not refresh.");
            c.Navigate(Path.Combine(root, "없는 폴더")); await c.RefreshAsync();
            Check(c.Status.Contains("읽을 수 없습니다"), "Missing path must show an error.");
            c.Home.Execute(null); await c.RefreshAsync(); Check(c.Entries.Count == 3, "Recover from missing path failed.");
            results.Add("PASS: FileSystemWatcher and unavailable-path recovery.");
            c.Navigate(child); await c.RefreshAsync(); c.SetCollapsed(true);
            Check(vm.Save(), "State save failed.");
            var saved = new StateStore(directory).Load(); Check(saved.Cards.Count == 4, "Saved cards missing.");
            Check(File.ReadAllText(file) == "Mycelview 개발 전용 테스트 파일", "Original fixture was modified.");
            results.Add("PASS: persisted state and original file preserved.");
            var corruptDir = Path.Combine(directory, "corrupt-state"); Directory.CreateDirectory(corruptDir);
            File.WriteAllText(Path.Combine(corruptDir, "workspace.json"), "{broken");
            bool rejected = false; try { new StateStore(corruptDir).Load(); } catch (System.Text.Json.JsonException) { rejected = true; }
            Check(rejected, "Corrupt state must not silently become empty state.");
            results.Add("PASS: malformed settings detected.");
            await Capture(window, Path.Combine(directory, "windows11.png"));
            Check(window.Icon != null, "Window must use the Mycelview icon.");
            vm.TextScalePercent = 200;
            await Capture(window, Path.Combine(directory, "text-200.png"));
            Check(AllCards(window).All(v => Math.Abs(v.FileList.FontSize - 24.2) < .001), "Folder filenames must scale with text zoom.");
            vm.TextScalePercent = 100;

            var picker = (System.Windows.Controls.ComboBox)window.FindName("SkinPicker");
            picker.SelectedValue = "vercel";
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
            Check(vm.SelectedSkinId == "vercel", "Skin picker binding must update selection.");
            Check(((SolidColorBrush)window.Background).Color == Color.FromRgb(250, 250, 250), "Skin must change current window immediately.");
            Check(ReferenceEquals(vm.Favorites[0].IconImage, icon), "Skins must preserve original Shell icon images.");
            await Capture(window, Path.Combine(directory, "vercel.png"));
            picker.SelectedValue = "windows11";
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
            Check(((SolidColorBrush)window.Background).Color == Color.FromRgb(243, 243, 243), "Switching back must restore Windows palette.");
            picker.SelectedValue = "vercel";
            await DragInteractionTests.RunAsync(window, directory);
            results.Add("PASS: stable drag preview, immediate reversal, no collection churn and single move on release (see drag-results.txt).");
            var firstFolder = vm.Folders[0]; var secondFolder = vm.Folders[1];
            window.UpdateLayout();
            vm.BeginReorderPreview();
            vm.Reorder(firstFolder.State.Id, secondFolder.State.Id, true);
            window.UpdateLayout();
            var container = (FrameworkElement)window.FolderCards.ItemContainerGenerator.ContainerFromItem(firstFolder);
            if (SystemParameters.ClientAreaAnimation)
                Check(container.RenderTransform is TranslateTransform { HasAnimatedProperties: true }, "Reordered cards must animate to their new slots.");
            vm.Save();
            Check(new StateStore(directory).Load().Cards[0].Id == firstFolder.State.Id, "A drag preview must not persist before drop.");
            vm.EndReorderPreview(false);
            Check(vm.Folders[0] == firstFolder, "Cancelled drag must restore its original order.");
            vm.BeginReorderPreview();
            window.UpdateLayout();
            var favoriteToMove = vm.Favorites[0];
            vm.Reorder(vm.Favorites[0].State.Id, vm.Favorites[1].State.Id, true);
            window.UpdateLayout();
            var favoriteContainer = (FrameworkElement)window.FavoritesList.ItemContainerGenerator.ContainerFromItem(favoriteToMove);
            if (SystemParameters.ClientAreaAnimation)
                Check(favoriteContainer.RenderTransform is TranslateTransform { HasAnimatedProperties: true }, "Favorite rows must animate to their new slots.");
            vm.EndReorderPreview(false);
            results.Add("PASS: animated folder movement and cancelled folder/favorite preview; uncommitted order is not saved.");
            Check(!vm.Reorder(firstFolder.State.Id, firstFolder.State.Id, true), "Self-drop must not change order.");
            Check(!vm.Reorder(firstFolder.State.Id, vm.Favorites[0].State.Id, true), "Cross-section drops must not change order.");
            Check(vm.Reorder(firstFolder.State.Id, secondFolder.State.Id, true), "Forward folder reorder failed.");
            Check(vm.Reorder(firstFolder.State.Id, secondFolder.State.Id, false), "Backward folder reorder failed.");
            Check(vm.Reorder(firstFolder.State.Id, secondFolder.State.Id, true), "Final folder reorder failed.");
            Check(vm.Reorder(vm.Favorites[0].State.Id, vm.Favorites[1].State.Id, true), "Duplicate-path favorites must reorder by ID.");
            window.IncreaseHeightButton.Command.Execute(null);
            window.IncreaseHeightButton.Command.Execute(null);
            vm.ShowModified = false; vm.ShowSize = false; vm.ShowType = true; vm.ShowCreated = true; vm.ShowExtension = true;
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
            Check(vm.Folders.All(x => x.Height == 420), "Height control must affect all folders.");
            foreach (var folderView in AllCards(window))
            {
                var grid = (System.Windows.Controls.DataGrid)folderView.FindName("FileList");
                Check(grid.Columns[0].Visibility == Visibility.Visible && grid.Columns[1].Visibility == Visibility.Collapsed && grid.Columns[2].Visibility == Visibility.Collapsed && grid.Columns.Skip(3).All(col => col.Visibility == Visibility.Visible), "Visible columns must update in every card.");
                Check(grid.Columns[1].Header.ToString() == "수정한 날짜", "Date column label must match Windows.");
            }
            await Task.Delay(900);
            var reordered = new StateStore(directory).Load();
            Check(reordered.Cards[0].Title == "결산" && reordered.Cards[2].Note == "파일 메모 B" && reordered.FolderHeight == 420, "Debounced save must include displayed order and settings.");
            results.Add("PASS: forward/backward reorder, identity-based favorites, cross-section rejection, common height, live optional columns and autosave.");
            Check(vm.Save(), "Skin selection must save.");
            Check(vm.Cards.Count == 4 && c.Note == "카드 메모" && vm.Folders.Count == 2, "Skin switching must preserve workspace data.");
            results.Add("PASS: default/fallback skins, live picker switching, return to Windows, workspace preserved.");
            var sortCard = AllCards(window).First();
            var sortModel = (CardViewModel)sortCard.DataContext;
            var oldProperty = sortModel.State.SortProperty; var oldDescending = sortModel.State.SortDescending;
            var sortHandler = typeof(CardView).GetMethod("SortEntries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            foreach (var column in sortCard.FileList.Columns)
            {
                sortModel.State.SortProperty = "different"; sortModel.State.SortDescending = true;
                foreach (var expectedDescending in new[] { false, true, false })
                {
                    sortHandler.Invoke(sortCard, new object[] { sortCard.FileList, new System.Windows.Controls.DataGridSortingEventArgs(column) });
                    Check(sortModel.State.SortDescending == expectedDescending, "Header direction must alternate and new columns start ascending.");
                    Check(column.SortDirection == (expectedDescending ? System.ComponentModel.ListSortDirection.Descending : System.ComponentModel.ListSortDirection.Ascending), "Header indicator must match actual direction.");
                }
            }
            sortModel.State.SortProperty = oldProperty; sortModel.State.SortDescending = oldDescending;
            typeof(CardView).GetMethod("ApplySort", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(sortCard, null);
            var numbered = new[] { "10.txt", "2.txt", "1.txt" }.Select((name, i) => new FileEntry(Path.Combine(directory, name), name, false, DateTime.Today.AddDays(i), 100 - i)).ToArray();
            Array.Sort(numbered, new FileEntryComparer("Name", false));
            Check(numbered.Select(x => x.Name).SequenceEqual(new[] { "1.txt", "2.txt", "10.txt" }), "Natural name ascending order.");
            Array.Sort(numbered, new FileEntryComparer("Name", true));
            Check(numbered.Select(x => x.Name).SequenceEqual(new[] { "10.txt", "2.txt", "1.txt" }), "Natural name descending order.");
            Array.Sort(numbered, new FileEntryComparer("Length", false));
            Check(numbered[0].Length == 98, "Size uses numeric bytes.");
            Array.Sort(numbered, new FileEntryComparer("Modified", false));
            Check(numbered[0].Name == "10.txt", "Modified uses chronological ordering.");
            results.Add("PASS: all header directions alternate; natural numeric names, byte sizes and dates sort correctly.");
            await WorkspaceBackupTests.RunAsync(directory, vm.SelectedSkinId);
            results.Add("PASS: settings export/import and pre-restore backup safety (see settings-backup-results.txt).");
            await FileUndoTests.RunAsync(window, directory);
            results.Add("PASS: completed file operations undo and safety guards (see undo-results.txt).");
            await FileOperationTests.RunAsync(window, directory);
            results.Add("PASS: real Windows file operations, conflicts, recycle and cancellation (see file-operation-results.txt).");
            await CardFileDragTests.RunAsync(window, directory);
            results.Add("PASS: card-to-card routed file drag, batch copy/move and cancelled drag (see card-file-drag-results.txt).");
            ContextMenuTests.Run(directory);
            results.Add("PASS: context menus, multiple selection and non-destructive unregister (see context-menu-results.txt).");
            await SearchTests.RunAsync(window, directory);
            results.Add("PASS: search parser, traversal, cancellation, result registration and recent files (see search-results.txt).");
            window.ViewModel.Search.HideResults();
            window.UpdateLayout();
            var workspacePoint = window.FolderCards.TranslatePoint(new Point(), window);
            vm.Search.Query = "popup-layout-check";
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
            Check(window.SearchPopup.IsOpen && window.SearchResultsPanel.ActualWidth > 0, "Search results must open in a popup.");
            var savedLeft = window.Left; var savedTop = window.Top; var savedWidth = window.Width;
            window.Width = 1120; window.Left = 0; window.Top = 0;
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
            var anchorBefore = window.SearchBox.PointToScreen(new Point());
            var popupBefore = window.SearchResultsPanel.PointToScreen(new Point());
            var queryBeforeMove = vm.Search.Query; var countBeforeMove = vm.Search.Results.Count;
            window.Left = 24; window.Top = 20;
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
            var anchorDelta = window.SearchBox.PointToScreen(new Point()) - anchorBefore;
            var popupDelta = window.SearchResultsPanel.PointToScreen(new Point()) - popupBefore;
            Check((popupDelta - anchorDelta).Length < 2, "Search popup must follow owner movement in both axes.");
            Check(window.SearchPopup.IsOpen && vm.Search.Query == queryBeforeMove && vm.Search.Results.Count == countBeforeMove, "Moving must preserve open popup, query and results.");
            window.Left = savedLeft; window.Top = savedTop; window.Width = savedWidth;
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
            results.Add("PASS: open search popup follows owner screen coordinates without clearing results.");

            Check(window.FolderCards.TranslatePoint(new Point(), window) == workspacePoint, "Search popup must not push workspace content.");
            var panel = window.SearchResultsPanel;
            var popupBitmap = new RenderTargetBitmap((int)panel.ActualWidth, (int)panel.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            popupBitmap.Render(panel); var popupEncoder = new PngBitmapEncoder(); popupEncoder.Frames.Add(BitmapFrame.Create(popupBitmap));
            using (var output = File.Create(Path.Combine(directory, "search-popup.png"))) popupEncoder.Save(output);
            var noteCard = AllCards(window).First();
            noteCard.NoteButton.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent });
            Check(noteCard.NotePopup.IsOpen, "Hover must open the note popup.");
            Check(noteCard.NotePopup.StaysOpen, "Hover preview must not capture outside mouse input.");
            noteCard.NoteButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            noteCard.NoteButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(noteCard.NotePopup.IsOpen, "Repeated note clicks must keep editing open.");
            noteCard.NoteEditor.Text = "Hover note test";
            Check(((CardViewModel)noteCard.DataContext).Note == "Hover note test", "Popup edits must update the saved card note.");
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
            var noteOldLeft = window.Left;
            var noteAnchorBefore = noteCard.NoteButton.PointToScreen(new Point());
            var notePopupBefore = noteCard.NoteSurface.PointToScreen(new Point());
            window.Left += 20;
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
            Check(((noteCard.NoteSurface.PointToScreen(new Point()) - notePopupBefore) - (noteCard.NoteButton.PointToScreen(new Point()) - noteAnchorBefore)).Length < 2, "Note popup must track owner movement.");
            Check(noteCard.NotePopup.IsOpen && noteCard.NoteEditor.Text == "Hover note test", "Moving note popup must preserve open editor and text.");
            window.Left = noteOldLeft;
            var trackingMenu = new System.Windows.Controls.ContextMenu { PlacementTarget = window.SkinPicker };
            trackingMenu.Items.Add(new System.Windows.Controls.MenuItem { Header = "Popup tracking test" });
            trackingMenu.IsOpen = true;
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
            window.Left += 20;
            Check(!trackingMenu.IsOpen, "Menus must dismiss on owner movement.");
            window.Left = noteOldLeft;
            var trackingTip = new System.Windows.Controls.ToolTip { PlacementTarget = window.SkinPicker, Content = "Popup tracking test", IsOpen = true };
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
            window.Left += 20;
            Check(!trackingTip.IsOpen, "Tooltips must dismiss on owner movement.");
            window.Left = noteOldLeft;
            results.Add("PASS: note popup follows owner and preserves text; menus/tooltips dismiss instead of detaching.");

            window.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left) { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseDownEvent });
            Check(!noteCard.NotePopup.IsOpen, "Outside window click must close the note preview.");
            results.Add("PASS: note hover popup and immediate note binding.");
            vm.Search.Clear.Execute(null);
            results.Add("PASS: floating search popup preserves workspace layout.");
            vm.Search.Results.ReplaceAll(new[] {
                new FileEntry(Path.Combine(directory,"1.txt"),"1.txt",false,DateTime.Now,1),
                new FileEntry(Path.Combine(directory,"10-folder"),"10-folder",true,DateTime.Now,0),
                new FileEntry(Path.Combine(directory,"2-folder"),"2-folder",true,DateTime.Now,0) });
            var sortedSearch = window.SearchResults.Items.Cast<FileEntry>().ToArray();
            Check(sortedSearch[0].Name == "2-folder" && sortedSearch[1].Name == "10-folder" && !sortedSearch[2].IsDirectory, "Search name ascending must place folders first with natural ordering.");
            vm.Search.Results.Clear();
            Check(StateStore.Parse("{\"Version\":1,\"Cards\":[],\"TextScalePercent\":110}").TextScalePercent == 100, "Legacy text scale must migrate to new default 100 percent.");
            results.Add("PASS: search folders-first natural name sorting and new font baseline migration.");

            await IndexTests.RunAsync(window, directory);
            await IndexManagementTests.RunAsync(window, directory);
            await WorkspaceTabTests.RunAsync(directory, vm.SelectedSkinId, vm.TextScalePercent);
            await FolderPerformanceTests.RunAsync(directory, vm.SelectedSkinId, vm.TextScalePercent);
            var enlargedView = AllCards(window).First();
            var enlargedModel = (CardViewModel)enlargedView.DataContext;
            enlargedModel.State.ColumnWidths["Modified"]=123;
            enlargedModel.State.ColumnWidths["Length"]=66;
            enlargedModel.State.ColumnWidths["Type"]=111;
            enlargedView.ApplyColumnWidths(); window.UpdateLayout();
            var metadataWidth=enlargedView.TypeColumn.ActualWidth;
            var savedHeight = enlargedModel.State.Height;
            var savedCollapsed = enlargedModel.State.Collapsed;
            var oldWidth = enlargedView.ActualWidth;
            window.OpenCardEnlargement(enlargedView,false);
            Check(enlargedView.EnlargeHotspot.Height == 28,"Enlargement indicator must show active state.");
            await Task.Delay(100); window.UpdateLayout();
            if (!window.IsCardEnlarged(enlargedView) || enlargedView.ActualWidth <= oldWidth || enlargedModel.State.Height != savedHeight || enlargedModel.State.Collapsed != savedCollapsed)
                throw new InvalidOperationException("Card enlargement must grow without modifying saved layout.");
            Check(Math.Abs(enlargedView.TypeColumn.ActualWidth-metadataWidth)<1,"Enlargement must preserve metadata width and give extra room to name.");
            var enlargedBitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            enlargedBitmap.Render(window); var enlargedEncoder = new PngBitmapEncoder(); enlargedEncoder.Frames.Add(BitmapFrame.Create(enlargedBitmap));
            using (var image = File.Create(Path.Combine(directory, "card-enlargement.png"))) enlargedEncoder.Save(image);
            var oldWindowWidth = window.Width; window.Width = oldWindowWidth - 80; window.UpdateLayout();
            if (enlargedView.ActualWidth > window.ActualWidth) throw new InvalidOperationException("Enlargement must stay inside resized window.");
            window.CloseCardEnlargement(); window.Width = oldWindowWidth; await Task.Delay(100); window.UpdateLayout();
            if (window.IsCardEnlarged(enlargedView) || Math.Abs(enlargedView.ActualWidth - oldWidth) > 2 || enlargedModel.State.Height != savedHeight)
                throw new InvalidOperationException("Closing enlargement must restore original card slot.");
            Check(enlargedView.ModifiedColumn.Width.Value==123 && enlargedView.SizeColumn.Width.Value==66 && enlargedView.TypeColumn.Width.Value==111,"Shrink must restore custom column widths.");
            Check(enlargedView.EnlargementPin.Visibility == Visibility.Visible && enlargedView.EnlargementPin.Opacity == .25,"Indicator must restore idle state.");
            results.Add("PASS: enlargement indicator, larger in-window card, resize containment and original layout restoration.");
            for(int columns=1;columns<=5;columns++)
            {
                var layout = new FolderGridPanel { Columns=columns };
                for(int i=0;i<8;i++) layout.Children.Add(new System.Windows.Controls.Border { Height=100, DataContext=i==1 ? enlargedModel : new object() });
                layout.Measure(new Size(1000,double.PositiveInfinity)); layout.Arrange(new Rect(0,0,1000,layout.DesiredSize.Height));
                Check(Math.Abs(layout.DesiredSize.Height-Math.Ceiling(8d/columns)*100)<1,"Grid row count must match selected columns.");
                for(int i=0;i<8;i++)
                {var rect=System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot((FrameworkElement)layout.Children[i]);Check(Math.Abs(rect.X-i%columns*1000d/columns)<1 && Math.Abs(rect.Y-i/columns*100)<1,"Cards must flow in selected column count.");}
                layout.ExpandedCard=enlargedModel;layout.ExpandedWidth=800;((FrameworkElement)layout.Children[1]).Height=200;layout.InvalidateMeasure();
                layout.Measure(new Size(1000,double.PositiveInfinity));layout.Arrange(new Rect(0,0,1000,layout.DesiredSize.Height));
                var rects=layout.Children.Cast<FrameworkElement>().Select(System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot).ToArray();
                for(int i=0;i<rects.Length;i++) for(int j=i+1;j<rects.Length;j++) {var intersection=Rect.Intersect(rects[i],rects[j]);Check(intersection.IsEmpty||intersection.Width<1||intersection.Height<1,"B enlarged and neighboring cards must not overlap for all column counts.");}
            }
            vm.FolderColumns=4;window.UpdateLayout();await Task.Delay(100);await Capture(window,Path.Combine(directory,"four-columns.png"));vm.FolderColumns=2;window.UpdateLayout();
            var savedEnlargement = vm.Enlargement.Normalize();
            vm.SetEnlargement(new CardEnlargementSettings { Mode="B", FixedSize=false, Percent=175 });
            var peerView = AllCards(window).First(v => v != enlargedView);
            var peerSlot = FileContextMenus.Parent<ViewportCardHost>(peerView)!;
            var peerBefore = peerSlot.TranslatePoint(new Point(), window);
            window.OpenCardEnlargement(enlargedView, false);
            await Task.Delay(100); window.UpdateLayout();
            Check(window.IsCardEnlarged(enlargedView) && enlargedView.ActualWidth > oldWidth, "B must enlarge the actual card.");
            Check(Math.Abs(peerSlot.TranslatePoint(new Point(),window).Y-peerBefore.Y)<2 && peerSlot.ActualWidth < oldWidth, "B must shrink neighbors on the same row.");
            await Capture(window,Path.Combine(directory,"card-enlargement-B.png"));
            window.CloseCardEnlargement(); await Task.Delay(100); window.UpdateLayout();
            Check(Math.Abs(peerSlot.TranslatePoint(new Point(),window).Y-peerBefore.Y)<2,"B close must restore the two-column row.");
            vm.SetEnlargement(new CardEnlargementSettings { Mode="C" });
            window.OpenCardEnlargement(enlargedView,true);
            Check(!window.IsCardEnlarged(enlargedView),"C must disable enlargement.");
            vm.SetEnlargement(new CardEnlargementSettings { Mode="A",FixedSize=true,Width=900,Height=600,Percent=200,DelayMs=450 });
            window.OpenCardEnlargement(enlargedView,false);await Task.Delay(100);window.UpdateLayout();
            Check(Math.Abs(enlargedView.ActualWidth-894)<3 && Math.Abs(enlargedView.ActualHeight-594)<3,"A fixed size must apply to overlay bounds.");
            window.CloseCardEnlargement();
            var enlargementBackup=Path.Combine(directory,"enlargement-backup.pinshelf.json");vm.ExportSettings(enlargementBackup);
            var restoredEnlargement=WorkspaceBackup.Read(enlargementBackup).Enlargement;
            Check(restoredEnlargement.FixedSize && restoredEnlargement.Width==900 && restoredEnlargement.DelayMs==450,"Enlargement preferences must round-trip through settings backup.");
            var clamped=new CardEnlargementSettings {Mode="invalid",Percent=900,Width=2,Height=9000,DelayMs=-2}.Normalize();
            Check(clamped.Mode=="A" && clamped.Percent==300 && clamped.Width==400 && clamped.Height==1800 && clamped.DelayMs==0,"Invalid enlargement preferences must normalize safely.");
            results.Add("PASS: A/B/C enlargement, B neighboring layout restore, fixed dimensions, settings backup and validation.");
            await FilePreviewTests.RunAsync(window, directory);
            results.Add("PASS: bounded image/text preview, menu and Space/Esc, selection updates and cancellation (file-preview-results.txt).");
            results.Add("PASS: progressive folder loading, bounded I/O, viewport reclamation and restored interaction (folder-performance-results.txt).");
            results.Add("PASS: workspace lifecycle, shared undo, all-tab backups and alternating collapse button (workspace-tab-results.txt).");
            results.Add("PASS: index management, exclusions, rebuild availability, cancellation and retries (index-management-results.txt).");
            results.Add("PASS: index, watcher updates, immediate search and saved pane boundaries (see index-results.txt).");
            foreach (var cardView in AllCards(window))
            {
                var card = (CardViewModel)cardView.DataContext;
                var property = card.Title == "결산" ? "Name" : "Modified";
                card.State.SortProperty = "different";
                var column = cardView.FileList.Columns.Single(x => x.SortMemberPath == property);
                var handler = typeof(CardView).GetMethod("SortEntries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                handler.Invoke(cardView, new object[] { cardView.FileList, new System.Windows.Controls.DataGridSortingEventArgs(column) });
                if (card.Title == "결산") handler.Invoke(cardView, new object[] { cardView.FileList, new System.Windows.Controls.DataGridSortingEventArgs(column) });
            }
            results.Add("PASS: distinct card sort preferences saved through header sorting before process exit.");
            vm.TextScalePercent = 101;
            Check(Math.Abs((double)Application.Current.Resources["FontSize11"] - 12.221) < .001, "Text zoom must change by one percent.");
            vm.Save();
        }
        await Capture(window, Path.Combine(directory, verify ? "restart.png" : "smoke.png"));
        if (!verify)
        {
            var restartSource = Path.Combine(directory, "undo-fixtures", "source", "a.txt");
            await vm.FileTasks.ExecuteAsync("재시작 검증", [new(FileAction.Rename, restartSource, Name: "pipeline.txt")]);
            await vm.FileTasks.UndoAsync();
            Check(vm.FileTasks.CanRedo, "Restart fixture must finish with redo available.");
        }
        File.WriteAllLines(Path.Combine(directory, verify ? "restart-results.txt" : "test-results.txt"), results);
    }
    private static async Task Capture(MainWindow window, string path)
    {
        await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path)) encoder.Save(stream);
    }
    private static IEnumerable<CardView> AllCards(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is CardView card) yield return card;
            foreach (var nested in AllCards(child)) yield return nested;
        }
    }
}














