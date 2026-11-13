namespace Mycelview;

public sealed class SearchOptions
{
    public string Target { get; set; } = "All";
    public bool IncludeParentFolders { get; set; }
    public bool Exact { get; set; }
    public bool UseInitials { get; set; } = true;
    public SearchOptions Copy() => new() { Target = Target is "Files" or "Folders" ? Target : "All", IncludeParentFolders = IncludeParentFolders, Exact = Exact, UseInitials = UseInitials };
    public bool Accepts(bool folder) => Target == "Files" ? !folder : Target != "Folders" || folder;
    internal string SearchText(string path, bool folder) => Exact && !folder ? System.IO.Path.ChangeExtension(path, null) : path;
    internal bool MatchTerm(string path, string term)
    {
        if (!IncludeParentFolders) path = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
        if (!Exact) return path.Contains(term, StringComparison.OrdinalIgnoreCase);
        // The caller removes only the final file extension; folder names remain intact.
        return IncludeParentFolders
            ? path.Split('\\', '/').Any(name => name.Equals(term, StringComparison.OrdinalIgnoreCase))
            : path.Equals(term, StringComparison.OrdinalIgnoreCase);
    }
}
