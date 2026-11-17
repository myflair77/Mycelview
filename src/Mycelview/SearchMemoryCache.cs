using System.Buffers;
using System.Runtime.InteropServices;

namespace Mycelview;

// Share directory prefixes across siblings; keep only names per indexed item.
// SQLite remains the source of full paths and detailed result metadata.
internal sealed class SearchMemoryCache
{
    private sealed class Prefix(string text, string initials)
    {
        public readonly string Text = text;
        public readonly string Initials = text == initials ? text : initials;
        public int References;
    }
    private readonly record struct Entry(long Id, Prefix? Parent, string Name, string Initials, bool Folder);
    private readonly List<Entry> entries = [];
    private readonly Dictionary<(Prefix Parent, string Name), int> locations = [];
    private readonly Dictionary<string, Prefix> prefixes = new(StringComparer.Ordinal);
    private readonly Stack<int> free = new();
    private readonly ReaderWriterLockSlim gate = new();
    public void Reserve(int count) { entries.EnsureCapacity(count); locations.EnsureCapacity(count); }
    private static int NameStart(string text) => text.AsSpan().LastIndexOfAny('\\', '/') + 1;
    public void Put(long id, string text, string initials, bool folder)
    {
        int split = NameStart(text);
        string path = text[..split], name = text[split..], initialName = initials[split..];
        if (initialName == name) initialName = name;
        gate.EnterWriteLock();
        try
        {
            if (!prefixes.TryGetValue(path, out var parent))
                prefixes.Add(path, parent = new Prefix(path, initials[..split]));
            var key = (parent, name);
            var entry = new Entry(id, parent, name, initialName, folder);
            if (locations.TryGetValue(key, out var slot))
            {
                var old = entries[slot];
                entries[slot] = entry with { Name = old.Name, Initials = old.Initials == initialName ? old.Initials : initialName };
            }
            else
            {
                parent.References++;
                if (free.TryPop(out slot)) entries[slot] = entry;
                else { slot = entries.Count; entries.Add(entry); }
                locations.Add(key, slot);
            }
        }
        finally { gate.ExitWriteLock(); }
    }
    private void Remove(int slot)
    {
        var entry = entries[slot]; var parent = entry.Parent!;
        locations.Remove((parent, entry.Name));
        entries[slot] = default; free.Push(slot);
        if (--parent.References == 0) prefixes.Remove(parent.Text);
    }
    public void RemoveTree(string key)
    {
        gate.EnterWriteLock();
        try
        {
            int split = NameStart(key);
            if (prefixes.TryGetValue(key[..split], out var parent) && locations.TryGetValue((parent, key[split..]), out var slot))
            {
                bool folder = entries[slot].Folder;
                Remove(slot);
                if (!folder) return;
            }
            // A folder row may already be absent while its descendants remain.
            string descendant = key.TrimEnd('\\', '/') + '\\';
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].Parent is { } prefix && prefix.Text.StartsWith(descendant, StringComparison.Ordinal)) Remove(i);
        }
        finally { gate.ExitWriteLock(); }
    }
    public List<long> Find(SearchQuery.Plan query, int limit, CancellationToken token, SearchOptions? options = null)
    {
        var found = new List<long>(Math.Min(limit, 4096));
        if (limit <= 0) return found;
        options ??= new SearchOptions();
        char[]? textArray = null, initialsArray = null;
        Span<char> textBuffer = stackalloc char[512];
        Span<char> initialsBuffer = stackalloc char[512];
        gate.EnterReadLock();
        try
        {
            var items = CollectionsMarshal.AsSpan(entries);
            for (int i = 0; i < items.Length; i++)
            {
                if ((i & 255) == 0) token.ThrowIfCancellationRequested();
                ref readonly var entry = ref items[i];
                if (entry.Parent is not { } parent || !options.Accepts(entry.Folder)) continue;
                scoped ReadOnlySpan<char> name = entry.Name, initials = entry.Initials;
                if (options.Exact && !entry.Folder)
                {
                    int dot = name.LastIndexOf('.');
                    if (dot >= 0) { name = name[..dot]; initials = initials[..dot]; }
                }
                if (!options.IncludeParentFolders && name.IsEmpty) { name = parent.Text; initials = parent.Initials; }
                if (options.IncludeParentFolders)
                {
                    int length = parent.Text.Length + name.Length;
                    if (length > textBuffer.Length)
                    {
                        if (textArray != null) ArrayPool<char>.Shared.Return(textArray);
                        if (initialsArray != null) ArrayPool<char>.Shared.Return(initialsArray);
                        textArray = ArrayPool<char>.Shared.Rent(length);
                        initialsArray = ArrayPool<char>.Shared.Rent(length);
                        textBuffer = textArray; initialsBuffer = initialsArray;
                    }
                    parent.Text.AsSpan().CopyTo(textBuffer); name.CopyTo(textBuffer[parent.Text.Length..]);
                    parent.Initials.AsSpan().CopyTo(initialsBuffer); initials.CopyTo(initialsBuffer[parent.Initials.Length..]);
                    name = textBuffer[..length]; initials = initialsBuffer[..length];
                }
                if (query.MatchSpans(name, initials)) { found.Add(entry.Id); if (found.Count >= limit) break; }
            }
        }
        finally
        {
            gate.ExitReadLock();
            if (textArray != null) ArrayPool<char>.Shared.Return(textArray);
            if (initialsArray != null) ArrayPool<char>.Shared.Return(initialsArray);
        }
        return found;
    }
}
