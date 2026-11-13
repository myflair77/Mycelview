using System.IO;
using System.Text;
using System.Windows.Media.Imaging;
namespace Mycelview;
internal sealed record FilePreviewData(string Path, long Length, DateTime Modified, string Text, BitmapSource? Image, string Message, int BytesRead)
{
    public IReadOnlyList<System.Windows.Size> PageSizes { get; init; } = [];
    public int PageNumber { get; init; }
    public int PageCount { get; init; }
    public double PageWidth { get; init; }
    public double PageHeight { get; init; }
    public double RenderScale { get; init; } = 1;
}
internal static class FilePreviewReader
{
    internal const int TextLimit = 128 * 1024;
    internal const int ImageLimit = 16 * 1024 * 1024;
    private static readonly SemaphoreSlim decoder = new(1);
    private static readonly HashSet<string> textTypes = new(".txt .csv .log .md .json .xml .yaml .yml .ini .config .cs .py .js .ts .tsx .jsx .html .htm .css .sql .bat .cmd .ps1 .sh .toml .sln .csproj .props .gitignore".Split(' '), StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> imageTypes = new(".png .jpg .jpeg .bmp .gif .tif .tiff .ico".Split(' '), StringComparer.OrdinalIgnoreCase);
    internal static async Task<FilePreviewData> ReadAsync(string path, CancellationToken token, int pageNumber = 1, double scale = 1)
    {
        if (Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) return await PdfPreviewRenderer.ReadAsync(path,pageNumber,scale,token);
        await decoder.WaitAsync(token);
        try
        {
            return await Task.Run(async () =>
            {
                token.ThrowIfCancellationRequested();
                var info = new FileInfo(path); var length = info.Length; var modified = info.LastWriteTime; var extension = info.Extension;
                FilePreviewData Result(string text = "", BitmapSource? image = null, string message = "", int bytes = 0) => new(path, length, modified, text, image, message, bytes);
                bool imageFile = imageTypes.Contains(extension);
                if (!imageFile && !textTypes.Contains(extension)) return Result(message: "이 형식은 아직 미리보기를 지원하지 않습니다. ‘열기’로 확인하세요.");
                if (imageFile && length > ImageLimit) return Result(message: "16 MB를 넘는 이미지는 정보만 표시합니다. ‘열기’로 원본을 확인하세요.");
                var limit = imageFile ? ImageLimit : TextLimit;
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var bytes = new byte[(int)Math.Min(file.Length, limit)]; int read = 0;
                while (read < bytes.Length) { var count = await file.ReadAsync(bytes.AsMemory(read), token); if (count == 0) break; read += count; }
                token.ThrowIfCancellationRequested();
                if (imageFile)
                {
                    using var stream = new MemoryStream(bytes, 0, read, false);
                    var header = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    var frame = header.Frames[0]; int width = frame.PixelWidth, height = frame.PixelHeight;
                    if (width <= 0 || height <= 0 || (long)width * height > 100_000_000) return Result(message: "이미지 해상도가 너무 커 정보만 표시합니다.", bytes: read);
                    token.ThrowIfCancellationRequested(); stream.Position = 0;
                    var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    if (width >= height) bitmap.DecodePixelWidth = Math.Min(width, 1600); else bitmap.DecodePixelHeight = Math.Min(height, 1600);
                    bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); token.ThrowIfCancellationRequested();
                    return Result(image: bitmap, message: $"{width:N0} × {height:N0} · 미리보기 최대 1,600px" + (header.Frames.Count > 1 ? " · 첫 프레임/페이지만 표시" : ""), bytes: read);
                }
                bool partial = length > read; int offset = 0; Encoding encoding;
                if (read >= 4 && bytes[0] == 0xff && bytes[1] == 0xfe && bytes[2] == 0 && bytes[3] == 0) { encoding = new UTF32Encoding(false, false, true); offset = 4; }
                else if (read >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xfe && bytes[3] == 0xff) { encoding = new UTF32Encoding(true, false, true); offset = 4; }
                else if (read >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe) { encoding = new UnicodeEncoding(false, false, true); offset = 2; }
                else if (read >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff) { encoding = new UnicodeEncoding(true, false, true); offset = 2; }
                else { encoding = new UTF8Encoding(false, true); if (read >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf) offset = 3; }
                string Decode(Encoding selected)
                {
                    var chars = new char[selected.GetMaxCharCount(read - offset)];
                    var size = selected.GetDecoder().GetChars(bytes, offset, read - offset, chars, 0, !partial);
                    return new string(chars, 0, size);
                }
                string text;
                try { text = Decode(encoding); }
                catch (DecoderFallbackException) when (offset == 0)
                { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); encoding = Encoding.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback); text = Decode(encoding); }
                if (text.Contains('\0')) return Result(message: "바이너리 데이터가 포함되어 정보만 표시합니다.", bytes: read);
                if (text.Length > 40000) { text = text[..40000]; partial = true; }
                var lines = text.Split('\n');
                for (int i = 0; i < lines.Length; i++) if (lines[i].Length > 4000) { lines[i] = lines[i][..4000] + " …"; partial = true; }
                token.ThrowIfCancellationRequested();
                return Result(string.Join('\n', lines), message: (partial ? "일부만 표시 · 최대 128 KB / 40,000자, 긴 줄 4,000자" : "전체 텍스트") + " · " + encoding.WebName, bytes: read);
            }, token);
        }
        finally { decoder.Release(); }
    }
}

