using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Mycelview.BrowserIntegration;
namespace Mycelview;

internal sealed class BrowserTabInfo
{
    public int Id { get; set; }
    public string Title { get; set; }="";
    public bool Active { get; set; }
    public string Icon { get; set; }="";
}
internal sealed class BrowserWindowInfo
{
    public int Id { get; set; }
    public bool Focused { get; set; }
    public List<BrowserTabInfo> Tabs { get; set; }=[];
}
internal sealed class BrowserSnapshot
{
    public string Type { get; set; }="";
    public List<BrowserWindowInfo> Windows { get; set; }=[];
    public Dictionary<string,string> Icons { get; set; }=[];
}
internal sealed record BrowserTabItem(int Id,string Title,bool Active,ImageSource? Icon);
internal sealed record BrowserWindowTabs(Guid Session,int WindowId,IReadOnlyList<BrowserTabItem> Tabs);
internal sealed class BrowserTabsBridge : IDisposable
{
    internal static BrowserTabsBridge? Current { get; private set; }
    readonly Dispatcher dispatcher;
    readonly CancellationTokenSource stop=new();
    readonly System.Collections.Concurrent.ConcurrentDictionary<Guid,Client> clients=new();
    readonly System.Collections.Concurrent.ConcurrentDictionary<string,TaskCompletionSource<bool>> requests=new();
    readonly string pipeName;
    readonly Func<List<AppOpenWindow>> nativeWindows;
    readonly Func<IntPtr> foregroundWindow;
    Task? listener;
    internal long Revision { get; private set; }
    sealed class Client(Guid id,NamedPipeServerStream pipe,string browser){
        internal Guid Id=id;
        internal NamedPipeServerStream Pipe=pipe;
        internal string Browser=browser;
        internal SemaphoreSlim Writer=new(1,1);
        internal BrowserSnapshot Snapshot=new();
        internal string LastSnapshot="";
        internal Dictionary<int,IntPtr> Mapping=[];
        internal HashSet<int> FocusMappings=[];
        internal Dictionary<string,ImageSource> Images=[];
    }
    internal BrowserTabsBridge(Dispatcher dispatcher,string? testPipe=null,Func<List<AppOpenWindow>>? nativeWindows=null,Func<IntPtr>? foregroundWindow=null){
        this.dispatcher=dispatcher;pipeName=testPipe??BrowserFrames.PipeName;this.nativeWindows=nativeWindows??(()=>AppWindows.List());this.foregroundWindow=foregroundWindow??GetForegroundWindow;
        if(testPipe==null)Current=this;
        listener=Listen();
    }
    internal static string? BrowserKind(string path)=>Path.GetFileName(path).ToLowerInvariant() switch{"chrome.exe"=>"chrome","msedge.exe"=>"edge","whale.exe"=>"whale",_=>null};
    internal static bool TitleMatches(string native,string tab)=>tab.Length>0&&(native==tab||native.StartsWith(tab+" - ",StringComparison.Ordinal)||native.StartsWith(tab+" — ",StringComparison.Ordinal));
    async Task Listen(){
        while(!stop.IsCancellationRequested){
            var pipe=new NamedPipeServerStream(pipeName,PipeDirection.InOut,16,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
            try{await pipe.WaitForConnectionAsync(stop.Token);_=Serve(pipe);}
            catch(OperationCanceledException){pipe.Dispose();break;}
            catch(IOException){pipe.Dispose();if(!stop.IsCancellationRequested)await Task.Delay(500,stop.Token);}
        }
    }
    async Task Serve(NamedPipeServerStream pipe){
        Client? client=null;
        try{
            using var handshake=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);handshake.CancelAfter(5000);
            var hello=await BrowserFrames.Read(pipe,handshake.Token);if(hello==null)return;
            using var json=JsonDocument.Parse(hello);
            if(json.RootElement.GetProperty("type").GetString()!="hello")return;
            var browser=json.RootElement.GetProperty("browser").GetString();
            if(browser is not ("chrome" or "edge" or "whale"))return;
            client=new(Guid.NewGuid(),pipe,browser);clients[client.Id]=client;
            await Send(client,new{type="ready"});
            while(await BrowserFrames.Read(pipe,stop.Token) is {} bytes){
                using var message=JsonDocument.Parse(bytes);
                var type=message.RootElement.GetProperty("type").GetString();
                if(type=="snapshot"){
                    var snapshot=JsonSerializer.Deserialize<BrowserSnapshot>(bytes,BrowserFrames.Json);
                    if(snapshot==null||snapshot.Windows==null||snapshot.Icons==null||snapshot.Windows.Count>128||snapshot.Windows.Any(w=>w.Tabs==null)||snapshot.Windows.Sum(w=>w.Tabs.Count)>2000)continue;
                    var raw=System.Text.Encoding.UTF8.GetString(bytes);
                    await dispatcher.InvokeAsync(()=>{
                        if(!clients.ContainsKey(client.Id)||client.LastSnapshot==raw)return;
                        client.LastSnapshot=raw;client.Snapshot=snapshot;client.Images.Clear();
                        foreach(var pair in snapshot.Icons.Take(256)){
                            var image=DecodeIcon(pair.Value);if(image!=null)client.Images[pair.Key]=image;
                        }
                        BindWindows();Revision++;
                    });
                }else if(type=="result"){
                    var id=message.RootElement.GetProperty("requestId").GetString()??"";
                    if(requests.TryRemove(client.Id+":"+id,out var pending))pending.TrySetResult(message.RootElement.GetProperty("ok").GetBoolean());
                }
            }
        }catch(Exception ex) when(ex is InvalidDataException or IOException or OperationCanceledException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException){}
        finally{
            if(client!=null){
                clients.TryRemove(client.Id,out _);
                foreach(var request in requests.Where(p=>p.Key.StartsWith(client.Id+":")).ToArray())
                    if(requests.TryRemove(request.Key,out var pending))pending.TrySetResult(false);
                if(!dispatcher.HasShutdownStarted)await dispatcher.InvokeAsync(()=>Revision++);
            }
            pipe.Dispose();
        }
    }
    internal static ImageSource? DecodeIcon(string encoded){
        if(string.IsNullOrEmpty(encoded)||encoded.Length>12000)return null;
        try{
            var bytes=Convert.FromBase64String(encoded);
            // Only browser-cached PNG data is accepted; never fetch remote favicons from Mycelview.
            if(bytes.Length<24||!bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))return null;
            int w=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16,4)),h=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20,4));
            if(w<1||h<1||w>128||h>128)return null;
            using var stream=new MemoryStream(bytes);
            var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=16;image.StreamSource=stream;image.EndInit();image.Freeze();return image;
        }catch(Exception ex) when(ex is FormatException or IOException or NotSupportedException or ArgumentException or InvalidOperationException){return null;}
    }
    void BindWindows(){
        var native=nativeWindows();var foreground=foregroundWindow();
        foreach(var client in clients.Values){
            var candidates=native.Where(w=>BrowserKind(w.AppPath)==client.Browser).ToList();
            foreach(var key in client.Mapping.Keys.Where(k=>!client.FocusMappings.Contains(k)).ToArray())client.Mapping.Remove(key);
            foreach(var key in client.Mapping.Keys.Where(id=>!client.Snapshot.Windows.Any(w=>w.Id==id)||!candidates.Any(w=>w.Handle==client.Mapping[id])).ToArray())client.Mapping.Remove(key);
            foreach(var window in client.Snapshot.Windows){
                var active=window.Tabs.FirstOrDefault(t=>t.Active)?.Title??"";
                if(window.Focused&&candidates.FirstOrDefault(w=>w.Handle==foreground&&TitleMatches(w.Title,active)) is {} focused){
                    foreach(var other in clients.Values.Where(c=>c.Browser==client.Browser))
                        foreach(var key in other.Mapping.Where(p=>p.Value==focused.Handle).Select(p=>p.Key).ToArray()){other.Mapping.Remove(key);other.FocusMappings.Remove(key);}
                    client.Mapping[window.Id]=focused.Handle;client.FocusMappings.Add(window.Id);continue;
                }
                if(client.Mapping.ContainsKey(window.Id))continue;
                var matches=candidates.Where(w=>TitleMatches(w.Title,active)).ToList();
                var snapshots=clients.Values.Where(c=>c.Browser==client.Browser).SelectMany(c=>c.Snapshot.Windows).Count(w=>w.Tabs.Any(t=>t.Active&&t.Title==active));
                if(matches.Count==1&&snapshots==1&&!clients.Values.Any(c=>c.Mapping.Values.Contains(matches[0].Handle)))client.Mapping[window.Id]=matches[0].Handle;
            }
        }
    }
    internal BrowserWindowTabs? ForWindow(AppOpenWindow window){
        if(BrowserKind(window.AppPath)==null)return null;
        var entries=clients.Values.SelectMany(c=>c.Mapping.Where(p=>p.Value==window.Handle).Select(p=>(Client:c,Id:p.Key))).ToList();
        if(entries.Count!=1)return null;
        var entry=entries[0];var snapshot=entry.Client.Snapshot.Windows.FirstOrDefault(w=>w.Id==entry.Id);if(snapshot==null)return null;
        return new(entry.Client.Id,snapshot.Id,snapshot.Tabs.Select(t=>new BrowserTabItem(t.Id,t.Title,t.Active,entry.Client.Images.GetValueOrDefault(t.Icon))).ToList());
    }
    internal bool Connected(string browser)=>clients.Values.Any(c=>c.Browser==browser);
    internal async Task<bool> Activate(BrowserWindowTabs window,int tabId){
        if(!clients.TryGetValue(window.Session,out var client)||!client.Snapshot.Windows.Any(w=>w.Id==window.WindowId&&w.Tabs.Any(t=>t.Id==tabId)))return false;
        var id=Guid.NewGuid().ToString("N");var key=client.Id+":"+id;
        var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);requests[key]=completion;
        try{
            await Send(client,new{type="activate",requestId=id,windowId=window.WindowId,tabId});
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(4),stop.Token);
        }catch(Exception ex) when(ex is InvalidDataException or IOException or OperationCanceledException or TimeoutException or ObjectDisposedException){return false;}
        finally{requests.TryRemove(key,out _);}
    }
    async Task Send(Client client,object value){
        await client.Writer.WaitAsync(stop.Token);
        try{await BrowserFrames.Write(client.Pipe,JsonSerializer.SerializeToUtf8Bytes(value,BrowserFrames.Json),stop.Token);}
        finally{client.Writer.Release();}
    }
    public void Dispose(){
        if(ReferenceEquals(Current,this))Current=null;
        stop.Cancel();foreach(var client in clients.Values)client.Pipe.Dispose();
        foreach(var pending in requests.Values)pending.TrySetResult(false);
    }
    [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
}