using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace PinShelf;

public class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; Notify(name); }
    protected void Notify([CallerMemberName] string name = "") => PropertyChanged?.Invoke(this, new(name));
}
public sealed class RelayCommand(Action action) : ICommand
{
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => action();
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}
public sealed class CardState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string InitialPath { get; set; } = "";
    public string CurrentPath { get; set; } = "";
    public string Title { get; set; } = "";
    public bool IsFile { get; set; }
    public double X { get; set; } = 20;
    public double Y { get; set; } = 20;
    public double Width { get; set; } = 460;
    public double Height { get; set; } = 340;
    public bool Collapsed { get; set; }
    public bool NoteVisible { get; set; }
    public string Note { get; set; } = "";
    public double ScrollOffset { get; set; }
    public Dictionary<string,double> ColumnWidths { get; set; } = new();
    public string SortProperty { get; set; } = "Name";
    public bool SortDescending { get; set; }
}
public sealed class WorkspaceTab : Observable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    private string name = "기본 작업공간";
    public string Name { get => name; set => Set(ref name, value); }
    private bool isActive;
    [System.Text.Json.Serialization.JsonIgnore] public bool IsActive { get => isActive; set => Set(ref isActive, value); }
    public List<CardState> Cards { get; set; } = [];
    public PaneLayout Layout { get; set; } = new();
    public int FavoriteColumns { get; set; } = 1;
    public int RecentColumns { get; set; } = 1;
    public int FolderColumns { get; set; } = 2;
    public double FolderHeight { get; set; } = 340;
    public bool ShowModified { get; set; } = true;
    public bool ShowSize { get; set; } = true;
    public bool ShowType { get; set; }
    public bool ShowCreated { get; set; }
    public bool ShowExtension { get; set; }
    public bool CollapseNext { get; set; } = true;
    public double FolderScroll { get; set; }
    public double FavoriteScroll { get; set; }
    public static WorkspaceTab FromLegacy(WorkspaceState state) => new()
    {
        Cards = state.Cards, Layout = state.Layout ?? new(), FolderHeight = state.FolderHeight, FolderColumns = state.FolderColumns,
        ShowModified = state.ShowModified, ShowSize = state.ShowSize, ShowType = state.ShowType,
        ShowCreated = state.ShowCreated, ShowExtension = state.ShowExtension
    };
}
public sealed class WorkspaceState
{
    public bool? ExplorerCollapsed { get; set; }
    public double? ExplorerWidth { get; set; }
    public double? ExplorerTreeHeight { get; set; }
    public double? FavoritesWidth { get; set; }
    public double? FavoritesRatio { get; set; }
    public CardEnlargementSettings Enlargement { get; set; } = new();
    public List<WorkspaceTab> Workspaces { get; set; } = [];
    public Guid ActiveWorkspaceId { get; set; }
    public int TextScalePercent { get; set; } = 100;
    public int TextScaleBaseVersion { get; set; } = 2;
    public PaneLayout Layout { get; set; } = new();
    public List<RecentFile> RecentFiles { get; set; } = [];
    public int FolderColumns { get; set; } = 2;
    public double FolderHeight { get; set; } = 340;
    public bool ShowModified { get; set; } = true;
    public bool ShowSize { get; set; } = true;
    public bool ShowType { get; set; }
    public bool ShowCreated { get; set; }
    public bool ShowExtension { get; set; }
    public string SkinId { get; set; } = SkinManager.DefaultId;
    public int Version { get; set; } = 1;
    public List<CardState> Cards { get; set; } = [];
}
public sealed class PaneLayout
{
    public bool ExplorerCollapsed { get; set; }
    public double ExplorerWidth { get; set; } = 280;
    public double FavoritesWidth { get; set; } = 265;
    public double TreeHeight { get; set; } = 160;
    public double FavoritesRatio { get; set; } = 0.5;
}
public sealed record RecentFile(string CurrentPath, DateTimeOffset OpenedAt)
{
    [System.Text.Json.Serialization.JsonIgnore] public string Title => System.IO.Path.GetFileName(CurrentPath);
    [System.Text.Json.Serialization.JsonIgnore] public string OpenedDisplay => OpenedAt.ToLocalTime().ToString("MM.dd HH:mm");
    [System.Text.Json.Serialization.JsonIgnore] public System.Windows.Media.ImageSource IconImage => ShellIcons.ForPath(CurrentPath, false);
}
public sealed record FileEntry(string FullPath, string Name, bool IsDirectory, DateTime Modified, long Length)
{
    public DateTime Created { get; init; }
    public string Extension => IsDirectory ? "" : System.IO.Path.GetExtension(Name);
    public string Type => ShellIcons.TypeName(FullPath, IsDirectory);
    public System.Windows.Media.ImageSource IconImage => ShellIcons.ForPath(FullPath, IsDirectory);
    public string Icon => IsDirectory ? "📁" : "📄";
    public string Size => IsDirectory ? "—" : Length < 1024 ? $"{Length:N0} B" : Length < 1048576 ? $"{Length / 1024d:N1} KB" : $"{Length / 1048576d:N1} MB";
}



