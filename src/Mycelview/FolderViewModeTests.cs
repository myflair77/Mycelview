using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Mycelview;
internal static class FolderViewModeTests
{
    internal static async Task RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        void Check(bool value,string message){if(!value)throw new Exception(message);}
        var file=Path.Combine(output,"문서.txt");File.WriteAllText(file,"fixture");
        using var model=new CardViewModel(new CardState {InitialPath=output,CurrentPath=output},()=>{});
        model.SetViewportActive(false);
        model.Entries.ReplaceAll(Enumerable.Range(0,5000).Select(i=>new FileEntry(i%3==0?output:file,i%3==0?$"폴더 {i:D4}":$"검증 문서 {i:D4}.txt",i%3==0,DateTime.Today.AddDays(-(i%60)),i*1024)).ToArray());
        using var source=new HwndSource(new HwndSourceParameters("Hidden folder modes test"){Width=520,Height=340,WindowStyle=0});
        var root=new System.Windows.Documents.AdornerDecorator();
        var host=new Grid {Width=500,Height=320};
        var grid=new DataGrid {DataContext=model,ItemsSource=model.Entries,AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,SelectionMode=DataGridSelectionMode.Extended};
        grid.Columns.Add(new DataGridTextColumn {Header="이름",Binding=new System.Windows.Data.Binding("Name"),SortMemberPath="Name",Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        host.Children.Add(grid);root.Child=host;source.RootVisual=root;
        var views=new FolderViewModes(grid);
        grid.SelectedItems.Add(model.Entries[1]);grid.SelectedItems.Add(model.Entries[2]);
        var logs=new List<string>();
        foreach(var mode in FolderViewModes.Modes)
        {
            views.Apply(mode.Id,false);
            root.Measure(new Size(520,340));root.Arrange(new Rect(0,0,520,340));root.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.Render);
            root.UpdateLayout();
            Check(model.State.ViewMode==mode.Id,"Card state follows mode");
            Check(grid.SelectedItems.Count==2 && views.Icons.SelectedItems.Count==2,"Selection survives mode switch");
            Check(JsonSerializer.Deserialize<CardState>(JsonSerializer.Serialize(model.State))!.ViewMode==mode.Id,"View mode serialization");
            if(mode.Id!="Details")
            {
                var panel=FolderViewModes.Find<FolderIconPanel>(views.Icons);
                Check(panel!=null,"Virtual panel exists: "+mode.Id);
                int count=VisualTreeHelper.GetChildrenCount(panel!);
                Check(count>0 && count<150,"Only visible cells realized: "+mode.Id+" "+count);
                Check(panel!.ColumnFlow==(mode.Id=="List"),"List flows top to bottom");
                if(mode.Id=="List")Check(panel.ExtentWidth>panel.ViewportWidth,"List scrolls horizontally");
                else Check(panel.ExtentHeight>panel.ViewportHeight,"Icon view scrolls vertically");
                // Enlarging/reparenting uses the same view and preserves mode/selection.
                host.Width=700;host.Height=600;
                root.Measure(new Size(720,620));root.Arrange(new Rect(0,0,720,620));root.UpdateLayout();
                Check(views.Mode==mode.Id && views.Icons.SelectedItems.Count==2,"Enlarged view retains mode and selection");
                host.Width=500;host.Height=320;root.Measure(new Size(520,340));root.Arrange(new Rect(0,0,520,340));root.UpdateLayout();
                logs.Add($"PASS {mode.Id}: {count} realized containers / 5000 entries");
            }
            var bitmap=new RenderTargetBitmap(520,340,96,96,PixelFormats.Pbgra32);bitmap.Render(root);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(Path.Combine(output,mode.Id+".png")))encoder.Save(stream);
        }
        foreach(var mode in new[]{"Large","Tiles"})
        foreach(var group in new[]{"Type","Modified","Length"})
        {
            views.Apply(mode,false);views.SetGrouping(group);
            foreach(var size in new[]{new Size(520,340),new Size(720,620)})
            {
                host.Width=size.Width-20;host.Height=size.Height-20;
                root.Measure(size);root.Arrange(new Rect(size));root.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.Render);
                var panel=FolderViewModes.Find<FolderIconPanel>(views.Icons)!;
                Check(panel.GroupCount>=2,"Visible group sections "+group);
                Check(VisualTreeHelper.GetChildrenCount(panel)<150,"Grouped virtualization");
                Check(panel.Intersecting(new Rect(0,0,500,29)).Count()==0,"Group header is not selectable");
                var first=model.Entries.First();
                var index=views.Icons.Items.IndexOf(first);panel.Reveal(index);root.UpdateLayout();
                Check(panel.Cell(index).Width>0,"Grouped item reveal");
                panel.SetVerticalOffset(Math.Max(0,panel.ExtentHeight-panel.ViewportHeight));root.UpdateLayout();
                Check(VisualTreeHelper.GetChildrenCount(panel)<150,"Grouped end scroll virtualization");
                panel.SetVerticalOffset(0);root.UpdateLayout();
            }
            var bitmap=new RenderTargetBitmap(720,620,96,96,PixelFormats.Pbgra32);bitmap.Render(root);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using(var stream=File.Create(Path.Combine(output,mode+"-"+group+".png")))encoder.Save(stream);
            Check(model.State.GroupMode==group,"Grouping setting persisted in card");
            views.Apply("Details",false);
            Check(System.Windows.Data.CollectionViewSource.GetDefaultView(grid.ItemsSource).Groups?.Count>=2,"Details preserves grouping");
            views.Apply(mode,false);
            Check(views.GroupMode==group&&views.Icons.Items.Cast<object>().All(x=>x is FileEntry),"Icons retain file identities");
        }
        views.SetGrouping("");
        using(var vm=new MainViewModel(new StateStore(Path.Combine(output,"missing-settings"))))
        {
            using var favorite=new CardViewModel(new CardState{InitialPath=Path.Combine(output,"missing.xlsx"),CurrentPath=Path.Combine(output,"missing.xlsx"),IsFile=true},()=>{});
            vm.Cards.Add(favorite);
            var menus=new FileContextMenus(vm);
            string? copied=null;menus.CopyText=text=>copied=text;
            var menu=menus.Build([new ContextTarget(favorite.CurrentPath,false,favorite)]);
            var commands=menu.Items.OfType<MenuItem>().ToArray();
            Check(commands.Any(x=>Equals(x.Header,"경로 재연결…")) && commands.Any(x=>Equals(x.Header,"즐겨찾기 등록 해제 (원본 유지)")),"Missing favorite has recovery menu");
            commands.Single(x=>Equals(x.Header,"저장된 경로 복사")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(copied==favorite.CurrentPath,"Missing path can be copied without Shell");
            logs.Add("PASS missing favorite recovery context menu");
        }
        var icon=ShellIcons.ForSize(output,true,64) as BitmapSource;
        Check(icon!=null && icon.PixelWidth>=32,"Large icon uses Shell image factory");
        var store=new StateStore(Path.Combine(output,"actual-card-settings"));
        store.Save(new WorkspaceState {Cards=[new CardState{InitialPath=output,CurrentPath=output,ViewMode="Large"}]});
        using(var vm=new MainViewModel(store))
        {
            var window=new MainWindow(vm){TestMode=true,Opacity=0,ShowActivated=false,ShowInTaskbar=false};
            window.Show();
            try
            {
                await vm.InitializeAsync();window.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ContextIdle);
                var card=CardView.FindChild<CardView>(window.FolderCards);
                Check(card!=null,"Actual card created");
                var actual=FolderViewModes.For(card!.FileList)!;
                foreach(var mode in FolderViewModes.Modes)
                {
                    var viewMenu=card.BuildViewMenu();
                    viewMenu.Items.OfType<MenuItem>().Single(x=>Equals(x.Header,mode.Label)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    window.ToggleCardEnlargement(card);window.UpdateLayout();
                    await Dispatcher.Yield(DispatcherPriority.ContextIdle);
                    Check(window.IsCardEnlarged(card) && actual.Mode==mode.Id,"Actual enlarged card preserves "+mode.Id);
                    window.ToggleCardEnlargement(card);window.UpdateLayout();
                    Check(!window.IsCardEnlarged(card) && actual.Mode==mode.Id,"Normal card restores "+mode.Id);
                }
                foreach(var group in new[]{"Type","Modified","Length"})
                {
                    actual.Apply("Tiles",false);actual.SetGrouping(group);
                    window.ToggleCardEnlargement(card);window.UpdateLayout();
                    Check(window.IsCardEnlarged(card)&&actual.GroupMode==group,"Actual enlarged grouping");
                    window.ToggleCardEnlargement(card);window.UpdateLayout();
                    Check(actual.GroupMode==group,"Actual normal grouping");
                }
                Check(store.Load().Workspaces.SelectMany(w=>w.Cards).Any(c=>c.GroupMode=="Length"),"Grouping survives saved workspace");
                actual.SetGrouping("");
                card.BuildSortMenu().Items.OfType<MenuItem>().Single(x=>Equals(x.Header,"크기")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                card.BuildSortMenu().Items.OfType<MenuItem>().Single(x=>Equals(x.Header,"내림차순")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Check(((CardViewModel)card.DataContext).State.SortProperty=="Length" && ((CardViewModel)card.DataContext).State.SortDescending,"Sort toolbar updates sorting state");
                Check(card.BuildSortMenu().Items.OfType<MenuItem>().Where(x=>x.IsChecked).Select(x=>x.Header).Contains("크기"),"Sort menu marks current field");
                Check(card.BuildViewMenu().Items.OfType<MenuItem>().Take(7).Count(x=>x.IsChecked)==1,"View menu marks exactly one mode");
                logs.Add("PASS sort/view toolbar commands and all seven modes through actual normal/enlarged CardView reparenting");
                var extra=Path.Combine(output,"pin-test-folder");Directory.CreateDirectory(extra);vm.AddPath(extra);
                var folders=vm.Folders.ToArray();folders[0].State.EnlargementPinned=true;folders[1].State.EnlargementPinned=false;
                Check(vm.ToggleFolderPinsLabel=="핀 일괄 설정","Mixed pins offer setting all");
                window.ToggleAllFolderPins();
                Check(folders.All(c=>c.State.EnlargementPinned)&&vm.ToggleFolderPinsLabel=="핀 일괄 해제","Set all mixed pins");
                window.ToggleCardEnlargement(card!);
                window.ToggleAllFolderPins();
                Check(folders.All(c=>!c.State.EnlargementPinned)&&!window.EnlargementStaysOpen&&vm.ToggleFolderPinsLabel=="핀 일괄 설정","Unpin all including enlarged card");
                window.ToggleAllFolderPins();
                Check(window.EnlargementStaysOpen,"Pin all includes enlarged card");
                Check(vm.ActiveWorkspace!.FavoritesEnlargementPinned==false && vm.ActiveWorkspace.RecentEnlargementPinned==false,"Bulk pins affect folders only");
                Check(store.Load().Workspaces.SelectMany(w=>w.Cards).All(c=>c.EnlargementPinned),"Pins persist");
                card!.ShrinkCardButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(!window.IsCardEnlarged(card) && folders.All(c=>c.State.EnlargementPinned),"Shrink overrides pin and preserves setting");

            }
            finally{window.Close();}
        }
        File.WriteAllLines(Path.Combine(output,"results.txt"),logs.Append("PASS mode persistence, selection synchronization, resize/enlargement and high-resolution icon. No computer use."));
    }
}
