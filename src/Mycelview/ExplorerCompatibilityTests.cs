using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Mycelview;
internal static class ExplorerCompatibilityTests
{
    internal static async Task RunAsync(string output)
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);}
        var source=Path.Combine(output,"source");var target=Path.Combine(output,"target");
        Directory.CreateDirectory(source);Directory.CreateDirectory(target);
        var file=Path.Combine(source,"sample.txt");File.WriteAllText(file,"copy and move fixture");
        var allowed=DragDropEffects.Copy|DragDropEffects.Move;
        Check(ExternalFileDrop.Effect([file],target,DragDropKeyStates.None,allowed,false)==DragDropEffects.Move,"same volume move");
        Check(ExternalFileDrop.Effect([file],target,DragDropKeyStates.ControlKey,allowed,false)==DragDropEffects.Copy,"Ctrl copy");
        Check(ExternalFileDrop.Effect([source],source,DragDropKeyStates.ControlKey,allowed,false)==DragDropEffects.None,"self drop rejected");
        Check(ExternalFileDrop.Effect([file],target,DragDropKeyStates.ShiftKey,allowed,true)==DragDropEffects.None,"busy rejected");
        var data=new CardFileDrag(Guid.NewGuid(),[new CardDragEntry(file,false)]).ToData();
        Check(ExternalFileDrop.Paths(data).SequenceEqual([file]),"outgoing Windows FileDrop");
        var copied=await WindowsFileOperations.RunAsync([new(FileAction.Copy,file,target)],IntPtr.Zero,default,flags:WindowsFileOperations.QuietTestFlags);
        Check(copied.Completed.Count==1&&File.Exists(file)&&File.ReadAllText(Path.Combine(target,"sample.txt"))=="copy and move fixture","native copy");
        var moved=await WindowsFileOperations.RunAsync([new(FileAction.Move,file,target,"moved.txt")],IntPtr.Zero,default,flags:WindowsFileOperations.QuietTestFlags);
        Check(moved.Completed.Count==1&&!File.Exists(file)&&File.Exists(Path.Combine(target,"moved.txt")),"native move");
        var deleted=await WindowsFileOperations.RunAsync([new(FileAction.Delete,Path.Combine(target,"moved.txt"))],IntPtr.Zero,default,flags:WindowsFileOperations.QuietTestFlags);
        Check(deleted.Completed.Count==1&&!File.Exists(Path.Combine(target,"moved.txt"))&&!deleted.Aborted,"permanent delete");
        var hidden=Path.Combine(source,"hidden.txt");File.WriteAllText(hidden,"hidden");File.SetAttributes(hidden,FileAttributes.Hidden);
        var entries=await FolderListing.ReadAsync(source,null,default);
        var view=CollectionViewSource.GetDefaultView(entries);var prefs=FileDisplayPreferences.Current;
        prefs.Watch(view);prefs.ShowHidden=false;Check(!view.Cast<FileEntry>().Any(x=>x.FullPath==hidden),"hidden omitted");
        prefs.ShowHidden=true;Check(view.Cast<FileEntry>().Any(x=>x.FullPath==hidden),"hidden shown");
        var converter=new FileNameConverter();
        Check((string)converter.Convert([new FileEntry(file,"report.final.txt",false,DateTime.Now,0),false],typeof(string),null!,CultureInfo.InvariantCulture)=="report.final","extension display only");
        Check((string)converter.Convert([new FileEntry(source,"folder.dot",true,DateTime.Now,0),false],typeof(string),null!,CultureInfo.InvariantCulture)=="folder.dot","folder name intact");
        var png=Path.Combine(source,"image.png");
        var pixels=Enumerable.Repeat(new byte[]{25,80,220,255},64*64).SelectMany(x=>x).ToArray();
        var bitmap=BitmapSource.Create(64,64,96,96,PixelFormats.Bgra32,null,pixels,256);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using(var stream=File.Create(png))encoder.Save(stream);
        var entry=new FileEntry(png,"image.png",false,File.GetLastWriteTime(png),new FileInfo(png).Length);
        var thumbnail=(BitmapSource)await ThumbnailCache.LoadAsync(entry,64,default);
        Check(thumbnail.IsFrozen&&thumbnail.PixelWidth>=32,"real thumbnail bitmap");
        var converted=new FormatConvertedBitmap(thumbnail,PixelFormats.Bgra32,null,0);
        var color=new byte[4];converted.CopyPixels(new Int32Rect(converted.PixelWidth/2,converted.PixelHeight/2,1,1),color,4,0);
        Check(Math.Abs(color[2]-220)<8&&Math.Abs(color[1]-80)<8,"thumbnail contains photo pixels");
        Check(ReferenceEquals(thumbnail,await ThumbnailCache.LoadAsync(entry,64,default)),"thumbnail cache reused");
        using(var cancelled=new CancellationTokenSource())
        {
            cancelled.Cancel();
            try {await ThumbnailCache.LoadAsync(entry,128,cancelled.Token);throw new Exception("cancellation ignored");}
            catch(OperationCanceledException){}
        }
        Check(ThumbnailCache.Count<=128,"bounded cache");
        var store=new StateStore(Path.Combine(output,"settings"));
        store.Save(new WorkspaceState{Cards=[new CardState{InitialPath=target,CurrentPath=target}]});
        using(var vm=new MainViewModel(store))
        {
            var window=new MainWindow(vm){TestMode=true,Opacity=0,ShowActivated=false,ShowInTaskbar=false};
            Application.Current.MainWindow=window;window.Show();
            try
            {
                await vm.InitializeAsync();window.UpdateLayout();
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ContextIdle);
                var card=CardView.FindChild<CardView>(window.FolderCards)!;
                var incoming=Path.Combine(source,"incoming.txt");File.WriteAllText(incoming,"routed external drop");
                var payload=new DataObject(DataFormats.FileDrop,new[]{incoming});
                DragEventArgs Event(RoutedEvent routed)
                {
                    var e=(DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs),System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,
                        new object[]{payload,DragDropKeyStates.ControlKey,allowed,card,new Point(10,10)},null)!;
                    e.RoutedEvent=routed;return e;
                }
                var over=Event(DragDrop.PreviewDragOverEvent);card.RaiseEvent(over);
                Check(over.Handled&&over.Effects==DragDropEffects.Copy,"external routed hover");
                var drop=Event(DragDrop.PreviewDropEvent);card.RaiseEvent(drop);
                Check(drop.Handled&&drop.Effects==DragDropEffects.Copy,"external routed drop");
                var deadline=DateTime.UtcNow.AddSeconds(15);
                while((!File.Exists(Path.Combine(target,"incoming.txt"))||vm.FileTasks.Busy)&&DateTime.UtcNow<deadline)await Task.Delay(20);
                Check(File.Exists(incoming)&&File.ReadAllText(Path.Combine(target,"incoming.txt"))=="routed external drop","external drop task pipeline");
                var model=(CardViewModel)card.DataContext;
                foreach(var enlarged in new[]{false,true})
                {
                    if(enlarged){window.ToggleCardEnlargement(card);window.UpdateLayout();}
                    var fresh=Path.Combine(target,enlarged?"expanded-refresh.txt":"normal-refresh.txt");File.WriteAllText(fresh,"F5");
                    var key=new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                        System.Windows.PresentationSource.FromVisual(card)!,0,System.Windows.Input.Key.F5){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent};
                    card.FileList.RaiseEvent(key);
                    deadline=DateTime.UtcNow.AddSeconds(5);
                    while(!model.Entries.Any(x=>x.FullPath==fresh)&&DateTime.UtcNow<deadline)await Task.Delay(20);
                    Check(key.Handled&&model.Entries.Any(x=>x.FullPath==fresh),"F5 refresh normal/enlarged");
                    if(enlarged){window.ToggleCardEnlargement(card);window.UpdateLayout();}
                }
                var child=Path.Combine(target,"child");Directory.CreateDirectory(child);await model.RefreshAsync();
                foreach(var expanded in new[]{false,true})
                foreach(var mode in FolderViewModes.Modes)
                {
                    FolderViewModes.For(card.FileList)!.Apply(mode.Id,false);
                    FolderViewModes.For(card.FileList)!.SetGrouping("Type");
                    if(expanded)window.ToggleCardEnlargement(card);
                    window.UpdateLayout();await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ContextIdle);
                    var childEntry=model.Entries.Single(x=>x.FullPath==child);
                    System.Windows.Controls.ItemsControl items=mode.Id=="Details"?card.FileList:FolderViewModes.For(card.FileList)!.Icons;
                    var container=(FrameworkElement)items.ItemContainerGenerator.ContainerFromItem(childEntry);
                    Check(container!=null,"folder childDrop container "+mode.Id);
                    DragEventArgs DropEvent(IDataObject data,DragDropKeyStates keys,RoutedEvent routed)
                    {
                        var e=(DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs),System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,
                            new object[]{data,keys,allowed,container!,new Point(5,5)},null)!;
                        e.RoutedEvent=routed;return e;
                    }
                    var name=$"external-{expanded}-{mode.Id}.txt";var src=Path.Combine(source,name);File.WriteAllText(src,name);
                    var external=new DataObject(DataFormats.FileDrop,new[]{src});
                    var hover=DropEvent(external,DragDropKeyStates.ControlKey,DragDrop.PreviewDragOverEvent);container!.RaiseEvent(hover);
                    Check(hover.Effects==DragDropEffects.Copy&&Equals(card.TransferHint.ToolTip,child),"child hover target");
                    container.RaiseEvent(DropEvent(external,DragDropKeyStates.ControlKey,DragDrop.PreviewDropEvent));
                    deadline=DateTime.UtcNow.AddSeconds(10);
                    while((!File.Exists(Path.Combine(child,name))||vm.FileTasks.Busy)&&DateTime.UtcNow<deadline)await Task.Delay(20);
                    Check(File.Exists(src)&&File.Exists(Path.Combine(child,name))&&!File.Exists(Path.Combine(target,name)),"external child copy "+mode.Id);
                    var moveName=$"internal-{expanded}-{mode.Id}.txt";var moveSource=Path.Combine(target,moveName);File.WriteAllText(moveSource,moveName);
                    var internalDrag=new CardFileDrag(model.State.Id,[new CardDragEntry(moveSource,false)]);
                    window.UpdateLayout();
                    container=(FrameworkElement)items.ItemContainerGenerator.ContainerFromItem(model.Entries.Single(x=>x.FullPath==child));
                    var childDrop=DropEvent(internalDrag.ToData(),DragDropKeyStates.ShiftKey,DragDrop.PreviewDropEvent);container.RaiseEvent(childDrop);
                    Check(childDrop.Effects==DragDropEffects.Move&&internalDrag.Accepted?.Destination==child,"same-card child move accepted");
                    await internalDrag.CompleteAsync(childDrop.Effects,vm.FileTasks);
                    Check(!File.Exists(moveSource)&&File.Exists(Path.Combine(child,moveName)),"same-card child moved "+mode.Id);
                    var self=new CardFileDrag(model.State.Id,[new CardDragEntry(child,true)]);
                    Check(self.Effect(model.State.Id,child,DragDropKeyStates.ControlKey,allowed,false)==DragDropEffects.None,"self folder rejected");
                    if(expanded){window.ToggleCardEnlargement(card);window.UpdateLayout();}
                }
                await CardFileDragTests.RunAsync(window,output);
            }
            finally{window.Close();}
        }
        prefs.ShowHidden=false;
        File.WriteAllText(Path.Combine(output,"passed.txt"),"PASS: external drag policy and FileDrop; native copy/move/permanent delete on fixtures; hidden filtering; extension display; real PNG thumbnail pixels, cache reuse and cancellation.");
    }
}
