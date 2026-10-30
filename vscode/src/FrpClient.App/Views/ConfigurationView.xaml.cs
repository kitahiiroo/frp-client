using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FrpClient.App.Views;

public partial class ConfigurationView : UserControl
{
    private static readonly SolidColorBrush SupportedBackground = new(Color.FromRgb(240, 253, 244));
    private static readonly SolidColorBrush SupportedBorder = new(Color.FromRgb(187, 247, 208));
    private static readonly SolidColorBrush SupportedForeground = new(Color.FromRgb(21, 128, 61));
    private static readonly SolidColorBrush WarningBackground = new(Color.FromRgb(255, 251, 235));
    private static readonly SolidColorBrush WarningBorder = new(Color.FromRgb(253, 230, 138));
    private static readonly SolidColorBrush WarningForeground = new(Color.FromRgb(180, 83, 9));
    private static readonly SolidColorBrush UnknownBackground = new(Color.FromRgb(248, 250, 252));
    private static readonly SolidColorBrush UnknownBorder = new(Color.FromRgb(221, 227, 234));
    private static readonly SolidColorBrush UnknownForeground = new(Color.FromRgb(102, 112, 133));

    private bool _stcpAvailable = true;

    public ConfigurationView()
    {
        InitializeComponent();
        ApplyTypeVisibility();
    }

    public event RoutedEventHandler? SaveConfigRequested;

    public event RoutedEventHandler? AddProxyRequested;

    public event RoutedEventHandler? DeleteSelectedProxyRequested;

    public event RoutedEventHandler? EditSelectedProxyRequested;

    public event RoutedEventHandler? CancelEditRequested;

    public event RoutedEventHandler? DetectVersionRequested;

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

    public string ProxyName
    {
        get => ProxyNameTextBox.Text;
        set => ProxyNameTextBox.Text = value;
    }

    /// <summary>类型下拉框的 Tag：tcp / udp / stcp-proxy / stcp-visitor。</summary>
    public string ProxyTypeTag => (ProxyTypeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "tcp";

    public string ProxyType => ProxyTypeTag.StartsWith("stcp", StringComparison.OrdinalIgnoreCase)
        ? FrpRuleTypes.Stcp
        : ProxyTypeTag;

    public string ProxyRole => ProxyTypeTag == "stcp-visitor"
        ? FrpRuleTypes.VisitorRole
        : FrpRuleTypes.ProxyRole;

    public string LocalIp
    {
        get => LocalIpTextBox.Text;
        set => LocalIpTextBox.Text = value;
    }

    public string LocalPort
    {
        get => LocalPortTextBox.Text;
        set => LocalPortTextBox.Text = value;
    }

    public string RemotePort
    {
        get => RemotePortTextBox.Text;
        set => RemotePortTextBox.Text = value;
    }

    /// <summary>stcp 服务端和访问端各有一个密钥输入框，读取当前类型对应的那一个。</summary>
    public string SecretKey
    {
        get => ProxyTypeTag == "stcp-visitor" ? VisitorSecretKeyTextBox.Text : SecretKeyTextBox.Text;
        set
        {
            SecretKeyTextBox.Text = value;
            VisitorSecretKeyTextBox.Text = value;
        }
    }

    public string ServerName
    {
        get => ServerNameTextBox.Text;
        set => ServerNameTextBox.Text = value;
    }

    public string BindAddr
    {
        get => BindAddrTextBox.Text;
        set => BindAddrTextBox.Text = value;
    }

    public string BindPort
    {
        get => BindPortTextBox.Text;
        set => BindPortTextBox.Text = value;
    }

    public object? SelectedProxy => ProxyDataGrid.SelectedItem;

    public void BindProxies(IEnumerable proxies)
    {
        ProxyDataGrid.ItemsSource = proxies;
    }

    /// <summary>把当前 frpc 的检测结果同步到规则卡片上的状态标签和类型下拉框。</summary>
    public void SetCapability(FrpClientCapabilities capabilities)
    {
        _stcpAvailable = capabilities.SupportsStcp;

        CapabilityStatusTextBlock.Text = capabilities.HasKnownVersion
            ? capabilities.SupportsStcp
                ? $"STCP 可用 · frpc {capabilities.VersionText}"
                : $"STCP 不可用 · frpc {capabilities.VersionText}"
            : "STCP 状态未知";

        CapabilityStatusTextBlock.ToolTip = $"{capabilities.StatusDetail}\n\n{capabilities.DetailText}";
        CapabilityChip.ToolTip = CapabilityStatusTextBlock.ToolTip;

        var (background, border, foreground) = capabilities.SupportsStcp
            ? (SupportedBackground, SupportedBorder, SupportedForeground)
            : capabilities.HasKnownVersion
                ? (WarningBackground, WarningBorder, WarningForeground)
                : (UnknownBackground, UnknownBorder, UnknownForeground);

        CapabilityChip.Background = background;
        CapabilityChip.BorderBrush = border;
        CapabilityStatusTextBlock.Foreground = foreground;

        foreach (var item in ProxyTypeComboBox.Items.OfType<ComboBoxItem>())
        {
            var tag = item.Tag?.ToString() ?? "";

            if (tag.StartsWith("stcp", StringComparison.OrdinalIgnoreCase))
            {
                item.IsEnabled = capabilities.SupportsStcp;
            }
        }

        if (!_stcpAvailable && ProxyTypeTag.StartsWith("stcp", StringComparison.OrdinalIgnoreCase))
        {
            SetProxyType("tcp", FrpRuleTypes.ProxyRole);
        }
    }

    public void ResetNewProxyForm()
    {
        ProxyNameTextBox.Clear();
        LocalIpTextBox.Text = "127.0.0.1";
        LocalPortTextBox.Clear();
        RemotePortTextBox.Clear();
        SecretKeyTextBox.Clear();
        VisitorSecretKeyTextBox.Clear();
        ServerNameTextBox.Clear();
        BindAddrTextBox.Text = "127.0.0.1";
        BindPortTextBox.Clear();
        SetProxyType("tcp", FrpRuleTypes.ProxyRole);
    }

    public void ResetProxyDefaults()
    {
        LocalIpTextBox.Text = "127.0.0.1";
        BindAddrTextBox.Text = "127.0.0.1";

        if (ProxyTypeTag.StartsWith("stcp", StringComparison.OrdinalIgnoreCase) && !_stcpAvailable)
        {
            SetProxyType("tcp", FrpRuleTypes.ProxyRole);
        }
    }

    /// <summary>编辑已有规则时，把规则内容填回表单。</summary>
    public void FillProxyForm(FrpProxyConfig rule)
    {
        ProxyNameTextBox.Text = rule.Name;
        LocalIpTextBox.Text = string.IsNullOrWhiteSpace(rule.LocalIP) ? "127.0.0.1" : rule.LocalIP;
        LocalPortTextBox.Text = rule.LocalPort;
        RemotePortTextBox.Text = rule.RemotePort;
        SecretKeyTextBox.Text = rule.SecretKey;
        VisitorSecretKeyTextBox.Text = rule.SecretKey;
        ServerNameTextBox.Text = rule.ServerName;
        BindAddrTextBox.Text = string.IsNullOrWhiteSpace(rule.BindAddr) ? "127.0.0.1" : rule.BindAddr;
        BindPortTextBox.Text = rule.BindPort;

        SetProxyType(
            rule.IsStcp ? (rule.IsStcpVisitor ? "stcp-visitor" : "stcp-proxy") : rule.Type,
            rule.IsStcpVisitor ? FrpRuleTypes.VisitorRole : FrpRuleTypes.ProxyRole);
    }

    public void SetProxyType(string type, string role)
    {
        var tag = string.Equals(type, FrpRuleTypes.Stcp, StringComparison.OrdinalIgnoreCase)
            ? string.Equals(role, FrpRuleTypes.VisitorRole, StringComparison.OrdinalIgnoreCase) ? "stcp-visitor" : "stcp-proxy"
            : type.ToLowerInvariant();

        var matchedItem = ProxyTypeComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase));

        ProxyTypeComboBox.SelectedItem = matchedItem ?? ProxyTypeComboBox.Items[0];
        ApplyTypeVisibility();
    }

    public void SetEditMode(bool isEditing, string? ruleName)
    {
        RuleFormTitleTextBlock.Text = isEditing
            ? $"编辑转发规则：{ruleName}"
            : "新增转发规则";
        AddProxyButton.Content = isEditing ? "保存修改" : "添加转发";
        CancelEditButton.Visibility = isEditing ? Visibility.Visible : Visibility.Collapsed;

        var accentBrush = (Brush)FindResource(isEditing ? "PrimaryBrush" : "SuccessBrush");
        AddProxyButton.Background = accentBrush;
        AddProxyButton.BorderBrush = accentBrush;
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

    private void EditSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        EditSelectedProxyRequested?.Invoke(sender, e);
    }

    private void CancelEditButton_Click(object sender, RoutedEventArgs e)
    {
        CancelEditRequested?.Invoke(sender, e);
    }

    private void DetectVersionButton_Click(object sender, RoutedEventArgs e)
    {
        DetectVersionRequested?.Invoke(sender, e);
    }

    private void ProxyTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // XAML 里设置 SelectedIndex 时，事件可能早于其它控件创建完成，这里先确认控件都已就绪。
        if (ProxyFieldsPanel is null || StcpVisitorFieldsPanel is null || StcpSecretPanel is null ||
            RemotePortPanel is null || VisitorSecretKeyTextBox is null || FormHintTextBlock is null)
        {
            return;
        }

        ApplyTypeVisibility();
    }

    private void ApplyTypeVisibility()
    {
        var tag = ProxyTypeTag;
        var isVisitor = tag == "stcp-visitor";
        var isStcpProxy = tag == "stcp-proxy";
        var usesRemotePort = tag is "tcp" or "udp";

        ProxyFieldsPanel.Visibility = isVisitor ? Visibility.Collapsed : Visibility.Visible;
        StcpVisitorFieldsPanel.Visibility = isVisitor ? Visibility.Visible : Visibility.Collapsed;
        StcpSecretPanel.Visibility = isStcpProxy ? Visibility.Visible : Visibility.Collapsed;
        RemotePortPanel.Visibility = usesRemotePort ? Visibility.Visible : Visibility.Collapsed;

        FormHintTextBlock.Visibility = isVisitor || isStcpProxy ? Visibility.Visible : Visibility.Collapsed;
        FormHintTextBlock.Text = isVisitor
            ? "服务器名称要填服务端那条 stcp 规则的“名称”，不是服务器 IP。"
            : "这条规则的“名称”会作为访问端的“服务器名称”。";

        // 两个面板各有一个密钥输入框，切换类型时把已输入的内容带过去。
        if (isVisitor && VisitorSecretKeyTextBox.Text.Length == 0)
        {
            VisitorSecretKeyTextBox.Text = SecretKeyTextBox.Text;
        }
        else if (!isVisitor && SecretKeyTextBox.Text.Length == 0)
        {
            SecretKeyTextBox.Text = VisitorSecretKeyTextBox.Text;
        }
    }
}
