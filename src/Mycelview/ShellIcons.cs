using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Mycelview;

public static class ShellIcons
{
    private static readonly ConcurrentDictionary<string, ImageSource> cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, ImageSource> fileIcons = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string> typeNames = new(StringComparer.OrdinalIgnoreCase);
    public static string TypeName(string path, bool folder) => DesktopItems.IsShellPath(path) ? "시스템 항목" : typeNames.GetOrAdd(folder ? "<folder>" : Path.GetExtension(path), extension =>
    {
        var info = new ShellFileInfo();
        SHGetFileInfo(folder ? "folder" : "file" + extension, folder ? 0x10u : 0x80u, ref info,
            (uint)Marshal.SizeOf<ShellFileInfo>(), 0x400u | 0x10u);
        return !string.IsNullOrWhiteSpace(info.TypeName) ? info.TypeName : folder ? "파일 폴더" : extension.Length == 0 ? "파일" : extension.TrimStart('.').ToUpperInvariant() + " 파일";
    });
    private static readonly ConcurrentDictionary<string, ImageSource> sizedIcons = new(StringComparer.OrdinalIgnoreCase);
    internal static ImageSource? Thumbnail(string path,int pixels)
    {
        IShellItemImageFactory? factory=null;IntPtr bitmap=IntPtr.Zero;
        try
        {
            var iid=typeof(IShellItemImageFactory).GUID;
            if(SHCreateItemFromParsingName(path,IntPtr.Zero,ref iid,out factory)<0)return null;
            // THUMBNAILONLY: never mistake the registered file-type icon for content.
            factory.GetImage(new IconSize{Width=pixels,Height=pixels},8,out bitmap);
            var image=Imaging.CreateBitmapSourceFromHBitmap(bitmap,IntPtr.Zero,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();return image;
        }
        catch(COMException){return null;}
        finally{if(bitmap!=IntPtr.Zero)DeleteObject(bitmap);if(factory!=null)Marshal.ReleaseComObject(factory);}
    }
    public static ImageSource ForSize(string path, bool folder, int pixels)
    {
        if (pixels <= 16) return ForPath(path, folder);
        string extension = Path.GetExtension(path).ToLowerInvariant();
        bool individual = DesktopItems.IsShellPath(path) || extension is ".lnk" or ".exe" or ".ico" or ".url" or ".appref-ms";
        string key = pixels + ":" + (individual ? path : folder ? "<folder>" : extension);
        if (sizedIcons.Count >= 256) sizedIcons.Clear();
        return sizedIcons.GetOrAdd(key, _ =>
        {
            IShellItemImageFactory? factory = null;
            IntPtr bitmap = IntPtr.Zero;
            try
            {
                var iid = typeof(IShellItemImageFactory).GUID;
                if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory) >= 0)
                {
                    factory.GetImage(new IconSize { Width=pixels,Height=pixels }, 4, out bitmap);
                    var image=Imaging.CreateBitmapSourceFromHBitmap(bitmap,IntPtr.Zero,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());
                    image.Freeze(); return image;
                }
            }
            catch (COMException) { }
            finally
            {
                if(bitmap!=IntPtr.Zero) DeleteObject(bitmap);
                if(factory!=null) Marshal.ReleaseComObject(factory);
            }
            return ForPath(path,folder);
        });
    }
    [StructLayout(LayoutKind.Sequential)] private struct IconSize { public int Width, Height; }
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory { void GetImage(IconSize size, uint flags, out IntPtr bitmap); }
    [DllImport("shell32.dll", CharSet=CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bind, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr bitmap);
    public static ImageSource ForPath(string path, bool folder)
    {
        string extension = Path.GetExtension(path);
        // Executables, shortcuts and icon files can each carry a different icon.
        bool individual = DesktopItems.IsShellPath(path) || (!folder && extension.ToLowerInvariant() is ".lnk" or ".url" or ".exe" or ".ico" or ".appref-ms");
        if (individual)
        {
            if (fileIcons.Count >= 1024) fileIcons.Clear();
            return fileIcons.GetOrAdd(DesktopItems.IsShellPath(path) ? path : Path.GetFullPath(path), fullPath => Load(fullPath, false, true));
        }
        if (cache.Count >= 512) cache.Clear();
        return cache.GetOrAdd(folder ? "<folder>" : extension, key => Load(key, folder));
    }
    private static ImageSource Load(string pathOrExtension, bool folder, bool individual = false)
    {
        var info = new ShellFileInfo();
        uint flags = 0x100u | 0x1u; // icon, small icon
        if (!individual) flags |= 0x10u; // Fast registered-type lookup for ordinary documents.
        if (DesktopItems.IsShellPath(pathOrExtension))
        {
            IntPtr pidl = IntPtr.Zero;
            try
            {
                if (SHParseDisplayName(pathOrExtension, IntPtr.Zero, out pidl, 0, out _) >= 0 && pidl != IntPtr.Zero)
                    SHGetFileInfo(pidl, 0, ref info, (uint)Marshal.SizeOf<ShellFileInfo>(), flags | 0x8u);
            }
            finally { if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl); }
        }
        else SHGetFileInfo(individual ? pathOrExtension : folder ? "folder" : "file" + pathOrExtension, folder ? 0x10u : 0x80u, ref info,
            (uint)Marshal.SizeOf<ShellFileInfo>(), flags);
        if (individual && info.Icon == IntPtr.Zero) return Load(Path.GetExtension(pathOrExtension), false);
        if (info.Icon != IntPtr.Zero)
        {
            try
            {
                var image = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                image.Freeze(); return image;
            }
            finally { DestroyIcon(info.Icon); }
        }
        var drawing = new GeometryDrawing(folder ? Brushes.Goldenrod : Brushes.WhiteSmoke, new Pen(Brushes.Gray, 1),
            Geometry.Parse(folder ? "M1,4 L6,4 8,6 15,6 15,14 1,14 Z" : "M3,1 L10,1 14,5 14,15 3,15 Z M10,1 L10,5 14,5"));
        var fallback = new DrawingImage(drawing); fallback.Freeze(); return fallback;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr Icon;
        public int Index;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShellFileInfo info, uint size, uint flags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHGetFileInfoW")]
    private static extern IntPtr SHGetFileInfo(IntPtr pidl, uint attributes, ref ShellFileInfo info, uint size, uint flags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr pidl, uint attributes, out uint actualAttributes);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(IntPtr icon);
}
