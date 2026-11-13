using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
namespace Mycelview;
internal static class DesktopItems
{
    private const string VisibilityKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";
    internal static string DesktopIdentity(string path)
    {
        var id = path.Length >= 40 ? path.Substring(2, 38) : path;
        // Control Panel has multiple parsing names but only one desktop icon.
        return id.Equals("{26EE0668-A00A-44D7-9371-BEB064C98683}", StringComparison.OrdinalIgnoreCase)
            || id.Equals("{21EC2020-3AEA-1069-A2DD-08002B30309D}", StringComparison.OrdinalIgnoreCase)
            ? "{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}" : id;
    }
    internal static bool IsVisibleOnDesktop(string path)
    {
        string id = DesktopIdentity(path);
        using var user = Registry.CurrentUser.OpenSubKey(VisibilityKey);
        using var machine = Registry.LocalMachine.OpenSubKey(VisibilityKey);
        return (user?.GetValue(id) ?? machine?.GetValue(id)) is not int hidden || hidden == 0;
    }
    internal static bool IsShellPath(string path) => path.StartsWith("::{", StringComparison.Ordinal);
    internal static bool IsDesktop(string path) => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), Path.TrimEndingDirectorySeparator(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)), StringComparison.OrdinalIgnoreCase);
    internal static Task<List<FileEntry>> ReadAsync(CancellationToken token)
    {
        var completion = new TaskCompletionSource<List<FileEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            object? shell = null, desktop = null, items = null;
            try
            {
                token.ThrowIfCancellationRequested();
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
                desktop = ((dynamic)shell!).NameSpace(0); items = ((dynamic)desktop!).Items();
                var result = new List<FileEntry>();
                var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < (int)((dynamic)items).Count; i++)
                {
                    token.ThrowIfCancellationRequested(); object item = ((dynamic)items).Item(i);
                    try
                    {
                        dynamic entry = item; string path = entry.Path; string name = entry.Name;
                        if (!(bool)entry.IsFileSystem && IsShellPath(path))
                        {
                            if (IsVisibleOnDesktop(path) && identities.Add(DesktopIdentity(path)))
                                result.Add(new(path, name, false, DateTime.MinValue, 0));
                        }
                        else if ((bool)entry.IsFileSystem)
                        {
                            FileSystemInfo info = (bool)entry.IsFolder ? new DirectoryInfo(path) : new FileInfo(path);
                            if (info.Exists) result.Add(new(path, info.Name, info is DirectoryInfo, info.LastWriteTime, info is FileInfo file ? file.Length : 0) { Created = info.CreationTime });
                        }
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or COMException) { }
                    finally { Marshal.FinalReleaseComObject(item); }
                }
                completion.SetResult(result);
            }
            catch (OperationCanceledException) { completion.TrySetCanceled(token); }
            catch (Exception error) { completion.TrySetException(error); }
            finally
            {
                if (items != null) Marshal.FinalReleaseComObject(items);
                if (desktop != null) Marshal.FinalReleaseComObject(desktop);
                if (shell != null) Marshal.FinalReleaseComObject(shell);
            }
        }) { IsBackground = true, Name = "Mycelview desktop listing" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
}
