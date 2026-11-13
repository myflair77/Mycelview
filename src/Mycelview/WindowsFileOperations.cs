using System.IO;
using System.Runtime.InteropServices;

namespace Mycelview;
internal enum FileAction { Copy, Move, Rename, NewFolder, Recycle }
internal sealed record FileRequest(FileAction Action, string Source, string? Destination = null, string? Name = null);
internal sealed record FileChange(FileRequest Request, string? NewPath);
internal sealed record FileProgress(string Path, uint Total, uint Done);
internal sealed record FileOperationResult(IReadOnlyList<FileChange> Completed, IReadOnlyList<string> Errors, bool Aborted);

internal static class FileOperationRules
{
    internal static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    internal static bool Same(string a, string b) => StringComparer.OrdinalIgnoreCase.Equals(Full(a), Full(b));
    internal static bool Inside(string child, string parent) => Full(child).StartsWith(Full(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    internal static string[] TopLevel(IEnumerable<string> paths)
    {
        var all = paths.Select(Full).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var folders = all.Where(Directory.Exists).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return all.Where(p => !HasSelectedAncestor(p, folders)).ToArray();
    }
    internal static bool HasSelectedAncestor(string path, HashSet<string> folders)
    {
        for (var parent = Path.GetDirectoryName(path); parent != null; parent = Path.GetDirectoryName(parent))
            if (folders.Contains(Full(parent))) return true;
        return false;
    }
    internal static void CheckName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.Length > 255 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith(' ') || name.EndsWith('.'))
            throw new IOException("이름에 사용할 수 없는 문자 또는 끝의 공백·마침표가 있습니다.");
        var stem = name.Split('.')[0].TrimEnd(' ');
        if (System.Text.RegularExpressions.Regex.IsMatch(stem, @"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new IOException("Windows에서 예약된 이름입니다.");
    }
    internal static void Validate(FileRequest request)
    {
        if (request.Action == FileAction.NewFolder)
        { if (!Directory.Exists(request.Source)) throw new DirectoryNotFoundException(request.Source); CheckName(request.Name ?? ""); return; }
        if (!File.Exists(request.Source) && !Directory.Exists(request.Source)) throw new FileNotFoundException("원본을 찾을 수 없습니다.", request.Source);
        if (Directory.GetParent(Full(request.Source)) == null) throw new IOException("드라이브 루트는 작업 대상으로 사용할 수 없습니다.");
        if (request.Action is FileAction.Copy or FileAction.Move)
        {
            if (request.Destination == null || !Directory.Exists(request.Destination)) throw new DirectoryNotFoundException("대상 폴더를 찾을 수 없습니다.");
            if (Directory.Exists(request.Source) && (Same(request.Destination, request.Source) || Inside(request.Destination, request.Source)))
                throw new IOException("폴더 자신 또는 그 하위 폴더로 복사·이동할 수 없습니다.");
        }
        if (request.Name != null) CheckName(request.Name);
    }
}

internal static class WindowsFileOperations
{
    internal const uint InteractiveFlags = 0x0040 | 0x0200 | 0x4000 | 0x00080000 | 0x20000000;
    // Test-only flags suppress Windows dialogs; production must retain collision/error UI.
    internal const uint QuietTestFlags = (InteractiveFlags & ~0x20000040u) | 0x0004 | 0x0010 | 0x0400 | 0x00100000;
    internal static Task<FileOperationResult> RunAsync(IReadOnlyList<FileRequest> requests, IntPtr owner,
        CancellationToken token, IProgress<FileProgress>? progress = null, uint flags = InteractiveFlags, Action<string>? diagnostic = null)
    {
        var completion = new TaskCompletionSource<FileOperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            IFileOperationNative? operation = null; var shells = new List<object>(); uint cookie = 0;
            try
            {
                token.ThrowIfCancellationRequested();
                foreach (var request in requests) FileOperationRules.Validate(request);
                operation = (IFileOperationNative)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09"))!)!;
                Check(operation.SetOperationFlags(flags)); Check(operation.SetOwnerWindow(owner));
                var sink = new FileProgressSink(requests, token, progress, diagnostic);
                Check(operation.Advise(sink, out cookie));
                IShellItemNative Item(string path)
                {
                    var iid = typeof(IShellItemNative).GUID;
                    Check(SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var item)); shells.Add(item); return item;
                }
                foreach (var request in requests)
                {
                    token.ThrowIfCancellationRequested();
                    var source = Item(request.Source);
                    int result = request.Action switch
                    {
                        FileAction.Copy => operation.CopyItem(source, Item(request.Destination!), request.Name, null),
                        FileAction.Move => operation.MoveItem(source, Item(request.Destination!), request.Name, null),
                        FileAction.Rename => operation.RenameItem(source, request.Name!, null),
                        FileAction.NewFolder => operation.NewItem(source, 0x10, request.Name!, null, null),
                        FileAction.Recycle => operation.DeleteItem(source, null),
                        _ => throw new InvalidOperationException()
                    };
                    Check(result);
                }
                int hr = operation.PerformOperations();
                Check(operation.GetAnyOperationsAborted(out var aborted));
                if (hr < 0 && hr != unchecked((int)0x800704C7) && hr != unchecked((int)0x80004004)) sink.Errors.Add(Marshal.GetExceptionForHR(hr)?.Message ?? $"오류 0x{hr:X8}");
                sink.FinalizeCompleted(aborted || token.IsCancellationRequested || hr < 0);
                completion.SetResult(new(sink.Completed.ToArray(), sink.Errors.Distinct().ToArray(), aborted || token.IsCancellationRequested || hr < 0));
            }
            catch (OperationCanceledException) { completion.SetResult(new([], [], true)); }
            catch (Exception error) { completion.SetException(error); }
            finally
            {
                if (operation != null) { if (cookie != 0) operation.Unadvise(cookie); Marshal.FinalReleaseComObject(operation); }
                foreach (var shell in shells.Distinct(ReferenceEqualityComparer.Instance)) Marshal.FinalReleaseComObject(shell);
            }
        }) { IsBackground = true, Name = "Mycelview file operations" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
    private static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr context, ref Guid iid, out IShellItemNative item);
}

[ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellItemNative
{
    [PreserveSig] int BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, out IntPtr result);
    [PreserveSig] int GetParent(out IShellItemNative parent);
    [PreserveSig] int GetDisplayName(uint type, out IntPtr name);
    [PreserveSig] int GetAttributes(uint mask, out uint attributes);
    [PreserveSig] int Compare(IShellItemNative other, uint hint, out int order);
}
[ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFileOperationNative
{
    [PreserveSig] int Advise(IFileProgressNative sink, out uint cookie);
    [PreserveSig] int Unadvise(uint cookie);
    [PreserveSig] int SetOperationFlags(uint flags);
    [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
    [PreserveSig] int SetProgressDialog(IntPtr dialog);
    [PreserveSig] int SetProperties(IntPtr properties);
    [PreserveSig] int SetOwnerWindow(IntPtr owner);
    [PreserveSig] int ApplyPropertiesToItem(IShellItemNative item);
    [PreserveSig] int ApplyPropertiesToItems(IntPtr items);
    [PreserveSig] int RenameItem(IShellItemNative item, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileProgressNative? sink);
    [PreserveSig] int RenameItems(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] string name);
    [PreserveSig] int MoveItem(IShellItemNative item, IShellItemNative destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, IFileProgressNative? sink);
    [PreserveSig] int MoveItems(IntPtr items, IShellItemNative destination);
    [PreserveSig] int CopyItem(IShellItemNative item, IShellItemNative destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, IFileProgressNative? sink);
    [PreserveSig] int CopyItems(IntPtr items, IShellItemNative destination);
    [PreserveSig] int DeleteItem(IShellItemNative item, IFileProgressNative? sink);
    [PreserveSig] int DeleteItems(IntPtr items);
    [PreserveSig] int NewItem(IShellItemNative destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string? template, IFileProgressNative? sink);
    [PreserveSig] int PerformOperations();
    [PreserveSig] int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
}
[ComVisible(true), Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IFileProgressNative
{
    [PreserveSig] int StartOperations();
    [PreserveSig] int FinishOperations(int result);
    [PreserveSig] int PreRenameItem(uint flags, IShellItemNative item, [MarshalAs(UnmanagedType.LPWStr)] string name);
    [PreserveSig] int PostRenameItem(uint flags, IShellItemNative item, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IShellItemNative? created);
    [PreserveSig] int PreMoveItem(uint flags, IShellItemNative item, IShellItemNative destination, [MarshalAs(UnmanagedType.LPWStr)] string? name);
    [PreserveSig] int PostMoveItem(uint flags, IShellItemNative item, IShellItemNative destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, int result, IShellItemNative? created);
    [PreserveSig] int PreCopyItem(uint flags, IShellItemNative item, IShellItemNative destination, [MarshalAs(UnmanagedType.LPWStr)] string? name);
    [PreserveSig] int PostCopyItem(uint flags, IShellItemNative item, IShellItemNative destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, int result, IShellItemNative? created);
    [PreserveSig] int PreDeleteItem(uint flags, IShellItemNative item);
    [PreserveSig] int PostDeleteItem(uint flags, IShellItemNative item, int result, IShellItemNative? created);
    [PreserveSig] int PreNewItem(uint flags, IShellItemNative destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
    [PreserveSig] int PostNewItem(uint flags, IShellItemNative destination, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string? template, uint attributes, int result, IShellItemNative? created);
    [PreserveSig] int UpdateProgress(uint total, uint done);
    [PreserveSig] int ResetTimer();
    [PreserveSig] int PauseTimer();
    [PreserveSig] int ResumeTimer();
}
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class FileProgressSink : IFileProgressNative
{
    private readonly IReadOnlyList<FileRequest> requests;
    private readonly CancellationToken token;
    private readonly IProgress<FileProgress>? progress;
    private readonly Action<string>? diagnostic;
    private readonly HashSet<string> folders;
    private string current = "";
    private readonly Dictionary<IntPtr, string> originalPaths = [];
    private readonly System.Diagnostics.Stopwatch reportClock = System.Diagnostics.Stopwatch.StartNew();
    internal List<FileChange> Completed { get; } = [];
    internal List<string> Errors { get; } = [];
    internal FileProgressSink(IReadOnlyList<FileRequest> requests, CancellationToken token, IProgress<FileProgress>? progress, Action<string>? diagnostic = null)
    { this.requests = requests; this.token = token; this.progress = progress; this.diagnostic = diagnostic;
      folders = requests.Where(r => Directory.Exists(r.Source)).Select(r => r.Source).ToHashSet(StringComparer.OrdinalIgnoreCase); }
    internal void FinalizeCompleted(bool aborted)
    {
        // Shell folder callbacks may precede their children. Only finalize after PerformOperations.
        Completed.RemoveAll(c => c.Request.Action == FileAction.Move && (File.Exists(c.Request.Source) || Directory.Exists(c.Request.Source)));
        if (aborted || Errors.Count > 0) Completed.RemoveAll(c => c.Request.Action == FileAction.Copy && folders.Contains(c.Request.Source));
    }
    private int Cancel => token.IsCancellationRequested ? unchecked((int)0x800704C7) : 0;
    private static string? PathOf(IShellItemNative? item)
    {
        if (item == null) return null;
        try { if (item.GetDisplayName(0x80058000, out var value) < 0) return null; try { return Marshal.PtrToStringUni(value); } finally { Marshal.FreeCoTaskMem(value); } }
        catch { return null; }
    }
    private static IntPtr Identity(IShellItemNative item)
    { var identity = Marshal.GetIUnknownForObject(item); Marshal.Release(identity); return identity; }
    private int Before(IShellItemNative item)
    {
        current = PathOf(item) ?? "";
        diagnostic?.Invoke($"Before {Identity(item)} {current}");
        if (current.Length > 0 && requests.Any(r => FileOperationRules.Same(r.Source, current))) originalPaths[Identity(item)] = current;
        progress?.Report(new(current, 0, 0)); return Cancel;
    }
    private int After(FileAction action, IShellItemNative item, int result, IShellItemNative? created, string? name = null)
    {
        var path = originalPaths.GetValueOrDefault(Identity(item)) ?? PathOf(item) ?? current;
        diagnostic?.Invoke($"After {action} {Identity(item)} path={path} hr=0x{result:X8} new={PathOf(created)}");
        if (path.Length == 0) return 0;
        var request = requests.FirstOrDefault(r => r.Action == action && FileOperationRules.Same(r.Source, path) && (action != FileAction.NewFolder || r.Name == name));
        // Optimized same-volume moves return COPYENGINE_S_DONT_PROCESS_CHILDREN (0x270008), not S_OK.
        if (result >= 0 && created != null && request != null && !Completed.Any(x => x.Request == request)) Completed.Add(new(request, PathOf(created)));
        else if (result < 0 && Errors.Count < 100 && result != unchecked((int)0x800704C7)) Errors.Add($"{path}: {Marshal.GetExceptionForHR(result)?.Message}");
        return 0;
    }
    public int StartOperations() => Cancel;
    public int FinishOperations(int result) => 0;
    public int PreRenameItem(uint flags, IShellItemNative item, string name) => Before(item);
    public int PostRenameItem(uint flags, IShellItemNative item, string name, int result, IShellItemNative? created) => After(FileAction.Rename, item, result, created);
    public int PreMoveItem(uint flags, IShellItemNative item, IShellItemNative destination, string? name) => Before(item);
    public int PostMoveItem(uint flags, IShellItemNative item, IShellItemNative destination, string? name, int result, IShellItemNative? created) => After(FileAction.Move, item, result, created);
    public int PreCopyItem(uint flags, IShellItemNative item, IShellItemNative destination, string? name) => Before(item);
    public int PostCopyItem(uint flags, IShellItemNative item, IShellItemNative destination, string? name, int result, IShellItemNative? created) => After(FileAction.Copy, item, result, created);
    public int PreDeleteItem(uint flags, IShellItemNative item)
    {
        var result = Before(item);
        diagnostic?.Invoke($"Delete flags=0x{flags:X8}");
        if (result == 0 && requests.Any(r => r.Action == FileAction.Recycle) && (flags & 0x80) == 0)
        { Errors.Add("휴지통을 사용할 수 없어 중단했습니다. 영구 삭제하지 않았습니다."); return unchecked((int)0x80070032); }
        return result;
    }
    public int PostDeleteItem(uint flags, IShellItemNative item, int result, IShellItemNative? created) => After(FileAction.Recycle, item, result, created);
    public int PreNewItem(uint flags, IShellItemNative destination, string name) => Before(destination);
    public int PostNewItem(uint flags, IShellItemNative destination, string name, string? template, uint attributes, int result, IShellItemNative? created) => After(FileAction.NewFolder, destination, result, created, name);
    public int UpdateProgress(uint total, uint done)
    { if (reportClock.ElapsedMilliseconds >= 50 || done == total) { progress?.Report(new(current, total, done)); reportClock.Restart(); } return Cancel; }
    public int ResetTimer() => 0;
    public int PauseTimer() => 0;
    public int ResumeTimer() => 0;
}

