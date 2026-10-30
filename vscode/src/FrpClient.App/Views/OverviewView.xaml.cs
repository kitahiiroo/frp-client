using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FrpClient.App.Views;

public partial class OverviewView : UserControl
{
    public OverviewView()
    {
        InitializeComponent();
    }

    public event EventHandler<OverviewProxyCopyRequestedEventArgs>? CopyProxyServerEndpointRequested;

    public void BindProxies(IEnumerable proxies)
    {
        OverviewProxyItemsControl.ItemsSource = proxies;
    }

    public void SetRunningState(bool isRunning)
    {
        OverviewStatusTextBlock.Text = isRunning ? "运行中" : "未运行";
        OverviewStatusTextBlock.Foreground = isRunning
            ? new SolidColorBrush(Color.FromRgb(26, 127, 55))
            : new SolidColorBrush(Color.FromRgb(207, 34, 46));
    }

    public void UpdateProxyCount(int count)
    {
        OverviewProxyCountTextBlock.Text = $"{count} 条";
        OverviewEmptyTextBlock.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void RefreshProxyCards()
    {
        OverviewProxyItemsControl.Items.Refresh();
    }

    private void CopyProxyServerEndpointButton_Click(object sender, RoutedEventArgs e) // 点击卡片上的复制地址按钮
    {
        if (sender is Button { Tag: global::FrpClient.App.FrpProxyConfig proxy })
        {
            CopyProxyServerEndpointRequested?.Invoke(this, new OverviewProxyCopyRequestedEventArgs(proxy));
        }
    }
}

public class OverviewProxyCopyRequestedEventArgs : EventArgs // 携带用户点击的端口转发规则
{
    public OverviewProxyCopyRequestedEventArgs(global::FrpClient.App.FrpProxyConfig proxy)
    {
        Proxy = proxy;
    }

    public global::FrpClient.App.FrpProxyConfig Proxy { get; }
}
