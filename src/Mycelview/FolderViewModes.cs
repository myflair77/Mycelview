using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Mycelview;

internal sealed class FolderViewModes
{
    static readonly ConditionalWeakTable<DataGrid, FolderViewModes> instances = new();
    internal static readonly (string Id, string Label)[] Modes = [("ExtraLarge","아주 큰 아이콘"),("Large","큰 아이콘"),("Medium","보통 아이콘"),("Small","작은 아이콘"),("List","목록"),("Details","자세히"),("Tiles","타일")];
    internal static FolderViewModes? For(DataGrid grid) => instances.TryGetValue(grid, out var value) ? value : null;
    readonly DataGrid grid;
    internal readonly ListBox Icons;
    bool syncing;
    int wheelRemainder;

    internal string Mode { get; private set; } = "Details";
    internal FolderViewModes(DataGrid grid)
    {
        this.grid = grid;
        instances.Add(grid,this);
        Icons = new ListBox { BorderThickness = new Thickness(0), SelectionMode = SelectionMode.Extended, Visibility = Visibility.Collapsed, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        TextSearch.SetTextPath(Icons,"Name");
        Icons.IsTextSearchEnabled=true;
        Icons.SetResourceReference(Control.BackgroundProperty,"SurfaceBrush");
        Icons.SetResourceReference(Control.ForegroundProperty,"TextBrush");
        Icons.SetBinding(ItemsControl.ItemsSourceProperty,new Binding("ItemsSource"){Source=grid});
        Icons.SetBinding(FrameworkElement.DataContextProperty,new Binding("DataContext"){Source=grid});
        ScrollViewer.SetCanContentScroll(Icons,true);
        var panel = new FrameworkElementFactory(typeof(FolderIconPanel));
        Icons.ItemsPanel = new ItemsPanelTemplate(panel);
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(4)));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(1)));
        style.Setters.Add(new Setter(Control.BorderBrushProperty,Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BackgroundProperty,Brushes.Transparent));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty,new Binding("Background"){RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)});
        border.SetBinding(Border.BorderBrushProperty,new Binding("BorderBrush"){RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)});
        border.SetValue(Border.BorderThicknessProperty,new Thickness(1));
        border.SetValue(Border.PaddingProperty,new Thickness(4));
        border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        style.Setters.Add(new Setter(Control.TemplateProperty,new ControlTemplate(typeof(ListBoxItem)){VisualTree=border}));
        var selected = new Trigger { Property=ListBoxItem.IsSelectedProperty,Value=true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromRgb(207,232,255))));
        selected.Setters.Add(new Setter(Control.BorderBrushProperty,Brushes.DodgerBlue));
        style.Triggers.Add(selected);
        Icons.ItemContainerStyle = style;
        _ = new FolderIconInput(Icons);
        var hostGrid=(Grid)grid.Parent;
        hostGrid.Children.Add(Icons);
        hostGrid.PreviewMouseWheel+=(_,e)=>
        {
            if(ChangeIconSize(e.Delta,Keyboard.Modifiers,e.OriginalSource as DependencyObject))e.Handled=true;
        };
        grid.SelectionChanged += (_,_)=>Sync(grid,Icons);
        Icons.SelectionChanged += (_,_)=>Sync(Icons,grid);
        Icons.MouseDoubleClick += (_,e) => { if(FileContextMenus.Parent<TextBox>(e.OriginalSource as DependencyObject)!=null)return; if (FileContextMenus.Parent<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is FileEntry entry && grid.DataContext is CardViewModel card) { card.OpenEntry(entry); e.Handled=true; } };
        Icons.PreviewKeyDown += (_,e)=> { if(FileContextMenus.Parent<TextBox>(e.OriginalSource as DependencyObject)!=null)return; if(e.Key==Key.Enter && grid.DataContext is CardViewModel card) { foreach(var item in Icons.SelectedItems.OfType<FileEntry>().ToArray()) card.OpenEntry(item); e.Handled=true; } };
        grid.DataContextChanged += (_,_)=>Apply((grid.DataContext as CardViewModel)?.State.ViewMode ?? "Details",false);
        Icons.Loaded += (_,_)=>ConfigurePanel();
        Icons.SizeChanged += (_,_)=>ConfigurePanel();
        Apply("Details",false);
    }
    internal bool ChangeIconSize(int delta,ModifierKeys modifiers,DependencyObject? source)
    {
        if(modifiers!=ModifierKeys.Control || grid.DataContext is not CardViewModel {IsFolder:true} ||
            FileContextMenus.Parent<TextBox>(source)!=null ||
            FileContextMenus.Parent<CardView>(grid)?.IsRenaming==true)
        {wheelRemainder=0;return false;}
        wheelRemainder+=delta;
        int steps=wheelRemainder/120;wheelRemainder%=120;
        if(steps==0)return true;
        string[] sizes=["Details","List","Small","Medium","Large","ExtraLarge"];
        int index=Mode=="Tiles" ? (steps>0?-1:1) : Math.Max(0,Array.IndexOf(sizes,Mode));
        int next=Math.Clamp(index+steps,0,sizes.Length-1);
        if(Mode!=sizes[next])Apply(sizes[next]);
        return true;
    }
    void Sync(System.Windows.Controls.Primitives.Selector from,System.Windows.Controls.Primitives.Selector to)
    {
        if(syncing) return;
        syncing=true;
        try
        {
            var source=from is DataGrid d ? d.SelectedItems : ((ListBox)from).SelectedItems;
            var target=to is DataGrid g ? g.SelectedItems : ((ListBox)to).SelectedItems;
            var selected=source.Cast<object>().ToArray();
            foreach(var item in target.Cast<object>().ToArray()) if(!selected.Contains(item)) target.Remove(item);
            foreach(var item in selected) if(!target.Contains(item)) target.Add(item);
        }
        finally { syncing=false; }
    }
    internal void Apply(string mode,bool save=true)
    {
        Mode=Modes.Any(x=>x.Id==mode)?mode:"Details";
        if(grid.DataContext is CardViewModel card) card.State.ViewMode=Mode;
        grid.Visibility=Mode=="Details" && grid.DataContext is not CardViewModel { IsFolder:false }?Visibility.Visible:Visibility.Collapsed;
        Icons.Visibility=Mode=="Details" || grid.DataContext is CardViewModel { IsFolder:false }?Visibility.Collapsed:Visibility.Visible;
        Icons.ItemTemplate=Template();
        ScrollViewer.SetHorizontalScrollBarVisibility(Icons,Mode=="List" && GroupMode==""?ScrollBarVisibility.Auto:ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(Icons,Mode=="List" && GroupMode==""?ScrollBarVisibility.Disabled:ScrollBarVisibility.Auto);
        ApplyGrouping(); Icons.ApplyTemplate(); ConfigurePanel();
        Sync(grid,Icons);
        if(save) { if(Mode=="Details")grid.Focus();else Icons.Focus(); }
        if(save && Window.GetWindow(grid) is MainWindow host) host.ViewModel.Save();
    }
    internal string GroupMode => (grid.DataContext as CardViewModel)?.State.GroupMode ?? "";
    internal void SetGrouping(string mode)
    {
        if(grid.DataContext is not CardViewModel card)return;
        var selected=grid.SelectedItems.Cast<object>().ToArray();
        card.State.GroupMode=FolderGrouping.Modes.Any(x=>x.Id==mode)?mode:"";
        Apply(Mode);
        foreach(var item in selected)if(grid.Items.Contains(item)&&!grid.SelectedItems.Contains(item))grid.SelectedItems.Add(item);
    }
    internal void ApplyGrouping()
    {
        if(grid.DataContext is not CardViewModel card || grid.ItemsSource==null || CollectionViewSource.GetDefaultView(grid.ItemsSource) is not ListCollectionView view)return;
        using(view.DeferRefresh())
        {
            view.GroupDescriptions.Clear();
            view.CustomSort=GroupMode=="" ? new FileEntryComparer(card.State.SortProperty,card.State.SortDescending)
                : new FolderGrouping.Comparer(GroupMode,card.State.SortProperty,card.State.SortDescending);
            if(Mode=="Details"&&GroupMode!="")view.GroupDescriptions.Add(new PropertyGroupDescription(null,new FolderGrouping.LabelConverter(GroupMode)));
        }
        if(grid.GroupStyle.Count==0)
        {
            var text=new FrameworkElementFactory(typeof(TextBlock));text.SetBinding(TextBlock.TextProperty,new Binding("Name"));
            text.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);text.SetValue(FrameworkElement.MarginProperty,new Thickness(6));
            grid.GroupStyle.Add(new GroupStyle{HeaderTemplate=new DataTemplate{VisualTree=text}});
            VirtualizingPanel.SetIsVirtualizingWhenGrouping(grid,true);
        }
        ConfigurePanel();
    }
    internal void ConfigurePanel()
    {
        if(Find<FolderIconPanel>(Icons) is not {} panel) return;
        var (width,height,_)=Dimensions();
        panel.CellWidth=width; panel.CellHeight=height; panel.ColumnFlow=Mode=="List" && GroupMode=="";
        panel.GroupMode=GroupMode;
        panel.InvalidateMeasure();
    }
    internal (double Width,double Height,int Pixels) Dimensions() => Mode switch
    {
        "ExtraLarge"=>(160,174,128),"Large"=>(112,114,64),"Medium"=>(96,84,32),
        "Tiles"=>(260,74,48),"Small"=>(200,30,16),"List"=>(220,30,16),_=>(200,30,16)
    };
    DataTemplate Template()
    {
        var (_,_,pixels)=Dimensions();
        bool stacked=Mode is "ExtraLarge" or "Large" or "Medium";
        var root=new FrameworkElementFactory(typeof(StackPanel));
        root.SetValue(StackPanel.OrientationProperty,stacked?Orientation.Vertical:Orientation.Horizontal);
        var image=new FrameworkElementFactory(typeof(ThumbnailImage));
        image.SetValue(FrameworkElement.WidthProperty,(double)pixels);image.SetValue(FrameworkElement.HeightProperty,(double)pixels);
        image.SetValue(FrameworkElement.MarginProperty,stacked?new Thickness(0,0,0,4):new Thickness(0,0,8,0));
        image.SetValue(ThumbnailImage.PixelsProperty,pixels);
        image.SetBinding(ThumbnailImage.EntryProperty,new Binding());
        root.AppendChild(image);
        var textPanel=new FrameworkElementFactory(typeof(StackPanel));
        if(!stacked) textPanel.SetValue(FrameworkElement.WidthProperty,Dimensions().Width-pixels-22);
        var name=new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty,FileDisplayPreferences.NameBinding());
        name.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);
        name.SetValue(TextBlock.TextWrappingProperty,stacked?TextWrapping.Wrap:TextWrapping.NoWrap);
        name.SetValue(FrameworkElement.MaxHeightProperty,stacked?36d:24d);
        name.SetValue(TextBlock.TextAlignmentProperty,stacked?TextAlignment.Center:TextAlignment.Left);
        var nameSlot=new FrameworkElementFactory(typeof(Grid));
        nameSlot.AppendChild(name);
        var editor=new FrameworkElementFactory(typeof(TextBox));
        editor.SetValue(UIElement.VisibilityProperty,Visibility.Collapsed);
        editor.SetValue(FrameworkElement.MinWidthProperty,40d);
        editor.SetValue(TextBox.TextWrappingProperty,TextWrapping.NoWrap);
        editor.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Top);
        editor.AddHandler(UIElement.KeyDownEvent,new KeyEventHandler((sender,e)=>FileContextMenus.Parent<CardView>((DependencyObject)sender)?.InlineRenameKeyDown(sender,e)));
        editor.AddHandler(UIElement.LostKeyboardFocusEvent,new KeyboardFocusChangedEventHandler((sender,e)=>FileContextMenus.Parent<CardView>((DependencyObject)sender)?.InlineRenameLostFocus(sender,e)));
        nameSlot.AppendChild(editor);
        textPanel.AppendChild(nameSlot);
        if(Mode=="Tiles") foreach(var property in new[]{"Type","Size"})
        {
            var text=new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty,new Binding(property));
            text.SetValue(TextBlock.ForegroundProperty,Brushes.Gray);
            text.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);
            textPanel.AppendChild(text);
        }
        root.AppendChild(textPanel);
        root.SetBinding(FrameworkElement.ToolTipProperty,new Binding("FullPath"));
        return new DataTemplate {VisualTree=root};
    }
    internal static T? Find<T>(DependencyObject root) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var c=VisualTreeHelper.GetChild(root,i);if(c is T value)return value;if(Find<T>(c) is {} nested)return nested;}
        return null;
    }
    sealed class IconConverter(int pixels):IValueConverter
    {
        public object? Convert(object value,Type target,object parameter,CultureInfo culture)=>value is FileEntry file?ShellIcons.ForSize(file.FullPath,file.IsDirectory,pixels):null;
        public object ConvertBack(object value,Type target,object parameter,CultureInfo culture)=>throw new NotSupportedException();
    }
}
