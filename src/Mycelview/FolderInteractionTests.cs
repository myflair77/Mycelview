using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Mycelview;
internal static class FolderInteractionTests
{
    internal static async Task RunAsync(string output)
    {
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        Directory.CreateDirectory(output);
        ContextMenuTests.Run(output);
        using var source = new HwndSource(new HwndSourceParameters("Hidden folder interaction test") { Width = 600, Height = 300, WindowStyle = 0 });
        using var vm = new MainViewModel(new StateStore(Path.Combine(output, "settings")));
        var grid = new DataGrid { Width = 580, Height = 250, AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, SelectionMode = DataGridSelectionMode.Extended, RowHeight = 26, GridLinesVisibility = DataGridGridLinesVisibility.None };
        grid.SetResourceReference(DataGrid.RowStyleProperty, typeof(DataGridRow));
        var root = new System.Windows.Documents.AdornerDecorator { Child = grid };
        source.RootVisual = root;
        grid.Columns.Add(new DataGridTextColumn { Header = "이름", Binding = new Binding("Name"), SortMemberPath = "Name", Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "크기", Binding = new Binding("Size"), SortMemberPath = "Length" });
        grid.ItemsSource = new[] { new FileEntry(Path.Combine(output,"b.txt"),"b.txt",false,DateTime.Now,20), new FileEntry(Path.Combine(output,"a.txt"),"a.txt",false,DateTime.Now,10) };
        root.Measure(new Size(600,300)); root.Arrange(new Rect(0,0,600,300)); root.UpdateLayout();
        grid.SelectedItem = grid.Items[0];
        grid.ScrollIntoView(grid.SelectedItem); root.Measure(new Size(600,300)); root.Arrange(new Rect(0,0,600,300)); root.UpdateLayout();
        var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
        Check(row != null && row.BorderThickness.Left == 1 && row.BorderBrush is SolidColorBrush brush && brush.Color == Color.FromRgb(0,120,215), $"Explorer-like selected row border: row={row != null}, selected={row?.IsSelected}, thickness={row?.BorderThickness}, brush={row?.BorderBrush}");
        using(var menu = new NativeShellMenu(output, source.Handle))
        {
            menu.AddBackgroundItems();
            var actions = FolderBackgroundCommands.Build(menu, grid, output, vm);
            var entries = menu.Entries();
            Check(entries.Any(x=>x.Text=="보기") && entries.Any(x=>x.Text=="정렬 기준") && entries.Any(x=>x.Text=="분류 방법"),"Host view menus");
            Check(entries.Any(x=>x.Id==NativeShellMenu.AddFolder) && entries.Any(x=>x.Verb=="New") && entries.Any(x=>x.Verb=="properties"),"Native New/properties and Mycelview menu");
            await actions[0x7207]();
            Check(((FileEntry)grid.Items[0]).Name == "a.txt", "Name ascending command");
            await actions[0x7208]();
            Check(((FileEntry)grid.Items[0]).Name == "b.txt", "Name descending command");
            await actions[0x7205]();
            Check(CollectionViewSource.GetDefaultView(grid.ItemsSource).Groups?.Count > 0, "Group command");
            await actions[0x7204]();
            Check(CollectionViewSource.GetDefaultView(grid.ItemsSource).GroupDescriptions.Count == 0, "Clear grouping command");
            // The view's last commands toggle non-name columns.
            actions[0x720f]().GetAwaiter().GetResult();
            Check(grid.Columns[1].Visibility==Visibility.Collapsed,"View command affects the actual grid");
            actions[0x720f]().GetAwaiter().GetResult();
            File.WriteAllLines(Path.Combine(output,"menu.txt"),entries.Select(x=>$"{x.Id}: {x.Text} [{x.Verb}]"));
        }
        Check(FileMarqueeSelection.Intersections(new Rect(new Point(100,99),new Point(10,27)),26,26,500,20).SequenceEqual(new[]{0,1,2}),"Reverse rectangle selection");
        Check(FileMarqueeSelection.Intersections(new Rect(10,27,90,72),-234,26,500,50).SequenceEqual(new[]{10,11,12}),"Scrolled rectangle selection");
        Check(!FileMarqueeSelection.Intersections(new Rect(510,27,10,72),26,26,500,20).Any(),"Outside list rectangle");
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
        root.Measure(new Size(600,300)); root.Arrange(new Rect(0,0,600,300)); root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(600,300,96,96,PixelFormats.Pbgra32); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using(var file=File.Create(Path.Combine(output,"selection.png"))) encoder.Save(file);
        File.WriteAllText(Path.Combine(output,"results.txt"),"PASS: selected row border; Windows background menu creation; host view commands; rectangle selection/reverse/scroll bounds. Hidden WPF render only, no computer use.");
    }
}
