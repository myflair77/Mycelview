using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Mycelview;
public sealed class FileDisplayPreferences : Observable
{
    public static FileDisplayPreferences Current { get; } = new();
    readonly List<WeakReference<ICollectionView>> views=[];
    bool showHidden,showExtensions=true;
    public bool ShowHidden {get=>showHidden;set{Set(ref showHidden,value);Refresh();}}
    public bool ShowExtensions {get=>showExtensions;set=>Set(ref showExtensions,value);}
    internal void Watch(ICollectionView view)
    {
        if(!view.CanFilter)return;
        if(!views.Any(w=>w.TryGetTarget(out var old)&&ReferenceEquals(old,view)))views.Add(new(view));
        view.Filter=item=>item is not FileEntry entry || ShowHidden || !entry.IsHidden;
    }
    void Refresh()
    {
        views.RemoveAll(w=>!w.TryGetTarget(out _));
        foreach(var weak in views)if(weak.TryGetTarget(out var view))view.Refresh();
    }
    internal static MultiBinding NameBinding()
    {
        var binding=new MultiBinding{Converter=new FileNameConverter()};
        binding.Bindings.Add(new Binding());
        binding.Bindings.Add(new Binding(nameof(ShowExtensions)){Source=Current});
        return binding;
    }
    internal static MenuItem HiddenMenu(Action save)
    {
        var item=new MenuItem{Header="숨김 항목 표시",IsCheckable=true,IsChecked=Current.ShowHidden};
        item.Click+=(_,_)=>{Current.ShowHidden=item.IsChecked;save();};return item;
    }
    internal static MenuItem ExtensionMenu(Action save)
    {
        var item=new MenuItem{Header="파일 확장명 표시",IsCheckable=true,IsChecked=Current.ShowExtensions};
        item.Click+=(_,_)=>{Current.ShowExtensions=item.IsChecked;save();};return item;
    }
}
public sealed class FileNameConverter : IMultiValueConverter
{
    public object Convert(object[] values,Type target,object parameter,CultureInfo culture)
    {
        if(values.Length==0 || values[0] is not FileEntry entry)return "";
        bool show=values.Length<2 || values[1] is not false;
        var stem=System.IO.Path.GetFileNameWithoutExtension(entry.Name);
        return show || entry.IsDirectory || entry.IsShellItem || stem.Length==0 ? entry.Name : stem;
    }
    public object[] ConvertBack(object value,Type[] target,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}
