$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationCore,PresentationFramework,WindowsBase
$root=$PSScriptRoot
$assets=Join-Path $root 'src\Mycelview\Assets'
[xml]$drawing=Get-Content -LiteralPath (Join-Path $assets 'Logo.xaml') -Raw
$paths=@($drawing.ResourceDictionary.Geometry)
$group=[System.Windows.Media.DrawingGroup]::new()
foreach($p in $paths){
 $brush=[System.Windows.Media.Brushes]::White
 $group.Children.Add([System.Windows.Media.GeometryDrawing]::new($brush,$null,[System.Windows.Media.Geometry]::Parse($p.InnerText)))
}
$frames=@()
foreach($size in @(16,20,24,32,40,48,64,128,256)){
 $visual=[System.Windows.Media.DrawingVisual]::new(); $dc=$visual.RenderOpen()
 $blue=[System.Windows.Media.BrushConverter]::new().ConvertFromString('#1687F8')
 $dc.DrawRoundedRectangle($blue,$null,[System.Windows.Rect]::new($size*0.03,$size*0.03,$size*0.94,$size*0.94),$size*0.22,$size*0.22)
 $scale=($size*0.66)/100
 $dc.PushTransform([System.Windows.Media.TranslateTransform]::new($size*0.17,$size*0.17))
 $dc.PushTransform([System.Windows.Media.ScaleTransform]::new($scale,$scale))
 $dc.DrawDrawing($group); $dc.Pop(); $dc.Pop(); $dc.Close()
 $bmp=[System.Windows.Media.Imaging.RenderTargetBitmap]::new($size,$size,96,96,[System.Windows.Media.PixelFormats]::Pbgra32);$bmp.Render($visual)
 $enc=[System.Windows.Media.Imaging.PngBitmapEncoder]::new();$enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp))
 $stream=[System.IO.MemoryStream]::new();$enc.Save($stream);$frames+=,@($size,$stream.ToArray());$stream.Dispose()
}
[System.IO.File]::WriteAllBytes((Join-Path $assets 'Mycelview.png'),$frames[-1][1])
$out=[System.IO.File]::Create((Join-Path $assets 'Mycelview.ico'));$writer=[System.IO.BinaryWriter]::new($out)
$writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$frames.Count)
$offset=6+16*$frames.Count
foreach($f in $frames){$s=if($f[0] -eq 256){0}else{$f[0]};$writer.Write([byte]$s);$writer.Write([byte]$s);$writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$f[1].Length);$writer.Write([uint32]$offset);$offset+=$f[1].Length}
foreach($f in $frames){$writer.Write([byte[]]$f[1])};$writer.Dispose()
