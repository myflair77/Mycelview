using System.Diagnostics;
using System.IO;

namespace Mycelview;

public sealed record SearchProgress(List<FileEntry> Matches, long Scanned, long Skipped, string CurrentPath, List<string> Issues);

public static class SearchService
{
    public static string[] LocalRoots() => DriveInfo.GetDrives().Where(d => d.DriveType is DriveType.Fixed or DriveType.Removable or DriveType.CDRom or DriveType.Ram).Select(d => d.Name).ToArray();

    // Runs off the UI thread; awaited batches provide backpressure instead of flooding Dispatcher.
    public static async Task ScanAsync(IEnumerable<string> roots, Func<string, bool> match, Func<SearchProgress, Task> publish, CancellationToken token)
    {
        long scanned = 0, skipped = 0;
        var batch = new List<FileEntry>(); var issues = new List<string>();
        var pending = new Stack<string>(roots.Reverse());
        var clock = Stopwatch.StartNew(); string current = "";
        void Skip(string path, string reason) { skipped++; if (issues.Count < 200) issues.Add(path + " — " + reason); }
        async Task Flush()
        {
            await publish(new(batch, scanned, skipped, current, issues));
            batch = []; issues = []; clock.Restart();
        }
        try
        {
            while (pending.TryPop(out current!))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var directory = new DirectoryInfo(current);
                    if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) { Skip(current, "연결 폴더: 순환 탐색 방지를 위해 제외"); continue; }
                    foreach (var item in directory.EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = false, AttributesToSkip = 0, RecurseSubdirectories = false }))
                    {
                        token.ThrowIfCancellationRequested(); scanned++;
                        try
                        {
                            bool folder = (item.Attributes & FileAttributes.Directory) != 0;
                            if (folder) pending.Push(item.FullName);
                            if (match(item.FullName)) batch.Add(new(item.FullName, item.Name, folder, item.LastWriteTime, folder ? 0 : ((FileInfo)item).Length) { Created = item.CreationTime });
                        }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Skip(item.FullName, e.Message); }
                        if (batch.Count >= 128 || clock.ElapsedMilliseconds >= 200) await Flush();
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Skip(current, e.Message); }
                if (clock.ElapsedMilliseconds >= 200) await Flush();
            }
        }
        finally { await Flush(); }
    }
}
