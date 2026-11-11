using System.Collections;
using System.IO;
using System.Windows;
using System.Windows.Interop;

namespace PinShelf;
internal static class ContextMenuTests
{
    internal static void Run(string directory)
    {
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        var root = Path.Combine(directory, "context-menu-fixtures"); Directory.CreateDirectory(root);
        var folder = Path.Combine(root, "하위 폴더"); Directory.CreateDirectory(folder);
        var first = Path.Combine(root, "첫 파일.txt"); var second = Path.Combine(folder, "다음 파일.txt");
        File.WriteAllText(first, "first"); File.WriteAllText(second, "second");
        using var owner = new HwndSource(new HwndSourceParameters("PinShelf hidden shell test") { Width = 0, Height = 0, WindowStyle = 0 });
        var log = new List<string>();
        foreach (var paths in new[] { new[] { first }, new[] { folder }, new[] { first, second }, new[] { folder, first } })
        {
            using var menu = new NativeShellMenu(paths, owner.Handle);
            var before = menu.Entries();
            Check(before.Count > 0 && before.Any(x => x.Verb == "copy"), "Shell must return real commands.");
            bool files = paths.All(File.Exists);
            menu.AddPinShelfItems(files, paths.Length == 1, false, false);
            var after = menu.Entries();
            Check(before.Where(x => x.Text.Length > 0).SequenceEqual(after.Where(x => x.Text.Length > 0 && (x.Id <= NativeShellMenu.Last || x.Id >= 0x7100))),
                "Shell command labels, order, IDs and icon handles must remain unchanged.");
            var custom = after.Where(x => x.Id >= NativeShellMenu.Preview && x.Id <= NativeShellMenu.AddFile).ToArray();
            Check(custom.Length == (files ? 4 : 3) + (paths.Any(Directory.Exists) ? 1 : 0) + (paths.Any(File.Exists) ? 1 : 0) && custom.All(x => x.Bitmap != IntPtr.Zero), "Every custom command needs the PinShelf bitmap.");
            Check(after.Any(x => x.Id == NativeShellMenu.Preview) == files, "Preview must only appear for files.");
            Check(after.Any(x => x.Id == NativeShellMenu.AddFolder) == paths.Any(Directory.Exists), "Folder registration must match selected types.");
            Check(after.Any(x => x.Id == NativeShellMenu.AddFile) == paths.Any(File.Exists), "Favorite registration must match selected types.");
            Check(after.Where(x => x.Id is NativeShellMenu.AddFolder or NativeShellMenu.AddFile).All(x => after.IndexOf(x) < after.FindIndex(e => e.Id == NativeShellMenu.Reveal)), "Registration belongs beside open/preview, before reveal.");
            int cut = after.FindIndex(x => x.Verb == "cut"), undo = after.FindIndex(x => x.Id == NativeShellMenu.Undo);
            Check(cut < 0 || undo + 1 == cut, "Undo must precede cut.");
            int properties = after.FindIndex(x => x.Verb == "properties"), unregister = after.FindIndex(x => x.Id == NativeShellMenu.Unregister);
            Check(properties < 0 || unregister > properties, "Unregister must follow properties.");
            log.Add("PASS: " + string.Join(", ", paths.Select(Path.GetFileName)));
            log.AddRange(after.Select(x => $"  {x.Id}: {x.Text} [{x.Verb}] bitmap={x.Bitmap != IntPtr.Zero}"));
        }
        var selection = new ArrayList { first, second }; FileContextMenus.SelectForContext(selection, first);
        Check(selection.Count == 2, "Right-click must retain multiselection.");
        FileContextMenus.SelectForContext(selection, folder);
        Check(selection.Count == 1, "Unselected target must replace selection.");
        var card = new CardView();
        Check(card.NewFolderButton.Width == 20 && card.NewFolderButton.Height == 22 && card.NewFolderButton.Margin.Left == 2.5,
            "New folder button must match existing dimensions and spacing.");
        Check(File.ReadAllText(first) == "first" && File.ReadAllText(second) == "second", "Fixtures must remain intact.");
        log.Add("PASS: native menu labels/order/icons, custom logos/positions, file/folder/mixed/cross-directory selection and new-folder button dimensions. No menu or window displayed.");
        File.WriteAllLines(Path.Combine(directory, "context-menu-results.txt"), log);
    }
}
