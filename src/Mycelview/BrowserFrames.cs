using System.IO;
using System.Buffers.Binary;
using System.Text.Json;
namespace Mycelview.BrowserIntegration;
internal static class BrowserFrames
{
    internal const int Limit=4*1024*1024;
    internal static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,PropertyNameCaseInsensitive=true};
    internal static async Task<byte[]?> Read(Stream stream,CancellationToken token=default){
        var prefix=new byte[4];int first=await stream.ReadAsync(prefix.AsMemory(0,1),token);
        if(first==0)return null;
        await stream.ReadExactlyAsync(prefix.AsMemory(1),token);
        int size=BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if(size<=0||size>Limit)throw new InvalidDataException("Invalid browser message size.");
        var payload=new byte[size];await stream.ReadExactlyAsync(payload,token);return payload;
    }
    internal static async Task Write(Stream stream,byte[] payload,CancellationToken token=default){
        if(payload.Length<=0||payload.Length>Limit)throw new InvalidDataException("Invalid browser message size.");
        var prefix=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(prefix,payload.Length);
        await stream.WriteAsync(prefix,token);await stream.WriteAsync(payload,token);await stream.FlushAsync(token);
    }
    internal static string PipeName=>"Mycelview.BrowserTabs."+System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
}