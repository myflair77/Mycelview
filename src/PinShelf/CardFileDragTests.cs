using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace PinShelf;
internal static class CardFileDragTests
{
    internal static async Task RunAsync(MainWindow window, string directory)
    {
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        var root = Path.Combine(directory, "card-file-drag-fixtures");
        var source = Path.Combine(root, "source"); var destination = Path.Combine(root, "destination"); var nested = Path.Combine(source, "nested");
        Directory.CreateDirectory(nested); Directory.CreateDirectory(destination);
        var a = Path.Combine(source, "a.txt"); var b = Path.Combine(source, "b.txt");
        File.WriteAllText(a, "drag-a"); File.WriteAllText(b, "drag-b"); File.WriteAllText(Path.Combine(nested, "leaf.txt"), "leaf");
        var largeSelection = Enumerable.Range(0, 5000).Select(i => Path.Combine(source, $"batch-{i}.txt")).ToArray();
        var preparation = System.Diagnostics.Stopwatch.StartNew();
        Check(FileOperationRules.TopLevel(largeSelection).Length == 5000, "Large sibling selection must be preserved.");
        Check(new CardFileDrag(Guid.NewGuid(), largeSelection.Select(p => new CardDragEntry(p, false))).Entries.Count == 5000, "Large drag payload must retain every sibling.");
        Check(preparation.Elapsed < TimeSpan.FromSeconds(10), "5000-item preparation must not perform quadratic filesystem probes.");
        Check(FileOperationRules.TopLevel([nested, Path.Combine(nested, "leaf.txt"), a]).SequenceEqual([nested, a]), "Selected ancestors must suppress only descendants.");
        File.WriteAllText(Path.Combine(directory, "large-selection-preparation.txt"), $"5000 items: {preparation.Elapsed.TotalMilliseconds:F0} ms");
        var vm = window.ViewModel; var originalIds = vm.Cards.Select(x => x.State.Id).ToHashSet();
        try
        {
            vm.AddPath(source); var sourceCard = vm.Cards.Last();
            vm.AddPath(destination); var destinationCard = vm.Cards.Last();
            vm.AddPath(a); var favorite = vm.Cards.Last();
            await sourceCard.RefreshAsync(); await destinationCard.RefreshAsync(); window.UpdateLayout();
            var container = (DependencyObject)window.FolderCards.ItemContainerGenerator.ContainerFromItem(destinationCard);
            ((FrameworkElement)container).BringIntoView();
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), System.Windows.Threading.DispatcherPriority.ContextIdle);
            var view = CardView.FindChild<CardView>(container) ?? throw new InvalidOperationException("Destination card must be realized in viewport.");
            CardFileDrag Payload() => new(sourceCard.State.Id, sourceCard.Entries.Select(x => new CardDragEntry(x.FullPath, x.IsDirectory)));
            var payload = Payload();
            var data = payload.ToData();
            var effects = DragDropEffects.Move | DragDropEffects.Copy;
            Check(!data.GetDataPresent(DataFormats.FileDrop), "Internal transfer payload must not become a registration/external move.");
            Check(payload.Effect(destinationCard.State.Id, destination, DragDropKeyStates.None, effects, false) == DragDropEffects.Move, "Normal drag must move.");
            Check(payload.Effect(destinationCard.State.Id, destination, DragDropKeyStates.ControlKey, effects, false) == DragDropEffects.Copy, "Ctrl drag must copy.");
            Check(payload.Effect(sourceCard.State.Id, source, DragDropKeyStates.None, effects, false) == DragDropEffects.None, "Same-card drop must be rejected.");
            Check(payload.Effect(destinationCard.State.Id, destination, DragDropKeyStates.None, effects, true) == DragDropEffects.None, "Concurrent transfer must be rejected.");
            Check(payload.Effect(destinationCard.State.Id, nested, DragDropKeyStates.ControlKey, effects, false) == DragDropEffects.None, "Cannot copy a selected folder into itself.");
            var count = vm.Cards.Count; window.RegisterDroppedPaths(data); Check(vm.Cards.Count == count, "Internal drag must not create favorite registrations.");
            DragEventArgs Event(CardFileDrag item, DragDropKeyStates keys, RoutedEvent routed) {
                var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, new object[] { item.ToData(), keys, effects, view, new Point(10, 10) }, null)!;
                args.RoutedEvent = routed; return args;
            }
            var over = Event(payload, DragDropKeyStates.ControlKey, DragDrop.PreviewDragOverEvent); view.RaiseEvent(over);
            Check(over.Handled && over.Effects == DragDropEffects.Copy && view.TransferOverlay.Visibility == Visibility.Visible, "Routed hover must show copy feedback.");
            var drop = Event(payload, DragDropKeyStates.ControlKey, DragDrop.PreviewDropEvent); view.RaiseEvent(drop);
            Check(drop.Handled && drop.Effects == DragDropEffects.Copy && payload.Accepted?.Destination == destination, "Routed drop must snapshot the target folder.");
            Check(!File.Exists(Path.Combine(destination, "a.txt")), "Drop must defer filesystem work until native drag exits.");
            var result = await payload.CompleteAsync(drop.Effects, vm.FileTasks);
            Check(result?.Completed.Count == 3 && File.Exists(a) && File.Exists(b) && File.ReadAllText(Path.Combine(destination, "a.txt")) == "drag-a" && File.Exists(Path.Combine(destination, "nested", "leaf.txt")), "Ctrl drop must copy selected files and folders through the real task pipeline.");
            Check(await payload.CompleteAsync(drop.Effects, vm.FileTasks) == null, "Drop can only execute once.");
            var inside = Path.Combine(destination, "current-folder"); Directory.CreateDirectory(inside);
            destinationCard.Navigate(inside); await destinationCard.RefreshAsync(); await sourceCard.RefreshAsync(); window.UpdateLayout();
            payload = new(sourceCard.State.Id, sourceCard.Entries.Where(x => !x.IsDirectory).Select(x => new CardDragEntry(x.FullPath, false)));
            drop = Event(payload, DragDropKeyStates.None, DragDrop.PreviewDropEvent); view.RaiseEvent(drop);
            result = await payload.CompleteAsync(drop.Effects, vm.FileTasks);
            Check(result?.Completed.Count == 2 && !File.Exists(a) && !File.Exists(b) && File.Exists(Path.Combine(inside, "a.txt")), "Normal drop must move both files into the card current path.");
            Check(favorite.CurrentPath == Path.Combine(inside, "a.txt"), "Moved file registrations must follow the real destination.");
            payload = new(sourceCard.State.Id, [new(nested, true)]); drop = Event(payload, DragDropKeyStates.None, DragDrop.PreviewDropEvent); view.RaiseEvent(drop);
            Check(await payload.CompleteAsync(DragDropEffects.None, vm.FileTasks) == null && Directory.Exists(nested) && !Directory.Exists(Path.Combine(inside, "nested")), "Cancelled native drag must perform no filesystem changes.");
            var duplicate = new CardFileDrag(sourceCard.State.Id, [new(nested, true), new(Path.Combine(nested, "leaf.txt"), false)]);
            Check(duplicate.Entries.Count == 1, "Parent-child selection must not transfer children twice.");
            window.ClearFileTransferHints(); Check(view.TransferOverlay.Visibility == Visibility.Collapsed, "Feedback must clear after drag.");
            File.WriteAllText(Path.Combine(directory, "card-file-drag-results.txt"), "PASS: private transfer data, Move/Ctrl-Copy mode, busy/same-card/self-descendant rejection, actual WPF hover/drop routing, deferred one-time execution, real batch file+folder copy, current-folder batch move, registration relocation and cancelled drag. Existing file-operation tests cover conflicts and mid-operation cancellation. No physical OS mouse input was injected.\n");
        }
        finally
        {
            window.ClearFileTransferHints();
            foreach (var card in vm.Cards.Where(x => !originalIds.Contains(x.State.Id)).ToArray()) vm.Remove(card);
            vm.FileTasks.Dismiss.Execute(null); window.FolderWorkspaceScroll.ScrollToTop(); window.UpdateLayout();
        }
    }
}


