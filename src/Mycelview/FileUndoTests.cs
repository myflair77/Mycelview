using System.IO;
using System.Windows.Interop;

namespace Mycelview;
internal static class FileUndoTests
{
    internal static async Task RunAsync(MainWindow window, string directory)
    {
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        var root = Path.Combine(directory, "undo-fixtures"); var source = Path.Combine(root, "source"); var target = Path.Combine(root, "target");
        Directory.CreateDirectory(source); Directory.CreateDirectory(target);
        var a = Path.Combine(source, "a.txt"); var b = Path.Combine(source, "b.txt");
        File.WriteAllText(a, "a-original"); File.WriteAllText(b, "b-original");
        var owner = new WindowInteropHelper(window).Handle;
        async Task<FileUndoJournal> Perform(params FileRequest[] requests)
        {
            var journal = await FileUndoJournal.PrepareAsync("test", Path.Combine(root, "history"), requests, CancellationToken.None);
            var result = await WindowsFileOperations.RunAsync(requests, owner, CancellationToken.None, flags: WindowsFileOperations.QuietTestFlags);
            Check(result.Errors.Count == 0 && result.Completed.Count == requests.Length, "Original batch must succeed.");
            Check(await journal!.CompleteAsync(result), "Completed operation must create undo record."); return journal;
        }
        var move = await Perform(new(FileAction.Move, a, target), new(FileAction.Move, b, target));
        await move.UndoAsync(CancellationToken.None);
        Check(File.ReadAllText(a) == "a-original" && File.ReadAllText(b) == "b-original" && !File.Exists(Path.Combine(target, "a.txt")), "Batch move undo restores sources.");
        File.WriteAllText(Path.Combine(target, "a.txt"), "overwritten-original");
        var copy = await Perform(new(FileAction.Copy, a, target), new(FileAction.Copy, b, target));
        await copy.UndoAsync(CancellationToken.None);
        Check(File.ReadAllText(Path.Combine(target, "a.txt")) == "overwritten-original" && !File.Exists(Path.Combine(target, "b.txt")) && File.Exists(a), "Copy undo restores overwritten destination and removes only new copies.");
        var rename = await Perform(new(FileAction.Rename, a, Name: "renamed.txt"), new(FileAction.Rename, b, Name: "renamed-b.txt"));
        await rename.UndoAsync(CancellationToken.None);
        Check(File.Exists(a) && File.Exists(b) && !File.Exists(Path.Combine(source, "renamed.txt")), "Batch rename undo restores names.");
        var folder = Path.Combine(source, "nested"); Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "child.txt"), "child");
        var existingFolder = Path.Combine(target, "nested"); Directory.CreateDirectory(existingFolder); File.WriteAllText(Path.Combine(existingFolder, "keep.txt"), "keep");
        var merge = await Perform(new FileRequest(FileAction.Move, folder, target));
        await merge.UndoAsync(CancellationToken.None);
        Check(File.Exists(Path.Combine(folder, "child.txt")) && File.ReadAllText(Path.Combine(existingFolder, "keep.txt")) == "keep" && !File.Exists(Path.Combine(existingFolder, "child.txt")), "Merged folder undo restores both trees.");
        var guarded = await Perform(new FileRequest(FileAction.Copy, b, target));
        File.WriteAllText(Path.Combine(target, "b.txt"), "edited-after-copy");
        bool blocked = false; try { await guarded.UndoAsync(CancellationToken.None); } catch (IOException) { blocked = true; }
        Check(blocked && File.ReadAllText(Path.Combine(target, "b.txt")) == "edited-after-copy", "Later changes must block undo without data loss.");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        bool cancelled = false; try { await guarded.UndoAsync(cancel.Token); } catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && File.Exists(b), "Cancelled undo must preserve files.");
        // Exercise the app's history, not only the journal implementation.
        var pipeline = await window.ViewModel.FileTasks.ExecuteAsync("이름 변경", [new(FileAction.Rename, a, Name: "pipeline.txt")]);
        Check(pipeline?.Completed.Count == 1 && window.ViewModel.FileTasks.CanUndo, "Successful app operation must enable Ctrl+Z/menu history.");
        await window.ViewModel.FileTasks.UndoAsync();
        Check(File.Exists(a) && !File.Exists(Path.Combine(source, "pipeline.txt")), "App undo pipeline restores original name.");
        Check(window.ViewModel.FileTasks.CanRedo, "Undo enables redo.");
        await window.ViewModel.FileTasks.RedoAsync();
        Check(!File.Exists(a) && File.Exists(Path.Combine(source, "pipeline.txt")), "Redo restores renamed file.");
        var loaded = new UndoHistoryStore(window.ViewModel.UndoDirectory);
        Check(loaded.Data.Undo.Count > 0 && loaded.Data.Redo.Count == 0, "Persistent stack reload.");
        var saved = loaded.Records[loaded.Data.Undo.Last()];
        loaded.Begin(false); await saved.UndoAsync(CancellationToken.None); loaded.Finish(false);
        loaded = new UndoHistoryStore(window.ViewModel.UndoDirectory);
        Check(loaded.Data.Redo.Count > 0 && File.Exists(a), "Persistent redo stack reload.");
        saved = loaded.Records[loaded.Data.Redo.Last()];
        loaded.Begin(true); await saved.RedoAsync(CancellationToken.None);
        loaded = new UndoHistoryStore(window.ViewModel.UndoDirectory);
        Check(loaded.Data.Pending == null && loaded.Data.Undo.Count > 0, "Completed transition recovered after interrupted catalog commit.");
        loaded.Data.LimitMB = 1; loaded.Save();
        bool quota = false; try { loaded.Cleanup(2 * 1024 * 1024); } catch (IOException) { quota = true; }
        Check(quota && File.Exists(Path.Combine(source, "pipeline.txt")), "Quota rejects oversized reservation without changing user data.");
        loaded.Begin(false); await saved.UndoAsync(CancellationToken.None); loaded.Finish(false);
        var caseOnly = await Perform(new FileRequest(FileAction.Rename, a, Name: "A.txt"));
        await caseOnly.PrepareRedoAsync(CancellationToken.None);
        await caseOnly.UndoAsync(CancellationToken.None); await caseOnly.RedoAsync(CancellationToken.None);
        Check(Directory.GetFiles(source).Any(p => Path.GetFileName(p) == "A.txt"), "Case-only rename redo preserves casing.");
        await caseOnly.UndoAsync(CancellationToken.None);
        var cleanupRoot = Path.Combine(root, "cleanup-history");
        var cleanupStore = new UndoHistoryStore(cleanupRoot);
        var cleanupTarget = Path.Combine(target, "quota.txt");
        File.WriteAllBytes(cleanupTarget, new byte[2 * 1024 * 1024]);
        var request = new FileRequest(FileAction.Copy, a, target, "quota.txt");
        var cleanupRecord = (await FileUndoJournal.PrepareAsync("quota", cleanupRoot, [request], CancellationToken.None))!;
        File.WriteAllText(cleanupTarget, "new-data");
        await cleanupRecord.CompleteAsync(new([new(request, cleanupTarget)], [], false));
        cleanupStore.Add(cleanupRecord); cleanupStore.Data.LimitMB = 1; cleanupStore.Cleanup();
        Check(cleanupStore.Data.Undo.Count == 0 && !Directory.Exists(cleanupRecord.DirectoryPath) && File.ReadAllText(cleanupTarget) == "new-data", "Quota eviction removes only backup copies.");
        cleanupRecord = (await FileUndoJournal.PrepareAsync("expiry", cleanupRoot, [request], CancellationToken.None))!;
        File.WriteAllText(cleanupTarget, "latest-data");
        await cleanupRecord.CompleteAsync(new([new(request, cleanupTarget)], [], false));
        cleanupStore.Add(cleanupRecord);
        var manifest = Path.Combine(cleanupRecord.DirectoryPath, "journal.json");
        var doc = System.Text.Json.JsonSerializer.Deserialize<FileUndoJournal.Document>(File.ReadAllText(manifest))!;
        doc.CreatedAt = DateTimeOffset.UtcNow.AddDays(-40); UndoHistoryStore.AtomicWrite(manifest, doc);
        cleanupStore = new UndoHistoryStore(cleanupRoot);
        Check(cleanupStore.Data.Undo.Count == 0 && File.ReadAllText(cleanupTarget) == "latest-data", "Startup expiry removes old history without deleting user data.");
        File.WriteAllText(Path.Combine(directory, "undo-results.txt"), "PASS: real Windows batch move/copy/rename undo, overwritten destination recovery, merged folder recovery, post-operation modification guard, cancellation, persistent undo/redo, interrupted commit recovery, case-only redo, quota and expiry cleanup. Test-only .hold copies retained in fixtures; app cleanup uses recycle, never permanent deletion.");
    }
}
