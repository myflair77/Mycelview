using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PinShelf;
public partial class MainWindow
{
    private void OpenRecent(RecentFile file)
    { try { FileService.Open(file.CurrentPath); } catch (Exception e) { ViewModel.Status = "최근 파일을 열 수 없습니다: " + e.Message; } }
    private void RecentOpen(object sender, MouseButtonEventArgs e)
    {
        if (FindParent<Button>(e.OriginalSource as DependencyObject) != null) return;
        if (FindParent<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is RecentFile file) OpenRecent(file);
    }
    private void RecentKeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Enter && RecentFilesList.SelectedItem is RecentFile file) { OpenRecent(file); e.Handled = true; } }
    private void RegisterRecent(object sender, RoutedEventArgs e)
    { if (((FrameworkElement)sender).DataContext is RecentFile file) ViewModel.AddPath(file.CurrentPath); }
    private void RecentMouseDown(object sender, MouseButtonEventArgs e)
    { dragStart = e.GetPosition(this); dragPath = FindParent<Button>(e.OriginalSource as DependencyObject) == null ? (FindParent<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as RecentFile)?.CurrentPath : null; }
    private void RecentMouseMove(object sender, MouseEventArgs e)
    { if (ShouldDrag(e)) { StartRegistrationDrag(RecentFilesList, [dragPath!]); e.Handled = true; } }
}
