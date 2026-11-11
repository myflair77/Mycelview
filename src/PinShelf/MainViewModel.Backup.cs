using System.IO;
using System.Text.Json;

namespace PinShelf;
public sealed partial class MainViewModel
{
    internal bool SettingsBusy { get; private set; }
    internal string BackupDirectory => Path.Combine(store.DirectoryPath, "backups");
    internal string UndoDirectory => Path.Combine(store.DirectoryPath, "undo", "history-v1");
    private WorkspaceState Snapshot()
    {
        CaptureWorkspace();
        return new()
    {
        Version = 2, Workspaces = Workspaces.ToList(), ActiveWorkspaceId = ActiveWorkspace?.Id ?? Guid.Empty,
        ExplorerCollapsed = ExplorerCollapsed, ExplorerWidth = ExplorerWidth, ExplorerTreeHeight = ExplorerTreeHeight, FavoritesWidth = FavoritesWidth, FavoritesRatio = FavoritesRatio,
        FolderColumns = FolderColumns,
        Enlargement = Enlargement.Normalize(),
        TextScalePercent = TextScalePercent,
        Layout = Layout, SkinId = SelectedSkinId, FolderHeight = FolderHeight, RecentFiles = RecentFiles.ToList(),
        ShowModified = ShowModified, ShowSize = ShowSize, ShowType = ShowType, ShowCreated = ShowCreated, ShowExtension = ShowExtension,
        Cards = (reorderSnapshot ?? Folders.Concat(Favorites).ToArray()).Select(c => c.State).ToList()
    };
    }
    private void EnsureBackupReady()
    {
        if (SettingsBusy || WorkspaceBusy || FileTasks.Busy || reorderSnapshot != null) throw new InvalidOperationException("파일 작업이나 순서 변경을 마친 뒤 다시 시도하세요.");
        if (!canSave) throw new InvalidOperationException("현재 설정을 읽지 못해 복원을 진행할 수 없습니다.");
    }
    internal void ExportSettings(string path)
    {
        EnsureBackupReady();
        if (StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(path), Path.GetFullPath(store.FilePath)) ||
            StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(path), Path.GetFullPath(store.FilePath + ".bak")))
            throw new InvalidOperationException("프로그램 내부 설정 파일과 다른 이름으로 내보내세요.");
        WorkspaceBackup.Write(path, Snapshot());
    }
    internal async Task<string> ImportSettingsAsync(string path)
    {
        EnsureBackupReady();
        var incoming = WorkspaceBackup.Read(path);
        var previous = StateStore.Parse(JsonSerializer.Serialize(Snapshot()));
        Directory.CreateDirectory(BackupDirectory);
        var backupPath = Path.Combine(BackupDirectory, $"before-restore-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.pinshelf.json");
        // If this fails, neither the active file nor the current workspace is replaced.
        WorkspaceBackup.Write(backupPath, previous);
        SettingsBusy = true;
        saveTimer.Stop(); reconcileTimer.Stop();
        try
        {
            store.Save(incoming);
            await ApplyWorkspaceAsync(incoming);
        }
        catch
        {
            store.Save(previous);
            await ApplyWorkspaceAsync(previous);
            throw;
        }
        finally { SettingsBusy = false; saveTimer.Stop(); reconcileTimer.Start(); }
        return backupPath;
    }
    private async Task ApplyWorkspaceAsync(WorkspaceState state)
    {
        Search.HideResults();
        foreach (var card in Cards) card.Dispose();
        Cards.Clear(); Folders.Clear(); Favorites.Clear(); RecentFiles.Clear();
        Enlargement = state.Enlargement ?? new();
        TextScalePercent = state.TextScalePercent;
        SelectedSkinId = state.SkinId;
        foreach (var recent in (state.RecentFiles ?? []).Where(r => r != null && !string.IsNullOrWhiteSpace(r.CurrentPath)).Take(100)) RecentFiles.Add(recent);
        LoadWorkspaces(state);
        Notify(nameof(IsEmpty));
        saveTimer.Stop();
        await Task.WhenAll(Cards.Select(c => c.RefreshAsync()));
        WorkspaceChanged?.Invoke();
    }
}


