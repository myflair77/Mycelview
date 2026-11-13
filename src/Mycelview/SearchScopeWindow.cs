using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Mycelview;

internal sealed class SearchScopeWindow : Window
{
    internal SearchScopeWindow(Window owner, LocalSearchIndex index)
    {
        Owner = owner; Icon = owner.Icon; Title = "검색할 드라이브·제외 폴더 관리";
        Width = 560; SizeToContent = SizeToContent.Height; MaxHeight = SystemParameters.WorkArea.Height - 40;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(FontSizeProperty, "FontSize12");
        SetResourceReference(BackgroundProperty, "SurfaceBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        var panel = new StackPanel { Margin = new Thickness(20) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var config = index.Settings;
        var all = new CheckBox { Content = "연결된 모든 로컬 드라이브 검색", IsChecked = config.AllLocalDrives, Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(all);
        static string[] Lines(string text) => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        (TextBox, Button) Paths(string heading, string[] paths)
        {
            panel.Children.Add(new TextBlock { Text = heading, Margin = new Thickness(0, 8, 0, 6), FontWeight = FontWeights.SemiBold });
            var box = new TextBox { Text = string.Join(Environment.NewLine, paths), AcceptsReturn = true, Height = 85, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
            panel.Children.Add(box);
            var browse = new Button { Content = "폴더 추가…", HorizontalAlignment = HorizontalAlignment.Left };
            browse.Click += (_, _) =>
            {
                var picker = new OpenFolderDialog { Title = heading, Multiselect = true };
                if (picker.ShowDialog(this) == true) box.Text = string.Join(Environment.NewLine, Lines(box.Text).Concat(picker.FolderNames).Distinct(StringComparer.OrdinalIgnoreCase));
            };
            panel.Children.Add(browse); return (box, browse);
        }
        var (included, includeButton) = Paths("검색할 드라이브·폴더", config.IncludedFolders);
        var (excluded, _) = Paths("제외 폴더 · 하위 폴더도 함께 제외", config.ExcludedFolders);
        void Update() => included.IsEnabled = includeButton.IsEnabled = all.IsChecked != true;
        all.Checked += (_, _) => Update(); all.Unchecked += (_, _) => Update(); Update();
        panel.Children.Add(new TextBlock { Text = "경로는 한 줄에 하나씩 입력하세요. 목록에서 지우면 제외됩니다.\n저장한 범위는 백그라운드에서 적용되며 다음 실행에도 유지됩니다.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 8) });
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap }; panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(new Button { Content = "취소", IsCancel = true });
        var save = new Button { Content = "저장", IsDefault = true }; buttons.Children.Add(save); panel.Children.Add(buttons);
        save.Click += (_, _) =>
        {
            try
            {
                var work = index.ApplySettingsAsync(new IndexSettings { AllLocalDrives = all.IsChecked == true, IncludedFolders = Lines(included.Text), ExcludedFolders = Lines(excluded.Text) });
                _ = Observe(work); DialogResult = true;
            }
            catch (Exception ex) { error.Text = ex.Message; }
        };
        DismissSettingsOnOutsideClick.Attach(this);
    }
    private static async Task Observe(Task work)
    {
        // LocalSearchIndex publishes completion/errors to the main window and index manager.
        try { await work; } catch (Exception) { }
    }
}
