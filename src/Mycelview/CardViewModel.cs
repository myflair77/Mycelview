using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Mycelview;

public sealed class CardViewModel : Observable, IDisposable
{
    public CardState State { get; }
    private readonly Action changed;
    private readonly Stack<string> back = new(), forward = new();
    private CancellationTokenSource? reading;
    private FileSystemWatcher? watcher;
    private readonly DispatcherTimer refresh;
    private bool disposed;
    private string status = "";
    private double dragOpacity = 1;
    public double DragOpacity { get => dragOpacity; internal set => Set(ref dragOpacity, value); }
    public ResultCollection<FileEntry> Entries { get; } = [];
    public event Action? ListingUpdating;
    internal HashSet<string> SelectedPaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    private Task? currentRead;
    private string? readPath;
    private bool viewportManaged, viewportActive = true, refreshAgain;
    internal bool ViewportActive => !viewportManaged || viewportActive;
    internal void SetViewportActive(bool value)
    {
        viewportManaged = true;
        if (viewportActive == value) { if (value && Entries.Count == 0) _ = RefreshAsync(); return; }
        viewportActive = value;
        if (value) _ = RefreshAsync();
        else { reading?.Cancel(); watcher?.Dispose(); watcher = null; refresh.Stop(); ListingUpdating?.Invoke(); Entries.Clear(); Status = "화면에 표시되면 불러옵니다."; }
    }
    public event Action? ListingRefreshed;
    public string Title => State.Title;
    public System.Windows.Media.ImageSource IconImage => ShellIcons.ForPath(State.InitialPath, !State.IsFile);
    public string CurrentPath => State.CurrentPath;
    public string Kind => State.IsFile ? "★" : "▣";
    public bool IsFolder => !State.IsFile;
    public bool Expanded => !State.Collapsed;
    public double DisplayHeight => Expanded ? State.Height : Math.Max(52, 12 * TextScale.Factor + 40);
    internal void RefreshDisplayHeight() => Notify(nameof(DisplayHeight));
    public double X { get => State.X; set { State.X = Math.Max(0, value); Notify(); changed(); } }
    public double Y { get => State.Y; set { State.Y = Math.Max(0, value); Notify(); changed(); } }
    public double Width { get => State.Width; set { State.Width = Math.Clamp(value, 340, 1800); Notify(); changed(); } }
    public double Height { get => State.Height; set { State.Height = Math.Clamp(value, 220, 1400); Notify(); Notify(nameof(DisplayHeight)); changed(); } }
    public string Note { get => State.Note; set { State.Note = value; Notify(); changed(); } }
    public bool NoteVisible { get => State.NoteVisible; set { State.NoteVisible = value; Notify(); changed(); } }
    public string Status { get => status; private set => Set(ref status, value); }
    public RelayCommand Back { get; }
    public RelayCommand Forward { get; }
    public RelayCommand Up { get; }
    public RelayCommand Home { get; }
    public RelayCommand Reload { get; }
    public RelayCommand Collapse { get; }
    public RelayCommand ToggleNote { get; }
    public RelayCommand OpenExternal { get; }
    public RelayCommand CopyPath { get; }
    public CardViewModel(CardState state, Action onChanged)
    {
        State = state; changed = onChanged;
        Back = new(() => { if (back.TryPop(out var p)) { forward.Push(CurrentPath); Navigate(p, false); } });
        Forward = new(() => { if (forward.TryPop(out var p)) { back.Push(CurrentPath); Navigate(p, false); } });
        Up = new(() => { try { var p = Directory.GetParent(CurrentPath); if (p != null) Navigate(p.FullName); } catch (Exception e) { Status = e.Message; } });
        Home = new(() => Navigate(State.InitialPath));
        Reload = new(() => _ = RefreshAsync());
        Collapse = new(() => SetCollapsed(!State.Collapsed));
        ToggleNote = new(() => NoteVisible = !NoteVisible);
        OpenExternal = new(() => OpenPath(CurrentPath));
        CopyPath = new(() => { try { Clipboard.SetText(CurrentPath); } catch (Exception e) { Status = e.Message; } });
        refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        refresh.Tick += (_, _) => { refresh.Stop(); _ = RefreshAsync(); };
    }
    public void SetCollapsed(bool value) { State.Collapsed = value; Notify(nameof(Expanded)); Notify(nameof(DisplayHeight)); changed(); }
    public void Navigate(string path, bool remember = true)
    {
        if (State.IsFile) return;
        if (remember && !StringComparer.OrdinalIgnoreCase.Equals(CurrentPath, path)) { back.Push(CurrentPath); forward.Clear(); }
        reading?.Cancel(); SelectedPaths.Clear(); ListingUpdating?.Invoke(); Entries.Clear(); State.CurrentPath = path; State.ScrollOffset = 0; Notify(nameof(CurrentPath)); changed(); _ = RefreshAsync();
    }
    public void Reconnect(string path)
    {
        reading?.Cancel(); SelectedPaths.Clear(); ListingUpdating?.Invoke(); Entries.Clear(); State.InitialPath = State.CurrentPath = path;
        State.Title = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        if (State.Title.Length == 0) State.Title = path;
        back.Clear(); forward.Clear(); Notify(nameof(Title)); Notify(nameof(CurrentPath)); Notify(nameof(IconImage)); changed(); _ = RefreshAsync();
    }
    internal void ApplyRelocation(string oldPath, string newPath)
    {
        var initial = FileTasks.Remap(State.InitialPath, oldPath, newPath);
        var current = FileTasks.Remap(State.CurrentPath, oldPath, newPath);
        if (initial == State.InitialPath && current == State.CurrentPath) return;
        if (State.Title == Path.GetFileName(Path.TrimEndingDirectorySeparator(State.InitialPath))) State.Title = Path.GetFileName(initial);
        State.InitialPath = initial; State.CurrentPath = current;
        back.Clear(); forward.Clear(); Notify(nameof(Title)); Notify(nameof(CurrentPath)); Notify(nameof(IconImage)); changed();
    }
    public void OpenEntry(FileEntry entry) { if (entry.IsDirectory) Navigate(entry.FullPath); else OpenPath(entry.FullPath); }
    public void OpenPath(string path) { try { FileService.Open(path); } catch (Exception e) { Status = "열기 실패: " + e.Message; } }
    public Task RefreshAsync()
    {
        if (disposed || !ViewportActive) return Task.CompletedTask;
        if (currentRead is { IsCompleted: false } && readPath == CurrentPath && reading?.IsCancellationRequested == false) return currentRead;
        readPath = CurrentPath; currentRead = ReadCoreAsync(CurrentPath); return currentRead;
    }
    private async Task ReadCoreAsync(string path)
    {
        reading?.Cancel(); var request = new CancellationTokenSource(); reading = request; var token = request.Token;
        watcher?.Dispose(); watcher = null; refreshAgain = false;
        if (State.IsFile) { Status = File.Exists(path) ? "더블클릭 또는 ‘열기’로 실행" : "파일을 찾을 수 없습니다. ‘재연결’을 사용하세요."; reading = null; request.Dispose(); return; }
        Status = "폴더를 읽는 중…";
        var preview = Entries.Count == 0;
        try
        {
            string? watchWarning = null;
            try
            {
                watcher = new FileSystemWatcher(path) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
                watcher.Changed += OnFileChange; watcher.Created += OnFileChange; watcher.Deleted += OnFileChange; watcher.Renamed += OnFileChange;
                watcher.Error += (_, _) => ScheduleRefresh(); watcher.EnableRaisingEvents = true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { watchWarning = " · 자동 감시 불가: 새로고침을 사용하세요."; }
            var items = await FolderListing.ReadAsync(path, preview ? batch => Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested || disposed || path != CurrentPath || !ViewportActive) return;
                ListingUpdating?.Invoke(); Entries.ReplaceAll(batch); ListingRefreshed?.Invoke(); Status = $"{batch.Count:N0}개 불러오는 중…";
            }, DispatcherPriority.Background, token).Task : null, token);
            if (token.IsCancellationRequested || disposed || path != CurrentPath || !ViewportActive) return;
            ListingUpdating?.Invoke(); Entries.ReplaceAll(items); SelectedPaths.IntersectWith(items.Select(x => x.FullPath)); ListingRefreshed?.Invoke();
            Status = $"{items.Count:N0}개 항목" + watchWarning;
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        { if (!token.IsCancellationRequested && !disposed) { ListingUpdating?.Invoke(); Entries.Clear(); ListingRefreshed?.Invoke(); Status = "폴더를 읽을 수 없습니다. 재연결 또는 새로고침: " + e.Message; } }
        finally
        {
            if (ReferenceEquals(reading, request)) { reading = null; if (refreshAgain && !disposed && ViewportActive) { refreshAgain = false; refresh.Stop(); refresh.Start(); } }
            request.Dispose();
        }
    }
    private void OnFileChange(object sender, FileSystemEventArgs e) => ScheduleRefresh();
    private void ScheduleRefresh() => Application.Current.Dispatcher.BeginInvoke(() => { if (!disposed && ViewportActive) { refreshAgain = currentRead is { IsCompleted: false }; refresh.Stop(); refresh.Start(); } });
    public void Dispose() { if (disposed) return; disposed = true; reading?.Cancel(); watcher?.Dispose(); refresh.Stop(); }
}
