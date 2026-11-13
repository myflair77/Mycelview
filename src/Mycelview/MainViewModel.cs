using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Mycelview;

public sealed partial class MainViewModel : Observable, IDisposable
{
    public ObservableCollection<CardViewModel> Cards { get; } = [];
    public ObservableCollection<CardViewModel> Folders { get; } = [];
    public ObservableCollection<CardViewModel> Favorites { get; } = [];
    public ExplorerViewModel Explorer { get; } = new();
    public SearchViewModel Search { get; }
    public LocalSearchIndex Index { get; }
    public FileTasks FileTasks { get; }
    public PaneLayout Layout { get; private set; } = new();
    public EdgeActivationSettings EdgeActivation { get; set; } = new();
    public bool ExplorerCollapsed { get; set; }
    public double ExplorerWidth { get; set; } = 280;
    public double ExplorerTreeHeight { get; set; } = 160;
    public double FavoritesWidth { get; set; } = 265;
    public double FavoritesRatio { get; set; } = .5;
    public ObservableCollection<RecentFile> RecentFiles { get; } = [];
    internal void RecordOpenedFile(string path)
    {
        path = Path.GetFullPath(path);
        var previous = RecentFiles.FirstOrDefault(item => StringComparer.OrdinalIgnoreCase.Equals(item.CurrentPath, path));
        if (previous != null) RecentFiles.Remove(previous);
        RecentFiles.Insert(0, new(path, DateTimeOffset.UtcNow));
        while (RecentFiles.Count > 100) RecentFiles.RemoveAt(RecentFiles.Count - 1);
        Changed();
    }
    private int folderColumns = 2;
    public int FolderColumns { get => folderColumns; set { var count=Math.Clamp(value,1,5); if(count==folderColumns)return; folderColumns=count; Notify(); Notify(nameof(FolderColumnsLabel)); Changed(); } }
    public string FolderColumnsLabel => $"{FolderColumns}열 배치 ▾";
    private int favoriteColumns = 1, recentColumns = 1;
    public int FavoriteColumns { get => favoriteColumns; set { var count = Math.Clamp(value, 1, 5); if (count == favoriteColumns) return; favoriteColumns = count; Notify(); Notify(nameof(FavoriteColumnsLabel)); Changed(); } }
    public int RecentColumns { get => recentColumns; set { var count = Math.Clamp(value, 1, 5); if (count == recentColumns) return; recentColumns = count; Notify(); Notify(nameof(RecentColumnsLabel)); Changed(); } }
    public string FavoriteColumnsLabel => $"{FavoriteColumns}열 배치 ▾";
    public string RecentColumnsLabel => $"{RecentColumns}열 배치 ▾";
    private double folderHeight = 340;
    public double FolderHeight
    {
        get => folderHeight;
        set
        {
            var height = double.IsFinite(value) ? Math.Clamp(Math.Round(value), 260, 800) : 340;
            if (folderHeight == height) return;
            folderHeight = height; Notify(); Notify(nameof(CanDecreaseHeight)); Notify(nameof(CanIncreaseHeight));
            foreach (var card in Folders) card.Height = height;
            Changed();
        }
    }
    public bool CanDecreaseHeight => FolderHeight > 260;
    public bool CanIncreaseHeight => FolderHeight < 800;
    public RelayCommand DecreaseFolderHeight { get; }
    public RelayCommand IncreaseFolderHeight { get; }
    private bool showModified = true, showSize = true, showType, showCreated, showExtension;
    public bool ShowModified { get => showModified; set { Set(ref showModified, value); Changed(); } }
    public bool ShowSize { get => showSize; set { Set(ref showSize, value); Changed(); } }
    public bool ShowType { get => showType; set { Set(ref showType, value); Changed(); } }
    public bool ShowCreated { get => showCreated; set { Set(ref showCreated, value); Changed(); } }
    public bool ShowExtension { get => showExtension; set { Set(ref showExtension, value); Changed(); } }
    private CardViewModel[]? reorderSnapshot;
    internal void BeginReorderPreview() => reorderSnapshot = Folders.Concat(Favorites).ToArray();
    internal void EndReorderPreview(bool commit)
    {
        var snapshot = reorderSnapshot; reorderSnapshot = null;
        if (snapshot == null) return;
        if (!commit)
            foreach (var group in new[] { Folders, Favorites })
            {
                var original = snapshot.Where(x => x.IsFolder == (group == Folders)).ToArray();
                for (int i = 0; i < original.Length; i++) group.Move(group.IndexOf(original[i]), i);
            }
        else Changed();
    }
    public bool Reorder(Guid sourceId, Guid targetId, bool after)
    {
        var source = Cards.FirstOrDefault(c => c.State.Id == sourceId);
        var target = Cards.FirstOrDefault(c => c.State.Id == targetId);
        if (source == null || target == null || source == target || source.IsFolder != target.IsFolder) return false;
        var items = source.IsFolder ? Folders : Favorites;
        var from = items.IndexOf(source); var to = items.IndexOf(target) + (after ? 1 : 0);
        if (from < to) to--;
        if (from == to) return false;
        items.Move(from, to); if (reorderSnapshot == null) Changed(); return true;
    }
    public IReadOnlyList<SkinOption> Skins => SkinManager.Options;
    private string selectedSkinId = SkinManager.DefaultId;
    public string SelectedSkinId
    {
        get => selectedSkinId;
        set
        {
            var id = SkinManager.Normalize(value);
            if (id == selectedSkinId) return;
            selectedSkinId = SkinManager.Apply(id); Notify(); Changed();
        }
    }
    private readonly StateStore store;
    private readonly DispatcherTimer saveTimer;
    private readonly DispatcherTimer reconcileTimer;
    private bool canSave = true;
    private string status = "폴더를 추가하거나 파일·폴더를 빈 작업공간에 끌어 놓으세요.";
    public string Status { get => status; set => Set(ref status, value); }
    public bool IsEmpty => Cards.Count == 0;
    public RelayCommand AddFolder { get; }
    public RelayCommand AddFiles { get; }
    private bool collapseNext = true;
    public string ToggleFoldersLabel => collapseNext ? "모두 접기" : "모두 펼치기";
    public RelayCommand ToggleFolders { get; }
    public RelayCommand CollapseAll { get; }
    public RelayCommand ExpandAll { get; }

    public MainViewModel(StateStore store, string? indexDirectory = null)
    {
        this.store = store;
        FileTasks = new(this);
        Index = new LocalSearchIndex(indexDirectory ?? store.DirectoryPath);
        Search = new SearchViewModel(Index);
        saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        saveTimer.Tick += (_, _) => Save();
        DecreaseFolderHeight = new(() => FolderHeight -= 20);
        IncreaseFolderHeight = new(() => FolderHeight += 20);
        FileService.FileOpened += RecordOpenedFile;
        reconcileTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
        reconcileTimer.Tick += (_, _) => { foreach (var c in Cards) _ = c.RefreshAsync(); };
        AddFolder = new(() => { var d = new OpenFolderDialog { Title = "카드로 등록할 폴더", Multiselect = true }; if (d.ShowDialog() == true) foreach (var p in d.FolderNames) AddPath(p); });
        AddFiles = new(() => { var d = new OpenFileDialog { Title = "즐겨찾기 파일 등록", Multiselect = true }; if (d.ShowDialog() == true) foreach (var p in d.FileNames) AddPath(p); });
        ToggleFolders = new(() => {
            foreach (var card in Folders) card.SetCollapsed(collapseNext);
            collapseNext = !collapseNext; Notify(nameof(ToggleFoldersLabel)); Changed();
        });
        CollapseAll = new(() => { foreach (var c in Folders) c.SetCollapsed(true); });
        ExpandAll = new(() => { foreach (var c in Folders) c.SetCollapsed(false); });
    }
    public async Task InitializeAsync()
    {
        try
        {
            var saved = store.Load();
            Enlargement = saved.Enlargement ?? new();
            TextScalePercent = saved.TextScalePercent;
            foreach (var recent in (saved.RecentFiles ?? []).Where(r => r != null && !string.IsNullOrWhiteSpace(r.CurrentPath)).DistinctBy(r => r.CurrentPath, StringComparer.OrdinalIgnoreCase).Take(100)) RecentFiles.Add(recent);
            selectedSkinId = SkinManager.Apply(saved.SkinId); Notify(nameof(SelectedSkinId));
            LoadWorkspaces(saved);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
        { canSave = false; Status = "설정 읽기 실패. 원본 보호를 위해 저장을 중지했습니다: " + e.Message; MessageBox.Show(Status + "\n설정 위치: " + store.DirectoryPath, "Mycelview", MessageBoxButton.OK, MessageBoxImage.Warning); }
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
        await Task.WhenAll(Cards.ToArray().Select(c => c.RefreshAsync())); WorkspaceChanged?.Invoke(); reconcileTimer.Start();
    }
    private void AddState(CardState state)
    { if (!state.IsFile) state.Height = FolderHeight; var vm = new CardViewModel(state, Changed); Cards.Add(vm); (state.IsFile ? Favorites : Folders).Add(vm); Notify(nameof(IsEmpty)); }
    public void AddPath(string path)
    {
        bool folder = Directory.Exists(path);
        if (!folder && !File.Exists(path)) { Status = "찾을 수 없는 경로: " + path; return; }
        var state = new CardState { InitialPath = path, CurrentPath = path, IsFile = !folder, Title = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)), X = 20 + (Cards.Count % 2) * 480, Y = 20 + (Cards.Count / 2) * 360, NoteVisible = false };
        if (state.Title.Length == 0) state.Title = path;
        AddState(state); _ = Cards[^1].RefreshAsync(); Changed(); Status = "등록했습니다. 원본 파일은 변경하지 않습니다.";
    }
    public void Remove(CardViewModel card) { card.Dispose(); Cards.Remove(card); Folders.Remove(card); Favorites.Remove(card); Notify(nameof(IsEmpty)); Changed(); }
    public void Duplicate(CardViewModel card)
    {
        var state = System.Text.Json.JsonSerializer.Deserialize<CardState>(System.Text.Json.JsonSerializer.Serialize(card.State))!;
        state.Id = Guid.NewGuid(); state.X += 28; state.Y += 28; AddState(state); _ = Cards[^1].RefreshAsync(); Changed();
    }
    public void Changed() { if (SettingsBusy || WorkspaceBusy) return; saveTimer.Stop(); saveTimer.Start(); }
    public bool Save()
    {
        saveTimer.Stop(); if (!canSave || SettingsBusy || WorkspaceBusy) return false;
        try { store.Save(Snapshot()); Status = "로컬 저장 완료 · " + DateTime.Now.ToString("HH:mm:ss"); return true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Status = "저장 실패: " + e.Message; return false; }
    }
    public void Dispose() { FileService.FileOpened -= RecordOpenedFile; saveTimer.Stop(); reconcileTimer.Stop(); Explorer.Dispose(); Search.Dispose(); Index.Dispose(); foreach (var c in Cards) c.Dispose(); }
}




