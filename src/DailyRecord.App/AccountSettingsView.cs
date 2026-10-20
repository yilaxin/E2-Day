using System.Windows;
using System.Windows.Controls;
using DailyRecord.Core;

namespace DailyRecord.App;

public partial class MainWindow
{
    private AccountVault? _accounts;
    private LocalAccount? _account;
    private Action? _switchAccount;
    public void ConfigureAccounts(AccountVault accounts, LocalAccount account, Action switchAccount)
    {
        _accounts = accounts; _account = account; _switchAccount = switchAccount;
        Title = "E²-Day · " + account.Name;
    }
    private void BuildAccountSettings()
    {
        if (_account is null) return;
        var panel = Ui.Stack(Ui.Text("本机账号", 18, bold: true), Ui.Text("当前账号：" + _account.Name + " · 每个账号的记录独立保存。", 12, Ui.Muted));
        panel.Children.Add(Ui.Row(Ui.Button("锁定 / 切换账号", () => _switchAccount?.Invoke()), Ui.Button("重置密码", ResetAccountPassword)));
        PageBody.Children.Add(GlassCardBorder.Create(panel));
    }
    private void ResetAccountPassword()
    {
        var dialog = new Window { Title = "重置账号密码", Owner = this, Width = 440, Height = 330, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Ui.Page };
        var panel = new StackPanel { Margin = new Thickness(26) }; dialog.Content = panel;
        panel.Children.Add(Ui.Text("设置新密码", 20, Ui.Accent, true)); panel.Children.Add(Ui.Text("重置后生成新的离线恢复码。新密码用于当前电脑账号。", 12, Ui.Muted));
        var password = new PasswordBox { MaxLength = 128, Margin = new Thickness(0, 18, 0, 12) }; panel.Children.Add(password);
        var confirm = new PasswordBox { MaxLength = 128 }; panel.Children.Add(confirm);
        foreach (var entry in new[] { password, confirm }) { entry.Padding = new Thickness(12); entry.FontSize = 14; entry.Background = Ui.Surface; entry.BorderBrush = Ui.Line; }
        var error = Ui.Text("", 12, Ui.Amber); panel.Children.Add(error);
        panel.Children.Add(Ui.Button("确认重置", () =>
        {
            try
            {
                if (password.Password != confirm.Password) throw new ArgumentException("两次密码不一致。");
                var code = _accounts!.ResetPassword(_account!.Id, password.Password); dialog.Close(); AccountWindow.ShowRecovery(this, _account.Name, code);
            }
            catch (Exception ex) { error.Text = ex.Message; }
        }, accent: true)); dialog.ShowDialog();
    }
}
