using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace PinShelf;

public sealed class StateStore(string directory)
{
    public string DirectoryPath => directory;
    public string FilePath => Path.Combine(directory, "workspace.json");
    public WorkspaceState Load()
    {
        if (!File.Exists(FilePath)) return new();
        return Parse(File.ReadAllText(FilePath));
    }
    internal static WorkspaceState Parse(string json)
    {
        var state = JsonSerializer.Deserialize<WorkspaceState>(json) ?? throw new InvalidDataException("설정 내용이 비어 있습니다.");
        state.Enlargement = (state.Enlargement ?? new()).Normalize();
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("TextScaleBaseVersion", out var baseline) || baseline.GetInt32() < 2)
        { state.TextScalePercent = 100; state.TextScaleBaseVersion = 2; }
        if (state.Version is not (1 or 2)) throw new InvalidDataException("지원하지 않는 설정 버전입니다.");
        if (state.Cards is null) throw new InvalidDataException("카드 목록이 없습니다.");
        state.Workspaces ??= [];
        if (state.Workspaces.Count == 0)
        {
            var initial = WorkspaceTab.FromLegacy(state); state.Workspaces.Add(initial); state.ActiveWorkspaceId = initial.Id;
        }
        var workspaceIds = new HashSet<Guid>();
        var cardIds = new HashSet<Guid>();
        foreach (var workspace in state.Workspaces)
        {
            if (workspace == null || workspace.Id == Guid.Empty || !workspaceIds.Add(workspace.Id) || string.IsNullOrWhiteSpace(workspace.Name) || workspace.Cards == null)
                throw new InvalidDataException("작업공간 이름 또는 식별자가 잘못되었습니다.");
            workspace.FolderColumns = Math.Clamp(workspace.FolderColumns,1,5);
            workspace.FavoriteColumns = Math.Clamp(workspace.FavoriteColumns,1,5);
            workspace.RecentColumns = Math.Clamp(workspace.RecentColumns,1,5);
            workspace.Layout ??= new();
            foreach (var card in workspace.Cards)
                if (card == null || card.Id == Guid.Empty || !cardIds.Add(card.Id)) throw new InvalidDataException("작업공간의 카드 식별자가 중복되거나 비어 있습니다.");
        }
        if (!workspaceIds.Contains(state.ActiveWorkspaceId)) throw new InvalidDataException("마지막 작업공간을 찾을 수 없습니다.");
        foreach (var c in state.Cards.Concat(state.Workspaces.SelectMany(x => x.Cards)))
        {
            if (c is null || string.IsNullOrWhiteSpace(c.InitialPath) || string.IsNullOrWhiteSpace(c.CurrentPath) || c.Title is null || c.Note is null)
                throw new InvalidDataException("카드 설정의 경로 또는 텍스트가 잘못되었습니다.");
            c.Width = double.IsFinite(c.Width) ? Math.Clamp(c.Width, 340, 1800) : 460;
            c.Height = double.IsFinite(c.Height) ? Math.Clamp(c.Height, 220, 1400) : 340;
            c.X = double.IsFinite(c.X) ? Math.Clamp(c.X, 0, 100000) : 20;
            c.Y = double.IsFinite(c.Y) ? Math.Clamp(c.Y, 0, 100000) : 20;
        }
        return state;
    }
    public void Save(WorkspaceState state)
    {
        Directory.CreateDirectory(directory);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak", true);
        else File.Move(temp, FilePath);
    }
}
public static class FileService
{
    public static event Action<string>? FileOpened;
    public static List<FileEntry> Read(string path, CancellationToken token)
    {
        var result = new List<FileEntry>();
        foreach (var item in new DirectoryInfo(path).EnumerateFileSystemInfos())
        {
            token.ThrowIfCancellationRequested();
            try
            {
                bool folder = (item.Attributes & FileAttributes.Directory) != 0;
                result.Add(new(item.FullName, item.Name, folder, item.LastWriteTime, folder ? 0 : ((FileInfo)item).Length) { Created = item.CreationTime });
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return result.OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
    public static void Open(string path)
    {
        var isFile = File.Exists(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        if (isFile) FileOpened?.Invoke(path);
    }
}



