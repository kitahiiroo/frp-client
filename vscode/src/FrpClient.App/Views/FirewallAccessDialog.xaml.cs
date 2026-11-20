using System.Windows;

namespace FrpClient.App.Views;

/// <summary>让用户明确选择允许的网络类型，确认后才会请求系统管理员权限。</summary>
internal partial class FirewallAccessDialog : Window
{
    private readonly FirewallNetworkProfiles _activeProfiles;

    /// <summary>显示程序路径和当前网络类型，默认仅选择专用网络。</summary>
    public FirewallAccessDialog(string programPath, FirewallNetworkProfiles activeProfiles)
    {
        _activeProfiles = activeProfiles;
        InitializeComponent();
        CurrentNetworkTextBlock.Text = activeProfiles == FirewallNetworkProfiles.None
            ? "暂时无法读取当前网络类型，请按实际连接选择。"
            : $"当前网络：{DescribeProfiles(activeProfiles)}";
        ProgramPathTextBlock.Text = $"授权程序：{programPath}";
        UpdateSelectionHint();
    }

    /// <summary>返回本次明确勾选的网络，不会自动追加公用网络。</summary>
    public FirewallNetworkProfiles SelectedProfiles
    {
        get
        {
            var profiles = FirewallNetworkProfiles.None;
            if (PrivateNetworkCheckBox.IsChecked == true) profiles |= FirewallNetworkProfiles.Private;
            if (PublicNetworkCheckBox.IsChecked == true) profiles |= FirewallNetworkProfiles.Public;
            if (DomainNetworkCheckBox.IsChecked == true) profiles |= FirewallNetworkProfiles.Domain;
            return profiles;
        }
    }

    /// <summary>勾选状态变化时检查是否至少选择一个网络。</summary>
    private void NetworkSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (ApplyButton is not null)
        {
            UpdateSelectionHint();
        }
    }

    /// <summary>提示当前网络是否在放行范围内，避免只配置专用网络却期待公用网络生效。</summary>
    private void UpdateSelectionHint()
    {
        var selected = SelectedProfiles;
        ApplyButton.IsEnabled = selected != FirewallNetworkProfiles.None;
        SelectionHintTextBlock.Text = selected == FirewallNetworkProfiles.None
            ? "请至少选择一种网络。"
            : _activeProfiles != FirewallNetworkProfiles.None && (selected & _activeProfiles) == FirewallNetworkProfiles.None
                ? "当前连接的网络未被勾选，规则只对所选网络生效。"
                : $"将允许：{DescribeProfiles(selected)}。";
    }

    /// <summary>确认网络选择；管理员授权由调用方在窗口关闭后单独请求。</summary>
    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfiles != FirewallNetworkProfiles.None)
        {
            DialogResult = true;
        }
    }

    /// <summary>将系统网络标志转换为清晰的中文说明。</summary>
    private static string DescribeProfiles(FirewallNetworkProfiles profiles)
    {
        var names = new List<string>();
        if ((profiles & FirewallNetworkProfiles.Private) != 0) names.Add("专用网络");
        if ((profiles & FirewallNetworkProfiles.Public) != 0) names.Add("公用网络");
        if ((profiles & FirewallNetworkProfiles.Domain) != 0) names.Add("域网络");
        return string.Join("、", names);
    }
}
