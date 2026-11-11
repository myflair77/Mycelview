using System.Collections.ObjectModel;
using System.IO;

namespace PinShelf;

public sealed class FolderNode(string path, string title) : Observable
{
    public string Path { get; } = path;
    public string Title { get; } = title;
    public ObservableCollection<FolderNode> Children { get; } = path.Length > 0 ? [new("", "불러오는 중…")] : [];
    private bool loaded;
    internal async Task RefreshLoadedAsync()
    {
        if (!loaded) return;
        try
        {
            var paths = await Task.Run(() => Directory.EnumerateDirectories(Path).OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase).ToArray());
            var wanted = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var child in Children.Where(x => !wanted.Contains(x.Path)).ToArray()) Children.Remove(child);
            for (int i = 0; i < paths.Length; i++)
            {
                var child = Children.FirstOrDefault(x => StringComparer.OrdinalIgnoreCase.Equals(x.Path, paths[i]));
                if (child == null) Children.Insert(i, new(paths[i], System.IO.Path.GetFileName(paths[i])));
                else if (Children.IndexOf(child) != i) Children.Move(Children.IndexOf(child), i);
            }
            foreach (var child in Children.ToArray()) await child.RefreshLoadedAsync();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
    public async Task ExpandAsync()
    {
        if (loaded || Path.Length == 0) return;
        loaded = true;
        try
        {
            var folders = await Task.Run(() => Directory.EnumerateDirectories(Path).OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase).ToArray());
            Children.Clear(); foreach (var p in folders) Children.Add(new(p, System.IO.Path.GetFileName(p)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { Children.Clear(); Children.Add(new("", "접근할 수 없음")); loaded = false; }
    }
}

public sealed class ExplorerViewModel : Observable, IDisposable
{
    public ObservableCollection<FolderNode> Roots { get; } = [];
    public ResultCollection<FileEntry> Entries { get; } = [];
    private string path = "", address = "", status = "폴더를 선택하세요.";
    private CancellationTokenSource? reading;
    private bool disposed;
    private readonly List<string> history = [];
    private int historyIndex = -1;
    public bool CanBack => historyIndex > 0;
    public bool CanForward => historyIndex + 1 < history.Count;
    public Task BackAsync() => CanBack ? NavigateCoreAsync(history[historyIndex - 1], historyIndex - 1) : Task.CompletedTask;
    public Task ForwardAsync() => CanForward ? NavigateCoreAsync(history[historyIndex + 1], historyIndex + 1) : Task.CompletedTask;
    public string CurrentPath { get => path; private set => Set(ref path, value); }
    public string Address { get => address; set => Set(ref address, value); }
    public string Status { get => status; private set => Set(ref status, value); }
    public ExplorerViewModel()
    {
        foreach (var folder in new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyDocuments })
        {
            var p = Environment.GetFolderPath(folder);
            if (Directory.Exists(p)) Roots.Add(new(p, folder == Environment.SpecialFolder.DesktopDirectory ? "바탕 화면" : "문서"));
        }
        foreach (var drive in DriveInfo.GetDrives()) Roots.Add(new(drive.Name, drive.Name));
    }
    public Task NavigateAsync(string target) => NavigateCoreAsync(target);
    private async Task NavigateCoreAsync(string target, int? destination = null)
    {
        if (disposed || string.IsNullOrWhiteSpace(target)) return;
        reading?.Cancel(); reading?.Dispose(); reading = new(); var token = reading.Token;
        Status = "폴더를 읽는 중…";
        try
        {
            target = System.IO.Path.GetFullPath(target);
            var nextPath = target;
            var items = await FolderListing.ReadAsync(target, batch => System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            { if (disposed || token.IsCancellationRequested) return; CurrentPath = nextPath; Address = nextPath; Entries.ReplaceAll(batch); Status = $"{batch.Count:N0}개 불러오는 중…"; }, System.Windows.Threading.DispatcherPriority.Background, token).Task, token);
            if (token.IsCancellationRequested || disposed) return;
            CurrentPath = target; Address = target; Entries.ReplaceAll(items);
            if (destination.HasValue) historyIndex = destination.Value;
            else if (historyIndex < 0 || !StringComparer.OrdinalIgnoreCase.Equals(history[historyIndex], target))
            {
                history.RemoveRange(historyIndex + 1, history.Count - historyIndex - 1);
                history.Add(target); historyIndex = history.Count - 1;
            }
            Notify(nameof(CanBack)); Notify(nameof(CanForward));
            Status = $"{items.Count:N0}개 · Ctrl / Shift로 여러 항목 선택";
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { if (!token.IsCancellationRequested) { Address = CurrentPath; Status = "폴더 열기 실패: " + e.Message; } }
    }
    public async Task UpAsync()
    { if (Directory.GetParent(CurrentPath) is { } parent) await NavigateAsync(parent.FullName); }
    internal void ApplyRelocation(string oldPath, string newPath)
    {
        for (int i = 0; i < history.Count; i++) history[i] = FileTasks.Remap(history[i], oldPath, newPath);
        if (CurrentPath.Length == 0) return;
        var relocated = FileTasks.Remap(CurrentPath, oldPath, newPath);
        if (relocated != CurrentPath) { CurrentPath = relocated; Address = relocated; }
    }
    internal Task RefreshLoadedTreeAsync() => Task.WhenAll(Roots.Select(x => x.RefreshLoadedAsync()));
    public void Dispose() { disposed = true; reading?.Cancel(); reading?.Dispose(); }
}
