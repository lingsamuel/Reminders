using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Reminders.Persistence;
using Reminders.Windows.Bridge;
using Reminders.Windows.Overlay;
using Reminders.Windows.Platform;

namespace Reminders.Windows;

public partial class App : Application
{
    private FileStream? instanceLock;
    private BackendSession? session;
    private JsonLineProtocol? protocol;
    private OverlayController? overlay;
    private TrayController? tray;
    private DispatcherTimer? refreshTimer;
    private StreamReader? input;
    private StreamWriter? output;
    private BackendOptions? options;
    private static string? logPath;
    private static App? running;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        running = this;
        try
        {
            options = BackendOptions.Parse(e.Args);
            var directory = options.DataDirectory;
            Directory.CreateDirectory(directory);
            logPath = Path.Combine(directory, "errors.log");
            // 继续锁定真实文件，目录别名和末尾分隔符不能绕过单实例保护。
            instanceLock = new FileStream(Path.Combine(directory, ".instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var store = new JsonStateStore(Path.Combine(directory, "state.json"));
            session = new BackendSession(store, () => store.RestoreBackup(), () => store.HasBackup);
            output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
            input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            protocol = new JsonLineProtocol(session, output);
            protocol.ExitRequested += (_, _) => Dispatcher.BeginInvoke(() => Shutdown());
            session.ServiceReplaced += (_, _) => InitializeRuntime();
            InitializeRuntime();
            refreshTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => session.RefreshTime(), Dispatcher);
            refreshTimer.Start();
            DispatcherUnhandledException += (_, args) =>
            {
                ReportError(args.Exception, true);
                args.Handled = true;
            };
            protocol.SendReady();
            _ = ReadRequests();
        }
        catch (Exception error)
        {
            ReportError(error, false);
            Shutdown(1);
        }
    }

    private void InitializeRuntime()
    {
        overlay?.Dispose();
        tray?.Dispose();
        overlay = null;
        tray = null;
        // 开发与 CI 的协议验收不创建任何 Overlay 或托盘窗口。
        if (options?.Headless == true || session?.Service is not { } service) return;
        tray = new TrayController(service, Dispatcher, () => protocol?.Notify("openEditor"), ExitApplication);
        overlay = new OverlayController(service, session, options?.EditorProcessId);
    }

    private async Task ReadRequests()
    {
        // 阻塞读取在后台执行，所有 Core 与 HWND 操作串行回到 WPF Dispatcher。
        await Task.Run(async () =>
        {
            try
            {
                while (input is not null && await input.ReadLineAsync() is { } line)
                    await Dispatcher.InvokeAsync(() => protocol?.ProcessLine(line));
            }
            catch (Exception error) when (error is IOException or ObjectDisposedException or TaskCanceledException)
            {
                ReportError(error, false);
            }
            finally
            {
                // 父进程退出或管道断开时关闭 Overlay、托盘并释放单实例锁。
                if (!Dispatcher.HasShutdownStarted) _ = Dispatcher.BeginInvoke(() => Shutdown());
            }
        });
    }

    private void ExitApplication()
    {
        protocol?.Notify("exit");
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        refreshTimer?.Stop();
        overlay?.Dispose();
        tray?.Dispose();
        protocol?.Dispose();
        session?.Dispose();
        output?.Dispose();
        instanceLock?.Dispose();
        running = null;
        base.OnExit(e);
    }

    internal static void ReportError(Exception error, bool notifyEditor)
    {
        try { Console.Error.WriteLine(error); }
        catch (IOException) { }
        try
        {
            if (logPath is not null)
            {
                if (File.Exists(logPath) && new FileInfo(logPath).Length > 1_048_576) File.Move(logPath, logPath + ".old", true);
                File.AppendAllText(logPath, $"{DateTimeOffset.Now:O} {error}\n");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        if (notifyEditor)
        {
            try { running?.protocol?.Notify("error", error.Message); }
            catch (IOException) { }
        }
    }
}
