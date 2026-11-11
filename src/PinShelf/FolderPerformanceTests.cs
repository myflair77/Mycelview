using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace PinShelf;
internal static class FolderPerformanceTests
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> condition)
    { var end = DateTime.UtcNow.AddSeconds(15); while (DateTime.UtcNow < end) { if (condition()) return; await Task.Delay(60); } throw new InvalidOperationException("Folder performance check timed out."); }
    internal static async Task RunAsync(string directory, string skin, int zoom)
    {
        var root = Path.Combine(directory, "folder-scale-tests"); var large = Path.Combine(root, "large"); var small = Path.Combine(root, "small"); Directory.CreateDirectory(large); Directory.CreateDirectory(small);
        await Task.Run(() => { for (int i = 0; i < 3000; i++) File.WriteAllText(Path.Combine(large, $"item-{i}.txt"), "test"); for (int i = 0; i < 12; i++) File.WriteAllText(Path.Combine(small, $"small-{i}.txt"), "test"); });
        var store = new StateStore(root);
        store.Save(new WorkspaceState { Cards = Enumerable.Range(0, 61).Select(i => new CardState { InitialPath = i == 0 ? large : small, CurrentPath = i == 0 ? large : small, Title = $"card-{i}" }).ToList() });
        var vm = new MainViewModel(store); var window = new MainWindow(vm) { TestMode = true, Title = "PinShelf — Scale tests" }; window.Show();
        try
        {
            bool partial = false;
            vm.Cards.CollectionChanged += (_, e) => { if (e.NewItems != null) foreach (CardViewModel c in e.NewItems) if (c.CurrentPath == large) c.Entries.CollectionChanged += (_, _) => { if (c.Entries.Count > 0 && c.Entries.Count < 3000) partial = true; }; };
            await vm.InitializeAsync(); await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
            Check(partial && vm.Folders[0].Entries.Count == 3000, "Large folder delivers preview before full list.");
            Check(FolderListing.PeakReaders <= FolderListing.ConcurrencyLimit, "Metadata readers respect shared concurrency bound.");
            int Views(DependencyObject node) { int count = node is CardView ? 1 : 0; for (int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++) count += Views(VisualTreeHelper.GetChild(node,i)); return count; }
            Check(Views(window) < 15 && vm.Folders[^1].Entries.Count == 0, "Offscreen cards do not retain heavy views or listings.");
            CardView? FindView(DependencyObject node, CardViewModel card)
            { if (node is CardView view && view.DataContext == card) return view; for (int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++) { var result=FindView(VisualTreeHelper.GetChild(node,i),card); if(result!=null)return result; } return null; }
            var first = vm.Folders[0]; var view = FindView(window, first)!;
            first.Note = "preserve";
            var selectedPath = first.Entries[60].FullPath;
            view.FileList.SelectedItem = first.Entries[60];
            var listScroll = CardView.FindChild<ScrollViewer>(view.FileList)!; listScroll.ScrollToVerticalOffset(50); window.UpdateLayout();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            var offset = first.State.ScrollOffset;
            Check(offset > 0 && first.SelectedPaths.Contains(selectedPath), "Selection and scroll captured before virtualization.");
            window.FolderWorkspaceScroll.ScrollToBottom();
            await Until(() => vm.Folders[^1].Entries.Count == 12 && first.Entries.Count == 0);
            Check(Views(window) < 15 && first.State.ScrollOffset == offset, "Scrolling releases old lists while preserving saved offset.");
            File.WriteAllText(Path.Combine(large, "offline-new.txt"), "new");
            window.FolderWorkspaceScroll.ScrollToTop();
            await Until(() => first.Entries.Count == 3001 && FindView(window, first) != null);
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
            view = FindView(window, first)!;
            Check(first.Note == "preserve" && first.State.ScrollOffset == offset && view.FileList.SelectedItems.Cast<FileEntry>().Any(x => x.FullPath == selectedPath), "Returning recreates view with note, scroll, selection and filesystem changes.");
            first.Navigate(large); first.Navigate(small); await first.RefreshAsync();
            Check(first.Entries.Count == 12 && first.Entries.All(x => x.FullPath.StartsWith(small, StringComparison.OrdinalIgnoreCase)), "Rapid navigation rejects stale old-path batches.");
            var switched = vm.CreateWorkspaceAsync("fast tab");
            Check(!vm.WorkspaceBusy, "Tab interaction unlocks before background reads complete.");
            await switched;
            File.WriteAllText(Path.Combine(directory,"folder-performance-results.txt"), "PASS: partial results, bounded readers, offscreen view/list release, viewport reactivation, note/scroll/multi-selection preservation, offscreen filesystem changes, rapid navigation cancellation, responsive tab transition.");
        }
        finally { window.Close(); SkinManager.Apply(skin); TextScale.Apply(zoom); }
    }
}
