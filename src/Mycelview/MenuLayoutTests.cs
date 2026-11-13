using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Mycelview;
internal static class MenuLayoutTests
{
    internal static async Task RunAsync(string output)
    {
        ContextMenuTests.Run(output);
        var folder=Path.Combine(output,"layout-fixture");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"확인.txt"),"preview");
        var first=new WorkspaceTab { Name="포상",Cards=[new CardState {InitialPath=folder,CurrentPath=folder,Title="검증 폴더"}] };
        var store=new StateStore(Path.Combine(output,"layout-state"));store.Save(new WorkspaceState {Workspaces=[first,new WorkspaceTab {Name="예산"}],ActiveWorkspaceId=first.Id});
        using var vm=new MainViewModel(store);var window=new MainWindow(vm) {TestMode=true,Opacity=0,ShowActivated=false,ShowInTaskbar=false};Application.Current.MainWindow=window;window.Show();
        try
        {
            await vm.InitializeAsync();window.UpdateLayout();await Task.Delay(120);window.UpdateLayout();
            window.ExplorerColumn.Width = new GridLength(300); window.UpdateLayout();
            void CheckPanes()
            {
                var heading = window.WorkspaceTabScroll.TranslatePoint(new Point(), window.PaneGrid);
                var edge = new Point(window.ExplorerColumn.ActualWidth + window.PaneGrid.ColumnDefinitions[1].ActualWidth + (window.ViewModel.ExplorerCollapsed ? 0 : 15), 0);
                if (Math.Abs(heading.X - edge.X) > 1) throw new Exception($"Workspace tabs must align with the first folder card. heading={heading.X}, edge={edge.X}, width={window.ExplorerColumn.ActualWidth}");
                if (window.ExplorerPane.TranslatePoint(new Point(), window.PaneGrid).Y != 0) throw new Exception("Explorer must begin beside the tab row.");
            }
            CheckPanes();
            window.ToggleExplorer(window, new RoutedEventArgs()); window.UpdateLayout(); window.SavePaneLayout();
            if (window.ExplorerPane.Visibility != Visibility.Collapsed || window.ViewModel.ExplorerWidth != 300) throw new Exception("Collapse must preserve expanded width.");
            CheckPanes();
            window.RestorePaneLayout(); window.UpdateLayout();
            if (window.ExplorerColumn.ActualWidth != 22.5) throw new Exception("Collapsed state must restore.");
            window.ToggleExplorer(window, new RoutedEventArgs()); window.UpdateLayout();
            if (window.ExplorerColumn.ActualWidth != 300) throw new Exception("Expand must restore previous width.");
            CheckPanes();
            var tab=FindTab(window.WorkspaceTabs)!;
            if(tab.BorderThickness.Bottom!=0 || tab.CornerRadius.BottomLeft!=0 || tab.CornerRadius.BottomRight!=0)throw new Exception("Tabs must have no lower border or rounded bottom.");
            if(((SolidColorBrush)tab.Background).Color!=((SolidColorBrush)window.Background).Color)throw new Exception("Active tab must match workspace background.");
            var create=await WindowsFileOperations.RunAsync([new(FileAction.NewFolder,folder,Name:"새 폴더 검증")],new WindowInteropHelper(window).Handle,CancellationToken.None,flags:WindowsFileOperations.QuietTestFlags);
            if(create.Errors.Count>0 || !Directory.Exists(Path.Combine(folder,"새 폴더 검증")))throw new Exception("Native new folder operation failed.");
            await MenuCommandTests.RunAsync(window,folder,output);
            var content=(FrameworkElement)window.Content;
            var bitmap=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32); var visual=new DrawingVisual(); using(var drawing=visual.RenderOpen()) { drawing.DrawRectangle(window.Background,null,new Rect(0,0,content.ActualWidth,content.ActualHeight)); drawing.DrawRectangle(new VisualBrush(content),null,new Rect(0,0,content.ActualWidth,content.ActualHeight)); } bitmap.Render(visual);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(Path.Combine(output,"layout.png")))encoder.Save(stream);
            File.WriteAllText(Path.Combine(output,"layout-results.txt"),"PASS: connected tab shape and background; native new-folder creation; hidden layout render. No physical mouse tests or third-party commands invoked.");
        }
        finally {window.Close();}
    }
    private static Border? FindTab(DependencyObject root)
    {
        if(root is Border b && Equals(b.Tag,"WorkspaceTab"))return b;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var found=FindTab(VisualTreeHelper.GetChild(root,i));if(found!=null)return found;}return null;
    }
}
