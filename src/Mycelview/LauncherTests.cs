using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Mycelview;
internal static class LauncherTests
{
    internal static async Task RunAsync(string output)
    {
        void Check(bool valid,string message){if(!valid)throw new InvalidOperationException(message);}
        var exe=Environment.ProcessPath!;
        var app=new LauncherFavorite{Name="프로그램",Target=exe};
        var originalEdges=new EdgeActivationSettings{Enabled=false};
        var draftEdges=originalEdges.Normalize();
        var zone=LauncherEdgeSetup.Prepare(draftEdges,app);
        Check(zone.AppPath==exe&&zone.Name==app.Name&&draftEdges.Enabled,"Favorite preselects edge app");
        Check(originalEdges.AppZones.Count==0&&!originalEdges.Enabled,"Cancel leaves original settings untouched");
        zone.StartPercent=30;zone.EndPercent=60;
        Check(ReferenceEquals(zone,LauncherEdgeSetup.Prepare(draftEdges,app))&&draftEdges.AppZones.Count==1&&zone.StartPercent==30,"Existing zone reused without losing range");
        var shortcut=Path.Combine(output,"edge-fixture.lnk");
        object? shell=null,link=null;
        try{
            shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            link=((dynamic)shell!).CreateShortcut(shortcut);
            ((dynamic)link).TargetPath=exe;((dynamic)link).Save();
            Check(LauncherEdgeSetup.Executable(new(){Name="Shortcut",Target=shortcut})==exe,"Shortcut resolves to actual executable");
        }finally{
            if(link!=null)System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);
            if(shell!=null)System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
        var browser=new LauncherFavorite{Name="브라우저",Kind="Browser",Target=exe};
        var site=new LauncherFavorite{Name="테스트 사이트",Kind="Website",Target="https://example.com/search?q=한글&name=two%20words",BrowserPath=exe};
        Check(LauncherService.Prepare(app).FileName==exe,"Application target");
        Check(LauncherService.Prepare(browser).FileName==exe,"Browser target");
        var launch=LauncherService.Prepare(site);
        Check(launch.FileName==exe && launch.ArgumentList.Count==1 && launch.ArgumentList[0].Contains("&name="),"Website must be one URL argument");
        var system=site.Copy();system.BrowserPath="";
        Check(LauncherService.Prepare(system).UseShellExecute && LauncherService.Prepare(system).FileName.StartsWith("https://"),"System browser");
        foreach(var invalid in new[]{"javascript:alert(1)","file:///C:/test","not a URL"})
        {
            var bad=site.Copy();bad.Target=invalid;
            try{LauncherService.Prepare(bad);throw new Exception("Accepted invalid URL");}catch(InvalidDataException){}
        }
        var missing=app.Copy();missing.Target=Path.Combine(output,"missing.exe");
        try{LauncherService.Prepare(missing);throw new Exception("Accepted missing application");}catch(InvalidDataException){}
        var catalogRoot=Path.Combine(output,"catalog");
        Directory.CreateDirectory(Path.Combine(catalogRoot,"Nested"));
        File.WriteAllText(Path.Combine(catalogRoot,"Alpha.lnk"),"fixture");
        File.WriteAllText(Path.Combine(catalogRoot,"Nested","한글 앱.appref-ms"),"fixture");
        File.WriteAllText(Path.Combine(catalogRoot,"ignore.txt"),"fixture");
        var discovered=LauncherCatalog.Discover([catalogRoot,catalogRoot,Path.Combine(output,"missing")]);
        Check(discovered.Count==2,"Recursive catalog and duplicate roots");
        Check(LauncherCatalog.Filter(discovered," ALPHA ").Single().Name=="Alpha","Case insensitive name search");
        Check(LauncherCatalog.Filter(discovered,"한글").Count()==1,"Korean name search");
        Check(LauncherCatalog.FromDrop([exe,exe,Path.Combine(catalogRoot,"ignore.txt"),catalogRoot]).Count==1,"Drop rejects documents and directories, deduplicates paths");
        var actualCatalog=await Task.Run(()=>LauncherCatalog.Discover());
        File.WriteAllText(Path.Combine(output,"catalog-summary.txt"),$"Discovered {actualCatalog.Count} shortcuts; {LauncherService.Browsers().Count-1} installed browsers.");
        Check(StateStore.Parse("{}").LauncherFavorites.Count==0,"Legacy settings");
        var first=new WorkspaceTab{Name="첫 번째"};var second=new WorkspaceTab{Name="두 번째"};
        var store=new StateStore(Path.Combine(output,"state"));
        store.Save(new WorkspaceState{Version=2,Workspaces=[first,second],ActiveWorkspaceId=first.Id,LauncherFavorites=[app,browser,site]});
        using var vm=new MainViewModel(store);
        var window=new MainWindow(vm){TestMode=true,Opacity=0,ShowActivated=false,ShowInTaskbar=false};
        Application.Current.MainWindow=window;window.Show();
        try
        {
            await vm.InitializeAsync();
            Check(vm.LauncherFavorites.Count==3,"Load favorites");
            await vm.SwitchWorkspaceAsync(second.Id);
            Check(vm.LauncherFavorites.Count==3 && vm.LauncherFavorites[2].BrowserPath==exe,"Shared across workspaces");
            vm.LauncherFavorites.Move(2,0);Check(vm.Save(),"Save");
            var reloaded=store.Load();Check(reloaded.LauncherFavorites[0].Id==site.Id && reloaded.LauncherFavorites[0].BrowserPath==exe,"Restart persistence and order");
            var backup=Path.Combine(output,"backup.json");vm.ExportSettings(backup);
            Check(WorkspaceBackup.Read(backup).LauncherFavorites.Count==3,"Backup includes launcher");
            vm.LauncherFavorites.Clear();await vm.ImportSettingsAsync(backup);
            Check(vm.LauncherFavorites.Count==3,"Restore launcher");
            var dropPath=Path.Combine(catalogRoot,"Alpha.lnk");
            Check(window.RegisterLauncherDrop([dropPath,dropPath])==1,"Register dropped shortcut once");
            Check(window.RegisterLauncherDrop([dropPath])==0,"Repeated drop avoids duplicates");
            Check(store.Load().LauncherFavorites.Count==4,"Dropped shortcut persisted");
            Check(((Border)window.FindName("LauncherBar")).AllowDrop,"Bar accepts drops");
            Check(vm.LauncherGroups.Count==3,"Three explicit groups");
            Check(vm.MoveLauncherGroup("Website",-1) && vm.MoveLauncherGroup("Website",-1),"Move site group to front");
            Check(!vm.MoveLauncherGroup("Website",-1),"Group boundary");
            var apps=vm.LauncherFavorites.Where(x=>x.Kind=="App").ToList();
            var browserBefore=vm.LauncherGroups.Single(g=>g.Kind=="Browser").Items.Select(x=>x.Id).ToArray();
            Check(vm.MoveLauncherItem(apps[1],-1),"Move item within app group");
            Check(vm.LauncherGroups.Single(g=>g.Kind=="App").Items[0].Id==apps[1].Id,"App order updated");
            Check(browserBefore.SequenceEqual(vm.LauncherGroups.Single(g=>g.Kind=="Browser").Items.Select(x=>x.Id)),"Other group order unaffected");
            Check(!vm.MoveLauncherItem(apps[1],-1),"Item cannot cross group boundary");
            vm.Save();
            Check(vm.InsertLauncherGroup("Website","Browser",true),"Drag group after another group");
            Check(vm.LauncherGroups.Last().Kind=="Website","Dragged group at end");
            Check(vm.LauncherGroupsTitle==string.Join("·",vm.LauncherGroups.Select(g=>g.Name)),"Heading follows dragged group order");
            Check(vm.InsertLauncherGroup("Website","App",false),"Drag group before another group");
            Check(vm.InsertLauncherItem(apps[1].Id,apps[0].Id,true),"Drag item to right");
            Check(vm.InsertLauncherItem(apps[1].Id,apps[0].Id,false),"Drag item to left");
            Check(!vm.InsertLauncherItem(apps[1].Id,site.Id,true),"Reject cross-group item drag");
            Check(!vm.InsertLauncherItem(apps[1].Id,apps[1].Id,true),"Self drop does not change order");
            vm.Save();
            Check(store.Load().LauncherGroupOrder[0]=="Website","Group order survives restart");
            vm.ExportSettings(backup);
            vm.LoadLauncherGroupOrder(null);
            await vm.ImportSettingsAsync(backup);
            Check(vm.LauncherGroups[0].Kind=="Website" && vm.LauncherGroups.Single(g=>g.Kind=="App").Items[0].Id==apps[1].Id,"Group and item order restored from backup");
            await vm.SwitchWorkspaceAsync(first.Id);
            Check(vm.LauncherGroups[0].Kind=="Website","Group order shared by workspaces");
            window.UpdateLayout();
            var bar=(Border)window.FindName("LauncherBar");var search=(Border)window.FindName("SearchArea");
            Check(bar.ActualHeight>0 && bar.TranslatePoint(new Point(),window).Y<search.TranslatePoint(new Point(),window).Y,"Bar above search");
            var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);
            bitmap.Render((Visual)window.Content);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using(var file=File.Create(Path.Combine(output,"launcher.png")))encoder.Save(file);
            File.WriteAllText(Path.Combine(output,"passed.txt"),"Launch routing, URL validation, legacy settings, shared workspaces, restart persistence, reordering, backup restoration and bar layout passed. No applications or browsers launched.");
        }
        finally{window.Close();}
    }
}






