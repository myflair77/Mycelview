using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Mycelview;
internal static class ThumbnailCache
{
    static readonly SemaphoreSlim workers=new(2);
    static readonly ConcurrentDictionary<string,ImageSource> cache=new(StringComparer.OrdinalIgnoreCase);
    static readonly ConcurrentQueue<string> order=new();
    internal static int Count=>cache.Count;
    internal static async Task<ImageSource> LoadAsync(FileEntry entry,int pixels,CancellationToken token)
    {
        pixels=Math.Clamp(pixels,32,256);
        string key=$"{entry.FullPath}|{entry.Modified.Ticks}|{entry.Length}|{pixels}";
        if(cache.TryGetValue(key,out var found))return found;
        await workers.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();
            if(cache.TryGetValue(key,out found))return found;
            var image=await Task.Run(()=>ShellIcons.Thumbnail(entry.FullPath,pixels) ?? ShellIcons.ForSize(entry.FullPath,false,pixels),token);
            token.ThrowIfCancellationRequested();
            if(cache.TryAdd(key,image))order.Enqueue(key);
            while(cache.Count>128 && order.TryDequeue(out var old))cache.TryRemove(old,out _);
            return image;
        }
        finally{workers.Release();}
    }
}
internal sealed class ThumbnailImage : Image
{
    public static readonly DependencyProperty EntryProperty=DependencyProperty.Register(nameof(Entry),typeof(FileEntry),typeof(ThumbnailImage),new PropertyMetadata(null,Changed));
    public static readonly DependencyProperty PixelsProperty=DependencyProperty.Register(nameof(Pixels),typeof(int),typeof(ThumbnailImage),new PropertyMetadata(64,Changed));
    public FileEntry? Entry {get=>(FileEntry?)GetValue(EntryProperty);set=>SetValue(EntryProperty,value);}
    public int Pixels {get=>(int)GetValue(PixelsProperty);set=>SetValue(PixelsProperty,value);}
    CancellationTokenSource? pending;
    public ThumbnailImage()
    {
        Stretch=Stretch.Uniform;
        Loaded+=(_,_)=>Load();
        Unloaded+=(_,_)=>{pending?.Cancel();pending=null;Source=null;};
    }
    static void Changed(DependencyObject owner,DependencyPropertyChangedEventArgs e){var image=(ThumbnailImage)owner;if(image.IsLoaded)image.Load();}
    async void Load()
    {
        pending?.Cancel();
        if(Entry is not {} entry){Source=null;return;}
        Source=entry.IsDirectory ? ShellIcons.ForSize(entry.FullPath,true,Pixels) : ShellIcons.ForPath(entry.FullPath,false);
        if(entry.IsDirectory || entry.IsShellItem || Pixels<=16)return;
        var request=new CancellationTokenSource();pending=request;
        try
        {
            int pixels=(int)Math.Ceiling(Pixels*VisualTreeHelper.GetDpi(this).DpiScaleX);
            var image=await ThumbnailCache.LoadAsync(entry,pixels,request.Token);
            if(!request.IsCancellationRequested && ReferenceEquals(pending,request) && IsLoaded)Source=image;
        }
        catch(OperationCanceledException){}
        catch(Exception){/* Keep the file-type icon when a provider cannot preview the file. */}
        finally{if(ReferenceEquals(pending,request))pending=null;request.Dispose();}
    }
}
