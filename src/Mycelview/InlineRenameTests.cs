using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Mycelview;
internal static class InlineRenameTests
{
    internal static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);string folder=Path.Combine(directory,"files");Directory.CreateDirectory(folder);string file=Path.Combine(folder,"original.txt");File.WriteAllText(file,"keep content");
        var store=new StateStore(directory);store.Save(new WorkspaceState {Cards=[new CardState {InitialPath=folder,CurrentPath=folder,Title="test"}]});
        using var vm=new MainViewModel(store);var window=new MainWindow(vm){TestMode=true,Opacity=0,ShowActivated=false,ShowInTaskbar=false};window.Show();
        try
        {
            await vm.InitializeAsync();window.UpdateLayout();await Task.Delay(100);
            var view=CardView.FindChild<CardView>(window)!;var entry=vm.Folders[0].Entries.Single();view.FileList.SelectedItem=entry;window.UpdateLayout();
            var row=(DataGridRow)view.FileList.ItemContainerGenerator.ContainerFromItem(entry);var cell=CardView.FindChild<DataGridCell>(row)!;
            if(((SolidColorBrush)cell.Foreground).Color!=((SolidColorBrush)Application.Current.Resources["TextBrush"]).Color)throw new Exception("Selected text must keep theme text color.");
            view.BeginInlineRename(entry);var editor=CardView.FindChild<TextBox>(row)!;if(editor.Visibility!=Visibility.Visible || !view.IsRenaming)throw new Exception("Inline editor must open in selected name cell.");
            editor.Text="cancelled.txt";
            editor.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),0,Key.Escape){RoutedEvent=Keyboard.KeyDownEvent});
            if(view.IsRenaming || !File.Exists(file) || File.Exists(Path.Combine(folder,"cancelled.txt")))throw new Exception("Escape must cancel without changing source.");
            view.BeginInlineRename(entry);editor.Text="renamed.txt";
            editor.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),0,Key.Enter){RoutedEvent=Keyboard.KeyDownEvent});
            var end=DateTime.UtcNow.AddSeconds(20);while(vm.FileTasks.Busy && DateTime.UtcNow<end)await Task.Delay(50);
            if(!File.Exists(Path.Combine(folder,"renamed.txt")) || File.Exists(file) || !vm.FileTasks.CanUndo)throw new Exception("Enter must rename through undoable file operations.");
            await vm.FileTasks.UndoAsync();if(!File.Exists(file)||File.ReadAllText(file)!="keep content")throw new Exception("Undo must restore original file.");
            File.WriteAllText(Path.Combine(directory,"results.txt"),"PASS: selected text contrast, inline editing, Escape cancellation, Enter real rename, undo and source content preservation. Window transparent, not activated or shown in taskbar.");
        }
        finally {window.Close();}
    }
}
