using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PinShelf;
public sealed partial class FileTasks : Observable
{
    private readonly MainViewModel vm;
    private CancellationTokenSource? cancellation;
    private bool busy, hasActivity;
    private string status = "";
    private double percent;
    public bool Busy { get => busy; private set { Set(ref busy, value); NotifyUndo(); } }
    public bool HasActivity { get => hasActivity; private set => Set(ref hasActivity, value); }
    public string Status { get => status; private set => Set(ref status, value); }
    public double Percent { get => percent; private set => Set(ref percent, value); }
    public RelayCommand Cancel { get; }
    public RelayCommand Dismiss { get; }
    internal FileTasks(MainViewModel vm)
    {
        this.vm = vm;
        try { history = new(vm.UndoDirectory); } catch (Exception ex) { Status = "실행 취소 기록 열기 실패: " + ex.Message; HasActivity = true; }
        Cancel = new(() => { cancellation?.Cancel(); if (Busy) Status = "취소 요청 중… 완료된 항목은 유지됩니다. Windows 확인 창이 열려 있으면 그 창에서도 취소하세요."; });
        Dismiss = new(() => { if (!Busy) HasActivity = false; });
    }
    private Window Owner => Application.Current.MainWindow;
    internal void PutClipboard(IEnumerable<string> paths, bool cut)
    {
        try
        {
            var items = FileOperationRules.TopLevel(paths);
            if (items.Length == 0) return;
            SetClipboard(items, cut); HasActivity = true;
            Status = $"{items.Length}개 {(cut ? "잘라내기" : "복사")} 준비 · 대상 폴더에서 붙여넣기";
        }
        catch (Exception ex) { HasActivity = true; Status = "클립보드 사용 실패: " + ex.Message; }
    }
    private static void SetClipboard(string[] paths, bool cut)
    {
        var collection = new StringCollection(); collection.AddRange(paths);
        var data = new DataObject(); data.SetFileDropList(collection);
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(cut ? 2 : 5)));
        Clipboard.SetDataObject(data, true);
    }
    internal static (string[] Paths, bool Cut) ReadClipboard()
    {
        var data = Clipboard.GetDataObject();
        return DecodeClipboard(data);
    }
    internal static (string[] Paths, bool Cut) DecodeClipboard(IDataObject? data)
    {
        var paths = data?.GetData(DataFormats.FileDrop) as string[] ?? [];
        var effect = data?.GetData("Preferred DropEffect");
        byte[]? bytes = effect is MemoryStream stream ? stream.ToArray() : effect as byte[];
        return (paths, bytes?.Length >= 4 && (BitConverter.ToInt32(bytes) & 2) != 0);
    }
    internal static bool CanPaste { get { try { return Clipboard.ContainsFileDropList(); } catch { return false; } } }
    internal async Task PasteAsync(string destination)
    {
        if (Busy || vm.WorkspaceBusy) return;
        try
        {
            var clip = ReadClipboard(); var sequence = GetClipboardSequenceNumber();
            if (clip.Paths.Length == 0) return;
            var requests = FileOperationRules.TopLevel(clip.Paths).Select(p => new FileRequest(clip.Cut ? FileAction.Move : FileAction.Copy, p, destination)).ToArray();
            var result = await ExecuteAsync(clip.Cut ? "이동" : "복사", requests);
            if (clip.Cut && result != null && GetClipboardSequenceNumber() == sequence)
            {
                var moved = result.Completed.Where(x => x.Request.Action == FileAction.Move).Select(x => x.Request.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var remaining = clip.Paths.Where(p => !moved.Any(m => FileOperationRules.Same(m, p) || FileOperationRules.Inside(p, m))).ToArray();
                if (remaining.Length == 0) Clipboard.Clear(); else SetClipboard(remaining, true);
            }
        }
        catch (Exception ex) { HasActivity = true; Status = "붙여넣기 실패: " + ex.Message; }
    }
    internal async Task<FileOperationResult?> TransferAsync(IEnumerable<string> paths, string destination, bool copy)
    {
        if (Busy || vm.WorkspaceBusy) return null;
        try
        {
            var items = FileOperationRules.TopLevel(paths);
            if (items.Length == 0) return null;
            return await ExecuteAsync(copy ? "복사" : "이동", items.Select(p => new FileRequest(copy ? FileAction.Copy : FileAction.Move, p, destination)).ToArray());
        }
        catch (Exception ex) { HasActivity = true; Status = "드래그 작업 실패: " + ex.Message; return null; }
    }
    internal async Task RenameAsync(IEnumerable<string> paths)
    {
        if (Busy || vm.WorkspaceBusy) return;
        try
        {
            var items = FileOperationRules.TopLevel(paths); if (items.Length == 0) return;
            var edits = FileOperationDialogs.Rename(Owner, items); if (edits == null) return;
            await ExecuteAsync("이름 변경", edits.Select(x => new FileRequest(FileAction.Rename, x.Path, Name: x.Name)).ToArray());
        }
        catch (Exception ex) { HasActivity = true; Status = "이름 변경 실패: " + ex.Message; }
    }
    internal async Task NewFolderAsync(string destination)
    {
        if (Busy || vm.WorkspaceBusy) return;
        var name = FileOperationDialogs.NewFolder(Owner); if (name == null) return;
        await ExecuteAsync("새 폴더 만들기", [new(FileAction.NewFolder, destination, Name: name)]);
    }
    internal async Task RecycleAsync(IEnumerable<string> paths)
    {
        if (Busy || vm.WorkspaceBusy) return;
        var items = FileOperationRules.TopLevel(paths); if (items.Length == 0) return;
        if (MessageBox.Show(Owner, $"선택한 {items.Length}개 원본을 휴지통으로 이동할까요?\n즐겨찾기 등록 해제와는 다릅니다.", "휴지통으로 이동", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        await ExecuteAsync("휴지통 이동", items.Select(x => new FileRequest(FileAction.Recycle, x)).ToArray());
    }
    internal async Task<FileOperationResult?> ExecuteAsync(string title, IReadOnlyList<FileRequest> requests)
    {
        if (Busy || vm.WorkspaceBusy) return null;
        Busy = true; HasActivity = true; Percent = 0;
        cancellation = new CancellationTokenSource(); var token = cancellation.Token;
        try
        {
            Status = title + " 준비 중…";
            var plan = FileOperationPlanner.Resolve(requests, (source, destination) => FileOperationDialogs.Conflict(Owner, source, destination));
            if (plan == null) { Status = title + " 취소 · 변경된 항목 없음"; return null; }
            if (plan.Count == 0) { Status = title + " · 모든 항목을 건너뛰었습니다."; return null; }
            Status = "실행 취소를 위한 원본 보관 중…";
            if (history == null) throw new IOException("실행 취소 기록을 열 수 없어 파일 작업을 중단했습니다.");
            if (plan.All(r => r.Action is FileAction.Copy or FileAction.Move or FileAction.Rename))
                await Task.Run(() => history.Cleanup(FileUndoJournal.AffectedPaths(plan).Sum(UndoHistoryStore.Size) + 65536));
            var journal = await FileUndoJournal.PrepareAsync(title, vm.UndoDirectory, plan, token);
            if (journal != null)
            {
                try { history.Cleanup(protect: journal.Id); }
                catch { UndoHistoryStore.RemoveOwned(journal.DirectoryPath, vm.UndoDirectory); throw; }
            }
            var progress = new Progress<FileProgress>(p =>
            {
                if (!Busy || token.IsCancellationRequested) return;
                if (p.Total > 0) Percent = Math.Clamp(100d * p.Done / p.Total, 0, 100);
                Status = $"{title} 중 · {Percent:0}% · {Path.GetFileName(p.Path)}";
            });
            var result = await WindowsFileOperations.RunAsync(plan, new WindowInteropHelper(Owner).Handle, token, progress);
            string? undoWarning = null;
            if (journal != null)
            {
                try { if (await journal.CompleteAsync(result)) { history.Add(journal); NotifyUndo(); } else UndoHistoryStore.RemoveOwned(journal.DirectoryPath, vm.UndoDirectory); }
                catch (Exception error) { undoWarning = "실행 취소 기록 실패: " + error.Message; }
            }
            foreach (var change in result.Completed)
                if (change.Request.Action is FileAction.Move or FileAction.Rename && change.NewPath != null) Relocate(change.Request.Source, change.NewPath);
            await RefreshAsync();
            var remaining = plan.Count - result.Completed.Count;
            Status = $"{title} {(result.Aborted ? "중단" : "완료")} · 완료 {result.Completed.Count}개 · 미완료/건너뜀 {remaining}개";
            if (result.Aborted) Status += " · 이미 완료된 항목은 유지됩니다.";
            if (result.Errors.Count > 0) Status += " · " + string.Join(" / ", result.Errors.Take(3));
            if (undoWarning != null) Status += " · " + undoWarning;
            if (!result.Aborted && remaining == 0) Percent = 100;
            return result;
        }
        catch (OperationCanceledException) { Status = title + " 준비 취소 · 원본 변경 없음"; return null; }
        catch (Exception ex) { Status = title + " 실패: " + ex.Message; return null; }
        finally { Busy = false; cancellation.Dispose(); cancellation = null; }
    }
    private void Relocate(string oldPath, string newPath)
    {
        foreach (var card in vm.Cards) card.ApplyRelocation(oldPath, newPath);
        vm.RelocateInactiveWorkspaces(oldPath, newPath);
        for (int i = 0; i < vm.RecentFiles.Count; i++)
        {
            var recent = vm.RecentFiles[i]; var path = Remap(recent.CurrentPath, oldPath, newPath);
            if (path != recent.CurrentPath) vm.RecentFiles[i] = recent with { CurrentPath = path };
        }
        vm.Changed();
        vm.Explorer.ApplyRelocation(oldPath, newPath);
    }
    internal static string Remap(string path, string oldPath, string newPath)
    {
        if (FileOperationRules.Same(path, oldPath)) return newPath;
        return FileOperationRules.Inside(path, oldPath) ? newPath + FileOperationRules.Full(path)[FileOperationRules.Full(oldPath).Length..] : path;
    }
    private async Task RefreshAsync()
    {
        var tasks = vm.Cards.ToArray().Select(x => x.RefreshAsync()).ToList();
        tasks.Add(vm.Explorer.RefreshLoadedTreeAsync());
        if (Directory.Exists(vm.Explorer.CurrentPath)) tasks.Add(vm.Explorer.NavigateAsync(vm.Explorer.CurrentPath));
        await Task.WhenAll(tasks);
    }
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
}
