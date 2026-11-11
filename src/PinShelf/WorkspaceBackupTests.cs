using System.IO;
using System.Text.Json;

namespace PinShelf;
internal static class WorkspaceBackupTests
{
    internal static async Task RunAsync(string directory, string skin)
    {
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        var root = Path.Combine(directory, "settings-backup-tests"); Directory.CreateDirectory(root);
        var folder = Path.Combine(root, "folder"); Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "original.txt"); File.WriteAllText(file, "preserve original");
        var state = new WorkspaceState
        {
            SkinId = skin,
            FolderHeight = 420, ShowType = true, TextScalePercent = 123,
            Layout = new() { ExplorerWidth = 300, FavoritesWidth = 280, TreeHeight = 180, FavoritesRatio = .6 },
            Cards = [new() { InitialPath = folder, CurrentPath = folder, Title = "folder", Note = "폴더 메모", SortProperty = "Length", SortDescending = true, Collapsed = true },
                     new() { InitialPath = file, CurrentPath = file, Title = "file", IsFile = true, Note = "즐겨찾기 메모" }]
        };
        var store = new StateStore(root); store.Save(state);
        using var vm = new MainViewModel(store); await vm.InitializeAsync();
        var export = Path.Combine(root, "export.pinshelf.json"); vm.ExportSettings(export);
        var exported = WorkspaceBackup.Read(export);
        Check(exported.TextScalePercent == 123 && exported.Cards[0].SortDescending && exported.Cards[0].SortProperty == "Length" && exported.Cards[1].Note == "즐겨찾기 메모", "Export must preserve card order, notes and sorting.");
        vm.Cards[0].Note = "복원 직전 최신 메모"; vm.FolderHeight = 500;
        var backup = await vm.ImportSettingsAsync(export);
        Check(WorkspaceBackup.Read(backup).Cards[0].Note == "복원 직전 최신 메모" && WorkspaceBackup.Read(backup).FolderHeight == 500, "Automatic backup must include unsaved current changes.");
        Check(vm.TextScalePercent == 123 && vm.FolderHeight == 420 && vm.Cards[0].Note == "폴더 메모" && vm.Cards[1].Note == "즐겨찾기 메모" && vm.Cards[0].State.SortDescending && vm.Layout.ExplorerWidth == 300, "Import must apply independent notes, layout, height and sort.");
        Check(store.Load().Cards[0].SortProperty == "Length", "Import must persist for restart.");
        await vm.ImportSettingsAsync(backup);
        Check(vm.Cards[0].Note == "복원 직전 최신 메모", "Automatic backup must itself be restorable.");
        var before = File.ReadAllText(store.FilePath);
        foreach (var invalid in new[] { "{", "{}", "{\"Format\":\"PinShelf.Settings\",\"Version\":999,\"Workspace\":{}}" })
        {
            var path = Path.Combine(root, "invalid.json"); File.WriteAllText(path, invalid);
            bool rejected = false; try { await vm.ImportSettingsAsync(path); } catch { rejected = true; }
            Check(rejected && File.ReadAllText(store.FilePath) == before && vm.Cards[0].Note == "복원 직전 최신 메모", "Invalid import must not change current settings.");
        }
        var duplicate = WorkspaceBackup.Read(export); duplicate.Cards[1].Id = duplicate.Cards[0].Id;
        var duplicateFile = Path.Combine(root, "duplicate.json"); WorkspaceBackup.Write(duplicateFile, duplicate);
        bool duplicateRejected = false; try { WorkspaceBackup.Read(duplicateFile); } catch (InvalidDataException) { duplicateRejected = true; }
        Check(duplicateRejected, "Duplicate identities must be rejected.");
        var blockedRoot = Path.Combine(root, "backup-failure"); var blockedStore = new StateStore(blockedRoot); blockedStore.Save(state);
        File.WriteAllText(Path.Combine(blockedRoot, "backups"), "block directory creation");
        using var blockedVm = new MainViewModel(blockedStore); await blockedVm.InitializeAsync();
        var blockedBefore = File.ReadAllText(blockedStore.FilePath);
        bool failed = false; try { await blockedVm.ImportSettingsAsync(export); } catch (IOException) { failed = true; }
        Check(failed && File.ReadAllText(blockedStore.FilePath) == blockedBefore, "Backup failure must prevent replacement.");
        Check(File.ReadAllText(file) == "preserve original", "Settings operations must not modify registered files.");
        TextScale.Apply(100);
        File.WriteAllText(Path.Combine(directory, "settings-backup-results.txt"), "PASS: export/import, independent notes/order/sort/layout, unsaved current state automatic backup, backup restore, persistence, invalid/unknown-version/duplicate rejection, backup failure leaves settings unchanged, original file preserved.");
    }
}
