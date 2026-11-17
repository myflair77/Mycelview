using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
namespace Mycelview;
internal static class ClipboardShortcutTests
{
    internal static void Run(MainViewModel vm, string directory)
    {
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        string root = Path.GetFullPath(directory);
        string first = Path.Combine(root, "first.txt"), second = Path.Combine(root, "second.txt");
        string[] copied = []; bool cut = false; string? pasted = null;
        var menus = new FileContextMenus(vm)
        {
            CopyFiles = (paths, isCut) => { copied = paths.ToArray(); cut = isCut; },
            PasteFiles = folder => { pasted = folder; return Task.CompletedTask; }
        };
        var grid = new DataGrid { SelectionMode = DataGridSelectionMode.Extended, SelectionUnit = DataGridSelectionUnit.FullRow };
        grid.AutoGenerateColumns = false;
        grid.Columns.Add(new DataGridTextColumn { Header = "이름", Binding = new System.Windows.Data.Binding("Name") });
        grid.Items.Add(new FileEntry(first, "first.txt", false, DateTime.Now, 1));
        grid.Items.Add(new FileEntry(second, "second.txt", false, DateTime.Now, 1));
        grid.SelectedItems.Add(grid.Items[0]); grid.SelectedItems.Add(grid.Items[1]);
        menus.AttachGrid(grid, directory: () => root);
        Check(ApplicationCommands.Copy.CanExecute(null, grid), "File copy command must be enabled.");
        ApplicationCommands.Copy.Execute(null, grid);
        Check(!cut && copied.SequenceEqual(new[] { first, second }), "Grid copy must carry selected file paths, not cell text.");
        ApplicationCommands.Cut.Execute(null, grid);
        Check(cut && copied.Length == 2, "Grid cut must keep multiple selected files.");
        ApplicationCommands.Paste.Execute(null, grid);
        Check(pasted == root, "Folder grids must paste into their current directory.");
        // A real click focuses a DataGridCell, so commands must also work from
        // that descendant rather than only when targeted directly at the grid.
        var host = new System.Windows.Window { Content = grid, Width = 400, Height = 200, ShowInTaskbar = false };
        try
        {
            host.Show(); host.UpdateLayout();
            grid.ScrollIntoView(grid.Items[0]); grid.UpdateLayout();
            T? Find<T>(System.Windows.DependencyObject node) where T : System.Windows.DependencyObject
            {
                if (node is T found) return found;
                for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++)
                    if (Find<T>(System.Windows.Media.VisualTreeHelper.GetChild(node, i)) is { } child) return child;
                return null;
            }
            var cell = Find<DataGridCell>(grid) ?? throw new Exception("Expected a realized file cell.");
            cell.Focus(); copied = []; pasted = null;
            ApplicationCommands.Copy.Execute(null, cell);
            Check(!cut && copied.Length == 2, "Focused cell copy must reach file commands before DataGrid cell-text copy.");
            ApplicationCommands.Cut.Execute(null, cell);
            Check(cut && copied.Length == 2, "Focused cell cut must preserve the selected paths.");
            ApplicationCommands.Paste.Execute(null, cell);
            Check(pasted == root, "Focused cell paste must reach the current folder.");
            cell.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
            Check(ReferenceEquals(Keyboard.FocusedElement, grid), "Click release must focus the stable file list rather than a recyclable cell.");
            host.Content = null;
            var overlay = new System.Windows.Controls.Border { Child = grid };
            host.Content = overlay; host.UpdateLayout();
            cell = Find<DataGridCell>(grid) ?? throw new Exception("Expected cell after reparenting.");
            cell.Focus();
            cell.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
            Check(ReferenceEquals(Keyboard.FocusedElement, grid), "Floating reparenting must retain stable list focus after click.");
            grid.SelectedItems.Clear(); grid.Items.Clear(); host.UpdateLayout();
            grid.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
            Check(ReferenceEquals(Keyboard.FocusedElement, grid), "Empty folder click must focus the paste destination.");
            pasted = null; ApplicationCommands.Paste.Execute(null, grid);
            Check(pasted == root, "Empty folder paste must retain its directory.");
        }
        finally { host.Close(); }
        var list = new ListBox(); list.Items.Add(new ContextTarget(first, false)); list.SelectedIndex = 0;
        menus.AttachList(list, item => (ContextTarget)item);
        copied = []; pasted = null;
        ApplicationCommands.Copy.Execute(null, list); ApplicationCommands.Paste.Execute(null, list);
        Check(copied.SequenceEqual(new[] { first }) && pasted == root, "Favorites and recent-file commands must use actual file paths and parent destination.");
        var search = new DataGrid(); search.Items.Add(new FileEntry(root, "folder", true, DateTime.Now, 0)); search.SelectedIndex = 0;
        menus.AttachGrid(search); pasted = null; ApplicationCommands.Paste.Execute(null, search);
        Check(pasted == root, "Search selection must accept paste into the selected folder.");
        Check(FileContextMenus.PasteDestination([new(first, false), new(Path.Combine(root, "other", "file.txt"), false)]) == null, "Mixed parent folders must not choose an arbitrary paste destination.");
    }
}
