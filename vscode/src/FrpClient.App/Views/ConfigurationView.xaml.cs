using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FrpClient.App.Views;

public partial class ConfigurationView : UserControl
{
    public ConfigurationView()
    {
        InitializeComponent();
    }

    public event RoutedEventHandler? SaveConfigRequested;

    public event RoutedEventHandler? AddProxyRequested;

    public event RoutedEventHandler? DeleteSelectedProxyRequested;

    public string ServerAddr
    {
        get => ServerAddrTextBox.Text;
        set => ServerAddrTextBox.Text = value;
    }

    public string ServerPort
    {
        get => ServerPortTextBox.Text;
        set => ServerPortTextBox.Text = value;
    }

    public string AuthToken
    {
        get => AuthTokenTextBox.Text;
        set => AuthTokenTextBox.Text = value;
    }

    public string ProxyName => ProxyNameTextBox.Text;

    public string ProxyType => (ProxyTypeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "tcp";

    public string LocalIp
    {
        get => LocalIpTextBox.Text;
        set => LocalIpTextBox.Text = value;
    }

    public string LocalPort => LocalPortTextBox.Text;

    public string RemotePort => RemotePortTextBox.Text;

    public object? SelectedProxy => ProxyDataGrid.SelectedItem;

    public void BindProxies(IEnumerable proxies)
    {
        ProxyDataGrid.ItemsSource = proxies;
    }

    public void CommitProxyGridEdits()
    {
        ProxyDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        ProxyDataGrid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    public void ResetNewProxyForm()
    {
        ProxyNameTextBox.Clear();
        LocalPortTextBox.Clear();
        RemotePortTextBox.Clear();
        LocalIpTextBox.Text = "127.0.0.1";
        ProxyTypeComboBox.SelectedIndex = 0;
    }

    public void ResetProxyDefaults()
    {
        LocalIpTextBox.Text = "127.0.0.1";
        ProxyTypeComboBox.SelectedIndex = 0;
    }

    private void SaveConfigButton_Click(object sender, RoutedEventArgs e)
    {
        SaveConfigRequested?.Invoke(sender, e);
    }

    private void AddProxyButton_Click(object sender, RoutedEventArgs e)
    {
        AddProxyRequested?.Invoke(sender, e);
    }

    private void DeleteSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        DeleteSelectedProxyRequested?.Invoke(sender, e);
    }

    private void ProxyDataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DataGrid dataGrid)
        {
            return;
        }

        var scrollViewer = FindVisualChild<ScrollViewer>(dataGrid);

        if (scrollViewer is null || scrollViewer.ScrollableHeight <= 0)
        {
            return;
        }

        var nextOffset = Math.Clamp(
            scrollViewer.VerticalOffset - e.Delta / 3.0,
            0,
            scrollViewer.ScrollableHeight);

        scrollViewer.ScrollToVerticalOffset(nextOffset);
        e.Handled = true;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);

            if (child is T typedChild)
            {
                return typedChild;
            }

            var nestedChild = FindVisualChild<T>(child);

            if (nestedChild is not null)
            {
                return nestedChild;
            }
        }

        return null;
    }
}
