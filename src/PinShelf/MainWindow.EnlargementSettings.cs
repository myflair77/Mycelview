using System.Windows;
using System.Windows.Controls;
namespace PinShelf;
public partial class MainWindow
{
    private void ShowEnlargementSettings(object sender,RoutedEventArgs e)
    {
        CloseCardEnlargement();
        var saved=ViewModel.Enlargement;
        var dialog=new Window { Owner=this, Title="카드 자동 확대 설정", Icon=Icon, Width=480, SizeToContent=SizeToContent.Height, ResizeMode=ResizeMode.NoResize, WindowStartupLocation=WindowStartupLocation.CenterOwner };
        dialog.SetResourceReference(BackgroundProperty,"SurfaceBrush");dialog.SetResourceReference(ForegroundProperty,"TextBrush");dialog.SetResourceReference(FontSizeProperty,"FontSize12");
        var panel=new StackPanel { Margin=new Thickness(20) };dialog.Content=panel;
        void Label(string value)=>panel.Children.Add(new TextBlock {Text=value,Margin=new Thickness(0,10,0,5),TextWrapping=TextWrapping.Wrap});
        Label("카드 우측 상단의 확대 반응 영역에 마우스를 올리면 확대합니다. 카드 밖으로 나가면 닫힙니다.");
        Label("확대 방식");
        var mode=new ComboBox {ItemsSource=new[]{"떠오르는 확대","밀어내는 확대","확대 안함"},SelectedIndex=saved.Mode=="B"?1:saved.Mode=="C"?2:0};panel.Children.Add(mode);
        Label("확대 정도");
        var sizeMode=new StackPanel { Orientation=Orientation.Horizontal }; var ratioOption=new RadioButton { Content="배율", IsChecked=!saved.FixedSize }; var fixedOption=new RadioButton { Content="크기", IsChecked=saved.FixedSize, Margin=new Thickness(0,0,20,0) }; sizeMode.Children.Add(fixedOption);sizeMode.Children.Add(ratioOption);panel.Children.Add(sizeMode);
        var percent=new TextBox {Text=saved.Percent.ToString()};Label("배율 (%)");panel.Children.Add(percent);
        var dimensions=new StackPanel {Orientation=Orientation.Horizontal};
        var width=new TextBox {Text=saved.Width.ToString(),Width=110};var height=new TextBox {Text=saved.Height.ToString(),Width=110};
        Label("너비 × 높이 (400~2400 × 260~1800)"); dimensions.Children.Add(width);dimensions.Children.Add(new TextBlock {Text=" × ",VerticalAlignment=VerticalAlignment.Center});dimensions.Children.Add(height);panel.Children.Add(dimensions);

        Label("설정한 너비가 원래 폴더 카드보다 작으면 원래 카드 너비로 표시합니다.");
        Label("창이나 작업공간보다 큰 크기는 표시 가능한 범위로 제한합니다. 파일 드래그 중에는 자동 확대하지 않습니다.");
        void EnableFields() {bool enabled=mode.SelectedIndex!=2;sizeMode.IsEnabled=enabled;percent.IsEnabled=enabled&&fixedOption.IsChecked!=true;dimensions.IsEnabled=enabled; width.IsEnabled=height.IsEnabled=enabled&&fixedOption.IsChecked==true;}
        mode.SelectionChanged+=(_,_)=>Dispatcher.BeginInvoke(new Action(EnableFields));ratioOption.Checked+=(_,_)=>EnableFields();fixedOption.Checked+=(_,_)=>EnableFields();EnableFields();
        var error=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};panel.Children.Add(error);
        var buttons=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};panel.Children.Add(buttons);
        var cancel=new Button {Content="취소",IsCancel=true};buttons.Children.Add(cancel);var save=new Button {Content="저장",IsDefault=true};buttons.Children.Add(save);
        save.Click+=(_,_)=>
        {
            var next=saved.Normalize();next.Mode=mode.SelectedIndex==1?"B":mode.SelectedIndex==2?"C":"A";next.FixedSize=fixedOption.IsChecked==true;
            if(next.Mode!="C")
            {

                if(next.FixedSize)
                {if(!int.TryParse(width.Text,out int w)||w<400||w>2400||!int.TryParse(height.Text,out int h)||h<260||h>1800){error.Text="크기의 입력 범위를 확인하세요.";return;}next.Width=w;next.Height=h;}
                else {if(!int.TryParse(percent.Text,out int p)||p<110||p>300){error.Text="배율은 110~300 사이 정수로 입력하세요.";return;}next.Percent=p;}
            }
            ViewModel.SetEnlargement(next);dialog.DialogResult=true;
        };
        dialog.ShowDialog();
    }
}



