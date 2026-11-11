using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace PinShelf;
internal sealed class FilePreviewController(Window owner, bool headless = false) : IDisposable
{
    private FilePreviewWindow? window;
    private CancellationTokenSource? cancellation;
    private int generation;
    internal string? CurrentPath { get; private set; }
    internal FilePreviewData? Data { get; private set; }
    internal string Status { get; private set; } = "파일을 선택하세요.";
    internal bool IsOpen => window != null;
    internal FilePreviewWindow? View => window;
    internal bool IsPdf => string.Equals(Path.GetExtension(CurrentPath),".pdf",StringComparison.OrdinalIgnoreCase);
    internal int PageNumber { get; private set; } = 1;
    internal int PageCount { get; private set; }
    internal double PdfScale { get; private set; } = 1;
    internal Task Loading { get; private set; } = Task.CompletedTask;
    internal event Action? Changed;
    internal void Show(string path)
    {
        if(window==null) {window=new FilePreviewWindow(owner,this);window.Closed+=(_,_)=>{window=null;Cancel();};if(!headless)window.Show();}
        Select(path,true);
    }
    internal void Select(string? path,bool force=false)
    {
        if(!IsOpen || !force && string.Equals(path,CurrentPath,StringComparison.OrdinalIgnoreCase))return;
        CurrentPath=path;PageNumber=1;PageCount=0;PdfScale=1;StartLoad();
    }
    internal void GoToPage(int page)
    {
        if(!IsOpen || !IsPdf || PageCount==0)return;
        int target=Math.Clamp(page,1,PageCount);if(target==PageNumber)return;
        PageNumber=target;StartLoad();
    }
    internal void SetPdfScale(double scale)
    {
        if(!IsOpen || !IsPdf || !double.IsFinite(scale))return;
        scale=Math.Clamp(scale,.25,4);if(Math.Abs(scale-PdfScale)<.001)return;
        PdfScale=scale;StartLoad();
    }
    internal void SetInteractivePdfScale(double scale)
    {
        PdfScale = scale;
        if (Data != null) Data = Data with { RenderScale = scale };
    }
    private void StartLoad()
    {
        cancellation?.Cancel();var request=new CancellationTokenSource();cancellation=request;int version=++generation;
        Data=null;Status=CurrentPath==null?"파일을 선택하세요.":"미리보기를 불러오는 중…";Changed?.Invoke();
        Loading=LoadAsync(CurrentPath,PageNumber,PdfScale,version,request);
    }
    private async Task LoadAsync(string? path,int page,double scale,int version,CancellationTokenSource request)
    {
        try
        {
            if(path==null)return;
            await Task.Delay(75,request.Token);
            var result=await FilePreviewReader.ReadAsync(path,request.Token,page,scale);
            if(request.IsCancellationRequested || version!=generation || !IsOpen)return;
            Data=result;PageCount=result.PageCount;if(PageCount>0)PageNumber=result.PageNumber;Status=result.Message;Changed?.Invoke();
        }
        catch(OperationCanceledException) { }
        catch(Exception ex)
        {
            if(version==generation && IsOpen){Data=null;Status="미리보기를 표시할 수 없습니다: "+ex.Message+(IsPdf?"\n암호로 보호되거나 손상된 PDF는 원본 프로그램에서 확인하세요.":"");Changed?.Invoke();}
        }
        finally {if(ReferenceEquals(cancellation,request))cancellation=null;request.Dispose();}
    }
    internal void FollowScroll(FilePreviewData data)
    {
        if (!IsOpen || cancellation != null || !IsPdf || data.Path != CurrentPath || data.RenderScale != PdfScale) return;
        PageNumber = data.PageNumber; Data = data; Status = data.Message;
    }
    private void Cancel(){generation++;cancellation?.Cancel();CurrentPath=null;Data=null;PageCount=0;}
    internal void Close(){var current=window;window=null;current?.Close();Cancel();}
    public void Dispose()=>Close();
}
internal sealed class FilePreviewWindow : Window
{
    private readonly FilePreviewController controller;
    private readonly TextBlock title = new() { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock details = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,5,0,8) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,0) };
    private readonly TextBox text = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas, 맑은 고딕") };
    private readonly Image image = new() { Stretch = Stretch.Uniform };
    private readonly ScrollViewer imageScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly StackPanel zoomButtons = new() { Orientation = Orientation.Horizontal };
    private readonly Button open = new() { Content = "열기" };
    internal readonly ContinuousPdfView pdfPages = new();
    private double zoom = 1, targetZoom = 1;
    private Point zoomAnchor;
    private (int Page, Point Position)? pdfZoomAnchor;
    private readonly System.Windows.Threading.DispatcherTimer zoomTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    internal bool IsZooming => zoomTimer.IsEnabled;
    private readonly StackPanel pageTools = new() {Orientation=Orientation.Horizontal,Margin=new Thickness(10,0,0,0)};
    internal readonly Button previousPage = new() {Content="◀ 이전"};
    internal readonly Button nextPage = new() {Content="다음 ▶"};
    private readonly TextBox pageNumber = new() {Width=55,VerticalContentAlignment=VerticalAlignment.Center,ToolTip="페이지 번호를 입력한 뒤 Enter"};
    private readonly TextBlock pageTotal = new() {VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(4,0,10,0)};
    private readonly TextBlock zoomLabel = new() {VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,0,0)};
    internal FilePreviewWindow(Window owner, FilePreviewController controller)
    {
        if(new System.Windows.Interop.WindowInteropHelper(owner).Handle!=IntPtr.Zero) Owner = owner; this.controller = controller; Title = "파일 미리보기"; Icon = owner.Icon; Width = 900; Height = Math.Min(800, SystemParameters.WorkArea.Height - 40); MinWidth = 680; MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowActivated = false;
        SetResourceReference(BackgroundProperty, "SurfaceBrush"); SetResourceReference(ForegroundProperty, "TextBrush"); SetResourceReference(FontSizeProperty, "FontSize12");
        var panel = new DockPanel { Margin = new Thickness(16) }; panel.SetResourceReference(Panel.BackgroundProperty,"SurfaceBrush"); Content = panel;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header); header.Children.Add(title); header.Children.Add(details);
        var tools = new DockPanel(); header.Children.Add(tools); DockPanel.SetDock(open, Dock.Right); tools.Children.Add(open);
        open.Click += (_, _) => { if (controller.CurrentPath is string path) try { FileService.Open(path); } catch (Exception ex) { status.Text = ex.Message; } };
        var close = new Button { Content = "닫기 (Esc)" }; close.Click += (_, _) => Close(); DockPanel.SetDock(close, Dock.Right); tools.Children.Add(close);
        var navigation = new StackPanel { Orientation = Orientation.Horizontal };
        navigation.Children.Add(zoomButtons); navigation.Children.Add(pageTools); tools.Children.Add(navigation);
        details.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        details.SetResourceReference(TextBlock.FontSizeProperty, "FontSize10");

        pageTools.Children.Add(previousPage);pageTools.Children.Add(pageNumber);pageTools.Children.Add(pageTotal);pageTools.Children.Add(nextPage);
        previousPage.Click+=(_,_)=>controller.GoToPage(controller.PageNumber-1);
        nextPage.Click+=(_,_)=>controller.GoToPage(controller.PageNumber+1);
        pageNumber.KeyDown+=(_,e)=> {if(e.Key==Key.Enter){if(int.TryParse(pageNumber.Text,out int number))controller.GoToPage(number);pageNumber.Text=controller.PageNumber.ToString();e.Handled=true;}};
        foreach (var label in new[] { "−", "맞춤", "+" })
        { var button = new Button { Content = label }; zoomButtons.Children.Add(button); button.Click += (_, _) => { if(controller.IsPdf) { double factor=label=="맞춤" && controller.Data is {PageWidth: >0,PageHeight: >0} data ? (pdfPages.ActualWidth-40)/data.PageSizes.Max(size => size.Width) : controller.PdfScale*(label=="+"?1.25:label=="−"?.8:1);controller.SetPdfScale(factor); } else {zoom = label == "맞춤" ? 1 : Math.Clamp(zoom * (label == "+" ? 1.25 : .8), .25, 4); ResizeImage();} }; }
        zoomButtons.Children.Add(zoomLabel);
        DockPanel.SetDock(status, Dock.Bottom); panel.Children.Add(status);
        var body = new Grid(); panel.Children.Add(body); body.Children.Add(text); imageScroll.Content = image; body.Children.Add(imageScroll); body.Children.Add(pdfPages);
        pdfPages.CurrentPageChanged += data => { controller.FollowScroll(data); RefreshPdfTools(); };
        imageScroll.SizeChanged += (_, _) => ResizeImage();
        PreviewMouseWheel += (_, e) => { if (TryZoomWheel(e.Delta, Keyboard.Modifiers, e.GetPosition(this))) e.Handled = true; };
        zoomButtons.ToolTip = "Ctrl + 마우스 휠로 확대·축소";
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } else if(controller.IsPdf && e.Key is Key.PageDown or Key.PageUp) {controller.GoToPage(controller.PageNumber+(e.Key==Key.PageDown?1:-1));e.Handled=true;} };
        zoomTimer.Tick += (_, _) => AnimateZoom();
        controller.Changed += Refresh; Closed += (_, _) => { zoomTimer.Stop(); controller.Changed -= Refresh; pdfPages.Clear(); }; Refresh();
    }
    internal double Zoom => controller.IsPdf ? controller.PdfScale : zoom;
    internal bool TryZoomWheel(int delta, ModifierKeys modifiers, Point? anchor = null)
    {
        if (modifiers != ModifierKeys.Control || delta == 0 || controller.Data == null) return false;
        if (!zoomTimer.IsEnabled) targetZoom = Zoom;
        targetZoom = Math.Clamp(targetZoom * Math.Pow(1.15, delta / 120.0), .25, 4);
        zoomAnchor = anchor ?? new Point(ActualWidth / 2, ActualHeight / 2);
        if (controller.IsPdf) pdfZoomAnchor = pdfPages.DocumentPointAt(TranslatePoint(zoomAnchor, pdfPages));
        zoomTimer.Start();
        return true;
    }
    private void AnimateZoom()
    {
        double previous = Zoom;
        double next = previous + (targetZoom - previous) * .28;
        bool done = Math.Abs(targetZoom - next) < .001;
        if (done) next = targetZoom;
        if (controller.IsPdf)
        {
            pdfPages.ZoomAt(next, TranslatePoint(zoomAnchor, pdfPages), pdfZoomAnchor);
            controller.SetInteractivePdfScale(next); RefreshPdfTools();
        }
        else if (controller.Data?.Image != null)
        {
            var local = TranslatePoint(zoomAnchor, image);
            double width = Math.Max(1, image.ActualWidth), height = Math.Max(1, image.ActualHeight);
            zoom = next; ResizeImage(); imageScroll.UpdateLayout();
            var moved = image.TranslatePoint(new Point(local.X * image.ActualWidth / width, local.Y * image.ActualHeight / height), this);
            imageScroll.ScrollToHorizontalOffset(imageScroll.HorizontalOffset + moved.X - zoomAnchor.X);
            imageScroll.ScrollToVerticalOffset(imageScroll.VerticalOffset + moved.Y - zoomAnchor.Y);
            imageScroll.UpdateLayout();
        }
        else
        {
            var point = TranslatePoint(zoomAnchor, text);
            double x = (text.HorizontalOffset + point.X) * next / previous - point.X;
            double y = (text.VerticalOffset + point.Y) * next / previous - point.Y;
            zoom = next; ResizeImage(); text.UpdateLayout();
            text.ScrollToHorizontalOffset(x); text.ScrollToVerticalOffset(y);
        }
        if (done) { zoomTimer.Stop(); if (controller.IsPdf) pdfPages.FinishZoom(); }
    }
    private void RefreshPdfTools()
    {
        pageNumber.Text=controller.PageNumber.ToString();pageTotal.Text=$"/ {controller.PageCount}";
        previousPage.IsEnabled=controller.PageNumber>1;nextPage.IsEnabled=controller.PageNumber<controller.PageCount;pageNumber.IsEnabled=controller.PageCount>0;
        zoomLabel.Text=controller.IsPdf?$"{controller.PdfScale:P0} · 연속 스크롤":"";
        status.Text = controller.Status;
    }
    private void ResizeImage() { if (!controller.IsPdf) { text.FontSize = FontSize * zoom; zoomLabel.Text = $"{zoom:P0}"; } if(controller.IsPdf) {if(controller.Data is {} pdf) {image.Width=pdf.PageWidth*controller.PdfScale;image.Height=pdf.PageHeight*controller.PdfScale;} return;} image.Width = Math.Max(100, imageScroll.ActualWidth - 20) * zoom; image.Height = Math.Max(100, imageScroll.ActualHeight - 20) * zoom; }
    private void Refresh()
    {
        zoomTimer.Stop();
        var data = controller.Data; title.Text = controller.CurrentPath == null ? "파일 미리보기" : Path.GetFileName(controller.CurrentPath);
        details.Text = data == null ? controller.CurrentPath ?? "목록에서 파일을 선택한 뒤 Space를 누르세요." : $"{data.Path}\n유형: {Path.GetExtension(data.Path)} · 크기: {data.Length:N0} B · 수정한 날짜: {data.Modified:yyyy-MM-dd HH:mm}";
        status.Text = controller.Status; text.Text = data?.Text ?? ""; text.ScrollToHome(); image.Source = data?.Image; zoom = 1;
        imageScroll.Visibility = data?.Image != null ? Visibility.Visible : Visibility.Collapsed;
        zoomButtons.Visibility = data != null ? Visibility.Visible : Visibility.Collapsed;
        text.Visibility = data?.Image == null ? Visibility.Visible : Visibility.Collapsed;
        pageTools.Visibility=controller.IsPdf?Visibility.Visible:Visibility.Collapsed;
        pdfPages.Visibility = controller.IsPdf ? Visibility.Visible : Visibility.Collapsed;
        if (controller.IsPdf)
        {
            imageScroll.Visibility = text.Visibility = Visibility.Collapsed;
            zoomButtons.Visibility = Visibility.Visible;
            if (data is { PageCount: > 0 }) pdfPages.ShowPage(data);
            else if (pdfPages.DocumentPath != controller.CurrentPath || controller.Status.StartsWith("미리보기를 표시할 수 없습니다")) pdfPages.Clear();
        }
        else pdfPages.Clear();
        RefreshPdfTools();
        open.IsEnabled = controller.CurrentPath != null; ResizeImage();
    }
}




