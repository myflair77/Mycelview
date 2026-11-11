using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace PinShelf;
internal static class SearchBenchmark
{
    internal static async Task RunAsync(MainWindow window, string output)
    {
        Directory.CreateDirectory(output);
        var index = window.ViewModel.Index; var load = Stopwatch.StartNew();
        await index.LoadReadOnlyAsync(); load.Stop();
        var metrics = new List<object>();
        foreach (var query in new[] { "가", "예산", "ㄱ", "ㅈㄱㅈㅂ", "(예산 OR 결산) AND NOT 초안", "PinShelf_NoMatch_938175" })
        {
            var times = new List<double>(); int found = 0;
            for (int i = 0; i < 4; i++) { var result = await index.QueryAsync(query, 200, default); times.Add(Math.Round(result.Milliseconds, 1)); found = result.Items.Count; }
            window.ViewModel.Search.Clear.Execute(null);
            var timer = Stopwatch.StartNew(); window.SearchBox.Text = query;
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!(window.ViewModel.Search.Status.Contains("ms") && !window.ViewModel.Search.Running))
            { if (DateTime.UtcNow > deadline) throw new TimeoutException("Live search UI benchmark timed out: " + window.ViewModel.Search.Status); await Task.Delay(5); }
            await window.Dispatcher.InvokeAsync(() =>
            { window.UpdateLayout(); window.SearchResultsPanel.UpdateLayout(); }, DispatcherPriority.ContextIdle);
            metrics.Add(new { query, returned = found, databaseAndMemoryMs = times, textToLayoutMs = Math.Round(timer.Elapsed.TotalMilliseconds, 1) });
        }
        var process = Process.GetCurrentProcess();
        File.WriteAllText(Path.Combine(output, "benchmark.json"), JsonSerializer.Serialize(new
        { cacheLoadMs = Math.Round(load.Elapsed.TotalMilliseconds), workingSetMb = process.WorkingSet64 / 1048576, privateMb = process.PrivateMemorySize64 / 1048576, metrics }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
