using System.Windows;
using System.Windows.Controls;
namespace Mycelview;
public partial class MainWindow
{
    private void ShowFileColumns(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        bool recent = Equals(button.Tag, "Recent");
        var menu = new ContextMenu { PlacementTarget = button };
        for (int i = 1; i <= 5; i++)
        {
            int columns = i;
            var item = new MenuItem { Header = i == 1 ? "1열 (기본값)" : $"{i}열", IsCheckable = true, IsChecked = (recent ? ViewModel.RecentColumns : ViewModel.FavoriteColumns) == i };
            item.Click += (_, _) => { if (recent) ViewModel.RecentColumns = columns; else ViewModel.FavoriteColumns = columns; ViewModel.Save(); };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }
    private void ShowFolderColumns(object sender,RoutedEventArgs e)
    {
        var button=(Button)sender;
        var menu=new ContextMenu { PlacementTarget=button };
        for(int i=1;i<=5;i++)
        {
            int columns=i;
            var item=new MenuItem { Header=i==2?"2열 (기본값)":$"{i}열",IsCheckable=true,IsChecked=ViewModel.FolderColumns==i };
            item.Click+=(_,_)=> { CloseCardEnlargement(); ViewModel.FolderColumns=columns; ViewModel.Save(); };
            menu.Items.Add(item);
        }
        menu.IsOpen=true;
    }
}

