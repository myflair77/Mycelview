using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Mycelview;
internal sealed record ContextTarget(string Path, bool Folder, CardViewModel? Registration = null);
internal sealed class FileContextMenus(MainViewModel vm)
{
    internal Action<string>? PreviewPath { get; set; }
    internal Action<string?>? UpdatePreview { get; set; }
    internal Action? ClosePreview { get; set; }
    internal Func<bool>? PreviewIsOpen { get; set; }
    internal Action<string> OpenPath { get; set; } = FileService.Open;
    internal Action<string> RevealPath { get; set; } = ShellActions.Reveal;
    internal Action<string> ShowProperties { get; set; } = ShellActions.Properties;
    internal Action<string> CopyText { get; set; } = Clipboard.SetText;
    internal Action<IEnumerable<string>, bool> CopyFiles { get; set; } = vm.FileTasks.PutClipboard;
    internal Func<string, Task> PasteFiles { get; set; } = vm.FileTasks.PasteAsync;

    private sealed record NativeRequest(ContextTarget[] Selected);
    internal ContextMenu Build(ContextTarget[] selected, Action? openSingle = null, string? directory = null)
    {
        var menu = new ContextMenu();
        if (selected.Any(item => DesktopItems.IsShellPath(item.Path)))
        {
            var open = new MenuItem { Header = "열기" };
            open.Click += (_, _) => { foreach (var item in selected) OpenPath(item.Path); };
            menu.Items.Add(open); return menu;
        }
        if (selected.Length == 0) AppendManagement(menu, [], directory);
        else menu.Tag = new NativeRequest(selected.DistinctBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToArray());
        return menu;
    }
    private async void ShowNative(FrameworkElement owner, ContextTarget[] selected, bool keyboard)
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(Window.GetWindow(owner)).Handle;
            var paths = selected.Select(x => x.Path).ToArray();
            var registrations = vm.Cards.Where(c => selected.Any(x => x.Registration == c ||
                string.Equals(x.Path, c.State.InitialPath, StringComparison.OrdinalIgnoreCase))).ToArray();
            uint command; string verb;
            using (var menu = new NativeShellMenu(paths, hwnd))
            {
                menu.AddMycelviewItems(selected.All(x => !x.Folder), selected.Length == 1,
                    vm.FileTasks.CanUndo && !vm.FileTasks.Busy, registrations.Length > 0);
                command = menu.Track(owner, keyboard);
                verb = menu.Verb(command).ToLowerInvariant();
                if (command == 0) return;
                // Close search results before a command opens another window (preview,
                // rename or a Shell dialog); its topmost Popup would cover that window.
                vm.Search.HideResults();
                if ((command <= NativeShellMenu.Last || command >= 0x7100) && verb is not ("cut" or "copy" or "rename" or "delete"))
                { menu.Invoke(command, hwnd); return; }
            }
            await ExecuteMycelviewCommandAsync(command, verb, selected);
        }
        catch (Exception error) { vm.Status = "Windows 메뉴 실행 실패: " + error.Message; }
    }
    internal async Task ExecuteMycelviewCommandAsync(uint command, string verb, ContextTarget[] selected)
    {
        var paths = selected.Select(x => x.Path).ToArray();
        var registrations = vm.Cards.Where(c => selected.Any(x => x.Registration == c ||
            string.Equals(x.Path, c.State.InitialPath, StringComparison.OrdinalIgnoreCase))).ToArray();
            switch (command)
            {
                case NativeShellMenu.Preview: if (selected.Length == 1 && !selected[0].Folder) PreviewPath?.Invoke(paths[0]); break;
                case NativeShellMenu.Reveal:
                    foreach (var item in selected) { if (item.Folder) OpenPath(item.Path); else RevealPath(item.Path); }
                    break;
                case NativeShellMenu.AddFolder:
                case NativeShellMenu.AddFile:
                    foreach (var item in selected.Where(x => x.Folder == (command == NativeShellMenu.AddFolder)))
                        vm.AddPath(item.Path);
                    break;
                case NativeShellMenu.Undo: await vm.FileTasks.UndoAsync(); break;
                case NativeShellMenu.Unregister:
                    foreach (var card in registrations) vm.Remove(card);
                    vm.Status = $"{registrations.Length}개 등록 해제 · 원본 유지";
                    break;
                default:
                    if (vm.FileTasks.Busy) { vm.Status = "진행 중인 파일 작업이 끝난 뒤 다시 시도하세요."; break; }
                    if (verb is "copy" or "cut") vm.FileTasks.PutClipboard(paths, verb == "cut");
                    else if (verb == "rename") await vm.FileTasks.RenameAsync(paths);
                    else if (verb == "delete") await vm.FileTasks.RecycleAsync(paths);
                    break;
            }
    }
    private void AppendManagement(ContextMenu menu, ContextTarget[] selected, string? directory)
    {
        void Add(string id, string title, Func<Task> action, bool enabled = true)
        {
            var item = new MenuItem { Header = title, Tag = id, IsEnabled = enabled && !vm.FileTasks.Busy };
            item.Click += async (_, _) => { try { await action(); } catch (Exception error) { vm.Status = title + " 실패: " + error.Message; } };
            menu.Items.Add(item);
        }
        if (menu.Items.Count > 0) menu.Items.Add(new Separator());
        var paths = selected.Select(x => x.Path).ToArray();
        var suffix = paths.Length > 1 ? $" ({paths.Length}개)" : "";
        Add("redo", vm.FileTasks.RedoLabel, () => vm.FileTasks.RedoAsync(), vm.FileTasks.CanRedo);
        Add("undo", vm.FileTasks.UndoLabel, () => vm.FileTasks.UndoAsync(), vm.FileTasks.CanUndo);
        if (paths.Length > 0)
        {
            Add("file-copy", "복사" + suffix, () => { vm.FileTasks.PutClipboard(paths, false); return Task.CompletedTask; });
            Add("file-cut", "잘라내기" + suffix, () => { vm.FileTasks.PutClipboard(paths, true); return Task.CompletedTask; });
        }
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Add("paste", "이 폴더에 붙여넣기", () => vm.FileTasks.PasteAsync(directory), FileTasks.CanPaste && System.IO.Directory.Exists(directory));
            Add("new-folder", "새 폴더 만들기", () => vm.FileTasks.NewFolderAsync(directory), System.IO.Directory.Exists(directory));
            foreach (var item in menu.Items.OfType<MenuItem>().Where(x => Equals(x.Tag, "paste") || Equals(x.Tag, "new-folder"))) item.ToolTip = directory;
        }
        if (paths.Length > 0)
        {
            Add("rename", "이름 변경…" + suffix, () => vm.FileTasks.RenameAsync(paths));
            Add("recycle", "휴지통으로 이동…" + suffix, () => vm.FileTasks.RecycleAsync(paths));
        }
    }
    private void AttachKeys(FrameworkElement owner, Func<string[]> selection, Func<string?> destination)
    {
        bool Editing(object? source) => Parent<TextBoxBase>(source as DependencyObject) != null || Parent<PasswordBox>(source as DependencyObject) != null;
        // WPF controls and IMEs can route clipboard shortcuts as commands instead of ordinary KeyDown events.
        foreach (var command in new[] { ApplicationCommands.Copy, ApplicationCommands.Cut, ApplicationCommands.Paste })
        {
            owner.CommandBindings.Add(new CommandBinding(command, async (_, e) =>
            {
                if (Editing(e.OriginalSource)) return;
                e.Handled = true;
                if (vm.FileTasks.Busy) return;
                try
                {
                    if (command == ApplicationCommands.Paste)
                    {
                        var folder = destination();
                        if (string.IsNullOrEmpty(folder)) vm.Status = "붙여넣을 폴더 또는 같은 폴더의 파일을 선택하세요.";
                        else await PasteFiles(folder);
                    }
                    else CopyFiles(selection(), command == ApplicationCommands.Cut);
                }
                catch (Exception error) { vm.Status = "파일 작업 실패: " + error.Message; }
            }, (_, e) =>
            {
                if (Editing(e.OriginalSource)) return;
                e.CanExecute = !vm.FileTasks.Busy && (command == ApplicationCommands.Paste || selection().Length > 0);
                e.Handled = true;
            }));
        }
        owner.PreviewKeyDown += async (_, e) =>
        {
            if (Editing(e.OriginalSource) || vm.FileTasks.Busy) return;
            var key = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key == Key.System ? e.SystemKey : e.Key;
            if (Keyboard.Modifiers == ModifierKeys.Control && key is Key.C or Key.X or Key.V)
            {
                var command = key == Key.C ? ApplicationCommands.Copy : key == Key.X ? ApplicationCommands.Cut : ApplicationCommands.Paste;
                e.Handled = true;
                if (command.CanExecute(null, owner)) command.Execute(null, owner);
                return;
            }
            var keys = Keyboard.Modifiers; var paths = selection(); var folder = destination();
            try
            {
                if (keys == ModifierKeys.Control && e.Key == Key.Y) { e.Handled = true; await vm.FileTasks.RedoAsync(); }
                else if (keys == ModifierKeys.Control && e.Key == Key.Z)
                { e.Handled = true; await vm.FileTasks.UndoAsync(); }
                else if (keys == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.N && !string.IsNullOrEmpty(folder))
                { e.Handled = true; await vm.FileTasks.NewFolderAsync(folder); }
                else if (keys == ModifierKeys.None && e.Key == Key.F2 && paths.Length > 0)
                { e.Handled = true; await vm.FileTasks.RenameAsync(paths); }
                else if (keys == ModifierKeys.None && e.Key == Key.Delete && paths.Length > 0)
                { e.Handled = true; await vm.FileTasks.RecycleAsync(paths); }
            }
            catch (Exception error) { vm.Status = "파일 작업 실패: " + error.Message; }
        };
    }
    internal static T? Parent<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node != null)
        {
            if (node is T value) return value;
            node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
        return null;
    }
    internal static void SelectForContext(System.Collections.IList selection, object item)
    { if (!selection.Contains(item)) { selection.Clear(); selection.Add(item); } }
    private void Show(FrameworkElement owner, ContextMenu menu, bool keyboard)
    {
        if (menu.Tag is NativeRequest request) { ShowNative(owner, request.Selected, keyboard); return; }
        if (menu.Items.Count == 0) return;
        menu.PlacementTarget = owner; menu.Placement = keyboard ? PlacementMode.Bottom : PlacementMode.MousePoint;
        menu.IsOpen = true;
    }
    private void AttachPreview(Selector selector, Func<object, ContextTarget> target)
    {
        selector.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && PreviewIsOpen?.Invoke() == true) { e.Handled = true; ClosePreview?.Invoke(); return; }
            if (e.Key != Key.Space || Keyboard.Modifiers != ModifierKeys.None || Parent<TextBoxBase>(e.OriginalSource as DependencyObject) != null) return;
            if (selector.SelectedItem is not { } item) return;
            var selected = target(item); if (selected.Folder || DesktopItems.IsShellPath(selected.Path)) return;
            e.Handled = true; PreviewPath?.Invoke(selected.Path);
        };
        selector.SelectionChanged += (_, e) =>
        {
            if (PreviewIsOpen?.Invoke() != true || !selector.IsKeyboardFocusWithin && !selector.IsMouseOver || e.OriginalSource != selector) return;
            var item = e.AddedItems.Cast<object>().LastOrDefault() ?? selector.SelectedItem;
            var selected = item == null ? null : target(item);
            UpdatePreview?.Invoke(selected is { Folder: false } && !DesktopItems.IsShellPath(selected.Path) ? selected.Path : null);
        };
    }
    internal void AttachGrid(DataGrid grid, Action<FileEntry>? singleOpen = null, Func<string?>? directory = null)
    {
        AttachPreview(grid, item => { var entry = (FileEntry)item; return new(entry.FullPath, entry.IsDirectory); });
        ContextMenu Menu() { var entries = grid.SelectedItems.Cast<FileEntry>().ToArray(); return Build(entries.Select(x => new ContextTarget(x.FullPath, x.IsDirectory)).ToArray(), entries.Length == 1 && singleOpen != null ? () => singleOpen(entries[0]) : null, directory?.Invoke()); }
        AttachKeys(grid, () => grid.SelectedItems.Cast<FileEntry>().Where(x => !x.IsShellItem).Select(x => x.FullPath).ToArray(), () => directory?.Invoke() ?? PasteDestination(grid.SelectedItems.Cast<FileEntry>().Where(x => !x.IsShellItem).Select(x => new ContextTarget(x.FullPath, x.IsDirectory))));
        grid.PreviewMouseRightButtonDown += (_, e) =>
        {
            var row = Parent<DataGridRow>(e.OriginalSource as DependencyObject);
            if (row?.Item is not FileEntry entry)
            { if (Parent<DataGridColumnHeader>(e.OriginalSource as DependencyObject) == null && directory != null) { e.Handled = true; Show(grid, Build([], directory: directory()), false); } return; }
            SelectForContext(grid.SelectedItems, entry); grid.Focus(); e.Handled = true; Show(row, Menu(), false);
        };
        grid.ContextMenu = new ContextMenu();
        grid.ContextMenuOpening += (_, e) => { e.Handled = true; if (e.CursorLeft < 0) Show(grid, Menu(), true); };
    }
    internal void AttachList(ListBox list, Func<object, ContextTarget> target)
    {
        list.SelectionMode = SelectionMode.Extended;
        AttachPreview(list, target);
        AttachKeys(list, () => list.SelectedItems.Cast<object>().Select(x => target(x).Path).ToArray(), () => PasteDestination(list.SelectedItems.Cast<object>().Select(target)));
        ContextMenu Menu() => Build(list.SelectedItems.Cast<object>().Select(target).ToArray());
        list.PreviewMouseRightButtonDown += (_, e) =>
        {
            if (Parent<TextBox>(e.OriginalSource as DependencyObject) != null) return;
            var row = Parent<ListBoxItem>(e.OriginalSource as DependencyObject);
            if (row == null) return;
            SelectForContext(list.SelectedItems, row.DataContext); list.Focus(); e.Handled = true; Show(row, Menu(), false);
        };
        list.ContextMenu = new ContextMenu();
        list.ContextMenuOpening += (_, e) => { if (Parent<TextBox>(e.OriginalSource as DependencyObject) != null) return; e.Handled = true; if (e.CursorLeft < 0) Show(list, Menu(), true); };
    }
    internal void AttachHeader(FrameworkElement header, Func<CardViewModel> card)
    {
        AttachKeys(header, () => [card().CurrentPath], () => card().IsFolder ? card().CurrentPath : System.IO.Path.GetDirectoryName(card().CurrentPath));
        header.ContextMenu = new ContextMenu();
        header.ContextMenuOpening += (_, e) =>
        { e.Handled = true; var c = card(); Show(header, Build([new(c.CurrentPath, c.IsFolder, c)]), e.CursorLeft < 0); };
    }
    internal static string? PasteDestination(IEnumerable<ContextTarget> selected)
    {
        var items = selected.ToArray();
        if (items.Length == 1 && items[0].Folder) return items[0].Path;
        var parents = items.Select(x => System.IO.Path.GetDirectoryName(x.Path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return parents.Length == 1 ? parents[0] : null;
    }
    internal void AttachTree(TreeView tree)
    {
        AttachKeys(tree, () => tree.SelectedItem is FolderNode n && n.Path.Length > 0 ? [n.Path] : [], () => (tree.SelectedItem as FolderNode)?.Path);
        tree.ContextMenu = new ContextMenu();
        tree.PreviewMouseRightButtonDown += (_, e) =>
        {
            if (Parent<TreeViewItem>(e.OriginalSource as DependencyObject) is not { DataContext: FolderNode node } row || node.Path.Length == 0) return;
            row.IsSelected = true; tree.Focus(); e.Handled = true; Show(row, Build([new(node.Path, true)]), false);
        };
        tree.ContextMenuOpening += (_, e) =>
        { e.Handled = true; if (e.CursorLeft < 0 && tree.SelectedItem is FolderNode node && node.Path.Length > 0) Show(tree, Build([new(node.Path, true)]), true); };
    }
}
