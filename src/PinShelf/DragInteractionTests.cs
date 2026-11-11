using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PinShelf;
internal static class DragInteractionTests
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static Point Center(ItemsControl control, int index, MainWindow window)
    {
        var child = (FrameworkElement)control.ItemContainerGenerator.ContainerFromIndex(index);
        var rect = LayoutInformation.GetLayoutSlot(child);
        return ((UIElement)VisualTreeHelper.GetParent(child)).TranslatePoint(new Point(rect.Left + rect.Width / 2, rect.Top + Math.Min(45, rect.Height / 2)), window);
    }
    private static void Capture(MainWindow window, string path)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); png.Save(stream);
    }
    internal static async Task RunAsync(MainWindow window, string directory)
    {
        foreach (var control in new ItemsControl[] { window.FolderCards, window.FavoritesList })
        {
            await Task.Delay(250); window.UpdateLayout();
            var items = control == window.FolderCards ? window.ViewModel.Folders : window.ViewModel.Favorites;
            var original = items.Select(x => x.State.Id).ToArray();
            var source = items[0]; var a = Center(control, 0, window); var b = Center(control, 1, window);
            int moves = 0;
            NotifyCollectionChangedEventHandler changed = (_, e) => { if (e.Action == NotifyCollectionChangedAction.Move) moves++; };
            items.CollectionChanged += changed;
            var session = new ReorderDragSession(window, control, source, a);
            Check(session.Move(b) && session.TargetIndex == 1, "Pointer must select the second stable slot.");
            await Task.Delay(70);
            Capture(window, Path.Combine(directory, source.IsFolder ? "folder-drag-preview.png" : "favorite-drag-preview.png"));
            for (int i = 0; i < 15; i++) session.Move(b);
            Check(session.PreviewChanges == 1, "Stationary pointer must not cause oscillation while cards animate.");
            Check(moves == 0 && items.Select(x => x.State.Id).SequenceEqual(original), "Preview must not mutate or regenerate the collection.");
            session.Move(a);
            Check(session.TargetIndex == 0 && session.PreviewChanges == 2, "Reverse direction must work immediately without a timer or target lock.");
            session.Move(b);
            Check(session.TargetIndex == 1, "Repeated forward direction must work in the same gesture.");
            Check(!session.Move(new Point(-20, -20)), "A point outside the section cannot accept a drop.");
            session.Finish(false);
            Check(moves == 0 && source.DragOpacity == 1, "Cancel must restore visuals without changing saved order.");
            await Task.Delay(220); window.UpdateLayout();
            session = new ReorderDragSession(window, control, source, a);
            session.Move(b); await Task.Delay(60); session.Finish(true);
            Check(moves == 1 && items[1] == source, "Release must perform exactly one order update.");
            items.CollectionChanged -= changed;
            window.ViewModel.Reorder(source.State.Id, items[0].State.Id, false);
            window.UpdateLayout(); await Task.Delay(220);
            Check(items.Select(x => x.State.Id).SequenceEqual(original), "Test must restore the original item order.");
        }
        File.WriteAllText(Path.Combine(directory, "drag-results.txt"), "PASS: folder and favorite drag controllers: stable-slot preview, stationary pointer, immediate reversal, zero collection mutations during preview, outside cancellation, one move on release.\nThese are in-process pointer-coordinate tests, not OS mouse input.\n");
    }
}
