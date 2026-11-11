using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PinShelf;
internal static class SearchTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    internal static async Task RunAsync(MainWindow window, string directory)
    {
        foreach (var (query, yes, no) in new[]
        {
            ("예산", "2027 예산.xlsx", "결산.xlsx"),
            ("예산 OR 결산 AND 2027", "예산 2026", "결산 2026"),
            ("(예산 or 결산) & 2027 & !초안", "결산 2027.pdf", "예산 2027 초안"),
            ("예산 2027", "예산_2027", "예산_2026"),
            ("\"2027년 예산\"", "최종 2027년 예산.xlsx", "2027년 최종 예산"),
            ("NOT (초안 OR 임시) AND (예산 OR 결산)", "최종 예산", "임시 결산"),
            ("!!예산", "예산", "보고"),
            ("\"OR\"", "or.txt", "예산.txt"),
            ("((예산) AND NOT (초안 OR 임시))", "예산 확정", "예산 초안")
        })
        {
            var predicate = SearchQuery.Parse(query);
            Check(predicate(yes) && !predicate(no), "Query semantics failed: " + query);
        }
        foreach (var query in new[] { "", "foo AND", "OR foo", "foo)", "(foo", "()", "\"bad", "foo && bar", "NOT", "foo OR )", "\"\"", new string('(', 65) + "foo" + new string(')', 65) })
        {
            bool failed = false; try { SearchQuery.Parse(query); } catch (FormatException) { failed = true; }
            Check(failed, "Invalid query must show a syntax error: " + query);
        }
        var root = Path.Combine(directory, "search-fixtures"); Directory.CreateDirectory(root);
        var folder = Path.Combine(root, "예산 2027"); Directory.CreateDirectory(folder);
        var xlsx = Path.Combine(root, "예산 2027.xlsx"); File.WriteAllText(xlsx, "test");
        File.WriteAllText(Path.Combine(root, "결산 2027.pdf"), "test");
        File.WriteAllText(Path.Combine(root, "예산 2027 초안.txt"), "test");
        File.WriteAllText(Path.Combine(root, "예산 2026.txt"), "test");
        File.WriteAllText(Path.Combine(folder, "보고서.txt"), "test");
        var hidden = Path.Combine(root, "숨김 예산 2027.txt"); File.WriteAllText(hidden, "test"); File.SetAttributes(hidden, FileAttributes.Hidden);
        var vm = window.ViewModel; var search = vm.Search;
        search.Query = "(예산 OR 결산) AND 2027 AND NOT 초안";
        await search.RunAsync([root, Path.Combine(root, "missing")]);
        Check(search.Results.Count == 5 && search.Results.Any(e => e.FullPath == hidden) && search.Results.Any(e => e.FullPath.EndsWith("보고서.txt")), "Full path matching, folders and hidden-file search failed.");
        Check(search.Issues.Count > 0 && !search.Running, "Skipped paths must be reported and scan must finish.");
        await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
        var before = vm.Cards.Select(c => c.State.Id).ToHashSet();
        window.SearchResults.SelectedItems.Add(search.Results.Single(e => e.FullPath == folder));
        window.SearchResults.SelectedItems.Add(search.Results.Single(e => e.FullPath == xlsx));
        window.RegisterSearchButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.RegisterDroppedPaths(new DataObject(DataFormats.FileDrop, new[] { xlsx }));
        var added = vm.Cards.Where(c => !before.Contains(c.State.Id)).ToArray();
        Check(added.Length == 3 && added.Count(c => c.IsFolder) == 1, "Search result selection/drop registration must classify correctly.");
        foreach (var card in added) vm.Remove(card);
        search.Query = "예산 AND"; await search.RunAsync([root]);
        Check(!search.Running && search.Results.Count == 0 && search.Status.Contains("필요"), "Invalid query UI must report syntax error without stale results.");
        search.Query = "예산"; var obsolete = search.RunAsync([root]);
        search.Query = "結算_unique_no_match"; await search.RunAsync([root]); await obsolete;
        Check(search.Results.Count == 0, "Previous generation results must not leak into replacement searches.");
        var large = Path.Combine(root, "cancel"); Directory.CreateDirectory(large);
        for (int i = 0; i < 300; i++) File.WriteAllText(Path.Combine(large, $"cancel-{i}.txt"), "test");
        using var cancel = new CancellationTokenSource(); int partial = 0;
        try
        {
            await Task.Run(() => SearchService.ScanAsync([large], _ => true, batch => { partial += batch.Matches.Count; cancel.Cancel(); return Task.CompletedTask; }, cancel.Token));
        }
        catch (OperationCanceledException) { }
        Check(partial > 0 && partial < 300, "Cancellation must stop traversal and retain only partial matches.");
        search.Query = "PinShelf_live_scan_no_match_" + Guid.NewGuid().ToString("N");
        var liveScan = search.RunAsync(SearchService.LocalRoots());
        await Task.Delay(750); search.Cancel.Execute(null); await liveScan;
        Check(!search.Running && search.Scope.Contains("검색 범위") && (search.Status.Contains("취소") || search.Status.Contains("완료")), "Actual PC scope scan/cancel must return cleanly.");
        File.WriteAllLines(Path.Combine(directory, "live-search-check.txt"), [search.Scope, search.Status, "Brief traversal/cancellation check only; not an exhaustive PC audit."]);
        search.Query = "(예산 or 결산) & 2027 & !초안"; await search.RunAsync([root]);
        // Exercise the history model without launching a user's associated application during tests.
        vm.RecordOpenedFile(xlsx); vm.RecordOpenedFile(Path.Combine(root, "결산 2027.pdf")); vm.RecordOpenedFile(xlsx);
        Check(vm.RecentFiles.Count == 2 && vm.RecentFiles[0].CurrentPath == xlsx, "Recent files must be latest-first with no duplicate paths.");
        Check(window.RecentFilesList.Items.Count == 2, "Recent files must render in their independent list.");
        Check(SearchService.LocalRoots().Length > 0, "Local-drive discovery must find this PC's drives.");
        vm.Save();
        File.WriteAllLines(Path.Combine(directory, "search-results.txt"),
        ["PASS: precedence, implicit AND, phrases, nested parentheses, case-insensitive operators and syntax errors.",
         "PASS: real recursive path/name search, folders, hidden files and skipped-path reporting.",
         "PASS: search result multi-selection button and drop payload registration.",
         "PASS: stale-search isolation and cooperative cancellation with partial results.",
         "PASS: local-drive scope discovery; recent-file ordering/deduplication and independent list."]);
    }
}

