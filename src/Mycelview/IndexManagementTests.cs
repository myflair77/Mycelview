using System.IO;
using System.Windows;
using System.Windows.Threading;
namespace Mycelview;
internal static class IndexManagementTests
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Until(Func<Task<bool>> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < until) { if (await condition()) return; await Task.Delay(100); }
        throw new InvalidOperationException("Index management check timed out.");
    }
    internal static async Task RunAsync(MainWindow owner, string directory)
    {
        var cache = Path.Combine(directory, "management-cache");
        var root = Path.Combine(directory, "management-fixtures"); Directory.CreateDirectory(root);
        var skip = Path.Combine(root, "excluded"); Directory.CreateDirectory(skip);
        File.WriteAllText(Path.Combine(root, "keep.txt"), "keep"); File.WriteAllText(Path.Combine(skip, "hidden.txt"), "hide");
        var other = Path.Combine(directory, "management-other"); Directory.CreateDirectory(other); File.WriteAllText(Path.Combine(other, "other.txt"), "other");
        var missing = Path.Combine(directory, "management-missing");
        var index = new LocalSearchIndex(cache);
        try
        {
            index.Start([root, missing]); await index.InitialPass;
            Check(index.EntryCount >= 3 && index.LastUpdated != null && index.StorageBytes > 0, "Management exposes count/date/size.");
            Check(index.ErrorDetails.Any(x => x.Path == missing), "Unavailable roots appear as retryable errors.");
            Directory.CreateDirectory(missing); File.WriteAllText(Path.Combine(missing, "recovered.txt"), "recover");
            index.RetryErrors([missing]);
            await Until(async () => (await index.QueryAsync("recovered.txt", 10, default)).Items.Count == 1 && !index.ErrorDetails.Any(x => x.Path == missing));
            // Keep enough work pending to observe the old generation while replacement is running.
            for (int i = 0; i < 1500; i++) File.WriteAllText(Path.Combine(other, $"batch-{i}.txt"), "fixture");
            var rebuilding = index.ApplySettingsAsync(new() { AllLocalDrives = false, IncludedFolders = [root, other], ExcludedFolders = [skip] });
            Check(index.IsRebuilding, "Rebuild runs asynchronously.");
            Check((await index.QueryAsync("hidden.txt", 10, default)).Items.Count == 1, "Old index remains searchable during rebuild.");
            await rebuilding;
            Check((await index.QueryAsync("hidden.txt", 10, default)).Items.Count == 0 && (await index.QueryAsync("other.txt", 10, default)).Items.Count == 1, "Atomic switch applies include/exclude settings.");
            Check((await index.QueryAsync("recovered.txt", 10, default)).Items.Count == 0, "Removed target roots do not leak into new index.");
            File.WriteAllText(Path.Combine(skip, "later-hidden.txt"), "hidden");
            var previousUpdate = index.LastUpdated;
            index.RecheckChanges();
            await Until(() => Task.FromResult(index.LastUpdated > previousUpdate && !index.Busy));
            Check((await index.QueryAsync("later-hidden", 10, default)).Items.Count == 0, "Exclusions apply to both scans and watchers.");
            var pointer = File.ReadAllText(Path.Combine(cache, "index-current.json"));
            var cancelled = index.RebuildAsync(); index.CancelRebuild();
            bool wasCancelled = false; try { await cancelled; } catch (OperationCanceledException) { wasCancelled = true; }
            Check(wasCancelled && pointer == File.ReadAllText(Path.Combine(cache, "index-current.json")) && (await index.QueryAsync("keep.txt", 10, default)).Items.Count == 1, "Cancelled rebuild preserves active pointer and search.");
            bool failed = false;
            using (var lockedPointer = new FileStream(Path.Combine(cache, "index-current.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            { try { await index.RebuildAsync(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; } }
            Check(failed && pointer == File.ReadAllText(Path.Combine(cache, "index-current.json")) && (await index.QueryAsync("keep.txt", 10, default)).Items.Count == 1, "Commit failure preserves original searchable generation.");
            await index.RebuildAsync();
            Check(Directory.GetDirectories(Path.Combine(cache, "index-generations")).Length == 1, "Completed replacement retires old generated caches after queries drain.");
            var dialog = new IndexManagementWindow(owner, index); dialog.Show();
            await dialog.Dispatcher.InvokeAsync(() => dialog.UpdateLayout(), DispatcherPriority.ContextIdle);
            Check(dialog.IsVisible && dialog.ActualWidth >= 600, "Modeless management screen renders.");
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(dialog); var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var image = File.Create(Path.Combine(directory, "index-management.png"))) encoder.Save(image);
            dialog.Close();
        }
        finally { await index.StopAsync(); }
        await VerifyAsync(directory);
        File.WriteAllText(Path.Combine(directory, "index-management-results.txt"), "PASS: live statistics, missing path retry/recovery, include/exclude application, search during shadow rebuild, atomic switch, watcher exclusions, cancellation and commit failure preserving active index, explicit recheck, modeless window, persisted settings and generation reload.");
    }
    internal static async Task VerifyAsync(string directory)
    {
        var index = new LocalSearchIndex(Path.Combine(directory, "management-cache"));
        try
        {
            Check(!index.Settings.AllLocalDrives && index.Settings.IncludedFolders.Length == 2 && index.Settings.ExcludedFolders.Length == 1, "Search settings survive restart.");
            index.Start(); await index.InitialPass;
            Check((await index.QueryAsync("keep.txt", 10, default)).Items.Count == 1 && (await index.QueryAsync("hidden.txt", 10, default)).Items.Count == 0 && (await index.QueryAsync("other.txt", 10, default)).Items.Count == 1, "Restart reopens the committed generation with correct scope.");
        }
        finally { await index.StopAsync(); }
    }
}
