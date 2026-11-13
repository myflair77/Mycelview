using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Mycelview;
internal static class IndexTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task WaitFor(Func<Task<bool>> condition, string label)
    {
        var until = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < until) { if (await condition()) return; await Task.Delay(150); }
        throw new InvalidOperationException("Index update timed out: " + label);
    }
    public static async Task RunAsync(MainWindow window, string directory)
    {
        var root = Path.Combine(directory, "index-fixtures"); Directory.CreateDirectory(root);
        var cache = Path.Combine(directory, "index-cache");
        var budget = Path.Combine(root, "예산 2027.xlsx"); File.WriteAllText(budget, "test");
        File.WriteAllText(Path.Combine(root, "결산 2027.txt"), "test");
        File.WriteAllText(Path.Combine(root, "예산 2027 초안.txt"), "test");
        File.WriteAllText(Path.Combine(root, "100%_확정.txt"), "test");
        for (int i = 0; i < 600; i++) File.WriteAllText(Path.Combine(root, $"일반자료-{i:000}.txt"), "test");
        var index = new LocalSearchIndex(cache);
        try
        {
            index.Start([root]); await index.InitialPass;
            var results = await index.QueryAsync("(예산 or 결산) & 2027 & !초안", 200, default);
            Check(results.Items.Count == 2, "SQL query must match boolean parser semantics.");
            Check((await index.QueryAsync("ㄱㅅ", 200, default)).Items.Single().Name == "결산 2027.txt", "Hangul initials must match.");
            Check((await index.QueryAsync("100%_", 200, default)).Items.Count == 1, "SQL wildcard characters must remain literal search terms.");
            Check((await index.QueryAsync(root, 200, default, new SearchOptions { IncludeParentFolders = true })).More, "Large matches must return a limited first page.");
            var created = Path.Combine(root, "새로운 보고서.txt"); File.WriteAllText(created, "new");
            await WaitFor(async () => (await index.QueryAsync("새로운 보고서", 20, default)).Items.Count == 1, "create");
            File.AppendAllText(created, " changed");
            await WaitFor(async () => (await index.QueryAsync("새로운 보고서", 20, default)).Items.SingleOrDefault()?.Length == new FileInfo(created).Length, "modify");
            var renamed = Path.Combine(root, "이름변경 보고서.txt"); File.Move(created, renamed);
            await WaitFor(async () => (await index.QueryAsync("이름변경", 20, default)).Items.Count == 1 && (await index.QueryAsync("새로운", 20, default)).Items.Count == 0, "rename");
            File.Delete(renamed);
            await WaitFor(async () => (await index.QueryAsync("이름변경", 20, default)).Items.Count == 0, "delete");
            var subtree = Path.Combine(root, "추가폴더"); Directory.CreateDirectory(subtree); File.WriteAllText(Path.Combine(subtree, "내부파일.txt"), "nested");
            await WaitFor(async () => (await index.QueryAsync("추가폴더", 20, default, new SearchOptions { IncludeParentFolders = true })).Items.Count == 2, "new subtree");
            using (var live = new SearchViewModel(index))
            {
                live.Query = "ㄱㅅ";
                await WaitFor(() => Task.FromResult(live.Results.Count == 1 && !live.Running), "live typing without button");
                live.HideResults(); var count = live.Results.Count;
                Check(!live.Visible && count == 1, "Outside click must preserve results.");
                live.ShowResults(); Check(live.Visible && live.Results.Count == count, "Search focus must reopen preserved results.");
                live.Clear.Execute(null); Check(live.Query == "" && !live.Visible && live.Results.Count == 0, "Clear button must empty query and results.");
                live.Query = "일반자료"; await WaitFor(() => Task.FromResult(live.Results.Count == 200 && !live.Running), "first page");
                live.LoadMore.Execute(null); await WaitFor(() => Task.FromResult(live.Results.Count == 400 && !live.Running), "load more");
            }
            File.WriteAllLines(Path.Combine(directory, "index-results.txt"),
            ["PASS: persistent SQLite initial indexing and parameterized logical/initial-consonant queries.",
             "PASS: create, content modify, rename, delete and directory subtree watcher updates.",
             "PASS: live typing, hide/reopen without loss, clear, first-page limit and load more.",
             $"Fixture query: {results.Milliseconds:N1}ms for {results.Items.Count} results (604 initial entries; not a whole-PC guarantee)."]);
        }
        finally { await index.StopAsync(); }
        File.WriteAllText(Path.Combine(root, "종료중 추가.txt"), "offline"); File.Delete(budget);
        await VerifyAsync(directory);
        var vm = window.ViewModel;
        window.ExplorerColumn.Width = new GridLength(300); window.FavoritesColumn.Width = new GridLength(280);
        window.ExplorerTreeRow.Height = new GridLength(180);
        window.FavoritesRow.Height = new GridLength(0.6, GridUnitType.Star); window.RecentRow.Height = new GridLength(0.4, GridUnitType.Star);
        await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
        window.SavePaneLayout(); vm.Save();
        var saved = new StateStore(directory).Load();
        Check(saved.ExplorerWidth == 300 && saved.FavoritesWidth == 280 && saved.ExplorerTreeHeight == 180 && Math.Abs((saved.FavoritesRatio ?? double.NaN) - 0.6) < 0.01, "Pane boundaries must save as widths/heights and ratio.");
    }
    public static async Task VerifyAsync(string directory)
    {
        var cache = Path.Combine(directory, "index-cache"); var root = Path.Combine(directory, "index-fixtures");
        using (var db = new IndexDatabase(Path.Combine(cache, "search-index.sqlite")))
        using (var count = db.Prepare("SELECT count(*) FROM entries")) { Check(count.Step() && count.Number(0) > 600, "Persisted rows must exist before reopening index service."); }
        var index = new LocalSearchIndex(cache);
        try
        {
            index.Start([root]); await index.InitialPass;
            Check((await index.QueryAsync("종료중 추가", 20, default)).Items.Count == 1, "Offline additions must reconcile after restart.");
            Check((await index.QueryAsync("예산 2027.xlsx", 20, default)).Items.Count == 0, "Offline deletions must reconcile after restart.");
            Check((await index.QueryAsync("ㄱㅅ", 20, default)).Items.Count == 1, "Saved index must query after reopening.");
        }
        finally { await index.StopAsync(); }
    }
}
