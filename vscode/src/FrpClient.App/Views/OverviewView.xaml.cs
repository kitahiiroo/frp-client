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
}
