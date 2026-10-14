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
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("zh-CN");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
        string? Argument(string name)
        {
            var index = Array.IndexOf(e.Args, name);
            return index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : null;
        }
        var smokeOutput = Argument("--smoke-test");
        if (Argument("--generate-mobile-assets") is { } assets)
        { try { MobileAssets.Generate(assets); Shutdown(0); } catch { Shutdown(1); } return; }
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _smoke = smokeOutput is not null;
        _dataDirectory = Argument("--data-dir") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DailyRecord");
        DispatcherUnhandledException += (_, args) =>
        {
            LogError(args.Exception);
            if (_smoke) { args.Handled = true; Shutdown(1); return; }
            MessageBox.Show("操作未完成，已保存的记录会保留。\n\n" + args.Exception.Message, "E²-Day", MessageBoxButton.OK, MessageBoxImage.Information);
            args.Handled = true;
        };
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
                _vault = new AccountVault(_dataDirectory);
                _session = e.Args.Contains("--login") ? null : RememberedLogin.Load(_vault);
                if (_session is null)
                { var login = new AccountWindow(_vault); if (login.ShowDialog() != true) { Shutdown(); return; } _session = login.Session!; }
            }
            _store = new SqliteStore(_smoke ? _dataDirectory : _vault!.DataDirectory(_session!.AccountId));
            IClock clock = _smoke ? new PreviewClock() : new SystemClock();
            var service = new TaskService(_store, clock);
            var holidays = new HolidayService(Path.Combine(AppContext.BaseDirectory, "Data", "Holidays"), Path.Combine(_dataDirectory, "Holidays"));
            var window = new MainWindow(service, _store, holidays, _smoke);
            MainWindow = window;
            if (_smoke)
            {
                await SmokeTest.Run(window, service, holidays, (PreviewClock)clock, smokeOutput!);
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
