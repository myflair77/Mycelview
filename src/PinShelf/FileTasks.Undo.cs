using System.IO;
using System.Windows.Interop;

namespace PinShelf;
public sealed partial class FileTasks
{
    private UndoHistoryStore? history;
    public bool CanUndo => !Busy && !vm.SettingsBusy && !vm.WorkspaceBusy && history?.Data.Undo.Count > 0;
    public bool CanRedo => !Busy && !vm.SettingsBusy && !vm.WorkspaceBusy && history?.Data.Redo.Count > 0;
    public string UndoLabel => "실행 취소 (Ctrl+Z)";
    public string RedoLabel => "다시 실행 (Ctrl+Y)";
    private void NotifyUndo() { Notify(nameof(CanUndo)); Notify(nameof(CanRedo)); Notify(nameof(UndoLabel)); }
    internal Task UndoAsync() => RestoreAsync(false);
    internal Task RedoAsync() => RestoreAsync(true);
    private async Task RestoreAsync(bool redo)
    {
        if (redo ? !CanRedo : !CanUndo) return;
        Busy = true; HasActivity = true; Percent = 0;
        cancellation = new CancellationTokenSource();
        var journal = history!.Records[(redo ? history.Data.Redo : history.Data.Undo).Last()];
        try
        {
            Status = journal.Title + " 실행 취소 준비 중 · 이후 변경 사항 확인…";
            if (!redo)
            {
                await Task.Run(() => history.Cleanup(journal.Items.Where(x => x.AfterBackup == null).Sum(x => UndoHistoryStore.Size(x.Path)) + 65536, journal.Id));
                await journal.PrepareRedoAsync(cancellation.Token);
            }
            history.Begin(redo);
            var retained = redo ? await journal.RedoAsync(cancellation.Token) : await journal.UndoAsync(cancellation.Token);
            history.Finish(redo); NotifyUndo();
            foreach (var change in journal.Changes.Reverse())
                if (change.Request.Action is FileAction.Move or FileAction.Rename && change.NewPath != null) Relocate(redo ? change.Request.Source : change.NewPath, redo ? change.NewPath : change.Request.Source);
            await RefreshAsync(); Percent = 100;
            Status = journal.Title + (redo ? " 다시 실행 완료" : " 실행 취소 완료");
            if (retained.Length > 0)
            {
                // This cleanup is recoverable: never permanently delete post-operation data.
                try
                {
                    var cleanup = await WindowsFileOperations.RunAsync(retained.Select(p => new FileRequest(FileAction.Recycle, p)).ToArray(), new WindowInteropHelper(Owner).Handle, CancellationToken.None,
                        flags: WindowsFileOperations.InteractiveFlags | 0x0010u | 0x0004u | 0x0400u);
                    if (cleanup.Completed.Count != retained.Length) Status += " · 일부 임시 보관 파일(.pinshelf-undo-*.hold)이 남아 있습니다.";
                }
                catch { Status += " · 임시 보관 파일 정리를 완료하지 못했습니다."; }
            }
        }
        catch (OperationCanceledException) { Status = "실행 취소 중단 · 작업 기록 유지"; }
        catch (Exception error) { Status = "실행 취소 실패: " + error.Message + " · 보관 위치: " + journal.DirectoryPath; }
        finally { if (history.Data.Pending != null) { try { if (journal.Matches(redo)) { history.Data.Pending = null; history.Save(); } } catch { } } Busy = false; cancellation.Dispose(); cancellation = null; }
    }
}
