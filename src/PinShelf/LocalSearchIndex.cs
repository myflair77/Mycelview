using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
namespace PinShelf;

public sealed record IndexIssue(string Path, string Reason);
public sealed class IndexSettings
{
    public int Version { get; set; } = 1;
    public bool AllLocalDrives { get; set; } = true;
    public string[] IncludedFolders { get; set; } = [];
    public string[] ExcludedFolders { get; set; } = [];
    internal IndexSettings Copy() => new() { AllLocalDrives = AllLocalDrives, IncludedFolders = (IncludedFolders ?? []).ToArray(), ExcludedFolders = (ExcludedFolders ?? []).ToArray() };
    internal string[] Roots => AllLocalDrives ? SearchService.LocalRoots() : IncludedFolders;
    internal IndexSettings Validate()
    {
        if (Version != 1) throw new IOException("지원하지 않는 검색 설정 버전입니다.");
        string[] Normalize(string[] values) => values.Select(x => {
            if (string.IsNullOrWhiteSpace(x) || !Path.IsPathFullyQualified(x) || x.StartsWith(@"\\")) throw new IOException("로컬 드라이브의 전체 폴더 경로를 입력하세요: " + x);
            var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(x.Trim()));
            if (File.Exists(path)) throw new IOException("파일 대신 폴더를 선택하세요: " + path);
            return path;
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var copy = Copy(); copy.IncludedFolders = Normalize(IncludedFolders ?? []); copy.ExcludedFolders = Normalize(ExcludedFolders ?? []);
        if (!copy.AllLocalDrives && copy.IncludedFolders.Length == 0) throw new IOException("검색 대상 폴더를 하나 이상 추가하세요.");
        return copy;
    }
}

// The active generation stays queryable until a fully scanned replacement is committed.
public sealed class LocalSearchIndex : Observable, IDisposable
{
    private sealed class Pointer
    {
        public int Version { get; set; } = 1;
        public string Generation { get; set; } = "";
        public IndexSettings Settings { get; set; } = new();
    }
    private readonly string directory;
    private readonly object generationGate = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly List<SearchIndexGeneration> retired = [];
    private SearchIndexGeneration active;
    private SearchIndexGeneration? candidate;
    private IndexSettings settings = new();
    private Pointer? pointer;
    private Task? rebuilding;
    private bool started, disposed;
    private long revision, notifiedRevision;
    private string managementMessage = "";
    private string SettingsPath => Path.Combine(directory, "index-settings.json");
    private string PointerPath => Path.Combine(directory, "index-current.json");
    public IndexSettings Settings => settings.Copy();
    public string Status => IsRebuilding ? "인덱스 재구축 중 · 기존 검색 사용 가능 · " + candidate!.Status : managementMessage.Length > 0 ? managementMessage : active.Status;
    public bool Busy => IsRebuilding || active.Busy;
    public bool IsRebuilding => candidate != null;
    public bool Ready => active.Ready;
    public string Scope => active.Scope;
    public long Revision => Interlocked.Read(ref revision);
    public long EntryCount => active.EntryCount;
    public long ScannedCount => (candidate ?? active).Visited;
    public DateTime? LastUpdated => active.LastCompleted?.ToLocalTime();
    public long StorageBytes { get { try { return active.StorageBytes + (candidate?.StorageBytes ?? 0); } catch (IOException) { return 0; } } }
    public IReadOnlyList<string> Issues => active.Issues;
    public IReadOnlyList<IndexIssue> ErrorDetails => (candidate ?? active).ErrorDetails;
    public Task InitialPass { get; private set; } = Task.CompletedTask;
    public LocalSearchIndex(string directory)
    {
        this.directory = Path.GetFullPath(directory);
        try { if (File.Exists(SettingsPath)) settings = (JsonSerializer.Deserialize<IndexSettings>(File.ReadAllText(SettingsPath)) ?? throw new IOException("검색 설정이 비어 있습니다.")).Validate(); }
        catch (Exception ex) when (ex is IOException or JsonException or ArgumentException) { managementMessage = "검색 설정 읽기 실패: " + ex.Message; }
        var activeDirectory = this.directory;
        try
        {
            if (File.Exists(PointerPath))
            {
                pointer = JsonSerializer.Deserialize<Pointer>(File.ReadAllText(PointerPath)) ?? throw new IOException("인덱스 위치가 비어 있습니다.");
                if (pointer.Version != 1 || !Guid.TryParseExact(pointer.Generation, "N", out _)) throw new IOException("인덱스 위치 정보 오류");
                activeDirectory = Path.Combine(this.directory, "index-generations", pointer.Generation);
                if (!File.Exists(Path.Combine(activeDirectory, "search-index.sqlite"))) throw new IOException("저장된 인덱스 파일이 없습니다.");
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException) { pointer = null; activeDirectory = this.directory; managementMessage = "인덱스 위치 읽기 실패: " + ex.Message; }
        active = Create(activeDirectory, pointer?.Settings ?? (File.Exists(Path.Combine(this.directory, "search-index.sqlite")) ? new IndexSettings() : settings));
    }
    private SearchIndexGeneration Create(string path, IndexSettings config)
    { var generation = new SearchIndexGeneration(path, config.ExcludedFolders, directory); generation.PropertyChanged += Changed; return generation; }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (disposed || sender != active && sender != candidate) return;
        if (sender == active && e.PropertyName == nameof(Status)) managementMessage = "";
        if (sender == active && e.PropertyName == nameof(Revision)) Interlocked.Increment(ref revision);
        Publish();
    }
    private void Publish()
    {
        if (Application.Current.Dispatcher.HasShutdownStarted) return;
        if (!Application.Current.Dispatcher.CheckAccess()) { Application.Current.Dispatcher.BeginInvoke(Publish); return; }
        if (notifiedRevision != Revision) { notifiedRevision = Revision; Notify(nameof(Revision)); }
        foreach (var name in new[] { nameof(Status), nameof(Busy), nameof(IsRebuilding), nameof(Ready), nameof(Scope), nameof(EntryCount), nameof(ScannedCount), nameof(LastUpdated), nameof(StorageBytes), nameof(ErrorDetails) }) Notify(name);
    }
    public void Start(string[]? scope = null)
    {
        if (started) return; started = true;
        if (scope != null) settings = new IndexSettings { AllLocalDrives = false, IncludedFolders = scope }.Validate();
        active.Start(scope ?? (pointer?.Settings ?? (File.Exists(Path.Combine(directory, "search-index.sqlite")) ? new IndexSettings() : settings)).Roots);
        InitialPass = active.InitialPass;
        if (scope == null && File.Exists(SettingsPath) && (pointer == null || JsonSerializer.Serialize(pointer.Settings) != JsonSerializer.Serialize(settings)))
            _ = ResumeSettingsAsync();
    }
    private async Task ResumeSettingsAsync()
    { try { await InitialPass; await RebuildAsync(); } catch (Exception ex) { managementMessage = "저장된 검색 범위 적용 실패: " + ex.Message; Publish(); } }
    public Task<IndexResults> QueryAsync(string query, int limit, CancellationToken token) { lock (generationGate) return active.QueryAsync(query, limit, token); }
    public Task ApplySettingsAsync(IndexSettings value)
    {
        if (IsRebuilding) throw new IOException("재구축을 마친 후 검색 범위를 변경하세요.");
        var validated = value.Validate();
        UndoHistoryStore.AtomicWrite(SettingsPath, validated); settings = validated;
        return RebuildAsync();
    }
    public Task RebuildAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (IsRebuilding) throw new IOException("이미 인덱스를 재구축하고 있습니다.");
        var config = settings.Copy();
        var roots = config.Roots;
        if (roots.Length == 0 || roots.Any(p => !Directory.Exists(p))) throw new IOException("연결되지 않았거나 접근할 수 없는 검색 대상이 있습니다. 대상 경로를 확인하세요.");
        var id = Guid.NewGuid().ToString("N");
        var path = Path.Combine(directory, "index-generations", id);
        candidate = Create(path, config); managementMessage = "";
        var replacement = candidate;
        replacement.Start(roots); Publish();
        rebuilding = RebuildCoreAsync(replacement, config, id);
        return rebuilding;
    }
    private async Task RebuildCoreAsync(SearchIndexGeneration replacement, IndexSettings config, string id)
    {
        bool committed = false;
        try
        {
            await replacement.InitialPass;
            lifetime.Token.ThrowIfCancellationRequested();
            if (replacement.Stopped) throw new OperationCanceledException();
            if (replacement.RootPaths.Any(p => !Directory.Exists(p))) throw new IOException("재구축 중 검색 대상 연결이 끊겼습니다.");
            // Persist the pointer first. A crash always reopens either the old or complete new database.
            var next = new Pointer { Generation = id, Settings = config };
            UndoHistoryStore.AtomicWrite(PointerPath, next);
            SearchIndexGeneration old;
            lock (generationGate) { old = active; active = replacement; }
            pointer = next; candidate = null; committed = true;
            old.PropertyChanged -= Changed; retired.Add(old); old.Dispose();
            Interlocked.Increment(ref revision); managementMessage = "재구축 완료 · 새 인덱스로 검색 중";
            Publish();
            await old.RetireAsync(); retired.Remove(old);
            var generationRoot = Path.Combine(directory, "index-generations");
            if (Guid.TryParseExact(Path.GetFileName(old.DirectoryPath), "N", out _) && FileOperationRules.Inside(old.DirectoryPath, generationRoot))
                try { await Task.Run(() => UndoHistoryStore.RemoveOwned(old.DirectoryPath, generationRoot)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { managementMessage += " · 이전 인덱스 정리 보류"; }
        }
        catch (OperationCanceledException) { managementMessage = "인덱스 재구축 취소 · 기존 검색 목록 유지"; throw; }
        catch (Exception ex) { managementMessage = "인덱스 재구축 실패 · 기존 검색 목록 유지: " + ex.Message; throw; }
        finally
        {
            if (!committed)
            {
                replacement.PropertyChanged -= Changed; await replacement.StopAsync(); candidate = null;
                // Only this uncommitted, generated cache directory may be removed.
                try { UndoHistoryStore.RemoveOwned(Path.Combine(directory, "index-generations", id), Path.Combine(directory, "index-generations")); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            Publish();
        }
    }
    public void CancelRebuild() => candidate?.Dispose();
    public void RecheckChanges()
    { if (IsRebuilding) throw new IOException("재구축 중에는 기존 인덱스의 변경 감지가 계속됩니다."); managementMessage = "변경 사항 재확인 요청됨"; active.RequestReconcile(); Publish(); }
    public void RetryErrors(IEnumerable<string> paths)
    { if (IsRebuilding) throw new IOException("재구축 완료 후 오류 경로를 재시도하세요."); managementMessage = "오류 경로 재시도 요청됨"; active.RetryErrors(paths); Publish(); }
    internal void RequestReconcile() => active.RequestReconcile();
    internal Task LoadReadOnlyAsync() => active.LoadReadOnlyAsync();
    public void Dispose() { if (disposed) return; disposed = true; lifetime.Cancel(); active.Dispose(); candidate?.Dispose(); foreach (var item in retired) item.Dispose(); }
    internal async Task StopAsync()
    {
        Dispose(); if (rebuilding != null) { try { await rebuilding; } catch (Exception) { /* Already reported by the rebuild operation. Shutdown still closes all generations. */ } }
        await active.StopAsync(); foreach (var item in retired.ToArray()) await item.StopAsync();
    }
}
