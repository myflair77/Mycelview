using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using Mycelview.BrowserIntegration;
try{
    if(args.Length==1 && (args[0]=="--register" || args[0]=="--unregister")){
        string manifest=Path.Combine(AppContext.BaseDirectory,"native-host.json");
        string[] keys={@"Software\Google\Chrome\NativeMessagingHosts\com.mycelview.tabs",@"Software\Microsoft\Edge\NativeMessagingHosts\com.mycelview.tabs",@"Software\Naver\Naver Whale\NativeMessagingHosts\com.mycelview.tabs"};
        if(args[0]=="--register"){
            string id=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"extension-id.txt")).Trim();
            if(id.Length!=32||id.Any(c=>c<'a'||c>'p'))return 4;
            File.WriteAllText(manifest,JsonSerializer.Serialize(new{name="com.mycelview.tabs",description="Mycelview browser tab connection",path=Environment.ProcessPath,type="stdio",allowed_origins=new[]{"chrome-extension://"+id+"/","whale-extension://"+id+"/"}}));
            foreach(string key in keys){using var registry=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(key);registry.SetValue("",manifest);}
        }else{
            foreach(string key in keys){
                using var registry=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key);
                bool owned=string.Equals(registry?.GetValue("") as string,manifest,StringComparison.OrdinalIgnoreCase);
                registry?.Dispose();if(owned)Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(key,false);
            }
        }
        return 0;
    }
    var expected=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"extension-id.txt")).Trim();
    if(args.Length==0||!new[]{"chrome-extension://"+expected+"/","whale-extension://"+expected+"/"}.Contains(args[0],StringComparer.Ordinal))return 2;
    var browser=BrowserParent();if(browser==null)return 3;
    using var pipe=new NamedPipeClientStream(".",BrowserFrames.PipeName,PipeDirection.InOut,PipeOptions.Asynchronous);
    using var stop=new CancellationTokenSource();await pipe.ConnectAsync(8000,stop.Token);
    await BrowserFrames.Write(pipe,JsonSerializer.SerializeToUtf8Bytes(new{type="hello",browser},BrowserFrames.Json),stop.Token);
    using var input=Console.OpenStandardInput();using var output=Console.OpenStandardOutput();
    async Task Relay(Stream from,Stream to){while(await BrowserFrames.Read(from,stop.Token) is {} packet)await BrowserFrames.Write(to,packet,stop.Token);}
    var incoming=Relay(input,pipe);var outgoing=Relay(pipe,output);
    await Task.WhenAny(incoming,outgoing);stop.Cancel();pipe.Dispose();
    try{await Task.WhenAll(incoming,outgoing);}catch(OperationCanceledException){}catch(IOException){}
    return 0;
}catch(Exception ex) when(ex is InvalidDataException or IOException or UnauthorizedAccessException or TimeoutException or System.ComponentModel.Win32Exception or InvalidOperationException){
    Console.Error.WriteLine("Mycelview browser connection unavailable.");return 1;
}
static string? BrowserParent(){
    int pid=Environment.ProcessId;
    for(int depth=0;depth<6;depth++){
        using var process=Process.GetProcessById(pid);
        var name=process.ProcessName.ToLowerInvariant();
        if(name=="chrome")return "chrome";if(name=="msedge")return "edge";if(name=="whale")return "whale";
        var basic=new ProcessBasicInformation();
        if(NtQueryInformationProcess(process.Handle,0,ref basic,Marshal.SizeOf<ProcessBasicInformation>(),out _)!=0)return null;
        pid=(int)basic.Parent;
    }
    return null;
}
[DllImport("ntdll.dll")]static extern int NtQueryInformationProcess(IntPtr handle,int info,ref ProcessBasicInformation value,int size,out int returned);
[StructLayout(LayoutKind.Sequential)]struct ProcessBasicInformation{public IntPtr ExitStatus,Peb,Affinity,Priority,Id,Parent;}