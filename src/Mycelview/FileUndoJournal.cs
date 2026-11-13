using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mycelview;

internal sealed class FileUndoJournal
{
    internal sealed record Snapshot(string Path, string Backup, string Before)
    {
        public string After { get; set; } = "";
        public string? AfterBackup { get; set; }
        public bool ForceRestore { get; set; }
    }
    internal string Title { get; }
    internal string DirectoryPath { get; }
    internal List<Snapshot> Items { get; } = [];
    internal IReadOnlyList<FileChange> Changes { get; private set; } = [];
    internal DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    internal bool Completed { get; private set; }
    internal string Id => Path.GetFileName(DirectoryPath);
    internal sealed class Document
    {
        public int Version { get; set; } = 1;
        public string Title { get; set; } = "";
        public DateTimeOffset CreatedAt { get; set; }
        public bool Completed { get; set; }
        public List<Snapshot> Items { get; set; } = [];
        public List<FileChange> Changes { get; set; } = [];
    }
    internal static FileUndoJournal Load(string directory)
    {
        var data = JsonSerializer.Deserialize<Document>(File.ReadAllText(Path.Combine(directory, "journal.json"))) ?? throw new IOException("실행 취소 기록이 비어 있습니다.");
        if (data.Version != 1 || !data.Completed || data.Items.Count == 0) throw new IOException("완료되지 않았거나 지원하지 않는 기록입니다.");
        var journal = new FileUndoJournal(data.Title, directory) { CreatedAt = data.CreatedAt, Completed = true, Changes = data.Changes };
        foreach (var item in data.Items)
        {
            if (!Path.IsPathFullyQualified(item.Path) || FileOperationRules.Inside(item.Path, directory) ||
                !FileOperationRules.Inside(item.Backup, directory) || item.AfterBackup != null && !FileOperationRules.Inside(item.AfterBackup, directory) || item.After.Length == 0)
                throw new IOException("실행 취소 기록 경로가 잘못되었습니다.");
            journal.Items.Add(item);
        }
        return journal;
    }
    internal static string[] AffectedPaths(IReadOnlyList<FileRequest> plan)
    {
        var paths = plan.SelectMany(r =>
        {
            var parent = r.Action == FileAction.Rename ? Path.GetDirectoryName(r.Source)! : r.Destination!;
            var target = Path.Combine(parent, r.Name ?? Path.GetFileName(r.Source));
            return r.Action == FileAction.Copy ? new[] { target } : new[] { r.Source, target };
        }).Select(FileOperationRules.Full).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var all = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return paths.Where(p => !FileOperationRules.HasSelectedAncestor(p, all)).ToArray();
    }
    private FileUndoJournal(string title, string directory) { Title = title; DirectoryPath = directory; }
    internal static async Task<FileUndoJournal?> PrepareAsync(string title, string directory, IReadOnlyList<FileRequest> plan, CancellationToken token)
    {
        if (plan.Any(r => r.Action is not (FileAction.Copy or FileAction.Move or FileAction.Rename))) return null;
        return await Task.Run(() =>
        {
            var journal = new FileUndoJournal(title, Path.Combine(directory, Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(journal.DirectoryPath);
            try
            {
            foreach (var path in AffectedPaths(plan))
            {
                token.ThrowIfCancellationRequested();
                if (FileOperationRules.Same(path, directory) || FileOperationRules.Inside(directory, path) || FileOperationRules.Inside(path, directory))
                    throw new IOException("실행 취소 보관 폴더 자체는 작업할 수 없습니다.");
                var before = Fingerprint(path, token);
                var backup = Path.Combine(journal.DirectoryPath, journal.Items.Count.ToString());
                if (before != "missing") Clone(path, backup, token);
                if (Fingerprint(path, token) != before || (before != "missing" && Fingerprint(backup, token) != before))
                    throw new IOException("백업 중 파일이 변경되었습니다. 다시 시도하세요: " + path);
                journal.Items.Add(new(path, backup, before));
            }
            journal.Persist(); return journal;
            }
            catch { UndoHistoryStore.RemoveOwned(journal.DirectoryPath, directory); throw; }
        }, token);
    }
    internal async Task<bool> CompleteAsync(FileOperationResult result)
    {
        Changes = result.Completed;
        return await Task.Run(() =>
        {
            // Unexpected Shell auto-renames cannot be safely undone using a different path's backup.
            var roots = Items.Select(i => FileOperationRules.Full(i.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (result.Completed.Any(c => c.NewPath != null && !roots.Contains(FileOperationRules.Full(c.NewPath)) && !FileOperationRules.HasSelectedAncestor(c.NewPath, roots)))
                throw new IOException("Windows에서 대상 이름이 변경되어 이 작업의 자동 실행 취소를 등록하지 못했습니다.");
            var caseRenames = result.Completed.Where(c => c.Request.Action == FileAction.Rename && c.NewPath != null && FileOperationRules.Same(c.Request.Source, c.NewPath) && c.Request.Source != c.NewPath).Select(c => FileOperationRules.Full(c.Request.Source)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var item in Items)
            {
                item.After = Fingerprint(item.Path, CancellationToken.None);
                item.ForceRestore = caseRenames.Contains(FileOperationRules.Full(item.Path));
            }
            Completed = true;
            Persist(); return Items.Any(i => i.Before != i.After || i.ForceRestore);
        });
    }
    private void Persist() => UndoHistoryStore.AtomicWrite(Path.Combine(DirectoryPath, "journal.json"), new Document { Title = Title, CreatedAt = CreatedAt, Completed = Completed, Items = Items, Changes = Changes.ToList() });

    internal async Task PrepareRedoAsync(CancellationToken token)
    {
        await Task.Run(() =>
        {
            foreach (var item in Items)
            {
                if (Fingerprint(item.Path, token) != item.After) throw new IOException("작업 이후 변경된 파일이 있어 실행 취소를 중단했습니다: " + item.Path);
                if (item.After == "missing") continue;
                var backup = item.Backup + "-after";
                if (item.AfterBackup == null)
                {
                    if (File.Exists(backup) || Directory.Exists(backup)) UndoHistoryStore.RemoveOwned(backup, DirectoryPath);
                    Clone(item.Path, backup, token);
                    if (Fingerprint(backup, token) != item.After || Fingerprint(item.Path, token) != item.After) throw new IOException("다시 실행 보관 중 파일이 변경됐습니다.");
                    item.AfterBackup = backup;
                    Persist();
                }
            }
        }, token);
    }
    internal Task<string[]> RedoAsync(CancellationToken token)
    {
        var inverse = new FileUndoJournal(Title, DirectoryPath);
        foreach (var item in Items)
        {
            if (item.After != "missing" && item.AfterBackup == null) throw new IOException("다시 실행 보관본이 없습니다.");
            var redoPath = item.ForceRestore ? Changes.FirstOrDefault(c => c.NewPath != null && FileOperationRules.Same(c.Request.Source, item.Path))?.NewPath ?? item.Path : item.Path;
            inverse.Items.Add(new(redoPath, item.AfterBackup ?? item.Backup, item.After) { After = item.Before, ForceRestore = item.ForceRestore });
        }
        return inverse.UndoAsync(token);
    }
    internal bool Matches(bool undone) => Items.All(i => Fingerprint(i.Path, CancellationToken.None) == (undone ? i.Before : i.After));

    internal async Task<string[]> UndoAsync(CancellationToken token)
    {
        return await Task.Run(() =>
        {
            var changed = Items.Where(i => i.Before != i.After || i.ForceRestore).ToArray();
            foreach (var item in Items)
                if (Fingerprint(item.Path, token) != item.After) throw new IOException("작업 이후 변경되거나 같은 이름이 생긴 항목이 있어 중단했습니다: " + item.Path);
            var staged = new List<(Snapshot Item, string Ready, string Hold)>();
            var held = new List<(Snapshot Item, string Ready, string Hold)>();
            var installed = new List<(Snapshot Item, string Ready, string Hold)>();
            try
            {
                foreach (var item in changed)
                {
                    var parent = Path.GetDirectoryName(item.Path)!;
                    if (!Directory.Exists(parent)) throw new IOException("원래 상위 폴더를 찾을 수 없습니다: " + parent);
                    var prefix = Path.Combine(parent, ".pinshelf-undo-" + Guid.NewGuid().ToString("N"));
                    var entry = (Item: item, Ready: prefix + ".ready", Hold: prefix + ".hold");
                    staged.Add(entry);
                    if (item.Before != "missing")
                    {
                        if (Fingerprint(item.Backup, token) != item.Before) throw new IOException("실행 취소 백업이 변경되었습니다.");
                        Clone(item.Backup, entry.Ready, token);
                    }
                }
                foreach (var item in Items)
                    if (Fingerprint(item.Path, token) != item.After) throw new IOException("실행 취소 준비 중 대상이 변경되었습니다: " + item.Path);
                foreach (var entry in staged)
                {
                    token.ThrowIfCancellationRequested();
                    if (entry.Item.After != "missing") { Move(entry.Item.Path, entry.Hold); held.Add(entry); }
                    if (entry.Item.Before != "missing") { Move(entry.Ready, entry.Item.Path); installed.Add(entry); }
                }
                // Retain post-operation items until the caller safely recycles them.
                return held.Select(x => x.Hold).ToArray();
            }
            catch
            {
                foreach (var entry in installed.AsEnumerable().Reverse()) Move(entry.Item.Path, entry.Ready);
                foreach (var entry in held.AsEnumerable().Reverse()) Move(entry.Hold, entry.Item.Path);
                throw;
            }
            finally
            {
                // Only our own prepared copies, never user paths, are removed here.
                foreach (var entry in staged)
                {
                    try { if (Directory.Exists(entry.Ready)) Directory.Delete(entry.Ready, true); else if (File.Exists(entry.Ready)) File.Delete(entry.Ready); } catch { }
                }
            }
        }, token);
    }
    private static void Move(string source, string target)
    { if (Directory.Exists(source)) Directory.Move(source, target); else File.Move(source, target); }

    internal static string Fingerprint(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!File.Exists(path) && !Directory.Exists(path)) return "missing";
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string text) => hash.AppendData(Encoding.UTF8.GetBytes(text + "\0"));
        void Visit(string current, string relative)
        {
            token.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("연결 폴더/심볼릭 링크는 안전한 실행 취소 백업을 지원하지 않습니다: " + current);
            Add(relative); Add(((int)attributes).ToString());
            if (Directory.Exists(current))
                foreach (var child in Directory.EnumerateFileSystemEntries(current).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) Visit(child, Path.GetRelativePath(path, child));
            else
            {
                Add(File.GetLastWriteTimeUtc(current).Ticks.ToString());
                using var stream = File.OpenRead(current); var buffer = new byte[131072]; int count;
                while ((count = stream.Read(buffer)) != 0) { token.ThrowIfCancellationRequested(); hash.AppendData(buffer.AsSpan(0, count)); }
            }
        }
        Visit(path, ""); return Convert.ToHexString(hash.GetHashAndReset());
    }
    private static void Clone(string source, string target, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var attributes = File.GetAttributes(source);
        if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("연결 경로는 백업할 수 없습니다: " + source);
        if (Directory.Exists(source))
        {
            Directory.CreateDirectory(target);
            foreach (var child in Directory.EnumerateFileSystemEntries(source)) Clone(child, Path.Combine(target, Path.GetFileName(child)), token);
            Directory.SetLastWriteTimeUtc(target, Directory.GetLastWriteTimeUtc(source));
        }
        else
        {
            File.Copy(source, target, false);
            token.ThrowIfCancellationRequested();
            File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(source));
        }
        File.SetAttributes(target, attributes);
    }
}
