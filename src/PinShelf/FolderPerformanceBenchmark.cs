using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
namespace PinShelf;
internal static class FolderPerformanceBenchmark
{
    internal static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var large = Path.Combine(directory, "large"); var small = Path.Combine(directory, "small");
        Directory.CreateDirectory(large); Directory.CreateDirectory(small);
        await Task.Run(() => { for (int i = 0; i < 12000; i++) { var path = Path.Combine(large, $"자료-{i}.txt"); if (!File.Exists(path)) File.WriteAllText(path, "fixture"); } for (int i = 0; i < 12; i++) { var path = Path.Combine(small, $"small-{i}.txt"); if (!File.Exists(path)) File.WriteAllText(path, "fixture"); } });
        var store = new StateStore(Path.Combine(directory, "state"));
        store.Save(new WorkspaceState { Cards = [new() { InitialPath = large, CurrentPath = large, Title = "Large" }] });
        var vm = new MainViewModel(store); var window = new MainWindow(vm) { TestMode = true, Title = "PinShelf — Folder performance" };
        var clock = Stopwatch.StartNew(); double last = 0, maxGap = 0, firstItems = -1;
        var heartbeat = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
        heartbeat.Tick += (_, _) => { var now = clock.Elapsed.TotalMilliseconds; maxGap = Math.Max(maxGap, now - last); last = now; }; heartbeat.Start();
        vm.Cards.CollectionChanged += (_, e) => { if (e.NewItems != null) foreach (CardViewModel card in e.NewItems) card.Entries.CollectionChanged += (_, _) => { if (card.CurrentPath == large && card.Entries.Count > 0 && firstItems < 0) firstItems = clock.Elapsed.TotalMilliseconds; }; };
        window.Show();
        try
        {
            await vm.InitializeAsync();
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
            var largeReady = clock.Elapsed.TotalMilliseconds;
            var batchStart = clock.Elapsed.TotalMilliseconds;
            for (int i = 0; i < 60; i++) vm.AddPath(small);
            await Task.WhenAll(vm.Cards.Select(c => c.RefreshAsync()));
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
            var manyReady = clock.Elapsed.TotalMilliseconds - batchStart;
            int CountViews(DependencyObject item) { int count = item is CardView ? 1 : 0; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++) count += CountViews(VisualTreeHelper.GetChild(item, i)); return count; }
            var activeViews = CountViews(window);
            var report = new { Files = 12000, Cards = vm.Cards.Count, FirstItemsMs = firstItems, LargeReadyMs = largeReady, ManyCardsReadyMs = manyReady, MaxUiGapMs = maxGap, RealizedCardViews = activeViews, LoadedEntries = vm.Cards.Sum(c => c.Entries.Count), WorkingSetMB = Process.GetCurrentProcess().WorkingSet64 / 1048576d };
            File.WriteAllText(Path.Combine(directory, "performance.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { heartbeat.Stop(); window.Close(); }
    }
}
