using System.IO;
using System.Text.Json;

namespace Mycelview;

internal sealed class WorkspaceBackup
{
    public string Format { get; set; } = "Mycelview.Settings";
    public int Version { get; set; } = 1;
    public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.Now;
    public WorkspaceState? Workspace { get; set; }

    internal static WorkspaceState Read(string path)
    {
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("설정 파일은 32 MB 이하여야 합니다.");
        var json = File.ReadAllText(path);
        var backup = JsonSerializer.Deserialize<WorkspaceBackup>(json) ?? throw new InvalidDataException("비어 있는 백업입니다.");
        // Require explicit metadata rather than accepting arbitrary JSON with default values.
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("Format", out var format) || format.GetString() is not ("Mycelview.Settings" or "PinShelf.Settings") ||
            !document.RootElement.TryGetProperty("Version", out var version) || version.GetInt32() != 1 || backup.Workspace == null)
            throw new InvalidDataException("지원하는 Mycelview 설정 백업 파일이 아닙니다.");
        var workspace = document.RootElement.GetProperty("Workspace");
        if (!workspace.TryGetProperty("Version", out _) || !workspace.TryGetProperty("Cards", out var cards) || cards.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("백업에 카드 목록 또는 설정 버전이 없습니다.");
        var state = StateStore.Parse(JsonSerializer.Serialize(backup.Workspace));
        ValidateCards(state.Cards);
        ValidateCards(state.Workspaces.SelectMany(x => x.Cards));
        return state;
    }
    private static void ValidateCards(IEnumerable<CardState> cards)
    {
        var ids = new HashSet<Guid>();
        foreach (var card in cards)
        {
            if (card.Id == Guid.Empty || !ids.Add(card.Id)) throw new InvalidDataException("카드 식별자가 중복되거나 비어 있습니다.");
            if (!Path.IsPathFullyQualified(card.InitialPath) || !Path.IsPathFullyQualified(card.CurrentPath))
                throw new InvalidDataException("카드 경로는 전체 경로여야 합니다.");
            _ = Path.GetFullPath(card.InitialPath); _ = Path.GetFullPath(card.CurrentPath);
            if (card.SortProperty is not ("Name" or "Modified" or "Length" or "Type" or "Created" or "Extension"))
                throw new InvalidDataException("지원하지 않는 정렬 기준입니다.");
            if (!double.IsFinite(card.ScrollOffset) || card.ScrollOffset < 0) throw new InvalidDataException("목록 위치가 잘못되었습니다.");
        }
    }

    internal static void Write(string path, WorkspaceState state)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new WorkspaceBackup { Workspace = state }, new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
