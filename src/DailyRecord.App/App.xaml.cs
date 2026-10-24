using System.Globalization;
using System.IO;
using System.Windows;
using DailyRecord.Core;

namespace DailyRecord.App;

public partial class App : Application
{
    private SqliteStore? _store;
    private FileStream? _instanceLock;
    private string _dataDirectory = "";
    private bool _smoke;
    private AccountVault? _vault;
    private AccountSession? _session;
    private bool _switching;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Theme brushes are immutable; freezing avoids per-owner change tracking across retired pages.
        foreach (var resource in Resources.Values)
            if (resource is System.Windows.Freezable { IsFrozen: false, CanFreeze: true } frozen) frozen.Freeze();
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("zh-CN");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
        string? Argument(string name)
        {
            var index = Array.IndexOf(e.Args, name);
            return index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : null;
        }
        var smokeOutput = Argument("--smoke-test");
        var memoryOutput = Argument("--memory-test");
        if (memoryOutput is not null && Argument("--data-dir") is null) { Shutdown(1); return; }
        if (Argument("--generate-mobile-assets") is { } assets)
        { try { MobileAssets.Generate(assets); Shutdown(0); } catch { Shutdown(1); } return; }
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _smoke = smokeOutput is not null || memoryOutput is not null;
        if (memoryOutput is not null && e.Args.Contains("--software-rendering"))
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        _dataDirectory = Argument("--data-dir") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DailyRecord");
        if (memoryOutput is not null) _dataDirectory = Path.Combine(_dataDirectory, "memory-fixture-" + Guid.NewGuid().ToString("N"));
        DispatcherUnhandledException += (_, args) =>
        {
            LogError(args.Exception);
            if (_smoke) { args.Handled = true; Shutdown(1); return; }
            MessageBox.Show("操作未完成，已保存的记录会保留。\n\n" + args.Exception.Message, "E²-Day", MessageBoxButton.OK, MessageBoxImage.Information);
            args.Handled = true;
        };
        StartupWindow? opening = null;
        MainWindow? window = null;
        try
        {
            Directory.CreateDirectory(_dataDirectory);
            try { _instanceLock = new FileStream(Path.Combine(_dataDirectory, "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException)
            {
                if (!_smoke) MessageBox.Show("E²-Day 已经打开，请使用已有窗口。", "E²-Day");
                Shutdown(2); return;
            }
            if (!_smoke)
            {
                window = new MainWindow();
                MainWindow = window;
                opening = new StartupWindow(window);
                var animation = opening.PlayAsync();
                var forceLogin = e.Args.Contains("--login");
                var prepared = Task.Run(() =>
                {
                    var vault = new AccountVault(_dataDirectory);
                    return (Vault: vault, Session: forceLogin ? null : RememberedLogin.Load(vault));
                });
                var result = await animation;
                var accountState = await prepared;
                if (result == StartupResult.Closed || !window.IsVisible) { Shutdown(); return; }
                _vault = accountState.Vault; _session = accountState.Session;
                if (_session is null)
                {
                    var login = new AccountWindow(_vault);
                    if (!await login.ShowInHostAsync(window)) { Shutdown(); return; }
                    _session = login.Session!;
                }
            }
            _store = new SqliteStore(_smoke ? _dataDirectory : _vault!.DataDirectory(_session!.AccountId));
            IClock clock = _smoke ? new PreviewClock() : new SystemClock();
            var service = new TaskService(_store, clock);
            var holidays = new HolidayService(Path.Combine(AppContext.BaseDirectory, "Data", "Holidays"), Path.Combine(_dataDirectory, "Holidays"));
            if (_smoke) window = new MainWindow(service, _store, holidays, true);
            else
            {
                opening!.RestoreContent();
                window!.InitializeData(service, _store, holidays);
            }
            MainWindow = window;
            if (memoryOutput is not null)
            {
                await SmokeTest.MeasureMemory(window, service, (PreviewClock)clock, memoryOutput);
                Shutdown(0);
            }
            else if (_smoke)
            {
                await SmokeTest.Run(window, service, _store, holidays, (PreviewClock)clock, smokeOutput!);
                Shutdown(0);
            }
            else
            {
                var account = _vault!.Accounts.Single(a => a.Id == _session!.AccountId);
                window.ConfigureAccounts(_vault, account, SwitchAccount);
                window.Closed += (_, _) => { if (!_switching) Shutdown(); };
                window.Show();
            }
        }
        catch (Exception ex)
        {
            LogError(ex);
            if (!_smoke) MessageBox.Show("无法打开 E²-Day。\n\n" + ex.Message + "\n\n数据目录：" + _dataDirectory, "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
        finally { opening?.Dispose(); }
    }
    private void SwitchAccount()
    {
        if (_switching) return; _switching = true;
        RememberedLogin.Clear(_dataDirectory);
        if (_session is not null) _vault!.Revoke(_session.AccountId, _session.DeviceId);
        MainWindow?.Close(); _store?.Dispose(); _store = null; _instanceLock?.Dispose(); _instanceLock = null;
        var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        start.ArgumentList.Add("--data-dir"); start.ArgumentList.Add(_dataDirectory); start.ArgumentList.Add("--login");
        System.Diagnostics.Process.Start(start); Shutdown();
    }
    private void LogError(Exception exception)
    {
        try { Directory.CreateDirectory(_dataDirectory); File.AppendAllText(Path.Combine(_dataDirectory, "errors.log"), $"{DateTimeOffset.Now:O}\n{exception}\n\n"); }
        catch { }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _store?.Dispose(); _instanceLock?.Dispose(); base.OnExit(e);
    }
}

internal sealed class PreviewClock : IClock
{
    public DateOnly Today { get; set; } = new(2026, 10, 7);
    public DateTimeOffset Now => new(Today.ToDateTime(new TimeOnly(9, 30)), TimeSpan.FromHours(8));
}
