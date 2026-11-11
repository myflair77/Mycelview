using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
namespace PinShelf;
public partial class CardView
{
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
    private readonly DispatcherTimer renameTimer=new();
    private string? lastNameClick;
    private DateTime lastNameClickAt;
    private FileEntry? renameCandidate;
    private bool nameClickReady;
    private TextBox? renameEditor;
    private FileEntry? renameEntry;
    internal bool IsRenaming => renameEditor!=null;
    private void InitializeInlineRename()
    {
        renameTimer.Interval=TimeSpan.FromMilliseconds(GetDoubleClickTime()+50);
        renameTimer.Tick+=(_,_)=> {renameTimer.Stop();var entry=renameCandidate;renameCandidate=null;if(entry!=null)BeginInlineRename(entry);};
        Unloaded+=(_,_)=> {CancelPendingRename();EndInlineRename(false);};
        FileList.SelectionChanged+=(_,_)=> {if(renameCandidate!=null && !FileList.SelectedItems.Contains(renameCandidate))CancelPendingRename();};
    }
    private void CancelPendingRename(){renameTimer.Stop();renameCandidate=null;nameClickReady=false;}
    private void TrackNameClick(MouseButtonEventArgs e)
    {
        CancelPendingRename();
        if(IsRenaming || e.ClickCount!=1 || Keyboard.Modifiers!=ModifierKeys.None)return;
        if(FindParent<TextBlock>(e.OriginalSource as DependencyObject)?.Tag as string!="FileName")return;
        if(FindParent<DataGridRow>(e.OriginalSource as DependencyObject)?.Item is not FileEntry entry)return;
        var now=DateTime.UtcNow;
        nameClickReady=lastNameClick==entry.FullPath && (now-lastNameClickAt).TotalMilliseconds>GetDoubleClickTime() && FileList.SelectedItems.Count==1 && Equals(FileList.SelectedItem,entry);
        lastNameClick=entry.FullPath;lastNameClickAt=now;
        if(nameClickReady)renameCandidate=entry;
    }
    private void CompleteNameClick(MouseButtonEventArgs e)
    {
        if(!nameClickReady || renameCandidate==null)return;
        if(!Equals(FindParent<DataGridRow>(e.OriginalSource as DependencyObject)?.Item,renameCandidate)){CancelPendingRename();return;}
        renameTimer.Start();
    }
    internal void BeginInlineRename(FileEntry entry)
    {
        if(!IsLoaded || Workspace.FileTasks.Busy || FileList.SelectedItems.Count!=1 || !Equals(FileList.SelectedItem,entry))return;
        if(FileList.ItemContainerGenerator.ContainerFromItem(entry) is not DataGridRow row)return;
        var editor=FindChild<TextBox>(row);if(editor==null)return;
        EndInlineRename(false);renameEntry=entry;renameEditor=editor;editor.Text=entry.Name;editor.Visibility=Visibility.Visible;
        editor.Focus();editor.Select(0,entry.IsDirectory?entry.Name.Length:Path.GetFileNameWithoutExtension(entry.Name).Length);
    }
    private void InlineRenameKeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.Escape){e.Handled=true;EndInlineRename(false);}
        else if(e.Key==Key.Enter){e.Handled=true;EndInlineRename(true);}
    }
    private void InlineRenameLostFocus(object sender,KeyboardFocusChangedEventArgs e)=>EndInlineRename(true);
    private async void EndInlineRename(bool commit)
    {
        if(renameEditor is not {} editor || renameEntry is not {} entry)return;
        string name=editor.Text;
        renameEditor=null;renameEntry=null;editor.Visibility=Visibility.Collapsed;
        if(!commit || name==entry.Name)return;
        var host=Window.GetWindow(this) as MainWindow;if(host==null)return;
        try
        {
            FileOperationRules.CheckName(name);
            await host.ViewModel.FileTasks.ExecuteAsync("이름 변경",[new(FileAction.Rename,entry.FullPath,Name:name)]);
        }
        catch(Exception ex){host.ViewModel.Status="이름 변경 실패: "+ex.Message;}
    }
}

