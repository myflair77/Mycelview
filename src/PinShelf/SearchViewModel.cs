using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using System.ComponentModel;

namespace PinShelf;

public sealed class SearchViewModel : Observable, IDisposable
{
    public ResultCollection<FileEntry> Results { get; } = [];
    public ObservableCollection<string> Issues { get; } = [];
    private string query = "", status = "파일명·폴더명·경로 검색", scope = "", currentPath = "";
    private bool running, visible;
    private long scanned, skipped;
    private CancellationTokenSource? cancellation;
    private int generation;
    private readonly LocalSearchIndex? index;
    private readonly DispatcherTimer typing = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private bool dismissed;
    private int limit = 200;
    private bool more;
    public bool More { get => more; private set => Set(ref more, value); }
    public string Query
    {
        get => query;
        set
        {
            if (query == value) return;
            Set(ref query, value); limit = 200; dismissed = false; typing.Stop();
            cancellation?.Cancel(); generation++;
            if (string.IsNullOrWhiteSpace(value)) { Results.Clear(); Issues.Clear(); Visible = false; Running = false; More = false; Status = "검색어를 입력하세요."; return; }
            Visible = true; typing.Start();
        }
    }
    public string Status { get => status; private set => Set(ref status, value); }
    public string Scope { get => scope; private set => Set(ref scope, value); }
    public string CurrentPath { get => currentPath; private set => Set(ref currentPath, value); }
    public bool Running { get => running; private set => Set(ref running, value); }
    public bool Visible { get => visible; set => Set(ref visible, value); }
    public RelayCommand Start { get; }
    public RelayCommand Cancel { get; }
    public RelayCommand Close { get; }
    public RelayCommand Clear { get; }
    public RelayCommand LoadMore { get; }
    public void HideResults() { dismissed = true; Visible = false; }
    public void ShowResults() { dismissed = false; Visible = !string.IsNullOrWhiteSpace(Query); }
    public SearchViewModel(LocalSearchIndex? index = null)
    {
        this.index = index;
        typing.Tick += (_, _) => { typing.Stop(); _ = RunAsync(); };
        if (index != null) index.PropertyChanged += IndexChanged;
        Start = new(() => _ = RunAsync()); Cancel = new(() => cancellation?.Cancel());
        Close = new(HideResults);
        Clear = new(() => Query = "");
        LoadMore = new(() => { limit += 200; _ = RunAsync(); });
    }
    private void IndexChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(LocalSearchIndex.Revision) && !Running && !typing.IsEnabled && !string.IsNullOrWhiteSpace(Query) && Visible) typing.Start(); }
    internal async Task RunAsync(string[]? roots = null)
    {
        typing.Stop();
        cancellation?.Cancel(); cancellation?.Dispose(); cancellation = new();
        var token = cancellation.Token; var version = ++generation;
        Issues.Clear(); scanned = skipped = 0; CurrentPath = ""; Scope = ""; Visible = !dismissed; Running = false; More = false;
        Func<string, bool> match;
        try { match = SearchQuery.Parse(Query); }
        catch (FormatException e) { Results.Clear(); Status = e.Message; return; }
        Running = true; Status = "검색 범위를 확인하는 중…";
        try
        {
            if (roots == null && index != null)
            {
                var result = await index.QueryAsync(Query, limit, token);
                if (version != generation) return;
                Results.ReplaceAll(result.Items);
                foreach (var issue in index.Issues) Issues.Add(issue);
                More = result.More; Scope = "로컬 인덱스 · " + index.Scope;
                Status = !index.Ready ? "최초 인덱스 준비 중… 수집된 항목부터 자동 검색합니다." : $"{Results.Count:N0}개 표시{(More ? " · 더 많은 결과 있음" : "")} · {result.Milliseconds:N0}ms · {(index.IsRebuilding ? "기존 인덱스 검색 · 재구축 중" : index.Busy ? "인덱싱 중 부분 결과" : "인덱스 검색")}";
                return;
            }
            Results.Clear();
            roots ??= await Task.Run(SearchService.LocalRoots, token);
            if (version != generation) return;
            Scope = "검색 범위: " + string.Join(" · ", roots) + " (로컬 드라이브, 파일명·경로)";
            if (roots.Length == 0) { Status = "검색할 로컬 드라이브가 없습니다."; return; }
            await Task.Run(() => SearchService.ScanAsync(roots, match, progress => Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (version != generation) return;
                foreach (var entry in progress.Matches) Results.Add(entry);
                foreach (var issue in progress.Issues) if (Issues.Count < 200) Issues.Add(issue);
                scanned = progress.Scanned; skipped = progress.Skipped; CurrentPath = progress.CurrentPath;
                Status = $"검색 중 · {scanned:N0}개 확인 · {Results.Count:N0}개 결과 · 제외 {skipped:N0}개";
            }).Task, token), token);
            if (version == generation) Status = Summary(token.IsCancellationRequested ? "검색 취소 — 부분 결과" : "검색 완료");
        }
        catch (OperationCanceledException) { if (version == generation) Status = Summary("검색 취소 — 부분 결과"); }
        catch (Exception e) { if (version == generation) Status = "검색 중단 — 부분 결과: " + e.Message; }
        finally { if (version == generation) Running = false; }
    }
    private string Summary(string label) => $"{label} · {scanned:N0}개 확인 · {Results.Count:N0}개 결과 · 접근 실패/연결 폴더 제외 {skipped:N0}개 (상세 최대 200개)";
    public void Dispose() { typing.Stop(); if (index != null) index.PropertyChanged -= IndexChanged; generation++; cancellation?.Cancel(); cancellation?.Dispose(); }
}
