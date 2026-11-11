using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PinShelf;

// Keep the Shell-owned HMENU intact: extensions can paint icons and lazily populate submenus.
internal sealed class NativeShellMenu : IDisposable
{
    internal const uint First = 1, Last = 0x6fff, Preview = 0x7000, Reveal = 0x7001, Undo = 0x7002, Unregister = 0x7003, AddFolder = 0x7004, AddFile = 0x7005;
    private object? array;
    private IContextMenu? context;
    private IntPtr logo;
    private readonly string[] paths;
    private readonly Dictionary<uint, string> browserVerbs = new();
    internal IntPtr Menu { get; private set; }
    internal static bool IsOpen { get; private set; }

    internal NativeShellMenu(string[] paths, IntPtr owner)
    {
        this.paths = paths;
        var pidls = new List<IntPtr>();
        try
        {
            foreach (var path in paths)
            {
                Marshal.ThrowExceptionForHR(SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _));
                pidls.Add(pidl);
            }
            var folderIid = typeof(IShellFolder).GUID;
            IShellFolder folder;
            IntPtr[] children;
            if (paths.Select(System.IO.Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
            {
                Marshal.ThrowExceptionForHR(SHBindToParent(pidls[0], ref folderIid, out folder, out _));
                children = pidls.Select(ILFindLastID).ToArray();
            }
            else
            {
                Marshal.ThrowExceptionForHR(SHGetDesktopFolder(out folder));
                children = pidls.ToArray();
            }
            array = folder;
            var iid = typeof(IContextMenu).GUID;
            folder.GetUIObjectOf(owner, (uint)children.Length, children, ref iid, IntPtr.Zero, out var result);
            context = (IContextMenu)result;
            Menu = CreatePopupMenu();
            if (Menu == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
            // EXPLORE | ITEMMENU | CANRENAME; preserve the normal Explorer selection menu, not extended Shift verbs.
            Marshal.ThrowExceptionForHR(context.QueryContextMenu(Menu, 0, First, Last, 0x94));
            AddBrowserVerbs();
        }
        catch { Dispose(); throw; }
        finally { foreach (var pidl in pidls) Marshal.FreeCoTaskMem(pidl); }
    }

    internal string Verb(uint id)
    {
        if (browserVerbs.TryGetValue(id, out var browserVerb)) return browserVerb;
        if (id < First || id > Last || context == null) return "";
        var buffer = Marshal.AllocHGlobal(1024);
        try
        {
            Marshal.WriteInt16(buffer, 0);
            return context.GetCommandString((UIntPtr)(id - First), 4, IntPtr.Zero, buffer, 512) >= 0
                ? Marshal.PtrToStringUni(buffer) ?? "" : "";
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    // Explorer suppresses these OnlyInBrowserWindow verbs for external menu hosts.
    // Read their localized labels from Windows rather than translating or hardcoding them.
    private void AddBrowserVerbs()
    {
        if (!paths.All(System.IO.Directory.Exists)) return;
        var entries = Entries();
        uint position = (uint)Math.Max(0, entries.FindIndex(e => e.Verb == "open") + 1);
        uint id = 0x7100;
        foreach (var verb in new[] { "opennewtab", "opennewwindow" })
        {
            if (entries.Any(e => e.Verb == verb)) continue;
            using var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"Folder\shell\" + verb);
            if (key?.GetValue("MUIVerb") is not string resource) continue;
            var label = new StringBuilder(512);
            if (SHLoadIndirectString(resource, label, (uint)label.Capacity, IntPtr.Zero) < 0) continue;
            if (!InsertMenu(Menu, position++, 0x400, id, label.ToString())) throw new System.ComponentModel.Win32Exception();
            browserVerbs.Add(id++, verb);
        }
    }
    internal record Entry(uint Id, string Text, string Verb, IntPtr Bitmap);
    internal List<Entry> Entries()
    {
        var entries = new List<Entry>();
        for (uint i = 0; i < GetMenuItemCount(Menu); i++)
        {
            var text = new StringBuilder(1024); GetMenuString(Menu, i, text, text.Capacity, 0x400);
            var info = new MenuInfo { Size = (uint)Marshal.SizeOf<MenuInfo>(), Mask = 0x2 | 0x80 };
            GetMenuItemInfo(Menu, i, true, ref info);
            entries.Add(new(info.Id, text.ToString(), Verb(info.Id), info.Bitmap));
        }
        return entries;
    }
    internal void AddPinShelfItems(bool filesOnly, bool single, bool canUndo, bool canUnregister)
    {
        logo = CreateLogo();
        var entries = Entries();
        int anchor = entries.FindIndex(e => e.Verb.Equals(filesOnly ? "open" : "opennewwindow", StringComparison.OrdinalIgnoreCase));
        if (anchor < 0) anchor = entries.FindIndex(e => e.Verb.Equals("open", StringComparison.OrdinalIgnoreCase));
        uint position = (uint)Math.Max(0, anchor + 1);
        if (filesOnly) Insert(position++, Preview, "미리보기\tSpace", single);
        if (paths.Any(System.IO.Directory.Exists)) Insert(position++, AddFolder, "폴더 즐겨찾기에 추가", true);
        if (paths.Any(System.IO.File.Exists)) Insert(position++, AddFile, "파일 즐겨찾기에 추가", true);
        Insert(position, Reveal, "Windows 탐색기로 열기", true);
        entries = Entries();
        int cut = entries.FindIndex(e => e.Verb.Equals("cut", StringComparison.OrdinalIgnoreCase));
        position = cut < 0 ? (uint)entries.Count : (uint)cut;
        InsertMenu(Menu, position++, 0x400 | 0x800, 0, null);
        Insert(position, Undo, "실행 취소\tCtrl+Z", canUndo);
        entries = Entries();
        int properties = entries.FindLastIndex(e => e.Verb.Equals("properties", StringComparison.OrdinalIgnoreCase));
        position = properties < 0 ? (uint)entries.Count : (uint)(properties + 1);
        InsertMenu(Menu, position++, 0x400 | 0x800, 0, null);
        Insert(position, Unregister, "선택 항목 등록 해제", canUnregister);
    }
    private void Insert(uint position, uint id, string text, bool enabled)
    {
        if (!InsertMenu(Menu, position, 0x400u | (enabled ? 0u : 3u), id, text)) throw new System.ComponentModel.Win32Exception();
        var info = new MenuInfo { Size = (uint)Marshal.SizeOf<MenuInfo>(), Mask = 0x80, Bitmap = logo };
        SetMenuItemInfo(Menu, id, false, ref info);
    }
    internal uint Track(FrameworkElement owner, bool keyboard)
    {
        // A WPF Popup has its own topmost HWND. Hosting the menu on the main
        // window puts it behind search results when WPF raises that popup.
        var source = PresentationSource.FromVisual(owner) as HwndSource
            ?? throw new InvalidOperationException("메뉴를 표시할 창이 없습니다.");
        var hwnd = source.Handle;
        var wasTopmost = (GetWindowLong(hwnd, -20) & 0x8) != 0;
        IntPtr Hook(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg is 0x117 or 0x2b or 0x2c or 0x120)
            {
                if (context is IContextMenu3 three && three.HandleMenuMsg2((uint)msg, w, l, out var result) == 0)
                { handled = true; return result; }
                if (context is IContextMenu2 two && two.HandleMenuMsg((uint)msg, w, l) == 0) handled = true;
            }
            return IntPtr.Zero;
        }
        source.AddHook(Hook);
        IsOpen = true;
        try
        {
            // Native menus must be above the WPF search popup, which is topmost by default.
            if (wasTopmost) SetWindowPos(hwnd, new IntPtr(-2), 0, 0, 0, 0, 0x13);
            GetCursorPos(out var point);
            if (keyboard) { var p = owner.PointToScreen(new Point(0, Math.Min(owner.ActualHeight, 28))); point = new() { X = (int)p.X, Y = (int)p.Y }; }
            return TrackPopupMenuEx(Menu, 0x100 | 0x2, point.X, point.Y, hwnd, IntPtr.Zero);
        }
        finally
        {
            if (wasTopmost) SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x13);
            IsOpen = false;
            source.RemoveHook(Hook);
        }
    }
    internal void Invoke(uint id, IntPtr owner)
    {
        if (browserVerbs.TryGetValue(id, out var verb))
        {
            foreach (var path in paths) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true, Verb = verb });
            return;
        }
        var info = new InvokeInfo { Size = Marshal.SizeOf<InvokeInfo>(), Owner = owner, Verb = (IntPtr)(id - First), Show = 1 };
        Marshal.ThrowExceptionForHR(context!.InvokeCommand(ref info));
    }
    private static IntPtr CreateLogo()
    {
        var decoder = new IconBitmapDecoder(new Uri("pack://application:,,,/Assets/PinShelf.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.OrderBy(f => Math.Abs(f.PixelWidth - 16)).First();
        var bitmap = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
        var header = new BitmapInfo { Size = 40, Width = bitmap.PixelWidth, Height = -bitmap.PixelHeight, Planes = 1, BitCount = 32 };
        var handle = CreateDIBSection(IntPtr.Zero, ref header, 0, out var bits, IntPtr.Zero, 0);
        if (handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        bitmap.CopyPixels(new Int32Rect(), bits, bitmap.PixelWidth * bitmap.PixelHeight * 4, bitmap.PixelWidth * 4);
        return handle;
    }
    public void Dispose()
    {
        if (Menu != IntPtr.Zero) { DestroyMenu(Menu); Menu = IntPtr.Zero; }
        if (logo != IntPtr.Zero) { DeleteObject(logo); logo = IntPtr.Zero; }
        if (context != null) { Marshal.ReleaseComObject(context); context = null; }
        if (array != null) { Marshal.ReleaseComObject(array); array = null; }
    }
    [ComImport, Guid("000214e6-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        void ParseDisplayName(IntPtr owner, IntPtr bind, [MarshalAs(UnmanagedType.LPWStr)] string name, ref uint eaten, out IntPtr pidl, ref uint attrs);
        void EnumObjects(IntPtr owner, uint flags, out IntPtr enumerator);
        void BindToObject(IntPtr pidl, IntPtr bind, ref Guid iid, out IntPtr result);
        void BindToStorage(IntPtr pidl, IntPtr bind, ref Guid iid, out IntPtr result);
        [PreserveSig] int CompareIDs(IntPtr param, IntPtr first, IntPtr second);
        void CreateViewObject(IntPtr owner, ref Guid iid, out IntPtr result);
        void GetAttributesOf(uint count, IntPtr[] children, ref uint attributes);
        void GetUIObjectOf(IntPtr owner, uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[] children, ref Guid iid, IntPtr reserved, [MarshalAs(UnmanagedType.Interface)] out object result);
    }
    [ComImport, Guid("000214e4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref InvokeInfo info);
        [PreserveSig] int GetCommandString(UIntPtr id, uint type, IntPtr reserved, IntPtr text, uint count);
    }
    [ComImport, Guid("000214f4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref InvokeInfo info);
        [PreserveSig] int GetCommandString(UIntPtr id, uint type, IntPtr reserved, IntPtr text, uint count);
        [PreserveSig] int HandleMenuMsg(uint msg, IntPtr w, IntPtr l);
    }
    [ComImport, Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref InvokeInfo info);
        [PreserveSig] int GetCommandString(UIntPtr id, uint type, IntPtr reserved, IntPtr text, uint count);
        [PreserveSig] int HandleMenuMsg(uint msg, IntPtr w, IntPtr l);
        [PreserveSig] int HandleMenuMsg2(uint msg, IntPtr w, IntPtr l, out IntPtr result);
    }
    [StructLayout(LayoutKind.Sequential)] private struct InvokeInfo { public int Size, Mask; public IntPtr Owner, Verb, Parameters, Directory; public int Show, HotKey; public IntPtr Icon; }
    [StructLayout(LayoutKind.Sequential)] private struct MenuInfo { public uint Size, Mask, Type, State, Id; public IntPtr Submenu, Checked, Unchecked, Data, Text; public uint Count; public IntPtr Bitmap; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, ImageSize; public int XPels, YPels; public uint Used, Important; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string path, IntPtr bind, out IntPtr pidl, uint mask, out uint attributes);
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] private static extern int SHLoadIndirectString(string source, StringBuilder target, uint count, IntPtr reserved);
    [DllImport("shell32.dll")] private static extern int SHBindToParent(IntPtr pidl, ref Guid iid, out IShellFolder parent, out IntPtr child);
    [DllImport("shell32.dll")] private static extern int SHGetDesktopFolder(out IShellFolder folder);
    [DllImport("shell32.dll")] private static extern IntPtr ILFindLastID(IntPtr pidl);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(IntPtr menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetMenuString(IntPtr menu, uint item, StringBuilder text, int max, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMenuItemInfo(IntPtr menu, uint item, bool position, ref MenuInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetMenuItemInfo(IntPtr menu, uint item, bool position, ref MenuInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool InsertMenu(IntPtr menu, uint position, uint flags, uint id, string? text);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr parameters);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
}
