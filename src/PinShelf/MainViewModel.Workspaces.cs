using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
namespace PinShelf;
public sealed partial class MainViewModel
{
    public ObservableCollection<WorkspaceTab> Workspaces { get; } = [];
    public WorkspaceTab? ActiveWorkspace { get; private set; }
    private bool workspaceBusy;
    public bool WorkspaceBusy { get => workspaceBusy; private set { Set(ref workspaceBusy, value); Notify(nameof(WorkspaceReady)); } }
    public bool WorkspaceReady => !WorkspaceBusy;
    internal event Action? WorkspaceChanging;
    internal event Action? WorkspaceChanged;
    private void EnsureWorkspaceReady()
    {
        if (!canSave || SettingsBusy || WorkspaceBusy || FileTasks.Busy || reorderSnapshot != null)
            throw new InvalidOperationException("현재 작업을 마친 뒤 작업공간을 변경하세요.");
    }
    private void CaptureWorkspace()
    {
        if (ActiveWorkspace == null) return;
        ActiveWorkspace.Cards = (reorderSnapshot ?? Folders.Concat(Favorites).ToArray()).Select(c => c.State).ToList();
        ActiveWorkspace.FolderColumns = FolderColumns;
        ActiveWorkspace.FavoriteColumns = FavoriteColumns; ActiveWorkspace.RecentColumns = RecentColumns;
        ActiveWorkspace.CollapseNext = collapseNext;
        ActiveWorkspace.Layout = Layout; ActiveWorkspace.FolderHeight = FolderHeight;
        ActiveWorkspace.ShowModified = ShowModified; ActiveWorkspace.ShowSize = ShowSize; ActiveWorkspace.ShowType = ShowType;
        ActiveWorkspace.ShowCreated = ShowCreated; ActiveWorkspace.ShowExtension = ShowExtension;
    }
    private void LoadWorkspaces(WorkspaceState state)
    {
        Workspaces.Clear();
        if (state.Workspaces.Count == 0) { var initial = WorkspaceTab.FromLegacy(state); state.Workspaces.Add(initial); state.ActiveWorkspaceId = initial.Id; }
        foreach (var tab in state.Workspaces) Workspaces.Add(tab);
        var active = Workspaces.Single(x => x.Id == state.ActiveWorkspaceId);
        ExplorerCollapsed = state.ExplorerCollapsed ?? active.Layout.ExplorerCollapsed;
        var width = state.ExplorerWidth ?? active.Layout.ExplorerWidth;
        ExplorerWidth = double.IsFinite(width) ? Math.Clamp(width, 210, 480) : 280;
        var treeHeight = state.ExplorerTreeHeight ?? active.Layout.TreeHeight;
        ExplorerTreeHeight = double.IsFinite(treeHeight) ? Math.Max(60, treeHeight) : 160;
        var favoritesWidth = state.FavoritesWidth ?? active.Layout.FavoritesWidth;
        FavoritesWidth = double.IsFinite(favoritesWidth) ? Math.Clamp(favoritesWidth, 220, 500) : 265;
        var favoritesRatio = state.FavoritesRatio ?? active.Layout.FavoritesRatio;
        FavoritesRatio = double.IsFinite(favoritesRatio) ? Math.Clamp(favoritesRatio, .1, .9) : .5;
        LoadWorkspace(active);
    }
    private void LoadWorkspace(WorkspaceTab tab)
    {
        Search.HideResults();
        foreach (var card in Cards) card.Dispose();
        Cards.Clear(); Folders.Clear(); Favorites.Clear();
        foreach (var item in Workspaces) item.IsActive = item == tab;
        ActiveWorkspace = tab; Notify(nameof(ActiveWorkspace));
        collapseNext = tab.CollapseNext; Notify(nameof(ToggleFoldersLabel));
        Layout = tab.Layout; FolderHeight = tab.FolderHeight; FolderColumns = tab.FolderColumns;
        FavoriteColumns = tab.FavoriteColumns; RecentColumns = tab.RecentColumns;
        ShowModified = tab.ShowModified; ShowSize = tab.ShowSize; ShowType = tab.ShowType;
        ShowCreated = tab.ShowCreated; ShowExtension = tab.ShowExtension;
        foreach (var card in tab.Cards) AddState(card);
        Notify(nameof(IsEmpty));
    }
    internal async Task SwitchWorkspaceAsync(Guid id)
    {
        EnsureWorkspaceReady();
        var tab = Workspaces.Single(x => x.Id == id);
        if (tab == ActiveWorkspace) return;
        WorkspaceChanging?.Invoke();
        if (!Save()) throw new IOException("현재 작업공간을 저장하지 못해 전환을 중단했습니다.");
        WorkspaceBusy = true; saveTimer.Stop();
        try
        {
            LoadWorkspace(tab);
            WorkspaceChanged?.Invoke();
        }
        finally { WorkspaceBusy = false; }
        if (!Save()) throw new IOException("작업공간은 전환했지만 저장에 실패했습니다. 다시 저장하세요.");
        // The new tab is interactive immediately; folder reads continue with bounded concurrency.
        var currentCards = Cards.ToArray();
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        await Task.WhenAll(currentCards.Select(x => x.RefreshAsync()));
    }
    private string CheckedName(string name, Guid? except = null)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 60 || name.Contains('\r') || name.Contains('\n')) throw new InvalidOperationException("작업공간 이름은 1~60자로 입력하세요.");
        if (Workspaces.Any(x => x.Id != except && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("같은 이름의 작업공간이 있습니다.");
        return name;
    }
    internal async Task<WorkspaceTab> CreateWorkspaceAsync(string name)
    {
        EnsureWorkspaceReady(); var tab = new WorkspaceTab { Name = CheckedName(name) };
        Workspaces.Add(tab);
        try { await SwitchWorkspaceAsync(tab.Id); return tab; }
        catch { if (ActiveWorkspace != tab) Workspaces.Remove(tab); throw; }
    }
    internal void RenameWorkspace(Guid id, string name)
    {
        EnsureWorkspaceReady(); var tab = Workspaces.Single(x => x.Id == id); var old = tab.Name;
        tab.Name = CheckedName(name, id);
        if (!Save()) { tab.Name = old; throw new IOException("작업공간 이름을 저장하지 못했습니다."); }
    }
    internal async Task<WorkspaceTab> DuplicateWorkspaceAsync(Guid id)
    {
        EnsureWorkspaceReady(); WorkspaceChanging?.Invoke(); CaptureWorkspace();
        var source = Workspaces.Single(x => x.Id == id);
        var clone = JsonSerializer.Deserialize<WorkspaceTab>(JsonSerializer.Serialize(source))!;
        clone.Id = Guid.NewGuid(); foreach (var card in clone.Cards) card.Id = Guid.NewGuid();
        var baseName = source.Name.Length > 45 ? source.Name[..45] : source.Name;
        var name = baseName + " (복사)"; int suffix = 2;
        while (Workspaces.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) name = baseName + $" (복사 {suffix++})";
        clone.Name = name; Workspaces.Insert(Workspaces.IndexOf(source) + 1, clone);
        try { await SwitchWorkspaceAsync(clone.Id); return clone; }
        catch { if (ActiveWorkspace != clone) Workspaces.Remove(clone); throw; }
    }
    internal async Task DeleteWorkspaceAsync(Guid id)
    {
        EnsureWorkspaceReady();
        if (Workspaces.Count <= 1) throw new InvalidOperationException("작업공간은 최소 하나가 필요합니다.");
        var tab = Workspaces.Single(x => x.Id == id);
        WorkspaceChanging?.Invoke(); CaptureWorkspace();
        Directory.CreateDirectory(BackupDirectory);
        WorkspaceBackup.Write(Path.Combine(BackupDirectory, $"before-delete-workspace-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.pinshelf.json"), Snapshot());
        if (tab == ActiveWorkspace)
        {
            var position = Workspaces.IndexOf(tab);
            await SwitchWorkspaceAsync(Workspaces[position == 0 ? 1 : position - 1].Id);
        }
        var original = Workspaces.IndexOf(tab); Workspaces.Remove(tab);
        if (!Save()) { Workspaces.Insert(original, tab); throw new IOException("작업공간 삭제를 저장하지 못했습니다."); }
    }
    internal bool ReorderWorkspace(Guid sourceId, Guid targetId, bool after)
    {
        EnsureWorkspaceReady(); var source = Workspaces.Single(x => x.Id == sourceId); var target = Workspaces.Single(x => x.Id == targetId);
        int from = Workspaces.IndexOf(source), to = Workspaces.IndexOf(target) + (after ? 1 : 0); if (from < to) to--;
        if (from == to) return false;
        Workspaces.Move(from, to);
        if (!Save()) { Workspaces.Move(to, from); throw new IOException("탭 순서를 저장하지 못했습니다."); }
        return true;
    }
    internal void RelocateInactiveWorkspaces(string oldPath, string newPath)
    {
        foreach (var tab in Workspaces.Where(x => x != ActiveWorkspace))
            foreach (var card in tab.Cards)
            {
                var initial = FileTasks.Remap(card.InitialPath, oldPath, newPath);
                if (card.Title == Path.GetFileName(Path.TrimEndingDirectorySeparator(card.InitialPath))) card.Title = Path.GetFileName(Path.TrimEndingDirectorySeparator(initial));
                card.InitialPath = initial; card.CurrentPath = FileTasks.Remap(card.CurrentPath, oldPath, newPath);
            }
    }
}

