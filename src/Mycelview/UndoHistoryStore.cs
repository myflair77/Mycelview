using System.IO;
using System.Text.Json;
namespace Mycelview;
internal sealed class UndoHistoryStore
{
    internal sealed class Catalog
    {
        public int Version { get; set; } = 1;
        public int LimitMB { get; set; } = 1024;
        public int Days { get; set; } = 30;
        public List<string> Undo { get; set; } = [];
        public List<string> Redo { get; set; } = [];
        public string? Pending { get; set; }
        public bool PendingRedo { get; set; }
    }
    internal Catalog Data { get; private set; } = new();
    internal string Root { get; }
    internal Dictionary<string, FileUndoJournal> Records { get; } = new();
    internal UndoHistoryStore(string root)
    {
        Root = root; Directory.CreateDirectory(root);
        var path = Path.Combine(root, "history.json");
        if (File.Exists(path)) Data = JsonSerializer.Deserialize<Catalog>(File.ReadAllText(path)) ?? throw new IOException("기록 목록 오류");
        if (Data.Version != 1 || Data.LimitMB < 1 || Data.Days < 1) throw new IOException("기록 설정 오류");
        foreach (var id in Data.Undo.Concat(Data.Redo).Distinct())
        {
            if (!Guid.TryParseExact(id, "N", out _)) throw new IOException("기록 ID 오류");
            Records.Add(id, FileUndoJournal.Load(Path.Combine(root, id)));
        }
        if (Data.Pending is string pending)
        {
            var record = Records[pending];
            if (record.Matches(!Data.PendingRedo)) Finish(Data.PendingRedo);
            else if (record.Matches(Data.PendingRedo)) { Data.Pending = null; Save(); }
            else throw new IOException("중단된 실행 취소 작업을 확인해야 합니다. 보관 기록은 유지됩니다.");
        }
        Cleanup();
    }
    internal void Save() => AtomicWrite(Path.Combine(Root, "history.json"), Data);
    internal static void AtomicWrite<T>(string path, T data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(stream, data); stream.Flush(true); }
        File.Move(temp, path, true);
    }
    internal static long Size(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return 0;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("연결된 경로는 보관할 수 없습니다.");
        return File.Exists(path) ? new FileInfo(path).Length : Directory.EnumerateFileSystemEntries(path).Sum(Size);
    }
    internal static void RemoveOwned(string path, string root)
    {
        if (!FileOperationRules.Inside(path, root) || FileOperationRules.Same(path, root)) throw new IOException("보관 경로 오류");
        if (!File.Exists(path) && !Directory.Exists(path)) return;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("연결된 보관 경로는 정리할 수 없습니다.");
        if (Directory.Exists(path)) { foreach (var child in Directory.EnumerateFileSystemEntries(path)) RemoveOwned(child, root); Directory.Delete(path); }
        else { File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); }
    }
    internal long Usage => Size(Root);
    internal void Cleanup(long reserve = 0, string? protect = null)
    {
        if (Data.Pending != null) throw new IOException("중단된 기록 작업을 먼저 확인하세요.");
        var limit = Data.LimitMB * 1024L * 1024;
        if (reserve > limit) throw new IOException("작업 보관 용량이 한도를 초과합니다. 기록 관리에서 한도를 늘리세요.");
        foreach (var record in Records.Values.OrderBy(x => x.CreatedAt).ToArray())
        {
            if (record.Id == protect) continue;
            if (DateTimeOffset.UtcNow - record.CreatedAt <= TimeSpan.FromDays(Data.Days) && Usage + reserve <= limit) continue;
            Data.Undo.Remove(record.Id); Data.Redo.Remove(record.Id); Save();
            RemoveOwned(record.DirectoryPath, Root); Records.Remove(record.Id);
        }
        if (Usage + reserve > limit) throw new IOException("보관 용량이 부족합니다. 기록 관리에서 한도를 늘리세요.");
    }
    internal void Add(FileUndoJournal record)
    {
        var discarded = Data.Redo.ToArray(); Data.Redo.Clear();
        Data.Undo.Add(record.Id); Records.Add(record.Id, record); Save();
        foreach (var id in discarded) { RemoveOwned(Records[id].DirectoryPath, Root); Records.Remove(id); }
        Cleanup(protect: record.Id);
    }
    internal void Begin(bool redo) { Data.Pending = (redo ? Data.Redo : Data.Undo).Last(); Data.PendingRedo = redo; Save(); }
    internal void Finish(bool redo)
    {
        var source = redo ? Data.Redo : Data.Undo; var target = redo ? Data.Undo : Data.Redo;
        var id = source.Last(); source.RemoveAt(source.Count - 1); target.Add(id); Data.Pending = null; Save();
    }
}
