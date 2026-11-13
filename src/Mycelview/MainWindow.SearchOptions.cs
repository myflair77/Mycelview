using System.Windows;
using System.Windows.Controls;

namespace Mycelview;

public partial class MainWindow
{
    private void ShowSearchOptions(object sender, RoutedEventArgs e) => CreateSearchOptionsWindow().ShowDialog();
    internal Window CreateSearchOptionsWindow()
    {
        var options = ViewModel.Search.Options;
        var dialog = new Window { Title = "검색 옵션", Owner = this, Width = Math.Min(560 * TextScale.Percent / 100d, SystemParameters.WorkArea.Width - 40), SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, MaxHeight = SystemParameters.WorkArea.Height - 40 };
        dialog.SetResourceReference(BackgroundProperty, "SurfaceBrush");
        dialog.SetResourceReference(ForegroundProperty, "TextBrush");
        var panel = new StackPanel { Margin = new Thickness(20) };
        dialog.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        void Divider(Thickness margin)
        {
            var line = new Border { Height = 1, Margin = margin };
            line.SetResourceReference(Border.BackgroundProperty, "BorderBrush"); panel.Children.Add(line);
        }
        void Heading(string text)
        {
            if (panel.Children.Count > 0) Divider(new Thickness(0, 16, 0, 12));
            var title = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
            title.SetResourceReference(TextBlock.FontSizeProperty, "FontSize13");
            panel.Children.Add(title);
        }
        void Hint(string text)
        {
            var hint = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 4) };
            hint.SetResourceReference(TextBlock.FontSizeProperty, "FontSize10");
            hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); panel.Children.Add(hint);
        }
        Heading("검색 대상");
        var target = new ComboBox { ItemsSource = new[] { "파일과 폴더", "파일만", "폴더만" }, SelectedIndex = options.Target == "Files" ? 1 : options.Target == "Folders" ? 2 : 0 };
        panel.Children.Add(target);
        Heading("이름 일치 방식");
        var matching = new ComboBox { ItemsSource = new[] { "일부 일치", "전부 일치" }, SelectedIndex = options.Exact ? 1 : 0 };
        panel.Children.Add(matching);
        var example = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
        example.SetResourceReference(TextBlock.FontSizeProperty, "FontSize10");
        example.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); panel.Children.Add(example);
        void UpdateExample() => example.Text = matching.SelectedIndex == 1
            ? "‘여행 사진’ → 여행 사진.jpg, 여행 사진.png\n확장자를 뺀 이름 전체가 같아야 합니다. ‘여행 사진 모음’은 제외됩니다."
            : "‘사진’ → 여행 사진.jpg, 사진 모음 폴더\n이름에 검색어가 들어 있으면 찾습니다.";
        matching.SelectionChanged += (_, _) => UpdateExample(); UpdateExample();
        Heading("추가 검색 옵션");
        var parents = new CheckBox { Content = new TextBlock { Text = "폴더 이름이 맞으면 그 안의 파일도 표시", TextWrapping = TextWrapping.Wrap }, IsChecked = options.IncludeParentFolders, Margin = new Thickness(0, 4, 0, 0) };
        panel.Children.Add(parents);
        Hint("예: 여행 사진\\2026\\제주도.jpg\n켜면 ‘여행 사진’ 검색에 제주도.jpg도 표시됩니다.\n끄면 하위 항목도 찾되 각 파일·폴더의 이름만 비교합니다.");
        var initials = new CheckBox { Content = "한글 초성 검색 — ㅅㅈ → 사진", IsChecked = options.UseInitials, Margin = new Thickness(0, 6, 0, 0) };
        panel.Children.Add(initials);
        Heading("검색 범위");
        var scope = new Button { Content = "검색할 드라이브·제외 폴더 관리…", HorizontalAlignment = HorizontalAlignment.Left };
        scope.Click += (_, _) => { dialog.Close(); new SearchScopeWindow(this, ViewModel.Index).ShowDialog(); }; panel.Children.Add(scope);
        Heading("검색식 사용 안내");
        Hint("OR: 둘 중 하나 · &: 모두 포함 · !: 제외\n일부 일치: 공백은 &와 같음\n전부 일치: 공백을 포함한 이름 전체 비교\n여러 이름 조합: \"여행 사진\" OR \"가족 사진\"\n\n영어 대소문자는 구분하지 않습니다.\n문서 내용·오타·동의어 검색은 지원하지 않습니다.");
        Hint("옵션은 모든 작업공간에 적용되며 다음 실행에도 유지됩니다.");
        Divider(new Thickness(0, 12, 0, 0));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var reset = new Button { Content = "기본값", Margin = new Thickness(0, 0, 12, 0) };
        reset.Click += (_, _) => { target.SelectedIndex = 0; matching.SelectedIndex = 0; parents.IsChecked = false; initials.IsChecked = true; };
        var cancel = new Button { Content = "취소", IsCancel = true };
        var apply = new Button { Content = "저장", IsDefault = true };
        apply.Click += (_, _) =>
        {
            ViewModel.Search.ApplyOptions(new SearchOptions { Target = target.SelectedIndex == 1 ? "Files" : target.SelectedIndex == 2 ? "Folders" : "All", Exact = matching.SelectedIndex == 1, IncludeParentFolders = parents.IsChecked == true, UseInitials = initials.IsChecked == true });
            ViewModel.Changed(); ViewModel.Save(); dialog.DialogResult = true;
        };
        buttons.Children.Add(reset); buttons.Children.Add(cancel); buttons.Children.Add(apply); panel.Children.Add(buttons);
        if (!TestMode) DismissSettingsOnOutsideClick.Attach(dialog);
        return dialog;
    }
}
