using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Mycelview;
internal static class ShellIconTests
{
    internal static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        void Icon(string path, byte red, byte green)
        {
            using var writer = new BinaryWriter(File.Create(path));
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)1);
            writer.Write((byte)16); writer.Write((byte)16); writer.Write((byte)0); writer.Write((byte)0);
            writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(40 + 16 * 16 * 4 + 64); writer.Write(22);
            writer.Write(40); writer.Write(16); writer.Write(32); writer.Write((ushort)1); writer.Write((ushort)32);
            writer.Write(0); writer.Write(1024); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
            for (int i = 0; i < 256; i++) { writer.Write((byte)0); writer.Write(green); writer.Write(red); writer.Write((byte)255); }
            writer.Write(new byte[64]);
        }
        string redIcon = Path.Combine(directory, "red.ico"), greenIcon = Path.Combine(directory, "green.ico");
        Icon(redIcon, 255, 0); Icon(greenIcon, 0, 255);
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        string redLink = Path.Combine(directory, "red.lnk"), greenLink = Path.Combine(directory, "green.lnk");
        try
        {
            foreach (var pair in new[] { (redLink, redIcon), (greenLink, greenIcon) })
            {
                dynamic shortcut = shell.CreateShortcut(pair.Item1);
                try { shortcut.TargetPath = Environment.ProcessPath!; shortcut.IconLocation = pair.Item2 + ",0"; shortcut.Save(); }
                finally { Marshal.FinalReleaseComObject(shortcut); }
            }
        }
        finally { Marshal.FinalReleaseComObject(shell); }
        byte[] Pixels(ImageSource image)
        {
            var bitmap = new FormatConvertedBitmap((BitmapSource)image, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); return pixels;
        }
        foreach (var pair in new[] { (redIcon, greenIcon), (redLink, greenLink) })
        {
            var red = ShellIcons.ForPath(pair.Item1, false); var green = ShellIcons.ForPath(pair.Item2, false);
            Check(red.IsFrozen && green.IsFrozen, "Shell icons must be immutable for binding.");
            Check(!Pixels(red).SequenceEqual(Pixels(green)), "Individual file icons must differ even with the same extension.");
            Check(ReferenceEquals(red, ShellIcons.ForPath(pair.Item1, false)), "Individual icons must be cached by full path.");
        }
        Check(ReferenceEquals(ShellIcons.ForPath("first.txt", false), ShellIcons.ForPath("second.txt", false)), "Ordinary document icons must retain type caching.");
        Check(ShellIcons.ForPath(Path.Combine(directory, "missing.lnk"), false) != null, "Broken shortcuts must have a fallback icon.");
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var desktopItems = await FolderListing.ReadAsync(desktopPath, null, CancellationToken.None);
        var virtualItems = desktopItems.Where(item => item.IsShellItem).ToArray();
        Check(virtualItems.Any(item => item.FullPath.Contains("20D04FE0", StringComparison.OrdinalIgnoreCase)), "Desktop must include This PC.");
        Check(virtualItems.Any(item => item.FullPath.Contains("5399E694", StringComparison.OrdinalIgnoreCase) || item.FullPath.Contains("26EE0668", StringComparison.OrdinalIgnoreCase) || item.FullPath.Contains("21EC2020", StringComparison.OrdinalIgnoreCase)), "Desktop must include enabled Control Panel.");
        Check(desktopItems.Select(item => item.FullPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() == desktopItems.Count, "Merged desktop items must not be duplicated.");
        Check(virtualItems.Select(item => DesktopItems.DesktopIdentity(item.FullPath)).Distinct(StringComparer.OrdinalIgnoreCase).Count() == virtualItems.Length, "Desktop aliases must not duplicate system icons.");
        Check(virtualItems.All(item => DesktopItems.IsVisibleOnDesktop(item.FullPath)), "Hidden desktop namespace items must be excluded.");
        foreach (string id in new[] { "20D04FE0-3AEA-1069-A2D8-08002B30309D", "5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0", "645FF040-5081-101B-9F08-00AA002F954E" })
            Check(!Pixels(ShellIcons.ForPath("::{" + id + "}", false)).SequenceEqual(Pixels(ShellIcons.ForPath("unknown.pinshelfunknown", false))), "System icons must differ from a generic document icon: " + id);
        foreach (var item in virtualItems) Check(item.IconImage != null && item.ModifiedDisplay == "" && item.Size == "—", "Virtual items need icons without fabricated file metadata.");
        File.WriteAllText(Path.Combine(directory, "results.txt"), "PASS: individual shortcut icons, system PIDL icons, desktop visibility, alias deduplication, caching and fallback. No programs launched.\n" + string.Join("\n", virtualItems.Select(item => item.Name + " " + item.FullPath)));
    }
}
