using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace PinShelf;
public partial class MainWindow
{
    private void SettingsMenu(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        button.ContextMenu.PlacementTarget = button; button.ContextMenu.IsOpen = true;
    }
    private void ExportSettings(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "설정 내보내기", Filter = "PinShelf 설정 백업|*.pinshelf.json", FileName = $"PinShelf-{DateTime.Now:yyyyMMdd-HHmmss}.pinshelf.json", AddExtension = true, DefaultExt = ".pinshelf.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { CaptureWorkspaceLayout(); ViewModel.ExportSettings(dialog.FileName); ViewModel.Status = "설정 내보내기 완료: " + dialog.FileName; }
        catch (Exception error) { MessageBox.Show(this, error.Message, "설정 내보내기 실패", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private async void ImportSettings(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "설정 가져오기 — 현재 설정은 자동 백업됩니다", Filter = "PinShelf 설정 백업|*.pinshelf.json|JSON 파일|*.json", CheckFileExists = true };
        if (sender is MenuItem { Tag: "backup" } && Directory.Exists(ViewModel.BackupDirectory)) dialog.InitialDirectory = ViewModel.BackupDirectory;
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var preview = WorkspaceBackup.Read(dialog.FileName);
            if (MessageBox.Show(this, $"작업공간 {preview.Workspaces.Count}개 · 폴더 카드 {preview.Workspaces.Sum(w => w.Cards.Count(c => !c.IsFile))}개 · 파일 즐겨찾기 {preview.Workspaces.Sum(w => w.Cards.Count(c => c.IsFile))}개\n모든 작업공간의 카드·메모·배치·정렬 설정을 선택한 백업으로 교체합니다.\n현재 설정은 복원 전에 자동 백업됩니다. 원본 파일은 변경하지 않습니다.\n\n가져올까요?", "설정 가져오기", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            IsEnabled = false;
            CaptureWorkspaceLayout();
            var backup = await ViewModel.ImportSettingsAsync(dialog.FileName);
            RestorePaneLayout();
            ViewModel.Status = "설정 복원 완료 · 이전 설정 자동 백업: " + backup;
            MessageBox.Show(this, "설정을 복원했습니다.\n이전 설정 백업:\n" + backup, "복원 완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "설정 가져오기 실패", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { IsEnabled = true; }
    }
}
