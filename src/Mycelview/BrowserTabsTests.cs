using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Mycelview.BrowserIntegration;
namespace Mycelview;
internal static class BrowserTabsTests
{
    static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    static async Task Until(Func<bool> check){for(int i=0;i<100&&!check();i++)await Task.Delay(20);Check(check(),"Bridge timeout");}
    internal static async Task Run(string output){
        var payload=System.Text.Encoding.UTF8.GetBytes("{\"title\":\"탭 제목\"}");
        using(var stream=new MemoryStream()){
            await BrowserFrames.Write(stream,payload);stream.Position=0;
            Check((await BrowserFrames.Read(stream))!.SequenceEqual(payload),"Frame roundtrip");
            Check(await BrowserFrames.Read(stream)==null,"EOF");
        }
        foreach(var bad in new[]{new byte[]{255,255,255,127},new byte[]{8,0,0,0,1}}){
            bool rejected=false;try{await BrowserFrames.Read(new MemoryStream(bad));}catch(Exception error) when(error is IOException or InvalidDataException){rejected=true;}
            Check(rejected,"Malformed frame rejected");
        }
        Check(AppWindows.SameExecutable(@"C:\Whale\Application\whale.exe",@"C:\Whale\Application\5.41.429.5\whale.exe"),"Versioned Whale identity");
        Check(!AppWindows.SameExecutable(@"C:\Other\whale.exe",@"C:\Whale\Application\5.41.429.5\whale.exe"),"Separate installs remain separate");
        foreach(var kind in new[]{"chrome","edge","whale"}){
            string exe=kind=="edge"?"msedge.exe":kind+".exe";
            var native=new AppOpenWindow((IntPtr)42,"테스트 - Browser",exe);
            string pipeName="Mycelview.Test."+Guid.NewGuid().ToString("N");
            using var bridge=new BrowserTabsBridge(Application.Current.Dispatcher,pipeName,()=>[native],()=>native.Handle);
            using var pipe=new NamedPipeClientStream(".",pipeName,PipeDirection.InOut,PipeOptions.Asynchronous);
            await pipe.ConnectAsync(2000);
            async Task Send(object message)=>await BrowserFrames.Write(pipe,JsonSerializer.SerializeToUtf8Bytes(message,BrowserFrames.Json));
            await Send(new{type="hello",browser=kind});
            using(var ready=JsonDocument.Parse((await BrowserFrames.Read(pipe))!))Check(ready.RootElement.GetProperty("type").GetString()=="ready","Handshake");
            await Send(new{type="snapshot",windows=new[]{new{id=7,focused=true,tabs=new[]{new{id=9,title="테스트",active=true,icon=""},new{id=10,title="다른 탭",active=false,icon=""}}}},icons=new{}});
            await Until(()=>bridge.ForWindow(native)?.Tabs.Count==2);
            var window=bridge.ForWindow(native)!;
            Check(!await bridge.Activate(window,999),"Reject stale tab");
            var activation=bridge.Activate(window,10);
            using(var command=JsonDocument.Parse((await BrowserFrames.Read(pipe))!)){
                var root=command.RootElement;
                Check(root.GetProperty("windowId").GetInt32()==7&&root.GetProperty("tabId").GetInt32()==10,"Exact window/tab routing");
                await Send(new{type="result",requestId=root.GetProperty("requestId").GetString(),ok=true});
            }
            Check(await activation,"Activation acknowledged");
            pipe.Dispose();await Until(()=>!bridge.Connected(kind));
        }
        var actualWhale=AppWindows.List(@"C:\Program Files\Naver\Naver Whale\Application\whale.exe");
        File.WriteAllText(Path.Combine(output,"whale-windows.txt"),"Matched windows: "+actualWhale.Count);
        File.WriteAllText(Path.Combine(output,"passed.txt"),"Framing, malformed frames, 3 browser sessions, exact tab routing, stale tabs, disconnect, Whale version matching passed.");
    }
}