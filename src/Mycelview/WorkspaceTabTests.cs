using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace Mycelview;
internal static class WorkspaceTabTests
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    internal static async Task RunAsync(string directory, string skin, int zoom)
    {
        var root = Path.Combine(directory, "workspace-tab-tests"); Directory.CreateDirectory(root);
        var folder = Path.Combine(root, "folder"); Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "original.txt"); File.WriteAllText(file, "original data");
        var store = new StateStore(root);
        store.Save(new WorkspaceState { Cards = [new() { InitialPath = folder, CurrentPath = folder, Title = "folder", Note = "기본 메모" }, new() { InitialPath = file, CurrentPath = file, Title = "original.txt", IsFile = true }] });
        var vm = new MainViewModel(store); var window = new MainWindow(vm) { TestMode = true, Title = "Mycelview — Workspace tests" }; window.Show();
        try
        {
            await vm.InitializeAsync();
            Check(vm.Workspaces.Count == 1 && vm.ActiveWorkspace!.Name == "기본 작업공간" && vm.Cards.Count == 2, "Legacy cards migrate intact into default workspace.");
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
            Check(window.WorkspaceHint.Visibility == Visibility.Visible && window.WorkspaceHint.Text == "＋를 눌러 작업공간을 추가하세요", "Single-tab hint is visible with requested wording.");
            var original = vm.ActiveWorkspace!;
            bool lastRejected = false; try { await vm.DeleteWorkspaceAsync(original.Id); } catch (InvalidOperationException) { lastRejected = true; }
            Check(lastRejected && vm.Workspaces.Count == 1, "Last workspace cannot be deleted.");
            vm.ToggleFolders.Execute(null);
            Check(vm.Folders.All(x => x.State.Collapsed) && vm.ToggleFoldersLabel == "모두 펼치기", "First toggle collapses all and changes next action.");
            vm.ToggleFolders.Execute(null);
            Check(vm.Folders.All(x => !x.State.Collapsed) && vm.ToggleFoldersLabel == "모두 접기", "Second toggle expands all.");
            vm.ToggleFolders.Execute(null);
            vm.FolderHeight = 460; vm.ShowModified = false; vm.FolderColumns = 5;
            vm.Folders[0].State.SortProperty = "Length"; vm.Folders[0].State.SortDescending = true;
            window.ExplorerColumn.Width = new GridLength(310); window.UpdateLayout(); window.SavePaneLayout();
            var second = await vm.CreateWorkspaceAsync("결산");
            bool nameRejected = false; try { await vm.CreateWorkspaceAsync("결산"); } catch (InvalidOperationException) { nameRejected = true; }
            Check(nameRejected && vm.Workspaces.Count == 2, "Duplicate workspace name is rejected.");
            Check(window.WorkspaceHint.Visibility == Visibility.Visible, "Add-workspace hint remains visible with multiple tabs.");
            Check(vm.Cards.Count == 0 && vm.ToggleFoldersLabel == "모두 접기", "New workspace is independent and empty.");
            vm.AddPath(folder); vm.AddPath(file); vm.Folders[0].Note = "결산 메모"; vm.FolderHeight = 300; vm.FolderColumns = 3;
            var duplicate = await vm.DuplicateWorkspaceAsync(original.Id);
            Check(vm.Cards[0].Note == "기본 메모" && vm.FolderColumns == 5 && vm.FolderHeight == 460 && vm.ToggleFoldersLabel == "모두 펼치기", "Duplication preserves layout, note and toggle state.");
            Check(duplicate.Cards.All(c => original.Cards.All(o => o.Id != c.Id)), "Duplicated cards have independent identities.");
            vm.Cards[0].Note = "복제 메모";
            Check(original.Cards[0].Note == "기본 메모", "Duplicated notes do not share mutable state.");
            vm.RenameWorkspace(duplicate.Id, "참고");
            Check(vm.ReorderWorkspace(duplicate.Id, original.Id, false) && vm.Workspaces[0] == duplicate, "Tab reorder changes persisted order.");
            var export = Path.Combine(root, "all-tabs.pinshelf.json"); vm.ExportSettings(export);
            Check(WorkspaceBackup.Read(export).Workspaces.Count == 3, "Backup includes every tab.");
            await vm.DeleteWorkspaceAsync(duplicate.Id);
            Check(vm.Workspaces.Count == 2 && File.ReadAllText(file) == "original data", "Tab deletion preserves original files.");
            Check(Directory.GetFiles(vm.BackupDirectory, "before-delete-workspace-*.pinshelf.json").Length == 1, "Deletion produces a restorable backup.");
            var preRestore = await vm.ImportSettingsAsync(export);
            Check(vm.Workspaces.Count == 3 && vm.ActiveWorkspace!.Name == "참고" && WorkspaceBackup.Read(preRestore).Workspaces.Count == 2, "All-tab restore and pre-restore backup preserve workspace sets.");
            await vm.DeleteWorkspaceAsync(vm.ActiveWorkspace!.Id);
            original = vm.Workspaces.Single(x => x.Name == "기본 작업공간"); second = vm.Workspaces.Single(x => x.Name == "결산");
            await vm.SwitchWorkspaceAsync(second.Id);
            Check(vm.Cards[0].Note == "결산 메모" && vm.FolderColumns == 3 && vm.FolderHeight == 300, "Switch restores second workspace content and height.");
            var moved = await vm.FileTasks.ExecuteAsync("탭 공통 이름 변경", [new(FileAction.Rename, file, Name: "renamed.txt")]);
            Check(moved?.Completed.Count == 1 && original.Cards.Single(c => c.IsFile).CurrentPath.EndsWith("renamed.txt"), "File relocation updates inactive workspace references.");
            await vm.SwitchWorkspaceAsync(original.Id);
            Check(vm.FileTasks.CanUndo, "File undo history remains shared across tab switch.");
            await vm.FileTasks.UndoAsync();
            Check(File.Exists(file) && second.Cards.Single(c => c.IsFile).CurrentPath == file, "Undo from another tab restores all workspace references.");
            await vm.FileTasks.RedoAsync(); await vm.FileTasks.UndoAsync();
            Check(File.ReadAllText(file) == "original data", "Shared redo and undo preserve actual contents.");
            Check(vm.Cards[0].Note == "기본 메모" && vm.Cards[0].State.SortDescending && vm.FolderColumns == 5 && vm.FolderHeight == 460 && !vm.ShowModified && Math.Abs(vm.ExplorerWidth - 310) < 1, "Original workspace independent note, sorting and layout restored.");
            await vm.SwitchWorkspaceAsync(second.Id); vm.RenameWorkspace(second.Id, "결산 2027"); vm.ReorderWorkspace(second.Id, original.Id, false);
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
            Border? FindTab(DependencyObject node, Guid id)
            {
                if (node is Border { Tag: "WorkspaceTab", DataContext: WorkspaceTab tab } border && tab.Id == id) return border;
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) { var found = FindTab(VisualTreeHelper.GetChild(node, i), id); if (found != null) return found; }
                return null;
            }
            var target = FindTab(window, second.Id)!;
            var data = new DataObject("Mycelview.WorkspaceTab", original.Id);
            DragEventArgs Drag(RoutedEvent routed)
            {
                var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, new object[] { data, DragDropKeyStates.None, DragDropEffects.Move, target, new Point(1, 1) }, null)!;
                args.RoutedEvent = routed; return args;
            }
            var over = Drag(DragDrop.DragOverEvent); target.RaiseEvent(over);
            Check(over.Handled && over.Effects == DragDropEffects.Move, "Tab drag routed hover accepts dedicated payload.");
            var drop = Drag(DragDrop.DropEvent); target.RaiseEvent(drop);
            Check(drop.Handled && vm.Workspaces[0] == original, "Tab drop routes to persisted reorder.");
            vm.ReorderWorkspace(second.Id, original.Id, false);
            vm.ExportSettings(export);
            var invalid = WorkspaceBackup.Read(export); invalid.Workspaces[1].Id = invalid.Workspaces[0].Id;
            var invalidPath = Path.Combine(root, "bad-tabs.json"); WorkspaceBackup.Write(invalidPath, invalid);
            var before = File.ReadAllText(store.FilePath); bool rejected = false;
            try { await vm.ImportSettingsAsync(invalidPath); } catch (InvalidDataException) { rejected = true; }
            Check(rejected && before == File.ReadAllText(store.FilePath), "Duplicate workspace identities rejected without replacing state.");
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(Path.Combine(directory, "workspace-tabs.png"))) encoder.Save(output);
            vm.Save();
        }
        finally { window.Close(); SkinManager.Apply(skin); TextScale.Apply(zoom); }
        await VerifyAsync(directory, skin, zoom);
        File.WriteAllText(Path.Combine(directory, "workspace-tab-results.txt"), "PASS: legacy migration, create/rename/duplicate/delete/reorder, independent notes/sort/layout/toggle, all-tab backup/restore and pre-delete/pre-restore backups, original-file preservation, shared file undo/redo and inactive-tab relocation, invalid identities rejected, restart restoration.");
    }
    internal static async Task VerifyAsync(string directory, string skin, int zoom)
    {
        using var vm = new MainViewModel(new StateStore(Path.Combine(directory, "workspace-tab-tests")));
        try
        {
            await vm.InitializeAsync();
            Check(vm.Workspaces.Count == 2 && vm.Workspaces[0].Name == "결산 2027" && vm.ActiveWorkspace!.Name == "결산 2027", "Tab order and active tab survive process restart.");
            Check(vm.Cards[0].Note == "결산 메모" && vm.FolderColumns == 3 && vm.FolderHeight == 300, "Active tab data restored.");
            var first = vm.Workspaces.Single(x => x.Name == "기본 작업공간");
            await vm.SwitchWorkspaceAsync(first.Id);
            Check(vm.Cards[0].Note == "기본 메모" && vm.ToggleFoldersLabel == "모두 펼치기" && vm.Folders[0].State.Collapsed && vm.FolderColumns == 5 && vm.FolderHeight == 460, "Inactive tab and next toggle action survive restart.");
            await vm.SwitchWorkspaceAsync(vm.Workspaces[0].Id);
        }
        finally { SkinManager.Apply(skin); TextScale.Apply(zoom); }
    }
}


