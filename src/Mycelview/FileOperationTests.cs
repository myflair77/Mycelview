using System.IO;
using System.Windows.Interop;

namespace Mycelview;
internal static class FileOperationTests
{
    private sealed class ImmediateProgress(Action<FileProgress> action) : IProgress<FileProgress>
    { public void Report(FileProgress value) => action(value); }
    internal static async Task RunAsync(MainWindow window, string directory)
    {
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        var root = Path.Combine(directory, "file-operation-fixtures"); var source = Path.Combine(root, "source"); var target = Path.Combine(root, "target");
        Directory.CreateDirectory(source); Directory.CreateDirectory(target);
        var a = Path.Combine(source, "a.txt"); var b = Path.Combine(source, "b.txt"); File.WriteAllText(a, "original-a"); File.WriteAllText(b, "original-b");
        var owner = new WindowInteropHelper(window).Handle;
        async Task<FileOperationResult> Run(params FileRequest[] requests) => await WindowsFileOperations.RunAsync(requests, owner, CancellationToken.None, flags: WindowsFileOperations.QuietTestFlags,
            diagnostic: text => File.AppendAllText(Path.Combine(directory, "file-operation-trace.txt"), text + Environment.NewLine)).WaitAsync(TimeSpan.FromSeconds(45));
        var log = new List<string>();
        var copied = await Run(new(FileAction.Copy, a, target), new(FileAction.Copy, b, target));
        Check(!copied.Aborted && copied.Errors.Count == 0 && copied.Completed.Count == 2, "Native batch copy must report both completed items: " + string.Join(";", copied.Errors));
        Check(File.ReadAllText(Path.Combine(target, "a.txt")) == "original-a" && File.Exists(a), "Copy must preserve original."); log.Add("PASS: native two-file copy and completion callbacks.");
        var tree = new FolderNode(target, "target"); await tree.ExpandAsync();
        var created = await Run(new FileRequest(FileAction.NewFolder, target, Name: "새 폴더"));
        Check(created.Completed.Count == 1 && Directory.Exists(Path.Combine(target, "새 폴더")), "Native new folder failed.");
        await tree.RefreshLoadedAsync();
        Check(tree.Children.Any(x => x.Title == "새 폴더"), "Expanded explorer tree must pick up newly created folders.");
        var moved = await Run(new FileRequest(FileAction.Move, b, Path.Combine(target, "새 폴더")));
        Check(moved.Completed.Count == 1 && !File.Exists(b) && File.Exists(Path.Combine(target, "새 폴더", "b.txt")), "Native move failed.");
        Check(moved.Completed[0].NewPath != null, "Move must report resulting path for registration updates.");
        var renamed = await Run(new FileRequest(FileAction.Rename, a, Name: "renamed.txt")); var renamedPath = Path.Combine(source, "renamed.txt");
        Check(renamed.Completed.Count == 1 && File.Exists(renamedPath) && !File.Exists(a), "Native rename failed."); log.Add("PASS: native new folder, move, rename and resulting path.");
        var conflictSource = Path.Combine(source, "a.txt"); File.WriteAllText(conflictSource, "replacement-a");
        var keep = FileOperationPlanner.Resolve([new(FileAction.Copy, conflictSource, target)], (_, _) => new(CollisionChoice.KeepBoth, false))!;
        Check(keep[0].Name == "a (2).txt", "Keep-both name must retain extension.");
        await Run(keep.ToArray()); Check(File.ReadAllText(Path.Combine(target, "a.txt")) == "original-a" && File.ReadAllText(Path.Combine(target, "a (2).txt")) == "replacement-a", "Keep-both must preserve existing file.");
        var skip = FileOperationPlanner.Resolve([new(FileAction.Copy, conflictSource, target)], (_, _) => new(CollisionChoice.Skip, true));
        Check(skip?.Count == 0, "Skip must queue no work.");
        Check(FileOperationPlanner.Resolve([new(FileAction.Copy, conflictSource, target)], (_, _) => new(CollisionChoice.Cancel, false)) == null, "Cancel preflight must not queue work.");
        var replace = FileOperationPlanner.Resolve([new(FileAction.Copy, conflictSource, target)], (_, _) => new(CollisionChoice.Replace, false))!;
        await Run(replace.ToArray()); Check(File.ReadAllText(Path.Combine(target, "a.txt")) == "replacement-a", "Explicit replace must replace target data."); log.Add("PASS: keep both, skip, cancel planning and native overwrite.");
        Directory.CreateDirectory(Path.Combine(source, "tree", "nested")); File.WriteAllText(Path.Combine(source, "tree", "nested", "leaf.txt"), "leaf");
        await Run(new FileRequest(FileAction.Copy, Path.Combine(source, "tree"), target));
        Check(File.Exists(Path.Combine(target, "tree", "nested", "leaf.txt")), "Recursive folder copy failed.");
        Check(FileOperationRules.TopLevel([Path.Combine(source, "tree"), Path.Combine(source, "tree", "nested", "leaf.txt")]).Length == 1, "Selected child beneath selected parent must not run twice.");
        bool blocked = false; try { FileOperationRules.Validate(new(FileAction.Copy, Path.Combine(source, "tree"), Path.Combine(source, "tree", "nested"))); } catch (IOException) { blocked = true; }
        Check(blocked, "Recursive self-copy must be rejected.");
        foreach (var bad in new[] { "..", "CON.txt", "x/y", "trailing.", "bad " })
        { blocked = false; try { FileOperationRules.CheckName(bad); } catch (IOException) { blocked = true; } Check(blocked, "Unsafe name accepted: " + bad); }
        log.Add("PASS: nested folder copy, parent-child deduplication, self-copy and invalid-name guards.");
        var cancelFirst = Path.Combine(source, "cancel-first.txt"); var cancelSecond = Path.Combine(source, "cancel-second.txt");
        File.WriteAllText(cancelFirst, "one"); File.WriteAllText(cancelSecond, "two");
        using var cts = new CancellationTokenSource();
        var cancelled = await WindowsFileOperations.RunAsync([new(FileAction.Copy, cancelFirst, target), new(FileAction.Copy, cancelSecond, target)], owner, cts.Token,
            new ImmediateProgress(p => { if (p.Path == cancelSecond) cts.Cancel(); }), WindowsFileOperations.QuietTestFlags).WaitAsync(TimeSpan.FromSeconds(45));
        Check(cancelled.Aborted && File.Exists(Path.Combine(target, "cancel-first.txt")) && !File.Exists(Path.Combine(target, "cancel-second.txt")), "Cancellation must stop remaining items while retaining completed file.");
        Check(File.Exists(cancelFirst) && File.Exists(cancelSecond), "Cancel copy must preserve sources."); log.Add("PASS: real native mid-batch cancellation, completed work retained and sources preserved.");
        var recycle = Path.Combine(source, "Mycelview-test-recycle-" + Guid.NewGuid().ToString("N") + ".txt"); File.WriteAllText(recycle, "Mycelview fixture only");
        var recycled = await Run(new FileRequest(FileAction.Recycle, recycle));
        Check(recycled.Completed.Count == 1 && !File.Exists(recycle), "Native recycle failed: " + string.Join(";", recycled.Errors));
        var recycledPath = recycled.Completed[0].NewPath;
        Check(recycledPath != null && File.Exists(recycledPath) && File.ReadAllText(recycledPath) == "Mycelview fixture only", "Recycled test file must remain recoverable with original data.");
        log.Add("PASS: native Recycle Bin operation on one uniquely named test fixture; no user files touched.");
        var mergeFrom = Path.Combine(source, "merge"); var mergeTo = Path.Combine(target, "merge");
        Directory.CreateDirectory(mergeFrom); Directory.CreateDirectory(mergeTo);
        File.WriteAllText(Path.Combine(mergeFrom, "new.txt"), "new"); File.WriteAllText(Path.Combine(mergeTo, "old.txt"), "old");
        var merged = await Run(new FileRequest(FileAction.Move, mergeFrom, target));
        Check(merged.Completed.Count == 1 && !Directory.Exists(mergeFrom) && File.Exists(Path.Combine(mergeTo, "old.txt")) && File.Exists(Path.Combine(mergeTo, "new.txt")), "Folder merge move must finalize only after all descendants are moved.");
        var folderCancel = Path.Combine(source, "cancel-tree"); Directory.CreateDirectory(folderCancel); File.WriteAllText(Path.Combine(folderCancel, "stop.txt"), "stay");
        using var folderCts = new CancellationTokenSource();
        var folderCancelled = await WindowsFileOperations.RunAsync([new(FileAction.Copy, folderCancel, target)], owner, folderCts.Token,
            new ImmediateProgress(p => { if (p.Path.EndsWith("stop.txt", StringComparison.Ordinal)) folderCts.Cancel(); }), WindowsFileOperations.QuietTestFlags).WaitAsync(TimeSpan.FromSeconds(45));
        Check(folderCancelled.Aborted && folderCancelled.Completed.Count == 0 && File.Exists(Path.Combine(folderCancel, "stop.txt")), "Cancelled folder contents must not be reported as fully copied.");
        var clipboardData = new System.Windows.DataObject(); clipboardData.SetData(System.Windows.DataFormats.FileDrop, new[] { renamedPath });
        clipboardData.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(2)));
        var decoded = FileTasks.DecodeClipboard(clipboardData);
        Check(decoded.Cut && decoded.Paths.Single() == renamedPath, "Windows FileDrop + Preferred DropEffect must preserve cut semantics.");
        var state = new CardState { InitialPath = Path.Combine(source, "tree"), CurrentPath = Path.Combine(source, "tree", "nested"), Title = "tree" };
        using var card = new CardViewModel(state, () => { }); card.ApplyRelocation(Path.Combine(source, "tree"), Path.Combine(target, "renamed-tree"));
        Check(card.CurrentPath == Path.Combine(target, "renamed-tree", "nested") && card.State.InitialPath == Path.Combine(target, "renamed-tree"), "Registered child paths must follow renamed/moved parents.");
        log.Add("PASS: merged-folder move finalization, cancelled folder completeness, Windows cut clipboard decoding and registration path relocation.");
        blocked = false;
        try { FileOperationPlanner.Resolve([new(FileAction.Rename, conflictSource, Name: "renamed.txt"), new(FileAction.Rename, renamedPath, Name: "a.txt")], (_, _) => new(CollisionChoice.Replace, false)); }
        catch (IOException) { blocked = true; }
        Check(blocked && File.Exists(conflictSource) && File.Exists(renamedPath), "Batch rename must not overwrite another selected source during a name swap.");
        File.WriteAllLines(Path.Combine(directory, "file-operation-results.txt"), log);
    }
}
