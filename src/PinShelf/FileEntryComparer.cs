using System.Collections;
using System.Runtime.InteropServices;

namespace PinShelf;

internal sealed class FileEntryComparer(string property, bool descending) : IComparer
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string left, string right);

    public int Compare(object? x, object? y)
    {
        if (x is not FileEntry a || y is not FileEntry b) return 0;
        // Keep folders above files in both directions, like the existing card listing.
        if (a.IsDirectory != b.IsDirectory) return a.IsDirectory ? -1 : 1;
        var value = property switch
        {
            "Modified" => a.Modified.CompareTo(b.Modified),
            "Created" => a.Created.CompareTo(b.Created),
            "Length" => a.Length.CompareTo(b.Length),
            "Type" => StrCmpLogicalW(a.Type, b.Type),
            "Extension" => StrCmpLogicalW(a.Extension, b.Extension),
            "FullPath" => StrCmpLogicalW(a.FullPath, b.FullPath),
            _ => StrCmpLogicalW(a.Name, b.Name)
        };
        if (value == 0) value = StrCmpLogicalW(a.Name, b.Name);
        if (value == 0) value = StringComparer.OrdinalIgnoreCase.Compare(a.FullPath, b.FullPath);
        return descending ? -Math.Sign(value) : Math.Sign(value);
    }
}
