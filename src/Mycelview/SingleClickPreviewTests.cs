using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
namespace Mycelview;
internal static class SingleClickPreviewTests
{
    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);
        void Check(bool value,string message){if(!value)throw new Exception(message);}
        var store=new StateStore(Path.Combine(output,"settings"));
        using var vm=new MainViewModel(store);
        var menus=new FileContextMenus(vm);
        int opened=0;string? last=null;
        menus.PreviewPath=path=>{opened++;last=path;};
        menus.PreviewIsOpen=()=>false;
        var file=new FileEntry(Path.Combine(output,"file.txt"),"file.txt",false,DateTime.Now,0);
        using var source=new HwndSource(new HwndSourceParameters("Hidden preview click test"){Width=400,Height=200,WindowStyle=0});
        var grid=new DataGrid {ItemsSource=new[]{file},AutoGenerateColumns=true,CanUserAddRows=false,IsReadOnly=true,Width=400,Height=200};
        source.RootVisual=grid;menus.AttachGrid(grid);
        grid.Measure(new Size(400,200));grid.Arrange(new Rect(0,0,400,200));grid.UpdateLayout();
        grid.SelectedItem=file;
        var row=(DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
        void Click(UIElement item)
        {
            item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseDownEvent});
            item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=Mouse.MouseUpEvent});
        }
        Click(row);Check(opened==0,"OFF click must only select");
        var space=new KeyEventArgs(Keyboard.PrimaryDevice,source,0,Key.Space){RoutedEvent=Keyboard.PreviewKeyDownEvent};
        row.RaiseEvent(space);Check(opened==1&&space.Handled,"OFF Space opens preview");
        vm.SingleClickPreview=true;
        Click(row);Check(opened==2&&last==file.FullPath,"ON routed click opens preview");
        Click(row);Check(opened==3,"Already selected file can reopen preview");
        menus.PreviewSingleClick(new(output,true));Check(opened==3,"Folders do not open preview");
        var list=new ListBox {ItemsSource=new[]{file},Width=400,Height=200};
        source.RootVisual=list;menus.AttachList(list,item=>new(((FileEntry)item).FullPath,false));
        list.Measure(new Size(400,200));list.Arrange(new Rect(0,0,400,200));list.UpdateLayout();list.SelectedItem=file;
        var listItem=(ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
        Click(listItem);Check(opened==4,"ON icon/favorite list click opens preview");
        vm.SingleClickPreview=false;Click(listItem);Check(opened==4,"OFF list click does not open");
        vm.SingleClickPreview=true;vm.Save();Check(store.Load().SingleClickPreview,"Preview toggle persists");
        File.WriteAllText(Path.Combine(output,"results.txt"),"PASS: routed grid/list clicks ON/OFF, Space while OFF, re-click, folder exclusion and setting persistence. No computer use.");
    }
}
