using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Mycelview;

// Explorer's view/sort/refresh verbs belong to its view host, not IContextMenu.
// Implement those against our list; keep filesystem verbs supplied by Windows.
internal static class FolderBackgroundCommands
{
    internal static Dictionary<uint, Func<Task>> Build(NativeShellMenu menu, DataGrid? grid, string directory, MainViewModel vm)
    {
        var actions = new Dictionary<uint, Func<Task>>();
        uint next = 0x7200;
        uint Add(Func<Task> action) { uint id = next++; actions[id] = action; return id; }
        uint Sync(Action action) => Add(() => { action(); return Task.CompletedTask; });
        menu.AddHostCommand(Add(() => vm.FileTasks.UndoAsync()), vm.FileTasks.UndoLabel, vm.FileTasks.CanUndo && !vm.FileTasks.Busy);
        menu.AddHostCommand(Sync(() => Process.Start(new ProcessStartInfo("cmd.exe") { WorkingDirectory = directory, UseShellExecute = true })), "터미널에서 열기");
        menu.AddHostCommand(Add(() => vm.FileTasks.PasteAsync(directory)), "붙여넣기\tCtrl+V", FileTasks.CanPaste && !vm.FileTasks.Busy);
        menu.AddHostCommand(Add(async () =>
        {
            if (grid?.DataContext is CardViewModel card) await card.RefreshAsync();
            else await vm.Explorer.NavigateAsync(directory);
        }), "새로 고침");
        if (grid == null) return actions;
        var view = CollectionViewSource.GetDefaultView(grid.ItemsSource);
        if (view is ListCollectionView list)
        {
            var groups = new List<(uint, string, bool)>();
            foreach (var (label, property) in new[] { ("없음", ""), ("유형", "Type"), ("확장명", "Extension") })
                groups.Add((Sync(() =>
                {
                    using (list.DeferRefresh())
                    {
                        list.GroupDescriptions.Clear();
                        if (property.Length > 0) list.GroupDescriptions.Add(new PropertyGroupDescription(property));
                    }
                }), label, property.Length == 0 ? list.GroupDescriptions.Count == 0 : list.GroupDescriptions.OfType<PropertyGroupDescription>().Any(x => x.PropertyName == property)));
            if (grid.GroupStyle.Count == 0)
            {
                var text = new FrameworkElementFactory(typeof(TextBlock));
                text.SetBinding(TextBlock.TextProperty, new Binding("Name"));
                text.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
                text.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 6, 0, 3));
                grid.GroupStyle.Add(new GroupStyle { HeaderTemplate = new DataTemplate { VisualTree = text } });
                VirtualizingPanel.SetIsVirtualizingWhenGrouping(grid, true);
            }
            if(FolderViewModes.For(grid) is {} groupedModes)
                menu.AddHostSubmenu("분류 방법",FolderGrouping.Modes.Select(mode=>(Sync(()=>groupedModes.SetGrouping(mode.Id)),mode.Label,groupedModes.GroupMode==mode.Id)));
            else menu.AddHostSubmenu("분류 방법",groups);
            var sorts = new List<(uint, string, bool)>();
            foreach (var (label, property) in new[] { ("이름", "Name"), ("수정한 날짜", "Modified"), ("유형", "Type"), ("크기", "Length") })
                foreach (bool descending in new[] { false, true })
                    sorts.Add((Sync(() =>
                    {
                        list.CustomSort = new FileEntryComparer(property, descending);
                        foreach (var column in grid.Columns) column.SortDirection = column.SortMemberPath == property ? descending ? ListSortDirection.Descending : ListSortDirection.Ascending : null;
                        if (grid.DataContext is CardViewModel card) { card.State.SortProperty = property; card.State.SortDescending = descending; FolderViewModes.For(grid)?.ApplyGrouping(); vm.Save(); }
                    }), label + (descending ? " · 내림차순" : " · 오름차순"), grid.Columns.Any(c => c.SortMemberPath == property && c.SortDirection == (descending ? ListSortDirection.Descending : ListSortDirection.Ascending))));
            menu.AddHostSubmenu("정렬 기준", sorts);
        }
        if (FolderViewModes.For(grid) is {} modes) menu.AddHostSubmenu("보기", FolderViewModes.Modes.Select(mode => (Sync(() => modes.Apply(mode.Id)), mode.Label, modes.Mode==mode.Id)));
        menu.AddHostSubmenu(FolderViewModes.For(grid) == null ? "보기" : "표시 정보", grid.Columns.Where(column => column.SortMemberPath != "Name").Select(column =>
            (Sync(() => column.Visibility = column.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible),
             column.Header?.ToString() ?? "열", column.Visibility == Visibility.Visible)).ToArray());
        menu.AddHostSubmenu("표시 옵션", new[]
        {
            (Sync(()=>{FileDisplayPreferences.Current.ShowHidden=!FileDisplayPreferences.Current.ShowHidden;vm.Save();}),"숨김 항목 표시",FileDisplayPreferences.Current.ShowHidden),
            (Sync(()=>{FileDisplayPreferences.Current.ShowExtensions=!FileDisplayPreferences.Current.ShowExtensions;vm.Save();}),"파일 확장명 표시",FileDisplayPreferences.Current.ShowExtensions)
        });
        return actions;
    }
}
