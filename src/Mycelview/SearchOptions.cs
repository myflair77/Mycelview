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
    internal bool MatchTerm(string path, string term) => MatchTerm(path.AsSpan(), term.AsSpan());
    internal bool MatchTerm(ReadOnlySpan<char> path, ReadOnlySpan<char> term)
    {
        if (!IncludeParentFolders)
        {
            path = path.TrimEnd("\\/".AsSpan());
            int slash = path.LastIndexOfAny('\\', '/');
            if (slash >= 0) path = path[(slash + 1)..];
        }
        if (!Exact) return path.Contains(term, StringComparison.OrdinalIgnoreCase);
        if (!IncludeParentFolders) return path.Equals(term, StringComparison.OrdinalIgnoreCase);
        while (true)
        {
            int slash = path.IndexOfAny('\\', '/');
            if (slash < 0) return path.Equals(term, StringComparison.OrdinalIgnoreCase);
            if (path[..slash].Equals(term, StringComparison.OrdinalIgnoreCase)) return true;
            path = path[(slash + 1)..];
        }
    }
}
