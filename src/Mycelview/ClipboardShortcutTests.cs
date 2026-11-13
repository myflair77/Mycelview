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
