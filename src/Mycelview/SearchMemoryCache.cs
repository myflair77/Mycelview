namespace Mycelview;

// Compact search-only projection. Detailed metadata stays in SQLite.
internal sealed class SearchMemoryCache
{
    private sealed record Entry(long Id, string Text, string Initials, bool Folder);
    private readonly List<Entry?> entries = [];
    private readonly Dictionary<string, int> locations = new(StringComparer.Ordinal);
    private readonly Stack<int> free = new();
    private readonly ReaderWriterLockSlim gate = new();
    public void Reserve(int count) { entries.EnsureCapacity(count); locations.EnsureCapacity(count); }
    public void Put(long id, string text, string initials, bool folder)
    {
        // Most PC paths are ASCII: reuse the same string instead of storing two copies.
        if (text == initials) initials = text;
        gate.EnterWriteLock();
        try
        {
            var entry = new Entry(id, text, initials, folder);
            if (locations.TryGetValue(text, out var slot)) entries[slot] = entry;
            else if (free.TryPop(out slot)) { entries[slot] = entry; locations[text] = slot; }
            else { locations[text] = entries.Count; entries.Add(entry); }
        }
        finally { gate.ExitWriteLock(); }
    }
    public void RemoveTree(string key)
    {
        gate.EnterWriteLock();
        try
        {
            if (!locations.TryGetValue(key, out var slot)) return;
            var folder = entries[slot]!.Folder;
            locations.Remove(key); entries[slot] = null; free.Push(slot);
            if (!folder) return;
            var prefix = key.TrimEnd('\\') + '\\';
            foreach (var child in locations.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
            { slot = locations[child]; locations.Remove(child); entries[slot] = null; free.Push(slot); }
        }
        finally { gate.ExitWriteLock(); }
    }
    public List<long> Find(SearchQuery.Plan query, int limit, CancellationToken token, SearchOptions? options = null)
    {
        var found = new List<long>(Math.Min(limit, 4096));
        gate.EnterReadLock();
        try
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if ((i & 255) == 0) token.ThrowIfCancellationRequested();
                var entry = entries[i];
                if (entry != null && (options == null || options.Accepts(entry.Folder)) && query.MatchCached(options?.SearchText(entry.Text, entry.Folder) ?? entry.Text, options?.SearchText(entry.Initials, entry.Folder) ?? entry.Initials))
                { found.Add(entry.Id); if (found.Count >= limit) break; }
            }
        }
        finally { gate.ExitReadLock(); }
        return found;
    }
}
