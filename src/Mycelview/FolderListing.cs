using System.Diagnostics;
using System.IO;
namespace Mycelview;
// Shared bound for card and explorer metadata reads. Await UI previews to avoid flooding Dispatcher.
internal static class FolderListing
{
    internal const int ConcurrencyLimit = 2;
    private static readonly SemaphoreSlim readers = new(ConcurrencyLimit);
    private static int active, peak;
    internal static int PeakReaders => Volatile.Read(ref peak);
    internal static async Task<List<FileEntry>> ReadAsync(string path, Func<IReadOnlyList<FileEntry>, Task>? preview, CancellationToken token)
    {
        await readers.WaitAsync(token);
        var count = Interlocked.Increment(ref active);
        int previous; do { previous = peak; if (count <= previous) break; } while (Interlocked.CompareExchange(ref peak, count, previous) != previous);
        try
        {
            return await Task.Run(async () =>
            {
                var items = new List<FileEntry>(); int published = 0; var clock = Stopwatch.StartNew();
                var comparer = new FileEntryComparer("Name", false);
                foreach (var item in new DirectoryInfo(path).EnumerateFileSystemInfos())
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var folder = (item.Attributes & FileAttributes.Directory) != 0;
                        items.Add(new(item.FullName, item.Name, folder, item.LastWriteTime, folder ? 0 : ((FileInfo)item).Length) { Created = item.CreationTime });
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    // Exponentially growing previews bound the total re-sort work on large folders.
                    if (preview != null && (published == 0 && items.Count >= 128 || items.Count >= Math.Max(512, published * 2) && clock.ElapsedMilliseconds >= 250))
                    {
                        var batch = items.ToArray(); Array.Sort(batch, (a,b) => comparer.Compare(a,b));
                        token.ThrowIfCancellationRequested(); await preview(batch); published = items.Count; clock.Restart();
                    }
                }
                if (DesktopItems.IsDesktop(path))
                {
                    var paths = items.Select(item => item.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    foreach (var item in await DesktopItems.ReadAsync(token)) if (paths.Add(item.FullPath)) items.Add(item);
                }
                token.ThrowIfCancellationRequested(); items.Sort((a,b) => comparer.Compare(a,b)); return items;
            }, token);
        }
        finally { Interlocked.Decrement(ref active); readers.Release(); }
    }
}
