using System.IO;
using System.Windows;
using FrpClient.App.Views;

namespace FrpClient.App;

/// <summary>下载页的防火墙授权入口及操作结果提示。</summary>
public partial class MainWindow
{
    private bool _isConfiguringFirewall;

    /// <summary>用户主动请求后，选择网络并运行一次带 UAC 授权的规则配置。</summary>
    private async void DownloadView_FirewallRequested(object? sender, EventArgs e)
    {
        if (_isExiting || _isDownloadingFrpc || _isConfiguringFirewall)
        {
            return;
        }

        if (!File.Exists(_frpcExePath))
        {
            MessageBox.Show(this, "请先在下载页安装 frpc，再设置其防火墙访问权限。", "找不到 frpc",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new FirewallAccessDialog(_frpcExePath, WindowsFirewall.GetActiveProfiles()) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _isConfiguringFirewall = true;
        DownloadView.SetFirewallBusy(true);
        DownloadView.SetFirewallStatus("正在等待管理员授权并设置 frpc 入站规则…");
        try
        {
            var result = await WindowsFirewall.ConfigureFrpcAsync(_frpcExePath, dialog.SelectedProfiles);
            if (_isExiting)
            {
                return;
            }

            var message = result switch
            {
                FirewallSetupResult.Success => "已为 frpc 设置所选网络的 TCP/UDP 入站允许规则。更新 frpc 后会沿用这些规则。",
                FirewallSetupResult.Canceled => "已取消管理员授权，未执行防火墙设置。",
                FirewallSetupResult.RestrictedByPolicy => "当前网络受组织策略或“阻止所有入站连接”设置限制，程序无法添加有效例外。请检查 Windows 安全中心的防火墙设置，或联系管理员。",
                _ => "设置防火墙失败。请确认 Windows 防火墙服务正常运行，并在 Windows 安全中心检查 frpc 的允许规则；组织管理的电脑可能需要联系管理员。"
            };
            DownloadView.SetFirewallStatus(message);
            AppendLog(message);
            if (result is FirewallSetupResult.RestrictedByPolicy or FirewallSetupResult.Failed)
            {
                MessageBox.Show(this, message, "防火墙设置未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            if (!_isExiting)
            {
                var message = $"无法设置防火墙：{ex.Message}";
                DownloadView.SetFirewallStatus(message);
                AppendLog(message);
                MessageBox.Show(this, message, "防火墙设置失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            _isConfiguringFirewall = false;
            if (!_isExiting)
            {
                DownloadView.SetFirewallBusy(false);
            }
        }
    }
}
