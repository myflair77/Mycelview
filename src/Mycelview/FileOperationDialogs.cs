using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Mycelview;
internal enum CollisionChoice { Replace, Skip, KeepBoth, Cancel }
internal sealed record CollisionDecision(CollisionChoice Choice, bool ApplyAll);
internal sealed class NameEdit
{
    public string Path { get; init; } = "";
    public string Original => System.IO.Path.GetFileName(Path);
    public string Name { get; set; } = "";
}
internal static class FileOperationDialogs
{
    private static Window Dialog(Window owner, string title, double width = 520) => new()
    { Owner = owner, Title = title, Width = width, Icon = owner.Icon, SizeToContent = SizeToContent.Height, MaxHeight = 650,
        WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
        Background = (System.Windows.Media.Brush)owner.FindResource("SurfaceBrush"), Foreground = (System.Windows.Media.Brush)owner.FindResource("TextBrush") };
    internal static CollisionDecision Conflict(Window owner, string source, string destination)
    {
        var window = Dialog(owner, "같은 이름이 있습니다");
        var panel = new StackPanel { Margin = new Thickness(18) }; window.Content = panel;
        panel.Children.Add(new TextBlock { Text = $"원본: {source}\n대상: {destination}", TextWrapping = TextWrapping.Wrap });
        var folderConflict = Directory.Exists(source) || Directory.Exists(destination);
        var mixedKinds = Directory.Exists(source) != Directory.Exists(destination);
        panel.Children.Add(new TextBlock { Text = mixedKinds ? "파일과 폴더의 이름이 겹칩니다. 건너뛰거나 다른 이름으로 보관하세요." : folderConflict ? "같은 폴더는 병합합니다. 내부 파일 충돌은 Windows 창에서 다시 선택합니다." : "기존 파일을 바꾸거나, 건너뛰거나, 두 파일을 모두 보관할 수 있습니다.", Margin = new Thickness(0,12,0,8), TextWrapping = TextWrapping.Wrap });
        var all = new CheckBox { Content = "남은 같은 종류의 충돌에도 적용", Margin = new Thickness(0,4,0,12) }; panel.Children.Add(all);
        var buttons = new WrapPanel(); panel.Children.Add(buttons);
        var result = CollisionChoice.Cancel;
        foreach (var pair in new[] { (CollisionChoice.Replace, folderConflict ? "폴더 병합" : "덮어쓰기"), (CollisionChoice.Skip, "건너뛰기"), (CollisionChoice.KeepBoth, "다른 이름으로 보관"), (CollisionChoice.Cancel, "전체 취소") })
        {
            var button = new Button { Content = pair.Item2, IsCancel = pair.Item1 == CollisionChoice.Cancel };
            button.Click += (_, _) => { result = pair.Item1; window.DialogResult = pair.Item1 != CollisionChoice.Cancel; }; buttons.Children.Add(button);
            if (pair.Item1 == CollisionChoice.Replace && Directory.Exists(source) != Directory.Exists(destination)) button.IsEnabled = false;
        }
        window.ShowDialog(); return new(result, all.IsChecked == true);
    }
    internal static NameEdit[]? Rename(Window owner, string[] paths)
    {
        var window = Dialog(owner, paths.Length == 1 ? "이름 변경" : $"선택한 {paths.Length}개 이름 변경", 640);
        var edits = paths.Select(p => new NameEdit { Path = p, Name = System.IO.Path.GetFileName(p) }).ToArray();
        var panel = new StackPanel { Margin = new Thickness(16) }; window.Content = panel;
        panel.Children.Add(new TextBlock { Text = "새 이름을 입력하세요. 파일의 확장자도 이름에 포함됩니다.", Margin = new Thickness(0,0,0,8) });
        var grid = new DataGrid { ItemsSource = edits, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, MaxHeight = 380, MinHeight = 85 };
        grid.Columns.Add(new DataGridTextColumn { Header = "현재 이름", Binding = new Binding("Original"), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "새 이름", Binding = new Binding("Name") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        panel.Children.Add(grid);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap }; panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(buttons);
        var cancel = new Button { Content = "취소", IsCancel = true }; buttons.Children.Add(cancel);
        var ok = new Button { Content = "변경", IsDefault = true }; buttons.Children.Add(ok);
        ok.Click += (_, _) =>
        {
            grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true);
            try
            {
                foreach (var edit in edits) FileOperationRules.CheckName(edit.Name);
                var names = edits.Select(x => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(x.Path)!, x.Name)).ToArray();
                if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length) throw new IOException("여러 항목에 같은 새 이름을 사용할 수 없습니다.");
                window.DialogResult = true;
            }
            catch (Exception ex) { error.Text = ex.Message; }
        };
        return window.ShowDialog() == true ? edits : null;
    }
    internal static string? NewFolder(Window owner)
    {
        var window = Dialog(owner, "새 폴더 만들기", 420); var panel = new StackPanel { Margin = new Thickness(16) }; window.Content = panel;
        var box = new TextBox { Text = "새 폴더" }; panel.Children.Add(box);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap }; panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(buttons);
        buttons.Children.Add(new Button { Content = "취소", IsCancel = true }); var ok = new Button { Content = "만들기", IsDefault = true }; buttons.Children.Add(ok);
        ok.Click += (_, _) => { try { FileOperationRules.CheckName(box.Text); window.DialogResult = true; } catch (Exception ex) { error.Text = ex.Message; } };
        window.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return window.ShowDialog() == true ? box.Text : null;
    }
}

internal static class FileOperationPlanner
{
    internal static List<FileRequest>? Resolve(IEnumerable<FileRequest> requests, Func<string, string, CollisionDecision> decide)
    {
        var output = new List<FileRequest>(); var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var decisions = new Dictionary<(bool SourceFolder, bool DestinationFolder), CollisionChoice>();
        var batch = requests.ToArray();
        foreach (var request in batch)
        {
            FileOperationRules.Validate(request);
            if (request.Action == FileAction.Recycle) { output.Add(request); continue; }
            var parent = request.Action switch { FileAction.NewFolder => request.Source, FileAction.Rename => Path.GetDirectoryName(request.Source)!, _ => request.Destination! };
            string name = request.Name ?? Path.GetFileName(request.Source);
            var destination = Path.Combine(parent, name);
            if (request.Action == FileAction.Rename && batch.Any(other => other != request && other.Action == FileAction.Rename && FileOperationRules.Same(other.Source, destination)))
                throw new IOException("다른 선택 항목의 현재 이름으로 바꿀 수 없습니다. 임시 이름으로 먼저 변경한 뒤 다시 시도하세요.");
            if (request.Action == FileAction.Move && FileOperationRules.Same(request.Source, destination)) continue;
            if (request.Action == FileAction.Rename && string.Equals(request.Source, destination, StringComparison.Ordinal)) continue;
            bool same = request.Action != FileAction.NewFolder && FileOperationRules.Same(request.Source, destination);
            bool exists = File.Exists(destination) || Directory.Exists(destination) || reserved.Contains(destination);
            if (exists && !(same && request.Action == FileAction.Rename))
            {
                var kinds = (request.Action == FileAction.NewFolder || Directory.Exists(request.Source), Directory.Exists(destination));
                CollisionChoice choice;
                if (same && request.Action == FileAction.Copy) choice = CollisionChoice.KeepBoth;
                else if (!decisions.TryGetValue(kinds, out choice))
                { var decision = decide(request.Source, destination); choice = decision.Choice; if (decision.ApplyAll) decisions[kinds] = choice; }
                if (choice == CollisionChoice.Cancel) return null;
                if (choice == CollisionChoice.Skip) continue;
                if (choice == CollisionChoice.Replace && kinds.Item1 != kinds.Item2) throw new IOException("파일과 폴더는 서로 덮어쓸 수 없습니다.");
                if (choice == CollisionChoice.Replace && request.Action == FileAction.NewFolder && Directory.Exists(destination)) continue;
                if (choice == CollisionChoice.KeepBoth)
                {
                    var extension = request.Action == FileAction.NewFolder || Directory.Exists(request.Source) ? "" : Path.GetExtension(name);
                    var stem = name[..(name.Length - extension.Length)]; int n = 2;
                    do { name = $"{stem} ({n++}){extension}"; destination = Path.Combine(parent, name); }
                    while (File.Exists(destination) || Directory.Exists(destination) || reserved.Contains(destination));
                    FileOperationRules.CheckName(name);
                }
                // The Shell still owns collision handling at execution time, including races and nested folder merges.
            }
            reserved.Add(destination); output.Add(request with { Name = name });
        }
        return output;
    }
}
