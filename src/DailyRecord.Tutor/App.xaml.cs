using System.IO;
using System.Windows;
using DailyRecord.Core;

namespace DailyRecord.App;

public partial class App : Application
{
    private TutorStore? _store;
    private FileStream? _lock;
    private string _root = "";
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        foreach (var resource in Resources.Values)
            if (resource is System.Windows.Freezable { IsFrozen: false, CanFreeze: true } frozen) frozen.Freeze();
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("zh-CN");
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        string? Arg(string name) { var i = Array.IndexOf(e.Args, name); return i >= 0 && i + 1 < e.Args.Length ? e.Args[i + 1] : null; }
        var smoke = Arg("--smoke-test");
        _root = Arg("--data-dir") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "E2DayTutor");
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true; Log(args.Exception);
            if (smoke is not null) Shutdown(1);
            else MessageBox.Show(args.Exception.Message, "操作未完成 · E²-Day 家教版");
        };
        StartupWindow? opening = null; TutorWindow? window = null;
        try
        {
            Directory.CreateDirectory(_root);
            try { _lock = new FileStream(Path.Combine(_root, "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { if (smoke is null) MessageBox.Show("家教版已经打开，请使用已有窗口。", "E²-Day 家教版"); Shutdown(2); return; }
            AccountVault? vault = null; AccountSession? session = null;
            if (smoke is null)
            {
                window = new TutorWindow(); MainWindow = window;
                opening = new StartupWindow(window); var animation = opening.PlayAsync();
                var forceLogin = e.Args.Contains("--login");
                var prepared = Task.Run(() => { var v = new AccountVault(_root); return (Vault: v, Session: forceLogin ? null : RememberedLogin.Load(v)); });
                var result = await animation; var accountState = await prepared;
                if (result == StartupResult.Closed || !window.IsVisible) { Shutdown(); return; }
                vault = accountState.Vault; session = accountState.Session;
                if (session is null) { var login = new AccountWindow(vault); if (!await login.ShowInHostAsync(window)) { Shutdown(); return; } session = login.Session!; }
            }
            _store = new TutorStore(smoke is null ? vault!.DataDirectory(session!.AccountId) : _root);
            var service = new TutorService(_store);
            if (smoke is not null) window = new TutorWindow(service, _store, new DateOnly(2026, 10, 15));
            else { opening!.RestoreContent(); window!.InitializeData(service, _store); }
            MainWindow = window;
            if (smoke is not null) { await TutorSmoke.Run(window, service, _store, smoke); Shutdown(0); return; }
            var account = vault!.Accounts.Single(a => a.Id == session!.AccountId);
            window.AccountName = account.Name;
            var switching = false;
            window.SwitchAccount = () =>
            {
                RememberedLogin.Clear(_root); vault.Revoke(session!.AccountId, session.DeviceId); switching = true;
                window.Close(); _store.Dispose(); _store = null; _lock.Dispose(); _lock = null;
                var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
                start.ArgumentList.Add("--data-dir"); start.ArgumentList.Add(_root); start.ArgumentList.Add("--login"); System.Diagnostics.Process.Start(start); Shutdown();
            };
            window.Closed += (_, _) => { if (!switching) Shutdown(); }; window.Show();
            try { _store.DailyBackup(service.State, window.Today); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { window.BackupWarning = "自动备份未完成，请在设置中导出备份。"; }
        }
        catch (Exception ex) { Log(ex); if (smoke is null) MessageBox.Show(ex.Message + "\n数据目录：" + _root, "无法打开家教版"); Shutdown(1); }
        finally { opening?.Dispose(); }
    }
    private void Log(Exception ex) { try { Directory.CreateDirectory(_root); File.AppendAllText(Path.Combine(_root, "errors.log"), DateTimeOffset.Now + " " + ex + "\n"); } catch (IOException) { } }
    protected override void OnExit(ExitEventArgs e) { _store?.Dispose(); _lock?.Dispose(); base.OnExit(e); }
}
