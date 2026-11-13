using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Mycelview;
internal static class PdfPreviewTests
{
    private static void Check(bool value,string message) {if(!value)throw new InvalidOperationException(message);}
    private static void Fixture(string path, int count = 3)
    {
        var objects=new List<string> {"<< /Type /Catalog /Pages 2 0 R >>",$"<< /Type /Pages /Kids [{string.Join(" ", Enumerable.Range(0, count).Select(i => $"{4+i*2} 0 R"))}] /Count {count} >>","<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"};
        for(int i=0;i<count;i++)
        {
            int width=i==1?792:612,height=i==1?612:792;
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] /Resources << /Font << /F1 3 0 R >> >> /Contents {5+i*2} 0 R >>");
            string contents=$"q {(i==0?"0.1 0.5 0.9":i==1?"0.9 0.3 0.1":"0.1 0.7 0.3")} rg 40 40 250 200 re f Q BT /F1 28 Tf 40 {height-60} Td (Mycelview PDF - Page {i+1}) Tj ET";
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(contents)} >>\nstream\n{contents}\nendstream");
        }
        var pdf=new StringBuilder("%PDF-1.4\n");var offsets=new List<int>{0};
        for(int i=0;i<objects.Count;i++){offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));pdf.Append($"{i+1} 0 obj\n{objects[i]}\nendobj\n");}
        int xref=Encoding.ASCII.GetByteCount(pdf.ToString());pdf.Append($"xref\n0 {objects.Count+1}\n0000000000 65535 f \n");
        foreach(var offset in offsets.Skip(1))pdf.Append($"{offset:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Count+1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");File.WriteAllText(path,pdf.ToString(),Encoding.ASCII);
    }
    internal static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);string path=Path.Combine(directory,"한글 [PDF] 테스트.pdf");Fixture(path);var original=File.ReadAllBytes(path);
        var first=await FilePreviewReader.ReadAsync(path,default);
        Check(first.PageCount==3 && first.PageNumber==1 && first.Image is {IsFrozen:true},"Native PDF must render page one and report count.");
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(first.Image!));using(var file=File.Create(Path.Combine(directory,"pdf-page1.png")))png.Save(file);
        var second=await FilePreviewReader.ReadAsync(path,default,2,1.5);
        Check(second.PageNumber==2 && second.PageWidth>second.PageHeight && second.Image!.PixelWidth>first.Image!.PixelWidth,"Page navigation, landscape and high resolution zoom must render.");
        var last=await FilePreviewReader.ReadAsync(path,default,900,4);
        Check(last.PageNumber==3 && last.Image!.PixelWidth<=4096 && last.Image.PixelHeight<=4096,"Page bounds and render resolution limit must hold.");
        using(var cancel=new CancellationTokenSource()){cancel.Cancel();bool cancelled=false;try{await FilePreviewReader.ReadAsync(path,cancel.Token);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"Cancelled PDF work must abort.");}
        using var controller=new FilePreviewController(new Window(),true);
        async Task SettleZoom()
        {
            for (int i = 0; i < 100 && controller.View!.IsZooming; i++) await Task.Delay(20);
            Check(!controller.View!.IsZooming, "Smooth zoom must settle.");
            await controller.View.pdfPages.Rendering;
        }
        controller.Show(path);await controller.Loading;Check(controller.View!.Width == 900 && controller.View.Height == Math.Min(800, SystemParameters.WorkArea.Height - 40), "Preview default dimensions and screen-height cap must hold.");Check(controller.PageCount==3,"Preview controller must publish PDF count.");
        controller.View!.nextPage.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));await controller.Loading;Check(controller.Data?.PageNumber==2,"Next button must navigate.");
        controller.View!.previousPage.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));await controller.Loading;Check(controller.Data?.PageNumber==1,"Previous button must navigate.");
        controller.GoToPage(2);controller.GoToPage(3);await controller.Loading;Check(controller.Data?.PageNumber==3,"Latest page wins rapid navigation.");
        controller.SetPdfScale(2);await controller.Loading;Check(controller.Data?.RenderScale==2,"Controller zoom must rerender.");
        Check(!controller.View!.TryZoomWheel(120, System.Windows.Input.ModifierKeys.None), "Normal wheel must retain scrolling.");
        Check(controller.View.TryZoomWheel(120, System.Windows.Input.ModifierKeys.Control), "Ctrl wheel must be consumed for PDF zoom.");
        await SettleZoom(); Check(Math.Abs(controller.PdfScale - 2.3) < .001, "Ctrl wheel up must smoothly increase PDF scale.");
        controller.View.TryZoomWheel(-120, System.Windows.Input.ModifierKeys.Control); await SettleZoom();
        Check(Math.Abs(controller.PdfScale - 2) < .001, "Ctrl wheel down must reduce PDF scale.");
        controller.GoToPage(1);controller.SetPdfScale(.65);await controller.Loading;
        var content=(FrameworkElement)controller.View!.Content;content.Measure(new Size(820,680));content.Arrange(new Rect(0,0,820,680));content.UpdateLayout();
        await Task.Delay(150); await controller.View.pdfPages.Rendering; content.UpdateLayout();
        var screenshot=new RenderTargetBitmap(820,680,96,96,PixelFormats.Pbgra32);screenshot.Render(content);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(screenshot));using(var file=File.Create(Path.Combine(directory,"pdf-preview.png")))encoder.Save(file);
        await Task.Delay(150); await controller.View.pdfPages.Rendering;
        Check(controller.View.pdfPages.ExtentHeight > controller.View.pdfPages.ViewportHeight, "PDF pages must form one scrollable document.");
        controller.View.pdfPages.ScrollToVerticalOffset((first.PageSizes[0].Height * .65) + 18);
        content.UpdateLayout(); await Task.Delay(150); await controller.View.pdfPages.Rendering;
        Check(controller.PageNumber == 2, $"Continuous scrolling must update current page without page buttons. Page={controller.PageNumber}, Offset={controller.View.pdfPages.VerticalOffset}, Viewport={controller.View.pdfPages.ViewportHeight}, Extent={controller.View.pdfPages.ExtentHeight}, Rendered={controller.View.pdfPages.RenderedCount}");
        controller.SetPdfScale(1.3); await controller.Loading; content.UpdateLayout();
        await Task.Delay(150); await controller.View.pdfPages.Rendering;
        controller.View.Opacity = 0; controller.View.ShowActivated = false; controller.View.ShowInTaskbar = false; controller.View.Show();
        controller.View.UpdateLayout(); await Task.Delay(100);
        var anchor = new Point(controller.View.pdfPages.ViewportWidth * .45, controller.View.pdfPages.ViewportHeight * .4);
        var beforeAnchor = controller.View.pdfPages.DocumentPointAt(anchor);
        double beforeZoom = controller.View.Zoom;
        controller.View.TryZoomWheel(240, System.Windows.Input.ModifierKeys.Control, controller.View.pdfPages.TranslatePoint(anchor, controller.View));
        Check(controller.View.Zoom == beforeZoom && controller.View.IsZooming, "Wheel zoom must animate rather than jump.");
        await SettleZoom(); content.UpdateLayout();
        var afterAnchor = controller.View.pdfPages.DocumentPointAt(anchor);
        Check(beforeAnchor.Page == afterAnchor.Page && (beforeAnchor.Position - afterAnchor.Position).Length < 2, $"PDF point under cursor must remain stationary during zoom. before={beforeAnchor} after={afterAnchor} viewport={controller.View.pdfPages.ViewportWidth} offset={controller.View.pdfPages.HorizontalOffset}");
        var longPdf = Path.Combine(directory, "long.pdf"); Fixture(longPdf, 30);
        controller.Select(longPdf); await controller.Loading; content.UpdateLayout();
        controller.View.pdfPages.ScrollToVerticalOffset(controller.Data!.PageSizes.Take(15).Sum(size => size.Height + 18)); content.UpdateLayout();
        await Task.Delay(150); await controller.View.pdfPages.Rendering;
        Check(controller.View.pdfPages.RenderedCount <= 5 && controller.PageNumber >= 15, "Long PDFs must render only viewport neighbours and allow deep scrolling.");
        var text=Path.Combine(directory,"next.txt");File.WriteAllText(text,"다음 파일");controller.GoToPage(2);controller.Select(text);await controller.Loading;Check(controller.Data?.Text=="다음 파일" && controller.PageCount==0,"File selection cancels PDF rendering and clears paging.");
        controller.View!.TryZoomWheel(120, System.Windows.Input.ModifierKeys.Control);
        await SettleZoom(); Check(Math.Abs(controller.View.Zoom - 1.15) < .001, "Ctrl wheel must zoom text.");
        controller.Select(Path.Combine(directory, "pdf-page1.png")); await controller.Loading;
        controller.View.TryZoomWheel(120, System.Windows.Input.ModifierKeys.Control);
        await SettleZoom(); Check(Math.Abs(controller.View.Zoom - 1.15) < .001, "Ctrl wheel must zoom images and reset scale on file change.");
        controller.View.TryZoomWheel(12000, System.Windows.Input.ModifierKeys.Control);
        await SettleZoom(); Check(controller.View.Zoom == 4, "Image zoom must respect maximum scale.");
        controller.View.TryZoomWheel(-12000, System.Windows.Input.ModifierKeys.Control);
        await SettleZoom(); Check(controller.View.Zoom == .25, "Image zoom must respect minimum scale.");
        var invalid=Path.Combine(directory,"broken.pdf");File.WriteAllText(invalid,"not a pdf");controller.Select(invalid);await controller.Loading;Check(controller.Data==null && controller.Status.Contains("표시할 수 없습니다"),"Invalid PDF must display an error instead of stale content.");
        controller.Select(path);controller.Close();await controller.Loading;Check(!controller.IsOpen && controller.Data==null,"Closing cancels pending PDF work.");
        Check(original.SequenceEqual(File.ReadAllBytes(path)),"Preview must not change the source PDF.");
        File.WriteAllText(Path.Combine(directory,"pdf-results.txt"),"PASS: cursor-anchored animated Ctrl-wheel zoom, image/text zoom limits, 900x800 default with work-area cap, real Windows PDF rendering, 3 pages, landscape, zoom resolution, bounds, cancellation, rapid latest-page selection, file switching, corrupt PDF, close during load, source preservation, continuous scrolling, mixed page dimensions, viewport-bounded rendering of 30 pages, hidden WPF content rendering. Preview verification used an invisible, non-activating test window.");
    }
}

