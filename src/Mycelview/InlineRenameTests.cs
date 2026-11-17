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
        using var vm=new MainViewModel(store);var window=new MainWindow(vm){TestMode=true,Opacity=0,ShowActivated=false,ShowInTaskbar=false};Application.Current.MainWindow=window;window.Show();
        try
        {
            await vm.InitializeAsync();window.UpdateLayout();await Task.Delay(350);window.UpdateLayout();
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
            foreach(var enlarged in new[]{false,true})
            foreach(var mode in new[]{"Large","Tiles"})
            {
                await vm.Folders[0].RefreshAsync();
                var modes=FolderViewModes.For(view.FileList)!;modes.Apply(mode,false);
                if(enlarged)window.ToggleCardEnlargement(view);
                window.UpdateLayout();await Task.Delay(350);window.UpdateLayout();
                entry=vm.Folders[0].Entries.Single();modes.Icons.SelectedItem=entry;window.UpdateLayout();
                void F2()=>modes.Icons.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),0,Key.F2){RoutedEvent=Keyboard.PreviewKeyDownEvent});
                F2();
                var item=(ListBoxItem)modes.Icons.ItemContainerGenerator.ContainerFromItem(entry);
                editor=CardView.FindChild<TextBox>(item)!;
                if(!view.IsRenaming||editor.Visibility!=Visibility.Visible||editor.SelectedText!="original")throw new Exception($"F2 {mode} enlarged={enlarged} active={view.IsRenaming} visible={editor.Visibility} selected={editor.SelectedText} list={modes.Icons.SelectedItems.Count} grid={view.FileList.SelectedItems.Count}");
                editor.Text="cancelled.txt";
                editor.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),0,Key.Escape){RoutedEvent=Keyboard.KeyDownEvent});
                if(view.IsRenaming||!File.Exists(file))throw new Exception("Icon Escape");
                F2();editor.Text="icon-renamed.txt";
                editor.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),0,Key.Enter){RoutedEvent=Keyboard.KeyDownEvent});
                end=DateTime.UtcNow.AddSeconds(15);while(vm.FileTasks.Busy&&DateTime.UtcNow<end)await Task.Delay(25);
                if(!File.Exists(Path.Combine(folder,"icon-renamed.txt"))||File.Exists(file))throw new Exception("Icon rename "+mode);
                await vm.FileTasks.UndoAsync();
                if(!File.Exists(file)||File.ReadAllText(file)!="keep content")throw new Exception("Icon undo");
                if(enlarged){window.ToggleCardEnlargement(view);window.UpdateLayout();}
            }
            foreach(var expanded in new[]{false,true})
            {
                var model=vm.Folders[0];model.Navigate(folder);await model.RefreshAsync();
                var modes=FolderViewModes.For(view.FileList)!;modes.Apply("Small",false);
                if(expanded)window.ToggleCardEnlargement(view);
                window.UpdateLayout();await Task.Delay(350);window.UpdateLayout();
                entry=model.Entries.Single();modes.Icons.SelectedItem=entry;
                if(modes.ChangeIconSize(120,ModifierKeys.None,modes.Icons)||modes.Mode!="Small")throw new Exception("Plain wheel must scroll");
                modes.ChangeIconSize(60,ModifierKeys.Control,modes.Icons);
                if(modes.Mode!="Small")throw new Exception("Partial wheel accumulation");
                modes.ChangeIconSize(60,ModifierKeys.Control,modes.Icons);
                if(modes.Mode!="Medium")throw new Exception("Ctrl wheel enlargement");
                modes.ChangeIconSize(240,ModifierKeys.Control,modes.Icons);
                if(modes.Mode!="ExtraLarge"||!modes.Icons.SelectedItems.Contains(entry))throw new Exception("Size and selection");
                modes.ChangeIconSize(120,ModifierKeys.Control,modes.Icons);
                if(modes.Mode!="ExtraLarge")throw new Exception("Upper size bound");
                modes.ChangeIconSize(-360,ModifierKeys.Control,modes.Icons);
                if(modes.Mode!="Small"||model.State.ViewMode!="Small")throw new Exception("Shrink and persist mode");
                foreach(var expected in new[]{"List","Details"})
                {
                    modes.ChangeIconSize(-120,ModifierKeys.Control,modes.Icons);
                    if(modes.Mode!=expected)throw new Exception("Full wheel sequence "+expected);
                }
                modes.ChangeIconSize(-120,ModifierKeys.Control,modes.Icons);
                if(modes.Mode!="Details")throw new Exception("Lower view bound");
                foreach(var expected in new[]{"List","Small","Medium","Large","ExtraLarge"})
                {
                    modes.ChangeIconSize(120,ModifierKeys.Control,modes.Icons);
                    if(modes.Mode!=expected)throw new Exception("Reverse wheel sequence "+expected);
                }
                modes.Apply("Small",false);
                window.UpdateLayout();view.BeginInlineRename(entry);
                var container=(ListBoxItem)modes.Icons.ItemContainerGenerator.ContainerFromItem(entry);
                var edit=CardView.FindChild<TextBox>(container)!;
                if(modes.ChangeIconSize(120,ModifierKeys.Control,edit))throw new Exception("Editing wheel must not rebuild editor");
                var backEdit=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),0,Key.Back){RoutedEvent=Keyboard.PreviewKeyDownEvent};
                edit.RaiseEvent(backEdit);
                if(model.CurrentPath!=folder||backEdit.Handled)throw new Exception("Backspace in editor must not navigate");
                edit.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),0,Key.Escape){RoutedEvent=Keyboard.KeyDownEvent});
                foreach(var mode in new[]{"Large","Details"})
                {
                    model.Navigate(folder);await model.RefreshAsync();modes.Apply(mode,false);window.UpdateLayout();
                    UIElement input=mode=="Details"?view.FileList:modes.Icons;
                    var backKey=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),0,Key.Back){RoutedEvent=Keyboard.PreviewKeyDownEvent};
                    input.RaiseEvent(backKey);
                    if(!backKey.Handled||model.CurrentPath!=directory)throw new Exception("Backspace parent navigation "+mode);
                }
                if(expanded){window.ToggleCardEnlargement(view);window.UpdateLayout();}
            }
            window.Width=1600;window.Height=950;window.UpdateLayout();
            var originalParent=window.FavoritesSection.Parent;
            var originalWidth=window.FavoritesColumn.Width;
            var originalRatio=window.FavoritesRow.Height;
            foreach(var mode in new[]{"A","B","C"})
            foreach(var fixedSize in new[]{true,false})
            {
                vm.SetEnlargement(new CardEnlargementSettings{Mode=mode,FixedSize=fixedSize,Width=700,Height=600,Percent=180});
                window.OpenFileSectionEnlargement(false);window.UpdateLayout();
                if(mode=="C")
                {
                    if(!ReferenceEquals(window.FavoritesSection.Parent,originalParent))throw new Exception("Disabled favorite enlargement");
                    continue;
                }
                if(window.FavoritesSection.Parent is not Border frame)throw new Exception("Favorite enlarged frame");
                if(mode=="A"&&frame.Parent is not Canvas)throw new Exception("Floating favorite mode");
                if(mode=="B"&&(frame.Parent is not Grid||window.FavoritesColumn.ActualWidth<=originalWidth.Value))throw new Exception("Pushing favorite mode");
                if(frame.ActualWidth<400)throw new Exception("Favorite enlargement size");
                window.CloseFileSectionEnlargement();window.UpdateLayout();
                if(!ReferenceEquals(window.FavoritesSection.Parent,originalParent)||window.FavoritesColumn.Width!=originalWidth||window.FavoritesRow.Height!=originalRatio)throw new Exception("Favorite layout restored");
            }
            foreach(var recent in new[]{false,true})
            foreach(var mode in new[]{"A","B"})
            {
                vm.SetEnlargement(new CardEnlargementSettings{Mode=mode});
                vm.ActiveWorkspace!.FavoritesEnlargementPinned=true;vm.ActiveWorkspace.RecentEnlargementPinned=true;
                var section=recent?window.RecentSection:window.FavoritesSection;
                var parent=section.Parent;
                window.OpenFileSectionEnlargement(recent);window.UpdateLayout();
                var shrink=recent?window.RecentShrinkButton:window.FavoritesShrinkButton;
                if(shrink.Visibility!=Visibility.Visible)throw new Exception("Shrink visible");
                shrink.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));window.UpdateLayout();
                if(!ReferenceEquals(section.Parent,parent)||shrink.Visibility!=Visibility.Collapsed)throw new Exception("Pinned section shrinks");
                if(!vm.ActiveWorkspace.FavoritesEnlargementPinned||!vm.ActiveWorkspace.RecentEnlargementPinned)throw new Exception("Shrink preserves pin");
            }
            File.WriteAllText(Path.Combine(directory,"results.txt"),"PASS: selected text contrast, inline editing, Escape cancellation, Enter real rename, undo and source content preservation. Window transparent, not activated or shown in taskbar.");
        }
        finally {window.Close();}
    }
}
