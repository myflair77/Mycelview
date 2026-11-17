using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
namespace Mycelview;
internal static class AppZoneTests
{
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint="SetWindowLongPtrW")] static extern IntPtr SetOwner(IntPtr hwnd,int index,IntPtr value);
    internal static void Run(string output)
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);}
        EdgeActivationTests.Run();
        var exe=Environment.ProcessPath!;
        var a=new AppWindowZone{Name="Test",AppPath=exe,StartPercent=0,EndPercent=25};
        var b=new AppWindowZone{Name="Test2",AppPath=exe,StartPercent=25,EndPercent=50};
        Check(MainWindow.ValidateAppZones([a,b],out _),"Adjacent zones allowed");
        b.StartPercent=20;Check(!MainWindow.ValidateAppZones([a,b],out _),"Overlap rejected");b.StartPercent=25;
        var settings=new EdgeActivationSettings{AppZones=[a,b]};
        var copy=settings.Normalize();copy.AppZones[0].Name="Copy";
        Check(settings.AppZones[0].Name=="Test","Dialog clone cannot mutate stored zones");
        var restored=StateStore.Parse(JsonSerializer.Serialize(new WorkspaceState{EdgeActivation=settings}));
        Check(restored.EdgeActivation.AppZones.Count==2&&restored.EdgeActivation.AppZones[1].AppPath==exe,"Global zone persistence");
        var monitor=new EdgeMonitor("test",-1920,0,0,1080,false);
        Check(EdgeActivationService.InRange(monitor,a.Region(settings),-1920,100),"First app zone");
        Check(!EdgeActivationService.InRange(monitor,a.Region(settings),-1920,500),"Different app zone");
        using(var vm=new MainViewModel(new StateStore(Path.Combine(output,"dialog-state")))){
            vm.EdgeActivation=settings;
            var owner=new MainWindow(vm){TestMode=true,Opacity=0,ShowActivated=false,ShowInTaskbar=false};owner.Show();
            var dialog=owner.CreateEdgeActivationDialog();
            var root=(System.Windows.Controls.DockPanel)dialog.Content;
            var tabs=root.Children.OfType<System.Windows.Controls.TabControl>().Single();
            Check(tabs.Items.Count==3,"Three simple setting categories");
            Check(root.Children.OfType<System.Windows.Controls.CheckBox>().Single().ToolTip!=null,"Enable help is a tooltip");
            var appTab=(System.Windows.Controls.TabItem)tabs.Items[1];
            var appPanel=(System.Windows.Controls.StackPanel)((System.Windows.Controls.ScrollViewer)appTab.Content).Content;
            var selector=appPanel.Children.OfType<System.Windows.Controls.ComboBox>().Single();
            Check(selector.Items.Count==2,"Registered apps selectable");
            selector.SelectedIndex=0;
            var rows=appPanel.Children.OfType<System.Windows.Controls.StackPanel>().Single();
            Check(rows.Children.OfType<System.Windows.Controls.Border>().Count(x=>x.Visibility==Visibility.Visible)==1,"Only selected app expanded");
            Check(settings.AppZones[0].Name=="Test","Editor changes isolated from saved settings");
            tabs.SelectedIndex=0;
            root.Measure(new Size(500,550));root.Arrange(new Rect(0,0,500,550));root.UpdateLayout();
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(500,550,96,96,System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(root);var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using(var stream=File.Create(Path.Combine(output,"settings.png")))png.Save(stream);
            dialog.Close();owner.Close();
        }
        var first=new Window{Title="Mycelview edge fixture 1",Width=300,Height=200,Left=-30000,Top=-30000,ShowActivated=false,ShowInTaskbar=false};
        var second=new Window{Title="Mycelview edge fixture 2",Width=300,Height=200,Left=-30000,Top=-30000,ShowActivated=false,ShowInTaskbar=false};
        WindowPicker? picker=null;
        try{
            first.Show();second.Show();
            var h1=new WindowInteropHelper(first).Handle;var h2=new WindowInteropHelper(second).Handle;
            SetOwner(h1,-8,IntPtr.Zero);SetOwner(h2,-8,IntPtr.Zero);
            var windows=AppWindows.List(exe,true);
            Check(windows.Any(x=>x.Handle==h1)&&windows.Any(x=>x.Handle==h2),"Enumerate two native windows for exact app");
            Check(AppWindows.List(Path.Combine(output,"different.exe"),true).Count==0,"Exclude different application");
            Check(!AppWindows.List(exe).Any(x=>x.Handle==h1),"Exclude Mycelview itself");
            picker=new WindowPicker(a,windows.Where(x=>x.Handle==h1||x.Handle==h2).ToList(),EdgeActivationService.GetMonitors()[0]){Opacity=0,ShowActivated=false};
            picker.Show();picker.UpdateLayout();
            Check(picker.ThumbnailCount==2,"Native DWM thumbnails registered");
            picker.Close();Check(picker.ThumbnailCount==0,"Thumbnail handles released");
            second.Close();Check(!AppWindows.List(exe,true).Any(x=>x.Handle==h2),"Closed window removed");
            Check(!AppWindows.Activate(IntPtr.Zero),"Invalid activation rejected");
            File.WriteAllText(Path.Combine(output,"passed.txt"),"Zone ranges, overlap, persistence, cloned settings, native window enumeration, process matching, DWM registration and disposal passed. No user's window activated.");
        }finally{picker?.Close();first.Close();second.Close();}
    }
}



