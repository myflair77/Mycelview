using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace PinShelf;

public sealed record IndexResults(List<FileEntry> Items, bool More, double Milliseconds);

internal sealed class SearchIndexGeneration : Observable, IDisposable
{
    private readonly string directory;
    private string DatabasePath => Path.Combine(directory, "search-index.sqlite");
    private readonly CancellationTokenSource lifetime = new();
    internal bool Stopped => lifetime.IsCancellationRequested;
    internal string DirectoryPath => directory;
    private int runningQueries;
    internal async Task RetireAsync() { await StopAsync(); while (Volatile.Read(ref runningQueries) > 0) await Task.Delay(25); }
    private readonly ConcurrentDictionary<string, bool> pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FileSystemWatcher> watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> issues = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> failedWatchers = new();
    private string[] roots = [];
    private Task? worker;
    private volatile bool reconcile;
    private volatile bool ready;
    private readonly SearchMemoryCache memory = new();
    private long visited, updates;
    private long revision, reportedRevision;
    public long Revision => Interlocked.Read(ref revision);
    private string status = "파일 검색 준비 중…";
    private bool busy;
    public string Status { get => status; private set => Set(ref status, value); }
    public bool Busy { get => busy; private set => Set(ref busy, value); }
    public bool Ready => ready;
    public IReadOnlyList<string> Issues => issues.Select(x => x.Key + " — " + x.Value).ToArray();
    internal IReadOnlyList<IndexIssue> ErrorDetails => issues.Select(x => new IndexIssue(x.Key, x.Value)).OrderBy(x => x.Path).ToArray();
    public string Scope => string.Join(" · ", roots);
    internal string[] RootPaths => roots;
    public Task InitialPass { get; private set; } = Task.CompletedTask;
    public SearchIndexGeneration(string directory, string[]? excluded = null, string? cacheRoot = null)
    { this.directory = Path.GetFullPath(directory); exclusions = (excluded ?? []).Append(cacheRoot ?? directory).Select(Key).ToArray(); }
    private readonly string[] exclusions;
    private long entryCount;
    public long EntryCount => Interlocked.Read(ref entryCount);
    public long Visited => Interlocked.Read(ref visited);
    public DateTime? LastCompleted { get; private set; }
    internal string[] ErrorPaths => issues.Keys.ToArray();
    internal void RetryErrors(IEnumerable<string> paths) { RefreshRequested = true; foreach (var path in paths) Queue(path, true); }
    private volatile bool RefreshRequested;
    internal long StorageBytes => new[] { DatabasePath, DatabasePath + "-wal", DatabasePath + "-shm" }.Sum(p => File.Exists(p) ? new FileInfo(p).Length : 0);
    private void Report(string message, bool active)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(() =>
        {
            Status = message; Busy = active;
            var current = Revision;
            if (current != reportedRevision) { reportedRevision = current; Notify(nameof(Revision)); }
        });
    }
    private void Issue(string path, string message)
    { if (issues.Count < 200 || issues.ContainsKey(path)) issues[path] = message; }
    private static string Key(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).ToUpperInvariant();
    private bool Excluded(string path) { var key = Key(path); return exclusions.Any(x => key == x || key.StartsWith(x.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal)); }
    public void Start(string[]? scope = null)
    {
        if (worker != null) return;
        roots = scope ?? SearchService.LocalRoots();
        var initial = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); InitialPass = initial.Task;
        worker = Task.Run(async () =>
        {
            try
            {
                Directory.CreateDirectory(directory);
                using var db = new IndexDatabase(DatabasePath);
                db.Execute("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; CREATE TABLE IF NOT EXISTS entries(key TEXT PRIMARY KEY,path TEXT NOT NULL,name TEXT NOT NULL,parent TEXT NOT NULL,folder INTEGER,modified INTEGER,created INTEGER,size INTEGER,search TEXT,initials TEXT); CREATE INDEX IF NOT EXISTS entries_parent ON entries(parent); CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY,value TEXT);");
                LoadMemory(db, lifetime.Token); ready = true;
                using (var completed = db.Prepare("SELECT value FROM meta WHERE key='last_complete'"))
                    if (completed.Step() && DateTime.TryParse(completed.Text(0), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var last)) LastCompleted = last;
                using (var count = db.Prepare("SELECT count(*) FROM entries")) { count.Step(); Interlocked.Exchange(ref entryCount, count.Number(0)); Report($"저장된 검색 목록 {count.Number(0):N0}개 · 바뀐 파일 확인 중…", true); }
                RefreshWatchers();
                reconcile = false;
                Scan(db, roots, lifetime.Token);
                FinishPass(db); initial.TrySetResult();
                var check = Stopwatch.StartNew();
                while (!lifetime.IsCancellationRequested)
                {
                    await Task.Delay(500, lifetime.Token);
                    if (RefreshRequested || check.Elapsed.TotalSeconds >= 60) { RefreshRequested = false; RefreshWatchers(); check.Restart(); }
                    if (reconcile)
                    {
                        RefreshWatchers(); reconcile = false; Report("앱이 꺼져 있던 동안 바뀐 파일 확인 중 · 검색 가능", true);
                        Scan(db, roots, lifetime.Token); FinishPass(db);
                    }
                    var changes = pending.Keys.Take(2048).ToArray();
                    foreach (var path in changes)
                    {
                        if (!pending.TryRemove(path, out var recursive)) continue;
                        lifetime.Token.ThrowIfCancellationRequested();
                        try
                        {
                            if (Directory.Exists(path) && recursive) { RefreshPath(db, path); Scan(db, [path], lifetime.Token); }
                            else RefreshPath(db, path);
                        }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Issue(path, e.Message); }
                    }
                    if (changes.Length > 0) { UpdateCount(db); LastCompleted = DateTime.UtcNow; db.Execute("INSERT OR REPLACE INTO meta VALUES('last_complete', datetime('now'))"); Report($"파일 변경 자동 감지 중 · {updates:N0}건 갱신 · 처리 대기 {pending.Count:N0}건 · 확인 못한 경로 {issues.Count}개", false); }
                }
            }
            catch (OperationCanceledException) { initial.TrySetCanceled(); }
            catch (Exception e) { Report("인덱스 오류: " + e.Message + " · 프로그램을 다시 시작하면 재시도합니다.", false); initial.TrySetException(e); }
            finally { foreach (var watcher in watchers.Values) watcher.Dispose(); watchers.Clear(); }
        });
        // Observe initialization errors even when the UI does not await the initial pass.
        _ = InitialPass.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
    }
    private void Queue(string path, bool recursive)
    {
        if (Excluded(path)) return;
        if (pending.Count > 50000) { reconcile = true; return; }
        pending.AddOrUpdate(path, recursive, (_, old) => old || recursive);
    }
    private void RefreshWatchers()
    {
        while (failedWatchers.TryDequeue(out var failed)) if (watchers.Remove(failed, out var old)) old.Dispose();
        foreach (var root in roots)
        {
            if (watchers.ContainsKey(root)) continue;
            try
            {
                var watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime, InternalBufferSize = 65536 };
                watcher.Created += (_, e) => Queue(e.FullPath, true);
                watcher.Changed += (_, e) => Queue(e.FullPath, false);
                watcher.Deleted += (_, e) => Queue(e.FullPath, false);
                watcher.Renamed += (_, e) => { Queue(e.OldFullPath, false); Queue(e.FullPath, true); };
                watcher.Error += (_, e) => { Issue(root, "변경 감지 누락: " + e.GetException().Message); failedWatchers.Enqueue(root); reconcile = true; };
                watcher.EnableRaisingEvents = true; watchers.Add(root, watcher); reconcile = true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { Issue(root, "감시 불가: " + e.Message); }
        }
    }
    private void Scan(IndexDatabase db, IEnumerable<string> start, CancellationToken token)
    {
        var directories = new Stack<string>(start.Reverse()); var clock = Stopwatch.StartNew(); int batched = 0;
        db.Execute("BEGIN IMMEDIATE");
        try
        {
            while (directories.TryPop(out var path))
            {
                token.ThrowIfCancellationRequested(); if (Excluded(path)) continue;
                try
                {
                    var folder = new DirectoryInfo(path);
                    if (!folder.Exists) { Issue(path, "드라이브 또는 폴더에 접근할 수 없음 (이전 인덱스 유지)"); continue; }
                    if ((folder.Attributes & FileAttributes.ReparsePoint) != 0) { Issue(path, "연결 폴더 하위 탐색 제외"); continue; }
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var item in folder.EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = false, AttributesToSkip = 0 }))
                    {
                        token.ThrowIfCancellationRequested(); if (Excluded(item.FullName)) continue;
                        seen.Add(Key(item.FullName)); visited++;
                        try
                        {
                            var isFolder = (item.Attributes & FileAttributes.Directory) != 0;
                            Upsert(db, item, isFolder);
                            if (isFolder) directories.Push(item.FullName);
                        }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Issue(item.FullName, e.Message); }
                        if (++batched >= 512) { db.Execute("COMMIT; BEGIN IMMEDIATE"); batched = 0; }
                        if (clock.ElapsedMilliseconds > 250)
                        { Report($"검색 목록 확인 중 · {visited:N0}개 확인 · 바뀐 항목 {updates:N0}개 갱신 · 남은 폴더 {directories.Count:N0}개", true); clock.Restart(); }
                    }
                    // Only prune a directory after complete, successful enumeration.
                    var stale = new List<string>();
                    using (var list = db.Prepare("SELECT key FROM entries WHERE parent=?1"))
                    { list.Bind(1, Key(path)); while (list.Step()) if (!seen.Contains(list.Text(0))) stale.Add(list.Text(0)); }
                    foreach (var key in stale) DeleteTree(db, key);
                    issues.TryRemove(path, out _);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Issue(path, e.Message); }
            }
            db.Execute("COMMIT");
        }
        catch { db.Execute("ROLLBACK"); throw; }
    }
    private void RefreshPath(IndexDatabase db, string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            bool folder = (attributes & FileAttributes.Directory) != 0;
            Upsert(db, folder ? new DirectoryInfo(path) : new FileInfo(path), folder);
            issues.TryRemove(path, out _);
        }
        catch (FileNotFoundException) { DeleteTree(db, Key(path)); }
        catch (DirectoryNotFoundException) { DeleteTree(db, Key(path)); }
    }
    private void Upsert(IndexDatabase db, FileSystemInfo item, bool folder)
    {
        using var command = db.Prepare("INSERT INTO entries VALUES(?1,?2,?3,?4,?5,?6,?7,?8,?9,?10) ON CONFLICT(key) DO UPDATE SET path=excluded.path,name=excluded.name,parent=excluded.parent,folder=excluded.folder,modified=excluded.modified,created=excluded.created,size=excluded.size,search=excluded.search,initials=excluded.initials WHERE path<>excluded.path OR folder<>excluded.folder OR modified<>excluded.modified OR created<>excluded.created OR size<>excluded.size");
        command.Bind(1, Key(item.FullName)); command.Bind(2, item.FullName); command.Bind(3, item.Name);
        command.Bind(4, Key(Path.GetDirectoryName(item.FullName)!)); command.Bind(5, folder ? 1 : 0);
        command.Bind(6, item.LastWriteTime.Ticks); command.Bind(7, item.CreationTime.Ticks); command.Bind(8, folder ? 0 : ((FileInfo)item).Length);
        command.Bind(9, item.FullName.ToUpperInvariant()); command.Bind(10, SearchQuery.Initials(item.FullName).ToUpperInvariant()); command.Step();
        using var changed = db.Prepare("SELECT changes()"); changed.Step(); var count = changed.Number(0); updates += count;
        if (count > 0)
        {
            using var row = db.Prepare("SELECT rowid,search,initials FROM entries WHERE key=?1"); row.Bind(1, Key(item.FullName));
            if (row.Step()) memory.Put(row.Number(0), row.Text(1), row.Text(2), folder);
            Interlocked.Increment(ref revision);
        }
    }
    private void DeleteTree(IndexDatabase db, string key)
    {
        var prefix = key.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var command = db.Prepare("DELETE FROM entries WHERE key=?1 OR (key>=?2 AND key<?3)");
        command.Bind(1, key); command.Bind(2, prefix); command.Bind(3, prefix[..^1] + ']'); command.Step();
        using var changed = db.Prepare("SELECT changes()"); changed.Step(); updates += changed.Number(0);
        memory.RemoveTree(key);
        if (changed.Number(0) > 0) Interlocked.Increment(ref revision);
    }
    private void UpdateCount(IndexDatabase db)
    { using var count = db.Prepare("SELECT count(*) FROM entries"); count.Step(); Interlocked.Exchange(ref entryCount, count.Number(0)); }
    private void FinishPass(IndexDatabase db)
    {
        LastCompleted = DateTime.UtcNow; UpdateCount(db);
        db.Execute("INSERT OR REPLACE INTO meta VALUES('last_complete', datetime('now'))");
        using var count = db.Prepare("SELECT count(*) FROM entries"); count.Step();
        Report($"검색 준비 완료 · {count.Number(0):N0}개 · 파일 변경 자동 감지 중 · 확인 못한 경로 {issues.Count}개", false);
    }
    public async Task<IndexResults> QueryAsync(string query, int limit, CancellationToken token)
    {
        var plan = SearchQuery.Compile(query);
        if (!ready) return new([], false, 0);
        Interlocked.Increment(ref runningQueries);
        try { return await Task.Run(() =>
        {
            var clock = Stopwatch.StartNew();
            var ids = memory.Find(plan, limit + 1, token);
            var more = ids.Count > limit; if (more) ids.RemoveAt(ids.Count - 1);
            var matched = new Dictionary<long, FileEntry>();
            using var db = new IndexDatabase(DatabasePath, readOnly: true);
            using var registration = token.Register(db.Interrupt);
            foreach (var chunk in ids.Chunk(256))
            {
                token.ThrowIfCancellationRequested();
                using var statement = db.Prepare($"SELECT rowid,path,name,folder,modified,created,size FROM entries WHERE rowid IN ({string.Join(',', chunk.Select((_, i) => '?' + (i + 1).ToString()))})");
                for (int i = 0; i < chunk.Length; i++) statement.Bind(i + 1, chunk[i]);
                while (statement.Step())
                {
                    token.ThrowIfCancellationRequested();
                    // A watcher may replace an entry between the memory match and metadata read.
                    if (plan.Match(statement.Text(1))) matched[statement.Number(0)] = new(statement.Text(1), statement.Text(2), statement.Number(3) != 0, new DateTime(statement.Number(4)), statement.Number(6)) { Created = new DateTime(statement.Number(5)) };
                }
            }
            return new IndexResults(ids.Where(matched.ContainsKey).Select(id => matched[id]).ToList(), more, clock.Elapsed.TotalMilliseconds);
        }, token); }
        finally { Interlocked.Decrement(ref runningQueries); }
    }
    private void LoadMemory(IndexDatabase db, CancellationToken token)
    {
        using (var size = db.Prepare("SELECT count(*) FROM entries")) { size.Step(); memory.Reserve((int)Math.Min(size.Number(0), int.MaxValue)); }
        var clock = Stopwatch.StartNew(); int count = 0;
        using var rows = db.Prepare("SELECT rowid,search,initials,folder FROM entries");
        while (rows.Step())
        {
            token.ThrowIfCancellationRequested();
            memory.Put(rows.Number(0), rows.Text(1), rows.Text(2), rows.Number(3) != 0); count++;
            if (clock.ElapsedMilliseconds > 250) { Report($"검색 준비 중 · 저장된 목록 {count:N0}개 불러오는 중…", true); clock.Restart(); }
        }
        Interlocked.Increment(ref revision);
    }
    internal async Task LoadReadOnlyAsync()
    {
        roots = SearchService.LocalRoots();
        await Task.Run(() => { using var db = new IndexDatabase(DatabasePath, readOnly: true); LoadMemory(db, lifetime.Token); ready = true; });
        Report("저장된 전체 인덱스 · 읽기 전용 성능 검증", false);
    }
    internal void RequestReconcile() => reconcile = true;
    public void Dispose() { lifetime.Cancel(); }
    internal async Task StopAsync() { Dispose(); if (worker != null) await worker; }
}
