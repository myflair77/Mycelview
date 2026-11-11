using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PinShelf;
internal static class ShellActions
{
    internal static void Reveal(string path) => Process.Start(new ProcessStartInfo("explorer.exe")
    { Arguments = "/select,\"" + path + "\"", UseShellExecute = true });
    internal static void Properties(string path)
    {
        var info = new ShellExecuteInfo { Size = Marshal.SizeOf<ShellExecuteInfo>(), Mask = 0x0000000C | 0x00000400,
            Verb = "properties", File = path, Show = 1 };
        if (!ShellExecuteEx(ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int Size, Mask;
        public IntPtr Window;
        public string? Verb, File, Parameters, Directory;
        public int Show;
        public IntPtr Instance, IdList;
        public string? Class;
        public IntPtr ClassKey;
        public int HotKey;
        public IntPtr Icon, Process;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellExecuteEx(ref ShellExecuteInfo info);
}
