using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using DailyRecord.Core;
using Microsoft.Win32;

namespace DailyRecord.App;

internal sealed class AccountWindow : Window
{
    public AccountSession? Session { get; private set; }
    private Window? _host;
    private event Action? Authenticated;
    public AccountWindow(AccountVault vault, bool smoke = false)
    {
        Title = "E²-Day · 账号登录"; Width = 500; Height = 690; MinWidth = 460; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = Ui.Page;
        var panel = new StackPanel { Margin = new Thickness(34) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(Ui.Text("E²-Day", 30, Ui.Accent, true));
        panel.Children.Add(Ui.Text("个人记录，只保存在你的设备上。", 13, Ui.Muted));
        var mode = new ComboBox { ItemsSource = new[] { "登录已有账号", "创建独立账号", "使用离线恢复码" }, SelectedIndex = vault.Accounts.Count == 0 ? 1 : 0, Margin = new Thickness(0, 26, 0, 12) };
        mode.Tag = "account-mode";
        panel.Children.Add(mode);
        var name = new TextBox { MaxLength = 40, Text = vault.Accounts.FirstOrDefault()?.Name ?? "" };
        var password = new PasswordBox { MaxLength = 128 };
        var confirm = new PasswordBox { MaxLength = 128 };
        var recovery = new TextBox { MaxLength = 80 };
        foreach (var entry in new[] { password, confirm }) { entry.Padding = new Thickness(14, 12, 14, 12); entry.FontSize = 14; entry.BorderBrush = Ui.Line; entry.Background = Ui.Surface; }
        mode.Padding = new Thickness(10, 8, 10, 8); mode.FontSize = 13;
        name.Tag = "account-name"; password.Tag = "account-password"; confirm.Tag = "account-confirm"; recovery.Tag = "account-recovery";
        panel.Children.Add(Ui.Label("账号名")); panel.Children.Add(name);
        var passwordLabel = Ui.Label("密码 · 8 至 128 个字符"); panel.Children.Add(passwordLabel); panel.Children.Add(password);
        var confirmLabel = Ui.Label("再次输入密码"); panel.Children.Add(confirmLabel); panel.Children.Add(confirm);
        var recoveryLabel = Ui.Label("离线恢复码"); panel.Children.Add(recoveryLabel); panel.Children.Add(recovery);
        var remember = new CheckBox { Content = "记住这台电脑", IsChecked = true, Margin = new Thickness(0, 20, 0, 8) }; panel.Children.Add(remember);
        var feedback = Ui.Text("", 12, Ui.Amber); feedback.Margin = new Thickness(0, 12, 0, 12); panel.Children.Add(feedback);
        var next = Ui.Button("继续", () =>
        {
            try
            {
                LocalAccount account;
                if (mode.SelectedIndex == 1)
                {
                    if (password.Password != confirm.Password) throw new ArgumentException("两次输入的密码不一致。");
                    var first = vault.Accounts.Count == 0;
                    var created = vault.Create(name.Text, password.Password, candidate =>
                    {
                        if (!first || !File.Exists(Path.Combine(vault.Root, "daily-record.db"))) return;
                        using var legacy = new SqliteStore(vault.Root); var state = legacy.Load();
                        var backup = Path.Combine(legacy.BackupDirectory, $"账号迁移前-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                        legacy.ExportBackup(backup, state);
                        using var destination = new SqliteStore(vault.DataDirectory(candidate.Id));
                        destination.Save(state, legacy.GetNoteImages(SyncMerge.ImageIds(state)));
                        destination.ExportBackup(Path.Combine(destination.BackupDirectory, "首次迁移.json"), state);
                    });
                    account = created.Account; if (!smoke) ShowRecovery(_host ?? this, account.Name, created.RecoveryCode);
                }
                else if (mode.SelectedIndex == 2)
                {
                    if (password.Password != confirm.Password) throw new ArgumentException("两次输入的新密码不一致。");
                    account = vault.Accounts.FirstOrDefault(a => a.Name.Equals(name.Text.Trim(), StringComparison.OrdinalIgnoreCase)) ?? throw new UnauthorizedAccessException("账号或恢复码不正确。");
                    var code = vault.ResetPassword(account.Id, password.Password, recovery.Text); if (!smoke) ShowRecovery(_host ?? this, account.Name, code);
                }
                else account = vault.Login(name.Text, password.Password);
                Session = vault.Bind(account.Id, "这台电脑");
                if (remember.IsChecked == true) RememberedLogin.Save(vault.Root, Session); else RememberedLogin.Clear(vault.Root);
                if (!smoke) { if (_host is null) DialogResult = true; else Authenticated?.Invoke(); }
            }
            catch (Exception ex) { feedback.Text = ex is IOException ? "备份或账号保存未完成，原记录仍保留，请检查存储空间。" : ex.Message; }
        }, accent: true); next.Tag = "account-next"; next.Margin = new Thickness(0, 6, 0, 12); panel.Children.Add(next);
        panel.Children.Add(Ui.Text("首个账号会先备份并接收旧版记录。其他账号从空白记录开始。电脑和手机各自创建本机账号，通过交换文件转移记录。", 12, Ui.Muted));
        void Update()
        {
            confirm.Visibility = confirmLabel.Visibility = mode.SelectedIndex == 0 ? Visibility.Collapsed : Visibility.Visible;
            recovery.Visibility = recoveryLabel.Visibility = mode.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            passwordLabel.Text = mode.SelectedIndex == 2 ? "新密码 · 8 至 128 个字符" : "密码 · 8 至 128 个字符";
            feedback.Text = "";
        }
        mode.SelectionChanged += (_, _) => Update(); Update();
    }
    internal async Task<bool> ShowInHostAsync(Window host)
    {
        _host = host;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Success() => completion.TrySetResult(true);
        void Closed(object? sender, EventArgs e) => completion.TrySetResult(false);
        Authenticated += Success; host.Closed += Closed;
        var form = (UIElement)Content; Content = null;
        host.Title = Title;
        host.Content = new Grid
        {
            Background = Ui.Page,
            Children = { new Border
            {
                Width = 500, MaxHeight = 690, Margin = new Thickness(24), HorizontalAlignment = HorizontalAlignment.Center,
                CornerRadius = new CornerRadius(20), Background = Ui.Surface, Child = form
            } }
        };
        try { return await completion.Task; }
        finally { Authenticated -= Success; host.Closed -= Closed; _host = null; Close(); }
    }

    internal static void ShowRecovery(Window owner, string name, string code)
    {
        var dialog = new Window { Title = "保存离线恢复码", Owner = owner, Width = 520, Height = 370, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Ui.Page };
        var panel = new StackPanel { Margin = new Thickness(28) }; dialog.Content = panel;
        panel.Children.Add(Ui.Text("保存这份恢复码", 22, Ui.Accent, true));
        panel.Children.Add(Ui.Text("忘记密码时可在电脑上恢复账号访问。记录仍需从已有设备或本地备份恢复。新恢复码生成后，旧恢复码失效。", 13, Ui.Muted));
        var text = new TextBox { Text = code, IsReadOnly = true, Margin = new Thickness(0, 20, 0, 20), TextWrapping = TextWrapping.Wrap }; panel.Children.Add(text);
        panel.Children.Add(Ui.Button("保存到自己的文件", () =>
        {
            var file = new SaveFileDialog { Filter = "文本文件 (*.txt)|*.txt", FileName = "E2-Day-恢复码.txt" };
            if (file.ShowDialog(dialog) == true) File.WriteAllText(file.FileName, $"E²-Day 账号：{name}\n恢复码：{code}\n请保存在自己的安全位置。\n");
        }));
        panel.Children.Add(Ui.Button("我已保存，继续", () => dialog.DialogResult = true, accent: true)); dialog.ShowDialog();
    }
}
internal static class RememberedLogin
{
    private static string PathFor(string root) => Path.Combine(root, "remembered.protected");
    public static AccountSession? Load(AccountVault vault)
    {
        try
        {
            if (!File.Exists(PathFor(vault.Root))) return null;
            var session = JsonSerializer.Deserialize<AccountSession>(WindowsSecret.Unprotect(File.ReadAllBytes(PathFor(vault.Root))), StateJson.Options);
            if (session is null) return null; vault.Authenticate(session.Token, session.AccountId); return session;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.ComponentModel.Win32Exception or JsonException or IOException) { return null; }
    }
    public static void Save(string root, AccountSession session) => File.WriteAllBytes(PathFor(root), WindowsSecret.Protect(JsonSerializer.SerializeToUtf8Bytes(session, StateJson.Options)));
    public static void Clear(string root) { if (File.Exists(PathFor(root))) File.Delete(PathFor(root)); }
}
