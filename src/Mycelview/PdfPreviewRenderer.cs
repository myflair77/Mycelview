using System.IO;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;
namespace Mycelview;

// Windows renders just the requested page. Keep one parsed document; never cache page bitmaps.
internal static class PdfPreviewRenderer
{
    private static readonly SemaphoreSlim gate = new(1);
    private static PdfDocument? cached;
    private static System.Windows.Size[] pageSizes = [];
    private static string? cachePath;
    private static long cacheLength;
    private static DateTime cacheModified;
    internal static async Task<FilePreviewData> ReadAsync(string path,int pageNumber,double scale,CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            return await Task.Run(async () =>
            {
                token.ThrowIfCancellationRequested();
                var info=new FileInfo(path);long length=info.Length;var modified=info.LastWriteTimeUtc;
                if(cached==null || !StringComparer.OrdinalIgnoreCase.Equals(cachePath,info.FullName) || cacheLength!=length || cacheModified!=modified)
                {
                    cached=null;cachePath=null;
                    var file=await StorageFile.GetFileFromPathAsync(info.FullName).AsTask(token);
                    var document=await PdfDocument.LoadFromFileAsync(file).AsTask(token);
                    token.ThrowIfCancellationRequested();
                    var sizes = new System.Windows.Size[checked((int)document.PageCount)];
                    for (int i = 0; i < sizes.Length; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        using var item = document.GetPage((uint)i);
                        sizes[i] = new System.Windows.Size(item.Size.Width, item.Size.Height);
                    }
                    cached=document;pageSizes=sizes;cachePath=info.FullName;cacheLength=length;cacheModified=modified;
                }
                int count=checked((int)cached.PageCount);
                if(count==0) throw new InvalidDataException("페이지가 없는 PDF입니다.");
                int number=Math.Clamp(pageNumber,1,count);scale=Math.Clamp(scale,.25,4);
                using var page=cached.GetPage((uint)(number-1));
                var size=page.Size;
                if(size.Width<=0 || size.Height<=0 || !double.IsFinite(size.Width+size.Height)) throw new InvalidDataException("PDF 페이지 크기가 올바르지 않습니다.");
                double pixels=Math.Min(1.5*scale,4096/Math.Max(size.Width,size.Height));
                using var stream=new InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(stream,new PdfPageRenderOptions { DestinationWidth=(uint)Math.Max(1,Math.Round(size.Width*pixels)),DestinationHeight=(uint)Math.Max(1,Math.Round(size.Height*pixels)),BackgroundColor=Windows.UI.Color.FromArgb(255,255,255,255) }).AsTask(token);
                token.ThrowIfCancellationRequested();
                if(stream.Size>128*1024*1024) throw new InvalidDataException("렌더링 결과가 너무 큽니다.");
                var bytes=new byte[(int)stream.Size];
                using(var reader=new DataReader(stream.GetInputStreamAt(0))) {await reader.LoadAsync((uint)bytes.Length).AsTask(token);reader.ReadBytes(bytes);}
                using var png=new MemoryStream(bytes);
                var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=png;bitmap.EndInit();bitmap.Freeze();
                token.ThrowIfCancellationRequested();
                return new FilePreviewData(info.FullName,length,info.LastWriteTime,"",bitmap,$"PDF · {number:N0} / {count:N0} 페이지 · {scale:P0}",bytes.Length) {PageSizes=pageSizes,PageNumber=number,PageCount=count,PageWidth=size.Width,PageHeight=size.Height,RenderScale=scale};
            },token);
        }
        finally {gate.Release();}
    }
}
