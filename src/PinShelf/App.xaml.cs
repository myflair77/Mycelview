using System.IO;
using System.Windows;

namespace PinShelf;
public partial class App : Application
{
    private Mutex? instanceMutex;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        TextScale.Apply(100);
        TransientPopupTracking.Initialize();
        SkinManager.Apply(SkinManager.DefaultId);
        if (e.Args.Length >= 2 && e.Args[0] == "--test-workspace-wheel")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var output = Path.GetFullPath(e.Args[1]);
            try { await WorkspaceWheelTests.RunAsync(output); Shutdown(0); }
            catch (Exception error) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString()); Shutdown(1); }
            return;
        }
        if (e.Args.Length >= 2 && e.Args[0] == "--test-shell-menu")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var output = Path.GetFullPath(e.Args[1]);
            try { await MenuLayoutTests.RunAsync(output); Shutdown(0); }
            catch (Exception error) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString()); Shutdown(1); }
            return;
        }
        if (e.Args.Length >= 2 && e.Args[0] == "--benchmark-index")
        {
            var output = Path.GetFullPath(e.Args[1]);
            var realIndex = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PinShelf");
            var benchmarkVm = new MainViewModel(new StateStore(output), realIndex);
            var benchmarkWindow = new MainWindow(benchmarkVm) { TestMode = true }; MainWindow = benchmarkWindow; benchmarkWindow.Show();
            try { await SearchBenchmark.RunAsync(benchmarkWindow, output); Shutdown(0); }
            catch (Exception error) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString()); Shutdown(1); }
            return;
        }
        if (e.Args.Length >= 2 && e.Args[0] == "--benchmark-folders")
        {
            var output = Path.GetFullPath(e.Args[1]);
            try { await FolderPerformanceBenchmark.RunAsync(output); Shutdown(0); }
            catch (Exception error) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString()); Shutdown(1); }
            return;
        }
        if(e.Args.Length>=2 && e.Args[0]=="--test-pdf")
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            var output=Path.GetFullPath(e.Args[1]);
            try {await PdfPreviewTests.RunAsync(output);Shutdown(0);}
            catch(Exception error){Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"failure.txt"),error.ToString());Shutdown(1);}
            return;
        }        if(e.Args.Length>=2 && e.Args[0]=="--test-inline-rename")
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;var output=Path.GetFullPath(e.Args[1]);
            try {await InlineRenameTests.RunAsync(output);Shutdown(0);}
            catch(Exception error){Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"failure.txt"),error.ToString());Shutdown(1);}
            return;
        }        bool test = e.Args.Length >= 2 && e.Args[0] is "--self-test" or "--verify-test";
        bool manualQa = e.Args.Length >= 2 && e.Args[0] == "--manual-qa";
        string directory = (test || manualQa) ? Path.GetFullPath(e.Args[1]) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PinShelf");
        if (!test && !manualQa)
        {
            instanceMutex = new Mutex(true, "Local\\PinShelf-" + Environment.UserName, out var first);
            if (!first) { MessageBox.Show("PinShelf가 이미 실행 중입니다.", "PinShelf"); Shutdown(); return; }
        }
        var vm = new MainViewModel(new StateStore(directory));
        var window = new MainWindow(vm) { TestMode = test }; MainWindow = window; if (manualQa) window.Title = "PinShelf — Manual QA"; window.Show();
        await vm.InitializeAsync();
        window.RestorePaneLayout();
        if (!test && !manualQa) vm.Index.Start();
        if (test)
        {
            try { await SmokeTests.RunAsync(window, directory, e.Args[0] == "--verify-test"); Shutdown(0); }
            catch (Exception ex) { Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString()); Shutdown(1); }
        }
    }
    protected override void OnExit(ExitEventArgs e) { instanceMutex?.Dispose(); base.OnExit(e); }
}



