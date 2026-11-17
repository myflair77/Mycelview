using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Mycelview;
public partial class MainWindow
{
    private void LauncherDragOver(object sender,DragEventArgs e)
    {
        if(HandleLauncherOrder(e,false))return;
        e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;
        e.Handled=true;
    }
    private void LauncherDrop(object sender,DragEventArgs e)
    {
        if(HandleLauncherOrder(e,true))return;
        e.Handled=true;
        if(e.Data.GetData(DataFormats.FileDrop) is not string[] paths)return;
        RegisterLauncherDrop(paths);
    }
    internal int RegisterLauncherDrop(IEnumerable<string> paths)
    {
        int count=0;
        foreach(var item in LauncherCatalog.FromDrop(paths))
        {
            if(ViewModel.LauncherFavorites.Any(x=>StringComparer.OrdinalIgnoreCase.Equals(x.Target,item.Target)))continue;
            ViewModel.LauncherFavorites.Add(item);count++;
        }
        if(count>0)ViewModel.Save();
        ViewModel.Status=count>0?$"{count}개 앱·브라우저 즐겨찾기를 등록했습니다.":"등록할 새 프로그램 또는 바로가기가 없습니다.";
        return count;
    }
    private void LauncherGroupMenu(object sender,RoutedEventArgs e)
    {
        if(sender is not Button button || button.DataContext is not LauncherGroup group)return;
        var menu=new ContextMenu();
        var index=ViewModel.LauncherGroups.FindIndex(g=>g.Kind==group.Kind);
        void Add(string title,int offset,bool enabled){
            var entry=new MenuItem{Header=title,IsEnabled=enabled};
            entry.Click+=(_,_)=>{ViewModel.MoveLauncherGroup(group.Kind,offset);ViewModel.Save();};
            menu.Items.Add(entry);
        }
        Add("묶음을 왼쪽으로 이동",-1,index>0);
        Add("묶음을 오른쪽으로 이동",1,index<2);
        button.ContextMenu=menu;menu.PlacementTarget=button;menu.IsOpen=true;
    }
    private void AddLauncher(object sender,RoutedEventArgs e) => EditLauncher(null, (sender as FrameworkElement)?.DataContext is LauncherGroup group ? group.Kind : "App");
    private void LaunchFavorite(object sender,RoutedEventArgs e)
    {
        if (launcherDragging) return;
        if ((sender as FrameworkElement)?.DataContext is not LauncherFavorite item) return;
        try { Process.Start(LauncherService.Prepare(item)); ViewModel.Status=item.Name+" 실행"; }
        catch(Exception error) { ViewModel.Status="실행 실패 · "+error.Message; }
    }
    private void LauncherContextMenu(object sender,ContextMenuEventArgs e)
    {
        if(sender is not Button button || button.DataContext is not LauncherFavorite item)return;
        var menu=new ContextMenu();
        void Add(string title,Action action,bool enabled=true) { var entry=new MenuItem{Header=title,IsEnabled=enabled};entry.Click+=(_,_)=>action();menu.Items.Add(entry); }
        Add("실행",()=>LaunchFavorite(button,new()));
        Add("수정…",()=>EditLauncher(item));
        if(item.Kind is "App" or "Browser" or "Website") Add(item.Kind=="Website"?"브라우저 창의 가장자리 구간 지정…":"화면 가장자리 구간 지정…",()=>{try{OpenEdgeActivationSettings(item);}catch(Exception error){ViewModel.Status="구간 지정 실패 · "+error.Message;}});
        menu.Items.Add(new Separator());
        var siblings=ViewModel.LauncherFavorites.Where(x=>x.Kind==item.Kind).ToList();
        var index=siblings.IndexOf(item);
        void Move(int offset){ViewModel.MoveLauncherItem(item,offset);ViewModel.Save();}
        Add("묶음 안에서 왼쪽으로 이동",()=>Move(-1),index>0);
        Add("묶음 안에서 오른쪽으로 이동",()=>Move(1),index<siblings.Count-1);
        menu.Items.Add(new Separator());
        Add("즐겨찾기에서 제거",()=>{ViewModel.LauncherFavorites.Remove(item);ViewModel.Save();});
        button.ContextMenu=menu;menu.PlacementTarget=button;menu.IsOpen=true;e.Handled=true;
    }
    private void EditLauncher(LauncherFavorite? original, string initialKind = "App")
    {
        var item=original?.Copy() ?? new LauncherFavorite { Kind = initialKind };
        var dialog=new Window{Title=original==null?"앱·브라우저·사이트 즐겨찾기 추가":"앱·브라우저·사이트 즐겨찾기 수정",Owner=this,Width=560,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false};
        dialog.SetResourceReference(FontSizeProperty,"FontSize11");
        dialog.SetResourceReference(BackgroundProperty,"SurfaceBrush");
        dialog.SetResourceReference(ForegroundProperty,"TextBrush");
        var body=new StackPanel{Margin=new Thickness(20)};
        dialog.Content=body;
        void Label(string text){body.Children.Add(new TextBlock{Text=text,Margin=new Thickness(0,10,0,5),FontWeight=FontWeights.SemiBold});}
        Label("등록 종류");
        var kind=new ComboBox{ItemsSource=new[]{"프로그램","브라우저","웹사이트"},SelectedIndex=item.Kind=="Website"?2:item.Kind=="Browser"?1:0};
        body.Children.Add(kind);
        var catalogPanel=new StackPanel{Margin=new Thickness(0,12,0,0)};
        body.Children.Add(catalogPanel);
        var catalogTitle=new TextBlock{Text="목록에서 선택",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,5)};
        catalogPanel.Children.Add(catalogTitle);
        var filter=new TextBox{ToolTip="앱 이름으로 검색",Margin=new Thickness(0,0,0,5)};
        System.Windows.Automation.AutomationProperties.SetName(filter,"앱 이름 검색");
        catalogPanel.Children.Add(filter);
        var catalogStatus=new TextBlock{Text="시작 메뉴와 바탕화면의 앱을 찾는 중…",Margin=new Thickness(0,0,0,5)};
        catalogStatus.SetResourceReference(TextBlock.ForegroundProperty,"MutedBrush");
        catalogPanel.Children.Add(catalogStatus);
        var list=new ListBox{Height=170,HorizontalContentAlignment=HorizontalAlignment.Stretch};
        ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);
        VirtualizingPanel.SetIsVirtualizing(list,true);
        VirtualizingPanel.SetVirtualizationMode(list,VirtualizationMode.Recycling);
        var template=new DataTemplate(typeof(LauncherFavorite));
        var line=new FrameworkElementFactory(typeof(StackPanel));line.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);
        var icon=new FrameworkElementFactory(typeof(Image));icon.SetValue(Image.WidthProperty,16d);icon.SetValue(Image.HeightProperty,16d);icon.SetValue(Image.MarginProperty,new Thickness(3,3,8,3));icon.SetBinding(Image.SourceProperty,new System.Windows.Data.Binding("Icon"));line.AppendChild(icon);
        var text=new FrameworkElementFactory(typeof(TextBlock));text.SetValue(TextBlock.VerticalAlignmentProperty,VerticalAlignment.Center);text.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Name"));line.AppendChild(text);
        template.VisualTree=line;list.ItemTemplate=template;catalogPanel.Children.Add(list);
        List<LauncherFavorite> apps=[];
        var detectedBrowsers=LauncherService.Browsers().Where(x=>x.Path.Length>0)
            .Select(x=>new LauncherFavorite{Name=x.Name,Target=x.Path,Kind="Browser"}).ToList();
        bool catalogLoaded=false,dialogClosed=false;
        void RefreshCatalog()
        {
            var source=kind.SelectedIndex==1?detectedBrowsers:apps;
            var matches=LauncherCatalog.Filter(source,filter.Text).ToList();
            list.ItemsSource=matches;
            catalogTitle.Text=kind.SelectedIndex==1?"설치된 브라우저 선택":"앱 이름을 검색하고 선택하세요";
            catalogStatus.Text=kind.SelectedIndex==0&&!catalogLoaded?"시작 메뉴와 바탕화면의 앱을 찾는 중…":
                matches.Count==0?"검색 결과가 없습니다. 아래에서 파일을 직접 선택할 수도 있습니다.":$"{matches.Count}개 · 선택하면 이름과 실행 경로가 채워집니다.";
        }
        filter.TextChanged+=(_,_)=>RefreshCatalog();
        dialog.Closed+=(_,_)=>dialogClosed=true;
        dialog.Loaded+=async (_,_)=>{
            try{apps=await Task.Run(()=>LauncherCatalog.Discover());}
            catch(Exception ex){if(!dialogClosed)catalogStatus.Text="앱 목록을 읽지 못했습니다: "+ex.Message;return;}
            catalogLoaded=true;if(!dialogClosed)RefreshCatalog();
        };
        Label("이름");
        var name=new TextBox{Text=item.Name};body.Children.Add(name);
        var targetLabel=new TextBlock{Margin=new Thickness(0,10,0,5),FontWeight=FontWeights.SemiBold};body.Children.Add(targetLabel);
        var row=new DockPanel();body.Children.Add(row);
        var browse=new Button{Content="파일에서 찾기…",Margin=new Thickness(8,0,0,0)};DockPanel.SetDock(browse,Dock.Right);row.Children.Add(browse);
        var target=new TextBox{Text=item.Target,MinWidth=260};row.Children.Add(target);
        var browserPanel=new StackPanel();body.Children.Add(browserPanel);
        browserPanel.Children.Add(new TextBlock{Text="이 웹사이트를 열 브라우저",Margin=new Thickness(0,14,0,5),FontWeight=FontWeights.SemiBold});
        var browserRow=new DockPanel();browserPanel.Children.Add(browserRow);
        var custom=new Button{Content="직접 선택…",Margin=new Thickness(8,0,0,0)};DockPanel.SetDock(custom,Dock.Right);browserRow.Children.Add(custom);
        var choices=LauncherService.Browsers();
        if(item.BrowserPath.Length>0 && !choices.Any(x=>StringComparer.OrdinalIgnoreCase.Equals(x.Path,item.BrowserPath)))
            choices.Add(new(Path.GetFileNameWithoutExtension(item.BrowserPath),item.BrowserPath));
        var browser=new ComboBox{ItemsSource=choices,DisplayMemberPath="Name",SelectedItem=choices.FirstOrDefault(x=>StringComparer.OrdinalIgnoreCase.Equals(x.Path,item.BrowserPath))??choices[0]};
        browserRow.Children.Add(browser);
        list.SelectionChanged+=(_,_)=>{if(list.SelectedItem is LauncherFavorite selected){name.Text=selected.Name;target.Text=selected.Target;}};
        void Update(){var site=kind.SelectedIndex==2;targetLabel.Text=site?"웹사이트 주소 (https://…)":"실행 파일 또는 바로가기";browse.Visibility=site?Visibility.Collapsed:Visibility.Visible;browserPanel.Visibility=site?Visibility.Visible:Visibility.Collapsed;catalogPanel.Visibility=site?Visibility.Collapsed:Visibility.Visible;RefreshCatalog();}
        kind.SelectionChanged+=(_,_)=>Update();Update();
        browse.Click+=(_,_)=>{
            var pick=new OpenFileDialog{Filter="프로그램 및 바로가기|*.exe;*.lnk;*.appref-ms",CheckFileExists=true,DereferenceLinks=false};
            if(pick.ShowDialog(dialog)==true){target.Text=pick.FileName;if(string.IsNullOrWhiteSpace(name.Text))name.Text=Path.GetFileNameWithoutExtension(pick.FileName);}
        };
        custom.Click+=(_,_)=>{
            var pick=new OpenFileDialog{Filter="브라우저 실행 파일|*.exe",CheckFileExists=true};
            if(pick.ShowDialog(dialog)==true){var choice=new BrowserChoice(Path.GetFileNameWithoutExtension(pick.FileName),pick.FileName);choices.Add(choice);browser.ItemsSource=null;browser.ItemsSource=choices;browser.SelectedItem=choice;}
        };
        var error=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0),Foreground=System.Windows.Media.Brushes.Firebrick};body.Children.Add(error);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,14,0,0)};body.Children.Add(actions);
        var cancel=new Button{Content="취소",IsCancel=true,MinWidth=70};actions.Children.Add(cancel);
        var save=new Button{Content="저장",IsDefault=true,MinWidth=70,Margin=new Thickness(8,0,0,0)};actions.Children.Add(save);
        save.Click+=(_,_)=>{
            item.Name=name.Text.Trim();item.Target=target.Text.Trim();item.Kind=kind.SelectedIndex==2?"Website":kind.SelectedIndex==1?"Browser":"App";
            item.BrowserPath=item.Kind=="Website"?(browser.SelectedItem as BrowserChoice)?.Path??"":"";
            try {LauncherService.Prepare(item);dialog.DialogResult=true;}
            catch(Exception ex){error.Text=ex.Message;}
        };
        DismissSettingsOnOutsideClick.Attach(dialog);
        if(dialog.ShowDialog()!=true)return;
        if(original==null)ViewModel.LauncherFavorites.Add(item);
        else ViewModel.LauncherFavorites[ViewModel.LauncherFavorites.IndexOf(original)]=item;
        ViewModel.Save();
    }
}
