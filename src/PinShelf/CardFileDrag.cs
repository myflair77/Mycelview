using System.Windows;

namespace PinShelf;
internal sealed record CardDragEntry(string Path, bool Folder);
internal sealed record AcceptedFileDrop(string Destination, bool Copy);
internal sealed class CardFileDrag
{
    internal const string Format = "PinShelf.CardFiles.v1";
    internal Guid SourceCard { get; }
    internal IReadOnlyList<CardDragEntry> Entries { get; }
    internal AcceptedFileDrop? Accepted { get; set; }
    internal CardFileDrag(Guid source, IEnumerable<CardDragEntry> entries)
    {
        SourceCard = source;
        var distinct = entries.Select(x => x with { Path = FileOperationRules.Full(x.Path) }).DistinctBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        var folders = distinct.Where(x => x.Folder).Select(x => x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Entries = distinct.Where(x => !FileOperationRules.HasSelectedAncestor(x.Path, folders)).ToArray();
    }
    internal DataObject ToData() => new(Format, this);
    internal static CardFileDrag? Read(IDataObject data) => data.GetDataPresent(Format, false) ? data.GetData(Format, false) as CardFileDrag : null;
    internal DragDropEffects Effect(Guid target, string destination, DragDropKeyStates keys, DragDropEffects allowed, bool busy)
    {
        if (busy || target == SourceCard || Entries.Count == 0 || string.IsNullOrWhiteSpace(destination) || (keys & DragDropKeyStates.AltKey) != 0) return DragDropEffects.None;
        bool copy = (keys & DragDropKeyStates.ControlKey) != 0;
        var effect = copy ? DragDropEffects.Copy : DragDropEffects.Move;
        if ((allowed & effect) == 0) return DragDropEffects.None;
        try
        {
            if (Entries.Any(x => x.Folder && (FileOperationRules.Same(destination, x.Path) || FileOperationRules.Inside(destination, x.Path)))) return DragDropEffects.None;
            if (!copy && Entries.All(x => FileOperationRules.Same(System.IO.Path.GetDirectoryName(x.Path)!, destination))) return DragDropEffects.None;
            return effect;
        }
        catch (ArgumentException) { return DragDropEffects.None; }
    }
    internal async Task<FileOperationResult?> CompleteAsync(DragDropEffects result, FileTasks tasks)
    {
        var drop = Accepted; Accepted = null;
        if (drop == null || result != (drop.Copy ? DragDropEffects.Copy : DragDropEffects.Move)) return null;
        return await tasks.TransferAsync(Entries.Select(x => x.Path), drop.Destination, drop.Copy);
    }
}

