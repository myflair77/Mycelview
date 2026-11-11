using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace PinShelf;
internal static class FilePreviewTests
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    internal static async Task RunAsync(MainWindow owner, string directory)
    {
        var root = Path.Combine(directory, "preview-fixtures"); Directory.CreateDirectory(root);
        var small = Path.Combine(root, "small.txt"); File.WriteAllText(small, "한글 미리보기\nSecond line");
        var large = Path.Combine(root, "large.txt"); File.WriteAllText(large, string.Concat(Enumerable.Repeat("한글 내용입니다\n", 50000)));
        var unicode = Path.Combine(root, "utf16.txt"); File.WriteAllText(unicode, "유니코드 내용", Encoding.Unicode);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); var korean = Path.Combine(root, "cp949.csv"); File.WriteAllText(korean, "이름,금액\n예산,1000", Encoding.GetEncoding(949));
        var binary = Path.Combine(root, "binary.txt"); File.WriteAllBytes(binary, [1, 0, 2, 0]);
        var unsupported = Path.Combine(root, "document.docx"); File.WriteAllText(unsupported, "%PDF fixture");
        var png = Path.Combine(root, "image.png");
        var pixels = new byte[3200 * 1800 * 3]; for (int i=0;i<pixels.Length;i+=3) { pixels[i]=248; pixels[i+1]=135; pixels[i+2]=22; }
        var source = BitmapSource.Create(3200,1800,96,96,PixelFormats.Bgr24,null,pixels,3200*3);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source)); using(var output=File.Create(png)) encoder.Save(output);
        var tooLarge = Path.Combine(root,"large.png"); using(var output=File.Create(tooLarge)) output.SetLength(FilePreviewReader.ImageLimit+1L);
        var truncated = await FilePreviewReader.ReadAsync(large, default);
        Check(truncated.BytesRead <= FilePreviewReader.TextLimit && truncated.Text.Length <= 40000 && truncated.Message.Contains("일부") && !truncated.Text.Contains('\ufffd'), "Large UTF8 preview is bounded and does not split characters.");
        Check((await FilePreviewReader.ReadAsync(unicode,default)).Text == "유니코드 내용", "UTF16 BOM supported.");
        Check((await FilePreviewReader.ReadAsync(korean,default)).Text.Contains("예산"), "CP949 Korean supported.");
        Check((await FilePreviewReader.ReadAsync(binary,default)).Text.Length == 0, "Binary text is not rendered.");
        Check((await FilePreviewReader.ReadAsync(unsupported,default)).BytesRead == 0, "Unsupported file uses metadata without reading contents.");
        Check((await FilePreviewReader.ReadAsync(tooLarge,default)).BytesRead == 0, "Oversized image is not decoded.");
        var bitmap = await FilePreviewReader.ReadAsync(png,default);
        Check(bitmap.Image is { IsFrozen: true, PixelWidth: 1600, PixelHeight: 900 }, "Image decoded down to bounded dimensions off the UI thread.");
        using(var cancel=new CancellationTokenSource()) { cancel.Cancel(); bool stopped=false; try { await FilePreviewReader.ReadAsync(large,cancel.Token); } catch(OperationCanceledException) { stopped=true; } Check(stopped,"Cancelled read stops before work."); }
        var preview=owner.FilePreview;
        try
        {
            var menu=owner.FileMenus.Build([new(small,false)]);
            menu.Items.OfType<MenuItem>().Single(x => Equals(x.Tag,"preview")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await preview.Loading;
            Check(preview.IsOpen && preview.Data?.Text.Contains("한글 미리보기") == true,"Context menu opens actual preview window.");
            preview.Show(large); var obsolete=preview.Loading; preview.Select(small); await Task.WhenAll(obsolete,preview.Loading);
            Check(preview.Data?.Path == small && preview.Data.Text.Contains("Second line"),"Rapid selection cancels stale load.");
            preview.Select(Path.Combine(root,"missing.txt")); await preview.Loading;
            Check(preview.Data == null && preview.Status.Contains("표시할 수 없습니다"),"Missing file clears old preview and reports error.");
            preview.Close();
            await owner.ViewModel.Explorer.NavigateAsync(root); owner.ExplorerList.SelectedItem=owner.ViewModel.Explorer.Entries.Single(x=>x.FullPath==small); owner.ExplorerList.Focus();
            KeyEventArgs Key(Key key) => new(Keyboard.PrimaryDevice, PresentationSource.FromVisual(owner.ExplorerList)!, 0, key) { RoutedEvent=Keyboard.PreviewKeyDownEvent };
            var space=Key(System.Windows.Input.Key.Space); owner.ExplorerList.RaiseEvent(space); await preview.Loading;
            Check(space.Handled && preview.IsOpen && preview.Data?.Path==small,"Space key route opens selected file.");
            owner.ExplorerList.SelectedItem=owner.ViewModel.Explorer.Entries.Single(x=>x.FullPath==png); await preview.Loading;
            Check(preview.Data?.Image != null && preview.CurrentPath==png,"List selection updates already open preview.");
            var view=Application.Current.Windows.OfType<FilePreviewWindow>().Single();
            await view.Dispatcher.InvokeAsync(()=>view.UpdateLayout(),DispatcherPriority.ContextIdle);
            var render=new RenderTargetBitmap((int)view.ActualWidth,(int)view.ActualHeight,96,96,PixelFormats.Pbgra32); render.Render(view);
            var screenshot=new PngBitmapEncoder(); screenshot.Frames.Add(BitmapFrame.Create(render)); using(var file=File.Create(Path.Combine(directory,"file-preview.png"))) screenshot.Save(file);
            var escape=Key(System.Windows.Input.Key.Escape); owner.RaiseEvent(escape); Check(escape.Handled&&!preview.IsOpen,"Esc closes preview from owner.");
            preview.Show(large); var pending=preview.Loading; preview.Close(); await pending; Check(!preview.IsOpen&&preview.Data==null,"Close cancels pending result without resurrecting window.");
            Check(File.ReadAllText(small)=="한글 미리보기\nSecond line","Preview does not modify source.");
        }
        finally { preview.Close(); }
        File.WriteAllText(Path.Combine(directory,"file-preview-results.txt"),"PASS: bounded UTF8/UTF16/CP949 text, binary/unsupported/oversized fallback, image downsampling, cancellation, stale-result protection, actual menu/Space/Esc routed events, selection updates, close during load and source preservation.");
    }
}

