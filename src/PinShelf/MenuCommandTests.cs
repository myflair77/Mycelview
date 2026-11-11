using System.IO;
using System.Windows;
using System.Windows.Interop;

namespace PinShelf;
internal static class MenuCommandTests
{
    internal static async Task RunAsync(MainWindow window, string folder, string output)
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        var vm = window.ViewModel;
        var file = Path.Combine(folder, "확인.txt");
        var second = Path.Combine(folder, "두번째.txt"); File.WriteAllText(second, "second");
        var destination = Path.Combine(folder, "대상"); Directory.CreateDirectory(destination);
        var selected = new[] { new ContextTarget(file, false), new ContextTarget(second, false) };
        var clipboard = Clipboard.GetDataObject();
        // Materialize formats before native commands replace the clipboard's delayed-rendered owner.
        var saved = new DataObject();
        if (clipboard != null) foreach (var format in clipboard.GetFormats(false))
        { try { var data = clipboard.GetData(format, false); if (data != null) saved.SetData(format, data, false); } catch { } }
        var log = new List<string>();
        try
        {
            using (var shell = new NativeShellMenu(selected.Select(x => x.Path).ToArray(), new WindowInteropHelper(window).Handle))
            {
                var pathCopy = shell.Entries().Single(e => e.Verb == "copyaspath");
                shell.Invoke(pathCopy.Id, new WindowInteropHelper(window).Handle);
                var copied = Clipboard.GetText().Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
                Check(copied.Length == 2 && selected.All(x => copied.Contains($"\"{x.Path}\"")),
                    "Shell copy-as-path must copy both quoted Unicode paths.");
                log.Add("PASS: actual Shell copy-as-path copied both quoted Unicode paths.");
            }
            using (var shell = new NativeShellMenu([file], new WindowInteropHelper(window).Handle))
            {
                var pathCopy = shell.Entries().Single(e => e.Verb == "copyaspath");
                shell.Invoke(pathCopy.Id, new WindowInteropHelper(window).Handle);
                Check(Clipboard.GetText().Contains(file), "Actual Shell copy-as-path must copy the selected file path.");
                log.Add("PASS: actual IContextMenu.InvokeCommand(copyaspath) copied the selected path.");
                var copy = shell.Entries().Single(e => e.Verb == "copy");
                shell.Invoke(copy.Id, new WindowInteropHelper(window).Handle);
                Check(FileTasks.ReadClipboard().Paths.Contains(file), "Actual Shell copy command must populate file clipboard.");
                log.Add("PASS: actual IContextMenu.InvokeCommand(copy) populated file clipboard.");
            }
            await window.FileMenus.ExecutePinShelfCommandAsync(1, "copy", selected);
            await vm.FileTasks.PasteAsync(destination);
            Check(File.Exists(Path.Combine(destination, "확인.txt")) && File.Exists(Path.Combine(destination, "두번째.txt")), "Menu batch copy must paste both files.");
            await window.FileMenus.ExecutePinShelfCommandAsync(NativeShellMenu.Undo, "", selected);
            Check(!File.Exists(Path.Combine(destination, "확인.txt")) && !File.Exists(Path.Combine(destination, "두번째.txt")), "Menu Undo must undo the batch copy.");
            log.Add("PASS: menu copy two files, real paste, menu Undo restored destination.");
            await window.FileMenus.ExecutePinShelfCommandAsync(1, "cut", selected);
            Check(FileTasks.ReadClipboard().Cut, "Menu cut must set move effect.");
            await vm.FileTasks.PasteAsync(destination);
            Check(!File.Exists(file) && File.Exists(Path.Combine(destination, "확인.txt")), "Cut/paste must move source.");
            await window.FileMenus.ExecutePinShelfCommandAsync(NativeShellMenu.Undo, "", selected);
            Check(File.ReadAllText(file) == "preview" && File.ReadAllText(second) == "second", "Undo move must restore both contents.");
            log.Add("PASS: menu cut two files, real paste/move, menu Undo restored sources.");
            await vm.FileTasks.ExecuteAsync("메뉴 검증 이름 변경", [new(FileAction.Rename, file, Name:"바뀐 이름.txt")]);
            Check(File.Exists(Path.Combine(folder,"바뀐 이름.txt")), "Rename operation failed.");
            await window.FileMenus.ExecutePinShelfCommandAsync(NativeShellMenu.Undo, "", selected);
            Check(File.Exists(file), "Menu undo rename failed.");
            log.Add("PASS: real rename and menu Undo; rename dialog itself not exercised.");
            var preview = new FilePreviewController(window, headless:true);
            try
            {
                var menus = new FileContextMenus(vm) { PreviewPath = preview.Show };
                await menus.ExecutePinShelfCommandAsync(NativeShellMenu.Preview, "", [selected[0]]);
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (preview.Data == null && DateTime.UtcNow < deadline) await Task.Delay(30);
                Check(preview.Data != null, "Preview menu must load actual file.");
                log.Add("PASS: preview menu loaded the actual text file in a headless preview.");
            }
            finally { preview.Close(); }
            var originalTab = vm.ActiveWorkspace!.Id;
            var originalCount = vm.Cards.Count;
            var otherTab = vm.Workspaces.First(x => x.Id != originalTab);
            await vm.SwitchWorkspaceAsync(otherTab.Id);
            ContextTarget[] mixed = [new(folder, true), ..selected];
            await window.FileMenus.ExecutePinShelfCommandAsync(NativeShellMenu.AddFolder, "", mixed);
            await window.FileMenus.ExecutePinShelfCommandAsync(NativeShellMenu.AddFile, "", mixed);
            Check(vm.Folders.Count == 1 && vm.Folders[0].CurrentPath == folder && vm.Favorites.Count == 2,
                "Type-specific registration must add selected folders/files to the active workspace.");
            await vm.SwitchWorkspaceAsync(originalTab);
            Check(vm.Cards.Count == originalCount, "Registration must not change an inactive workspace.");
            await vm.SwitchWorkspaceAsync(otherTab.Id);
            Check(vm.Favorites.Count == 2 && vm.Folders.Count == 1, "Added registrations must survive tab switching.");
            log.Add("PASS: folder/file registration commands filter mixed selection and target only the active workspace.");
            await window.FileMenus.ExecutePinShelfCommandAsync(NativeShellMenu.Unregister, "", selected);
            Check(!vm.Favorites.Any(c=>selected.Any(x=>x.Path==c.CurrentPath)) && File.Exists(file) && File.Exists(second), "Unregister must remove registrations only.");
            log.Add("PASS: multi-file unregister preserved source files.");
        }
        finally { if (saved.GetFormats(false).Length > 0) Clipboard.SetDataObject(saved,true); else Clipboard.Clear(); }
        log.Add("NOT TESTED by this automated suite: physical mouse, popup painting, Explorer new-tab/window, Properties, compression/third-party extensions, rename/recycle dialogs. See VALIDATION.md for separate mouse QA.");
        File.WriteAllLines(Path.Combine(output,"menu-command-results.txt"),log);
    }
}

