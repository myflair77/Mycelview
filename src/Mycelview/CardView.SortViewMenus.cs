using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Mycelview;
public partial class CardView
{
    private void OpenSortMenu(object sender, RoutedEventArgs e) => OpenHeaderMenu((Button)sender, BuildSortMenu());
    private void OpenViewMenu(object sender, RoutedEventArgs e) => OpenHeaderMenu((Button)sender, BuildViewMenu());
    private static void OpenHeaderMenu(Button button, ContextMenu menu)
    {
        button.ContextMenu = menu;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }
    internal ContextMenu BuildSortMenu()
    {
        var menu = new ContextMenu();
        void Add(string label, string property, bool descending, bool selected)
        {
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = selected };
            item.Click += (_, _) =>
            {
                Card.State.SortProperty = property;
                Card.State.SortDescending = descending;
                PrepareListing(); RestoreList(); Workspace.Save();
            };
            menu.Items.Add(item);
        }
        foreach (var (label, property) in new[] { ("이름", "Name"), ("수정한 날짜", "Modified"), ("만든 날짜", "Created"), ("유형", "Type"), ("크기", "Length"), ("확장명", "Extension") })
            Add(label, property, Card.State.SortDescending, Card.State.SortProperty == property);
        menu.Items.Add(new Separator());
        Add("오름차순", Card.State.SortProperty, false, !Card.State.SortDescending);
        Add("내림차순", Card.State.SortProperty, true, Card.State.SortDescending);
        menu.Items.Add(new Separator());
        var grouping=new MenuItem{Header="분류 방법"};
        foreach(var mode in FolderGrouping.Modes)
        {
            var item=new MenuItem{Header=mode.Label,IsCheckable=true,IsChecked=Card.State.GroupMode==mode.Id};
            item.Click+=(_,_)=>FolderViewModes.For(FileList)?.SetGrouping(mode.Id);
            grouping.Items.Add(item);
        }
        menu.Items.Add(grouping);
        return menu;
    }
    internal ContextMenu BuildViewMenu()
    {
        var menu = new ContextMenu();
        if (FolderViewModes.For(FileList) is not {} views) return menu;
        foreach (var mode in FolderViewModes.Modes)
        {
            var item = new MenuItem { Header = mode.Label, IsCheckable = true, IsChecked = views.Mode == mode.Id };
            item.Click += (_, _) => views.Apply(mode.Id);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(FileDisplayPreferences.HiddenMenu(() => Workspace.Save()));
        menu.Items.Add(FileDisplayPreferences.ExtensionMenu(() => Workspace.Save()));
        return menu;
    }
}
