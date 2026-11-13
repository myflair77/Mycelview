using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Mycelview;

// Keep only the viewport and its neighbours decoded, even for very long documents.
internal sealed class ContinuousPdfView : ScrollViewer
{
    private readonly Canvas pages = new();
    private readonly Dictionary<int, (Border Frame, Image Image)> visible = new();
    private readonly Dictionary<int, FilePreviewData> rendered = new();
    private FilePreviewData? document;
    private double[] offsets = [];
    private CancellationTokenSource? pending;
    private int generation;
    private bool zooming;
    internal event Action<FilePreviewData>? CurrentPageChanged;
    internal Task Rendering { get; private set; } = Task.CompletedTask;
    internal int RenderedCount => rendered.Count;
    internal string? DocumentPath => document?.Path;

    internal ContinuousPdfView()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        CanContentScroll = false;
        Content = pages;
        Background = new SolidColorBrush(Color.FromRgb(230, 233, 237));
        ScrollChanged += (_, e) => { if (!zooming && (e.VerticalChange != 0 || e.ViewportHeightChange != 0 || e.ViewportWidthChange != 0)) QueueRender(); };
        Unloaded += (_, _) => Clear();
    }

    internal void Clear()
    {
        generation++; pending?.Cancel(); pending = null;
        zooming = false; document = null; offsets = []; visible.Clear(); rendered.Clear(); pages.Children.Clear();
        pages.Height = pages.Width = 0;
    }

    internal void ShowPage(FilePreviewData data)
    {
        bool changed = document == null || document.Path != data.Path || document.Length != data.Length ||
            document.Modified != data.Modified || document.RenderScale != data.RenderScale;
        if (changed)
        {
            Clear(); document = data;
            offsets = new double[data.PageCount];
            double top = 12, width = 0;
            for (int i = 0; i < offsets.Length; i++)
            {
                offsets[i] = top;
                var size = data.PageSizes[i];
                top += size.Height * data.RenderScale + 18;
                width = Math.Max(width, size.Width * data.RenderScale);
            }
            pages.Height = top; pages.Width = width + 24;
        }
        rendered[data.PageNumber - 1] = data;
        ScrollToVerticalOffset(Math.Max(0, offsets[data.PageNumber - 1] - 12));
        QueueRender();
    }

    internal (int Page, Point Position) DocumentPointAt(Point anchor)
    {
        var point = TranslatePoint(anchor, pages);
        int page = PageAt(point.Y);
        var size = document!.PageSizes[page];
        return (page, new Point((point.X - (pages.Width - size.Width * document.RenderScale) / 2) / document.RenderScale,
            (point.Y - offsets[page]) / document.RenderScale));
    }
    internal void ZoomAt(double scale, Point anchor, (int Page, Point Position)? fixedPoint = null)
    {
        if (document == null || offsets.Length == 0) return;
        zooming = true; generation++; pending?.Cancel();
        var reference = fixedPoint ?? DocumentPointAt(anchor);
        int page = reference.Page;
        var pageSize = document.PageSizes[page];
        double pageX = reference.Position.X, pageY = reference.Position.Y;
        document = document with { RenderScale = scale };
        double top = 12, width = document.PageSizes.Max(size => size.Width) * scale;
        for (int i = 0; i < offsets.Length; i++) { offsets[i] = top; top += document.PageSizes[i].Height * scale + 18; }
        pages.Width = width + 24; pages.Height = top;
        foreach (var (i, item) in visible)
        {
            item.Frame.Width = document.PageSizes[i].Width * scale;
            item.Frame.Height = document.PageSizes[i].Height * scale;
            Canvas.SetTop(item.Frame, offsets[i]); Canvas.SetLeft(item.Frame, (pages.Width - item.Frame.Width) / 2);
        }
        UpdateLayout();
        var moved = pages.TranslatePoint(new Point((pages.Width - pageSize.Width * scale) / 2 + pageX * scale, offsets[page] + pageY * scale), this);
        ScrollToHorizontalOffset(HorizontalOffset + moved.X - anchor.X);
        ScrollToVerticalOffset(VerticalOffset + moved.Y - anchor.Y);
        UpdateLayout();
    }
    internal void FinishZoom() { zooming = false; QueueRender(); }

    private int PageAt(double y)
    {
        int found = Array.BinarySearch(offsets, y);
        return Math.Clamp(found >= 0 ? found : ~found - 1, 0, offsets.Length - 1);
    }

    private void QueueRender()
    {
        if (document == null || offsets.Length == 0) return;
        pending?.Cancel();
        var request = new CancellationTokenSource(); pending = request;
        Rendering = RenderAsync(document, ++generation, request);
    }

    private async Task RenderAsync(FilePreviewData data, int version, CancellationTokenSource request)
    {
        try
        {
            await Task.Delay(60, request.Token);
            int current = PageAt(VerticalOffset + Math.Max(0, ViewportHeight / 2));
            int first = Math.Max(0, PageAt(VerticalOffset) - 1);
            int last = Math.Min(offsets.Length - 1, PageAt(VerticalOffset + Math.Max(1, ViewportHeight)) + 1);
            foreach (int i in visible.Keys.Where(i => i < first || i > last).ToArray())
            { pages.Children.Remove(visible[i].Frame); visible.Remove(i); }
            foreach (int i in rendered.Keys.Where(i => i < first || i > last).ToArray()) rendered.Remove(i);
            for (int i = first; i <= last; i++)
            {
                if (visible.ContainsKey(i)) continue;
                var size = data.PageSizes[i];
                var image = new Image { Stretch = Stretch.Fill };
                var frame = new Border { Width = size.Width * data.RenderScale, Height = size.Height * data.RenderScale,
                    Background = Brushes.White, Child = image, ToolTip = $"{i + 1} 페이지" };
                Canvas.SetTop(frame, offsets[i]); Canvas.SetLeft(frame, (pages.Width - frame.Width) / 2);
                pages.Children.Add(frame); visible.Add(i, (frame, image));
            }
            // Render the current page before prefetching its neighbours.
            foreach (int i in Enumerable.Range(first, last - first + 1).OrderBy(i => Math.Abs(i - current)))
            {
                request.Token.ThrowIfCancellationRequested();
                if (!rendered.TryGetValue(i, out var result) || Math.Abs(result.RenderScale - data.RenderScale) > .001)
                    result = await PdfPreviewRenderer.ReadAsync(data.Path, i + 1, data.RenderScale, request.Token);
                if (version != generation) return;
                rendered[i] = result; visible[i].Image.Source = result.Image;
                if (i == current) CurrentPageChanged?.Invoke(result);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (version == generation && visible.Count > 0)
                visible.Values.First().Frame.Child = new TextBlock { Text = "페이지를 표시할 수 없습니다: " + error.Message, TextWrapping = TextWrapping.Wrap };
        }
        finally { if (ReferenceEquals(pending, request)) pending = null; request.Dispose(); }
    }
}
