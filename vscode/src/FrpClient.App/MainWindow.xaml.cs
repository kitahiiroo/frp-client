using System.Collections.ObjectModel; // 提供 ObservableCollection，用来让表格显示端口转发列表
using System.Diagnostics; // 提供 Process，用来启动、停止外部 exe 程序
using System.IO; // 提供 File、Path、DirectoryInfo，用来处理文件和路径
using System.IO.Compression; // 提供 ZipFile，用来解压 GitHub 下载的 frp 压缩包
using System.Net.Http; // 提供 HttpClient，用来从 GitHub 下载 frp
using System.Text; // 提供 StringBuilder 和 UTF8 编码，用来生成配置文件内容
using System.Text.Json; // 提供 JsonDocument，用来解析 GitHub Releases API 返回的 JSON
using System.Text.RegularExpressions; // 提供 Regex，用来校验用户输入的版本号
using System.Windows; // 提供 WPF 的 Window、RoutedEventArgs 等基础类型
using System.Windows.Controls; // 提供 ComboBox、TextBox、StackPanel 等 WPF 控件类型
using System.Windows.Input; // 提供 Key，用来处理弹窗里的 Enter/Esc
using System.Windows.Media; // 提供颜色、画刷等界面显示相关类型

namespace FrpClient.App;

/// <summary>
/// MainWindow.xaml 对应的后台代码。
/// XAML 负责界面长什么样，这个 .cs 文件负责按钮点击后做什么。
/// </summary>
public partial class MainWindow : Window // partial 表示这个类还有一部分由 WPF 根据 MainWindow.xaml 自动生成
{
    private const int ClipboardCopyRetryCount = 20; // 剪贴板可能被其他程序短暂占用，复制时允许重试几次
    private const int ClipboardCopyRetryDelayMilliseconds = 100; // 每次重试之间等待的毫秒数
    private static readonly HttpClient FrpDownloadHttpClient = CreateFrpDownloadHttpClient(); // 复用同一个 HttpClient，避免频繁创建网络连接

    private Process? _frpcProcess; // 保存当前启动的 frpc 进程；? 表示它可以是 null
    private readonly string _frpcExePath; // 用户数据目录里 frpc.exe 的完整路径；readonly 表示构造函数赋值后不再改
    private readonly FrpProfileStore _profileStore; // 管理多个配置方案和 profiles.json
    private readonly ObservableCollection<FrpProxyConfig> _proxies = []; // 表格显示的转发规则（含 tcp/udp 和 stcp）
    private FrpClientCapabilities _frpcCapabilities = FrpClientCapabilities.Unknown; // 当前 frpc 的版本和功能支持情况
    private FrpProxyConfig? _editingProxy; // 正在编辑的规则；null 表示表单处于“新增”状态
    private FrpProfileInfo _activeProfile; // 当前正在编辑和运行的配置方案
    private bool _isSynchronizingProfileSelection; // 程序主动同步下拉框时，避免触发切换逻辑
    private bool _isRunning; // 保存当前运行状态，方便同步到概览页
    private bool _isDownloadingFrpc; // 下载或安装 frpc 时，暂时不允许切换配置
    private bool _hasLoadedFrpReleases; // 是否已经进入过下载页并成功加载 release 列表
    private bool _isLoadingFrpReleases; // 避免重复触发 GitHub release 请求
    private string _frpcConfigPath => _profileStore.GetConfigPath(_activeProfile); // 当前配置方案对应的 frpc.toml

    public MainWindow() // 构造函数：窗口创建时会先执行这里
    {
        InitializeComponent(); // 加载 MainWindow.xaml 里写的界面控件

        _frpcExePath = InitializeUserFrpFile("frpc.exe"); // 从内置模板复制到用户数据目录，后续运行和下载都用这份
        var legacyConfigPath = InitializeUserFrpFile("frpc.toml"); // 旧版单配置文件路径；首次启用多配置时会复制成默认 profile
        InitializeUserFrpFile("LICENSE"); // 许可证文件不参与运行，但一起复制，方便发布后保留说明
        _profileStore = FrpProfileStore.Load(GetUserProfilesDirectory(), legacyConfigPath);
        _activeProfile = _profileStore.GetActiveProfile();
        _frpcCapabilities = FrpClientCapabilities.Detect(_frpcExePath); // 读取 frpc 版本，并检测 STCP 等功能的支持情况
        UpdateFrpcVersionDisplay(_frpcCapabilities.VersionText); // 同步顶部卡片和下载页里的版本显示

        OverviewView.BindProxies(_proxies); // 首页卡片列表显示同一份转发规则
        ConfigurationView.BindProxies(_proxies); // 配置页表格显示同一份转发规则
        BindProfilesToComboBox();
        OverviewView.CopyProxyServerEndpointRequested += OverviewView_CopyProxyServerEndpointRequested;
        ConfigurationView.SaveConfigRequested += SaveConfigButton_Click;
        ConfigurationView.AddProxyRequested += AddProxyButton_Click;
        ConfigurationView.DeleteSelectedProxyRequested += DeleteSelectedProxyButton_Click;
        ConfigurationView.EditSelectedProxyRequested += EditSelectedProxyButton_Click;
        ConfigurationView.CancelEditRequested += CancelEditButton_Click;
        ConfigurationView.DetectVersionRequested += DetectVersionButton_Click;
        DownloadView.RefreshRequested += DownloadView_RefreshRequested;
        DownloadView.DownloadRequested += DownloadView_DownloadRequested;
        DownloadView.FirewallRequested += DownloadView_FirewallRequested;

        AppendLog("客户端已启动。"); // 往日志框追加一行文字
        AppendLog($"frpc 版本：{_frpcCapabilities.VersionText}");
        AppendLog($"数据目录：{GetUserDataDirectory()}");
        AppendLog($"当前配置：{_activeProfile.Name}");
        AppendLog($"配置文件：{_frpcConfigPath}"); // $"" 是字符串插值，类似 Java/Python 里的格式化字符串

        ApplyCapabilityState(); // 把版本检测结果显示到配置页横幅和类型下拉框
        LoadConfigToForm(); // 启动时读取 frpc.toml，并填充到配置页面
        DownloadView.SetStatus("进入下载页后加载 GitHub Releases。");
    }

    private void LoadConfigToForm() // 把 frpc.toml 读取到界面输入框和表格里
    {
        _profileStore.EnsureProfileConfigFile(_activeProfile);
        FinishEditingProxy();
        _proxies.Clear();
        ConfigurationView.ServerAddr = "";
        ConfigurationView.ServerPort = "7000";
        ConfigurationView.AuthToken = "";
        ConfigurationView.ResetNewProxyForm();
        ConfigurationView.ResetProxyDefaults();

        if (!File.Exists(_frpcConfigPath))
        {
            ConfigStatusTextBlock.Text = $"未找到配置文件：{_frpcConfigPath}";
            UpdateOverview();
            return;
        }

        var document = FrpConfigSerializer.Parse(File.ReadAllText(_frpcConfigPath), out var warnings);

        ConfigurationView.ServerAddr = document.ServerAddr;
        ConfigurationView.ServerPort = string.IsNullOrWhiteSpace(document.ServerPort) ? "7000" : document.ServerPort;
        ConfigurationView.AuthToken = document.AuthToken;

        foreach (var rule in document.Rules)
        {
            _proxies.Add(rule);
        }

        foreach (var warning in warnings) // 解析器会报告被忽略的段落，例如暂不支持的 xtcp 访问端
        {
            AppendLog(warning);
        }

        if (_proxies.Any(rule => rule.IsStcp) && !_frpcCapabilities.SupportsStcp)
        {
            AppendLog($"当前 frpc {_frpcCapabilities.VersionText} 不支持 STCP，配置中的 STCP 规则无法启动。");
        }

        ConfigStatusTextBlock.Text = $"已加载配置“{_activeProfile.Name}”：{_frpcConfigPath}";
        UpdateOverview();
    }

    private void SaveConfigButton_Click(object sender, RoutedEventArgs e) // 点击“保存配置”按钮时执行
    {
        SaveConfigFromForm();
    }

    private void AddProxyButton_Click(object sender, RoutedEventArgs e) // 点击“添加转发/保存修改”按钮时执行
    {
        if (!TryBuildRuleFromForm(out var rule))
        {
            return;
        }

        if (_editingProxy is not null)
        {
            var index = _proxies.IndexOf(_editingProxy);

            if (index >= 0)
            {
                _proxies[index] = rule;
            }

            FinishEditingProxy();
            ConfigStatusTextBlock.Text = $"已更新规则“{rule.Name}”，点击“保存配置”后写入 frpc.toml。";
        }
        else
        {
            _proxies.Add(rule);
            ConfigurationView.ResetNewProxyForm();
            ConfigStatusTextBlock.Text = "已添加转发规则，点击“保存配置”后写入 frpc.toml。";
        }

        UpdateOverview();
    }

    private void EditSelectedProxyButton_Click(object sender, RoutedEventArgs e) // 把表格里选中的规则放回表单修改
    {
        if (ConfigurationView.SelectedProxy is not FrpProxyConfig selectedProxy)
        {
            MessageBox.Show("请先在表格中选择要编辑的规则。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _editingProxy = selectedProxy;
        ConfigurationView.FillProxyForm(selectedProxy);
        ConfigurationView.SetEditMode(true, selectedProxy.Name);
        ConfigStatusTextBlock.Text = $"正在编辑规则“{selectedProxy.Name}”，修改后点击“保存修改”。";
    }

    private void CancelEditButton_Click(object sender, RoutedEventArgs e)
    {
        FinishEditingProxy();
        ConfigStatusTextBlock.Text = "已取消编辑。";
    }

    private void FinishEditingProxy() // 退出编辑状态并清空表单
    {
        _editingProxy = null;
        ConfigurationView.SetEditMode(false, null);
        ConfigurationView.ResetNewProxyForm();
    }

    private bool TryBuildRuleFromForm(out FrpProxyConfig rule) // 从表单生成一条规则，并检查必填项
    {
        rule = new FrpProxyConfig();

        var proxyName = ConfigurationView.ProxyName.Trim();
        var proxyType = ConfigurationView.ProxyType;
        var proxyRole = ConfigurationView.ProxyRole;
        var localIP = string.IsNullOrWhiteSpace(ConfigurationView.LocalIp) ? "127.0.0.1" : ConfigurationView.LocalIp.Trim();

        if (proxyName.Length == 0)
        {
            MessageBox.Show("转发名称不能为空。", "配置错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (_proxies.Any(proxy =>
                !ReferenceEquals(proxy, _editingProxy) &&
                string.Equals(proxy.Name, proxyName, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("转发名称不能重复。", "配置错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (proxyType == FrpRuleTypes.Stcp && !_frpcCapabilities.SupportsStcp)
        {
            ShowStcpNotSupportedMessage();
            return false;
        }

        if (proxyType is not (FrpRuleTypes.Tcp or FrpRuleTypes.Udp or FrpRuleTypes.Stcp))
        {
            MessageBox.Show($"不支持的转发类型：{proxyType}。", "配置错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (proxyType == FrpRuleTypes.Stcp && proxyRole == FrpRuleTypes.VisitorRole)
        {
            var serverName = ConfigurationView.ServerName.Trim();
            var secretKey = ConfigurationView.SecretKey.Trim();
            var bindAddr = string.IsNullOrWhiteSpace(ConfigurationView.BindAddr) ? "127.0.0.1" : ConfigurationView.BindAddr.Trim();

            if (serverName.Length == 0)
            {
                MessageBox.Show("STCP 访问端需要填写服务器名称（即服务端规则的名称）。", "配置错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (secretKey.Length == 0)
            {
                MessageBox.Show("STCP 密钥不能为空，服务端和访问端必须填写相同的密钥。", "配置错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!TryReadPort(ConfigurationView.BindPort, "STCP 监听端口（bindPort）", out var bindPort))
            {
                return false;
            }

            rule = new FrpProxyConfig
            {
                Name = proxyName,
                Type = FrpRuleTypes.Stcp,
                Role = FrpRuleTypes.VisitorRole,
                ServerName = serverName,
                SecretKey = secretKey,
                BindAddr = bindAddr,
                BindPort = bindPort.ToString()
            };

            return true;
        }

        if (localIP.Length == 0)
        {
            MessageBox.Show("本地 IP 不能为空。", "配置错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!TryReadPort(ConfigurationView.LocalPort, "本地端口", out var localPort))
        {
            return false;
        }

        if (proxyType == FrpRuleTypes.Stcp)
        {
            var secretKey = ConfigurationView.SecretKey.Trim();

            if (secretKey.Length == 0)
            {
                MessageBox.Show("STCP 密钥不能为空，服务端和访问端必须填写相同的密钥。", "配置错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            rule = new FrpProxyConfig
            {
                Name = proxyName,
                Type = FrpRuleTypes.Stcp,
                Role = FrpRuleTypes.ProxyRole,
                LocalIP = localIP,
                LocalPort = localPort.ToString(),
                SecretKey = secretKey
            };

            return true;
        }

        if (!TryReadPort(ConfigurationView.RemotePort, "服务器端口", out var remotePort))
        {
            return false;
        }

        rule = new FrpProxyConfig
        {
            Name = proxyName,
            Type = proxyType,
            Role = FrpRuleTypes.ProxyRole,
            LocalIP = localIP,
            LocalPort = localPort.ToString(),
            RemotePort = remotePort.ToString()
        };

        return true;
    }

    private void ShowStcpNotSupportedMessage() // 版本不支持 STCP 时给出明确提示
    {
        MessageBox.Show(
            $"当前 frpc {_frpcCapabilities.VersionText} 不支持 STCP 配置。\n\n" +
            "STCP 本身需要 frpc 0.22.0 及以上版本；本客户端通过 TOML 配置，实际需要 0.52.0 及以上版本。\n" +
            "请先到“下载”页安装新版 frpc，然后回到配置页点击“检测版本”。",
            "STCP 不可用",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void DeleteSelectedProxyButton_Click(object sender, RoutedEventArgs e) // 删除表格里当前选中的转发规则
    {
        if (ConfigurationView.SelectedProxy is not FrpProxyConfig selectedProxy)
        {
            MessageBox.Show("请先在表格中选择要删除的转发规则。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (ReferenceEquals(selectedProxy, _editingProxy))
        {
            FinishEditingProxy();
        }

        _proxies.Remove(selectedProxy);
        ConfigStatusTextBlock.Text = "已删除选中的转发规则，点击“保存配置”后写入 frpc.toml。";
        UpdateOverview();
    }

    private void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) // 切换当前配置方案
    {
        if (_isSynchronizingProfileSelection || ProfileComboBox.SelectedItem is not FrpProfileInfo selectedProfile)
        {
            return;
        }

        if (selectedProfile.Id == _activeProfile.Id)
        {
            return;
        }

        if (!CanChangeProfile())
        {
            SelectProfileInComboBox(_activeProfile);
            return;
        }

        if (!SaveConfigFromForm())
        {
            SelectProfileInComboBox(_activeProfile);
            return;
        }

        ActivateProfile(selectedProfile);
    }

    private void RefreshProfileButton_Click(object sender, RoutedEventArgs e) // 从当前配置文件重新读取到界面
    {
        var result = MessageBox.Show(
            $"将重新读取配置“{_activeProfile.Name}”的 frpc.toml。\n\n当前界面中未保存的修改会被覆盖，是否继续？",
            "刷新配置",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            LoadConfigToForm();
            ConfigStatusTextBlock.Text = $"已刷新配置“{_activeProfile.Name}”：{_frpcConfigPath}";
            AppendLog($"已从文件刷新配置“{_activeProfile.Name}”。");

            if (_isRunning)
            {
                AppendLog("frpc 正在运行，刷新界面不会让运行中的 frpc 立即使用新配置，需要重启服务后生效。");
            }
        }
        catch (Exception ex)
        {
            ConfigStatusTextBlock.Text = "刷新配置失败。";
            AppendLog($"刷新配置失败：{ex.Message}");
            MessageBox.Show($"刷新配置失败：{ex.Message}", "刷新失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void CopyServerIpButton_Click(object sender, RoutedEventArgs e) // 复制当前配置里的服务器 IP
    {
        var serverAddr = ConfigurationView.ServerAddr.Trim();

        if (serverAddr.Length == 0)
        {
            MessageBox.Show("服务器 IP 为空，无法复制。", "复制服务器 IP", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await CopyTextToClipboardAsync(serverAddr, $"已复制服务器 IP：{serverAddr}", "复制服务器 IP");
    }

    private async void OverviewView_CopyProxyServerEndpointRequested(object? sender, Views.OverviewProxyCopyRequestedEventArgs e) // 复制卡片对应的访问地址
    {
        var proxy = e.Proxy;
        var serverAddr = ConfigurationView.ServerAddr.Trim();

        if (proxy.IsStcpProxy)
        {
            MessageBox.Show(
                "STCP 服务端没有固定的对外访问地址。\n\n请在需要访问的一端添加一条“stcp 访问端”规则，填写服务器名称、密钥和监听端口，之后访问监听地址 + 监听端口即可。",
                "复制访问地址",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var endpoint = proxy.GetCopyEndpointText(serverAddr);

        if (endpoint.Length == 0)
        {
            MessageBox.Show(
                proxy.IsStcpVisitor ? "STCP 访问端的监听端口为空，无法复制。" : "服务器 IP 为空，无法复制。",
                "复制访问地址",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        await CopyTextToClipboardAsync(endpoint, $"已复制访问地址：{endpoint}", "复制访问地址");
    }

    private async Task CopyTextToClipboardAsync(string text, string successMessage, string actionName) // 统一处理复制文本、状态提示和错误提示
    {
        try
        {
            await NativeClipboard.SetTextWithRetryAsync(
                text,
                ClipboardCopyRetryCount,
                ClipboardCopyRetryDelayMilliseconds);
            ConfigStatusTextBlock.Text = successMessage;
            AppendLog(successMessage);
        }
        catch (Exception ex)
        {
            AppendLog($"{actionName}失败：{ex.Message}");
            MessageBox.Show($"{actionName}失败：{ex.Message}", "复制失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void NewProfileButton_Click(object sender, RoutedEventArgs e) // 新建一个空配置方案
    {
        if (!CanChangeProfile() || !SaveConfigFromForm())
        {
            return;
        }

        var name = ShowProfileNameDialog("新建配置", "配置名称", "新配置");

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            var profile = _profileStore.CreateProfile(name, sourceProfile: null);
            ActivateProfile(profile);
            AppendLog($"已新建配置：{profile.Name}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"新建配置失败：{ex.Message}", "新建失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DuplicateProfileButton_Click(object sender, RoutedEventArgs e) // 复制当前配置方案
    {
        if (!CanChangeProfile())
        {
            return;
        }

        if (!SaveConfigFromForm())
        {
            return;
        }

        var name = ShowProfileNameDialog("复制配置", "新配置名称", $"{_activeProfile.Name} 副本");

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            var profile = _profileStore.CreateProfile(name, _activeProfile);
            ActivateProfile(profile);
            AppendLog($"已复制配置：{profile.Name}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"复制配置失败：{ex.Message}", "复制失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RenameProfileButton_Click(object sender, RoutedEventArgs e) // 重命名当前配置方案
    {
        var name = ShowProfileNameDialog("重命名配置", "配置名称", _activeProfile.Name);

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            _profileStore.RenameProfile(_activeProfile, name);
            BindProfilesToComboBox();
            ConfigStatusTextBlock.Text = $"当前配置已重命名为“{_activeProfile.Name}”。";
            AppendLog($"当前配置已重命名为：{_activeProfile.Name}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"重命名配置失败：{ex.Message}", "重命名失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteProfileButton_Click(object sender, RoutedEventArgs e) // 删除当前配置方案
    {
        if (!CanChangeProfile())
        {
            return;
        }

        if (_profileStore.Profiles.Count <= 1)
        {
            MessageBox.Show("至少需要保留一个配置。", "无法删除", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            $"确定删除配置“{_activeProfile.Name}”吗？\n\n对应的 frpc.toml 会一起删除。",
            "删除配置",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            var deletedProfileName = _activeProfile.Name;
            var nextProfile = _profileStore.DeleteProfile(_activeProfile);
            ActivateProfile(nextProfile);
            AppendLog($"已删除配置：{deletedProfileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"删除配置失败：{ex.Message}", "删除失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool SaveConfigFromForm() // 把界面里的配置重新生成并写入 frpc.toml
    {
        if (!TryBuildConfigText(out var configText))
        {
            return false;
        }

        try
        {
            var configDirectory = Path.GetDirectoryName(_frpcConfigPath);

            if (!string.IsNullOrWhiteSpace(configDirectory))
            {
                Directory.CreateDirectory(configDirectory);
            }

            File.WriteAllText(_frpcConfigPath, configText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var unsupportedStcpCount = _proxies.Count(rule => rule.IsStcp);

            if (unsupportedStcpCount > 0 && !_frpcCapabilities.SupportsStcp)
            {
                ConfigStatusTextBlock.Text = $"配置已保存，但当前 frpc {_frpcCapabilities.VersionText} 不支持 STCP，启动前请先更新 frpc。";
                AppendLog($"配置中仍有 {unsupportedStcpCount} 条 STCP 规则，当前 frpc 版本不支持，启动会被拦截。");
            }
            else
            {
                ConfigStatusTextBlock.Text = $"配置“{_activeProfile.Name}”已保存：{_frpcConfigPath}";
            }

            AppendLog($"配置“{_activeProfile.Name}”已保存。");
            UpdateOverview();

            if (_frpcProcess is not null && !_frpcProcess.HasExited)
            {
                AppendLog("frpc 正在运行，配置修改需要重启 frpc 后生效。");
            }

            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存配置失败：{ex.Message}", "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
            ConfigStatusTextBlock.Text = "保存配置失败。";
            return false;
        }
    }

    private void BindProfilesToComboBox() // 把 profiles.json 中的配置列表同步到下拉框
    {
        _isSynchronizingProfileSelection = true;

        try
        {
            ProfileComboBox.ItemsSource = null;
            ProfileComboBox.DisplayMemberPath = nameof(FrpProfileInfo.Name);
            ProfileComboBox.ItemsSource = _profileStore.Profiles;
            ProfileComboBox.SelectedItem = _activeProfile;
            UpdateProfileControlsState();
        }
        finally
        {
            _isSynchronizingProfileSelection = false;
        }
    }

    private void SelectProfileInComboBox(FrpProfileInfo profile) // 切换失败时，把下拉框选项恢复为当前配置
    {
        _isSynchronizingProfileSelection = true;

        try
        {
            ProfileComboBox.SelectedItem = profile;
        }
        finally
        {
            _isSynchronizingProfileSelection = false;
        }
    }

    private void ActivateProfile(FrpProfileInfo profile) // 设置当前配置并重新读取对应的 frpc.toml
    {
        _activeProfile = profile;
        _profileStore.SetActiveProfile(profile);
        BindProfilesToComboBox();
        LoadConfigToForm();
        AppendLog($"已切换到配置：{profile.Name}");
        AppendLog($"配置文件：{_frpcConfigPath}");
    }

    private bool CanChangeProfile() // frpc 运行或下载中时，不允许切换配置
    {
        if (_isRunning)
        {
            MessageBox.Show("请先停止 frpc，再切换或管理配置。", "frpc 正在运行", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        if (_isDownloadingFrpc)
        {
            MessageBox.Show("请等待 frpc 下载完成后再切换或管理配置。", "正在下载", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        return true;
    }

    private void UpdateProfileControlsState() // 根据运行状态更新 profile 管理按钮
    {
        if (ProfileComboBox is null)
        {
            return;
        }

        var canEditProfile = !_isRunning && !_isDownloadingFrpc;
        ProfileComboBox.IsEnabled = canEditProfile;
        RefreshProfileButton.IsEnabled = !_isDownloadingFrpc;
        NewProfileButton.IsEnabled = canEditProfile;
        DuplicateProfileButton.IsEnabled = canEditProfile;
        RenameProfileButton.IsEnabled = canEditProfile;
        DeleteProfileButton.IsEnabled = canEditProfile && _profileStore.Profiles.Count > 1;
    }

    private string? ShowProfileNameDialog(string title, string prompt, string defaultName) // 简单的配置名称输入弹窗
    {
        var dialog = new Window
        {
            Title = title,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            Background = new SolidColorBrush(Color.FromRgb(247, 249, 252))
        };

        var root = new Grid
        {
            Margin = new Thickness(18),
            MinWidth = 340
        };

        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var promptTextBlock = new TextBlock
        {
            Text = prompt,
            Foreground = new SolidColorBrush(Color.FromRgb(84, 96, 112)),
            FontSize = 12
        };
        Grid.SetRow(promptTextBlock, 0);

        var textBox = new TextBox
        {
            Text = defaultName,
            Margin = new Thickness(0, 7, 0, 0),
            MinWidth = 320
        };
        Grid.SetRow(textBox, 1);

        var errorTextBlock = new TextBlock
        {
            Margin = new Thickness(0, 7, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(207, 34, 46)),
            FontSize = 12
        };
        Grid.SetRow(errorTextBlock, 2);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        Grid.SetRow(buttonPanel, 3);

        var cancelButton = new Button
        {
            Width = 84,
            Content = "取消"
        };
        cancelButton.Click += (_, _) => dialog.DialogResult = false;

        var okButton = new Button
        {
            Width = 84,
            Margin = new Thickness(8, 0, 0, 0),
            Content = "确定",
            Background = (Brush)FindResource("PrimaryBrush"),
            BorderBrush = (Brush)FindResource("PrimaryBrush"),
            Foreground = Brushes.White
        };
        okButton.Click += (_, _) =>
        {
            var value = textBox.Text.Trim();

            if (value.Length == 0)
            {
                errorTextBlock.Text = "配置名称不能为空。";
                return;
            }

            dialog.Tag = value;
            dialog.DialogResult = true;
        };

        textBox.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                okButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                args.Handled = true;
            }
            else if (args.Key == Key.Escape)
            {
                dialog.DialogResult = false;
                args.Handled = true;
            }
        };

        buttonPanel.Children.Add(cancelButton);
        buttonPanel.Children.Add(okButton);
        root.Children.Add(promptTextBlock);
        root.Children.Add(textBox);
        root.Children.Add(errorTextBlock);
        root.Children.Add(buttonPanel);

        dialog.Content = root;
        dialog.Loaded += (_, _) =>
        {
            textBox.Focus();
            textBox.SelectAll();
        };

        return dialog.ShowDialog() == true ? dialog.Tag?.ToString() : null;
    }

    private bool TryBuildConfigText(out string configText) // 校验界面输入，并生成 frpc.toml 内容
    {
        configText = "";

        var serverAddr = ConfigurationView.ServerAddr.Trim();
        var authToken = ConfigurationView.AuthToken.Trim();

        if (serverAddr.Length == 0)
        {
            MessageBox.Show("服务器 IP 不能为空。", "配置错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!TryReadPort(ConfigurationView.ServerPort, "服务器端口", out var serverPort))
        {
            return false;
        }

        if (authToken.Length == 0)
        {
            MessageBox.Show("权限 token 不能为空。", "配置错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        foreach (var proxy in _proxies)
        {
            proxy.TrimAll();

            if (proxy.Name.Length == 0)
            {
                MessageBox.Show("转发规则名称不能为空。", "配置错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (proxy.Type is not (FrpRuleTypes.Tcp or FrpRuleTypes.Udp or FrpRuleTypes.Stcp))
            {
                MessageBox.Show($"转发“{proxy.Name}”的类型只能是 tcp、udp 或 stcp。", "配置错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (proxy.IsStcpVisitor)
            {
                if (proxy.ServerName.Length == 0)
                {
                    MessageBox.Show($"STCP 访问端“{proxy.Name}”的服务器名称不能为空。", "配置错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                if (proxy.SecretKey.Length == 0)
                {
                    MessageBox.Show($"STCP 访问端“{proxy.Name}”的密钥不能为空。", "配置错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                if (!TryReadPort(proxy.BindPort, $"STCP 访问端“{proxy.Name}”的监听端口", out _))
                {
                    return false;
                }

                continue;
            }

            if (proxy.LocalIP.Length == 0)
            {
                MessageBox.Show($"转发“{proxy.Name}”的本地 IP 不能为空。", "配置错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!TryReadPort(proxy.LocalPort, $"转发“{proxy.Name}”的本地端口", out _))
            {
                return false;
            }

            if (proxy.IsStcpProxy)
            {
                if (proxy.SecretKey.Length == 0)
                {
                    MessageBox.Show($"STCP 服务端“{proxy.Name}”的密钥不能为空。", "配置错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                continue;
            }

            if (!TryReadPort(proxy.RemotePort, $"转发“{proxy.Name}”的服务器端口", out _))
            {
                return false;
            }
        }

        if (_proxies.GroupBy(proxy => proxy.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            MessageBox.Show("转发规则名称不能重复。", "配置错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var document = new FrpConfigDocument
        {
            ServerAddr = serverAddr,
            ServerPort = serverPort.ToString(),
            AuthToken = authToken
        };

        document.Rules.AddRange(_proxies);
        configText = FrpConfigSerializer.Build(document);
        return true;
    }

    private static bool TryReadPort(string text, string fieldName, out int port) // 检查端口是否是 1-65535 的整数
    {
        if (int.TryParse(text.Trim(), out port) && port is >= 1 and <= 65535)
        {
            return true;
        }

        MessageBox.Show($"{fieldName}必须是 1 到 65535 之间的整数。", "配置错误", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private void StartButton_Click(object sender, RoutedEventArgs e) // 点击“启动 frpc”按钮时执行
    {
        StartFrpcProcess();
    }

    private void StartFrpcProcess() // 窗口和托盘共用的启动流程，包含版本检查、配置保存和日志监听
    {
        if (_isExiting || _isDownloadingFrpc) // 退出或下载时不能启动，避免运行正在替换的 frpc
        {
            return;
        }

        if (_frpcProcess is not null && !_frpcProcess.HasExited) // 如果进程对象存在，并且还没有退出
        {
            AppendLog("frpc 已经在运行。");
            return; // 直接结束这个函数，避免重复启动
        }

        if (!File.Exists(_frpcExePath)) // 启动前先确认 frpc.exe 是否真的存在
        {
            AppendLog($"找不到 frpc.exe：{_frpcExePath}");
            return;
        }

        if (_frpcCapabilities.HasKnownVersion && !_frpcCapabilities.SupportsTomlConfig) // 老版本 frpc 读不了 TOML 配置
        {
            MessageBox.Show(
                $"当前 frpc {_frpcCapabilities.VersionText} 低于 0.52.0，无法读取本客户端生成的 TOML 配置。\n\n" +
                "请到“下载”页安装 0.52.0 及以上版本的 frpc，然后点击配置页的“检测版本”。",
                "frpc 版本过低",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            AppendLog($"已取消启动：frpc {_frpcCapabilities.VersionText} 不支持 TOML 配置。");
            return;
        }

        if (_proxies.Any(rule => rule.IsStcp) && !_frpcCapabilities.SupportsStcp) // STCP 需要更新的 frpc
        {
            ShowStcpNotSupportedMessage();
            AppendLog("已取消启动：当前 frpc 不支持配置中的 STCP 规则。");
            return;
        }

        if (!SaveConfigFromForm()) // 如果用户修改了配置但没点保存，启动前自动保存一次
        {
            AppendLog("配置未保存，已取消启动 frpc。");
            return;
        }

        if (!File.Exists(_frpcConfigPath)) // 启动前先确认配置文件是否存在
        {
            AppendLog($"找不到 frpc.toml：{_frpcConfigPath}");
            return;
        }

        var startInfo = new ProcessStartInfo // 描述“要怎么启动 frpc.exe”
        {
            FileName = _frpcExePath, // 要运行的程序路径
            Arguments = $"-c \"{_frpcConfigPath}\"", // 命令行参数，等价于 frpc.exe -c frpc.toml
            WorkingDirectory = Path.GetDirectoryName(_frpcExePath) ?? AppContext.BaseDirectory, // 工作目录设为 frpc.exe 所在目录
            UseShellExecute = false, // false 才能重定向输出，让我们在程序里读取日志
            CreateNoWindow = true, // 不弹出额外的黑色命令行窗口
            RedirectStandardOutput = true, // 读取 frpc 的普通输出
            RedirectStandardError = true // 读取 frpc 的错误输出
        };

        var process = new Process // 创建进程对象，但此时还没有真正启动
        {
            StartInfo = startInfo, // 把启动参数交给进程对象
            EnableRaisingEvents = true // 允许监听 Exited 事件，也就是进程退出事件
        };

        _frpcProcess?.Dispose(); // 释放上一次已退出的进程句柄
        _frpcProcess = process;
        process.OutputDataReceived += (_, args) => AppendLog(args.Data); // frpc 有普通输出时，写入日志框
        process.ErrorDataReceived += (_, args) => AppendLog(args.Data); // frpc 有错误输出时，也写入日志框
        process.Exited += (_, _) => // frpc 进程退出时执行
        {
            if (_isExiting || Dispatcher.HasShutdownStarted)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() => // 不阻塞进程退出线程，避免退出客户端时相互等待
            {
                if (!_isExiting && ReferenceEquals(_frpcProcess, process)) // 忽略上一次进程延迟到达的退出通知
                {
                    SetRunningState(false);
                    AppendLog("frpc 已退出。");
                }
            }));
        };

        var processStarted = false;
        try
        {
            processStarted = process.Start(); // 真正启动 frpc.exe
            process.BeginOutputReadLine(); // 开始异步读取普通输出
            process.BeginErrorReadLine(); // 开始异步读取错误输出

            SetRunningState(true); // 更新按钮和状态文字
            AppendLog("frpc 已启动。");
        }
        catch (Exception ex) // 如果启动过程出错，比如权限不足、文件损坏，会进入这里
        {
            if (!processStarted)
            {
                _frpcProcess = null;
                process.Dispose();
            }

            SetRunningState(processStarted && !process.HasExited); // 即使日志监听失败，也保留对实际运行进程的控制
            AppendLog($"启动 frpc 失败：{ex.Message}");
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e) // 点击“停止 frpc”按钮时执行
    {
        StopFrpcProcess();
    }

    private void OpenFrpFolderButton_Click(object sender, RoutedEventArgs e) // 打开用户数据目录，里面包含 frp 程序和 profiles 配置
    {
        var userDataDirectory = GetUserDataDirectory();

        try
        {
            Directory.CreateDirectory(userDataDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = userDataDirectory,
                UseShellExecute = true
            });
            AppendLog($"已打开数据目录：{userDataDirectory}");
        }
        catch (Exception ex)
        {
            AppendLog($"打开数据目录失败：{ex.Message}");
            MessageBox.Show($"打开数据目录失败：{ex.Message}", "打开目录失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e) // 第一次进入下载页时才读取 GitHub Releases
    {
        if (!ReferenceEquals(e.OriginalSource, MainTabControl))
        {
            return;
        }

        if (MainTabControl.SelectedItem is not TabItem selectedTab)
        {
            return;
        }

        if (ReferenceEquals(selectedTab.Content, OverviewView))
        {
            UpdateOverview();
        }

        if (ReferenceEquals(selectedTab.Content, DownloadView))
        {
            await EnsureFrpReleasesLoadedAsync();
        }
    }

    private async Task EnsureFrpReleasesLoadedAsync() // 下载页懒加载：没进入下载页就不访问 GitHub
    {
        if (_hasLoadedFrpReleases || _isLoadingFrpReleases)
        {
            return;
        }

        await LoadFrpReleasesToDownloadViewAsync();
    }

    private async void DownloadView_RefreshRequested(object? sender, EventArgs e) // 下载页点击“刷新列表”时执行
    {
        await LoadFrpReleasesToDownloadViewAsync();
    }

    private async void DownloadView_DownloadRequested(object? sender, FrpReleaseDownloadRequestedEventArgs e) // 下载页点击某个版本的“下载”时执行
    {
        var release = e.Release;

        if (release.CanInstallOnThisClient)
        {
            if (_frpcProcess is not null && !_frpcProcess.HasExited)
            {
                MessageBox.Show("请先停止 frpc，再下载并替换客户端版本。", "frpc 正在运行", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirmResult = MessageBox.Show(
                $"将从 GitHub 下载 frpc {release.Version}（{release.PlatformArchitecture}），并替换当前正在使用的 frpc.exe。\n\n配置文件 frpc.toml 不会被覆盖。",
                "安装 frpc",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information);

            if (confirmResult != MessageBoxResult.OK)
            {
                return;
            }

            SetFrpcDownloadState(isDownloading: true);

            try
            {
                ConfigStatusTextBlock.Text = $"正在下载 frpc {release.Version}...";
                DownloadView.SetStatus($"正在下载 {release.Version}...");
                DownloadView.SetDownloadProgress(0, "准备下载...");
                AppendLog($"开始下载 frpc {release.Version}（{release.PlatformArchitecture}）。");

                var progress = new Progress<double?>(percent =>
                {
                    DownloadView.SetDownloadProgress(
                        percent,
                        percent is null ? "下载中..." : $"下载中 {percent.Value:0}%");
                });

                await DownloadAndReplaceFrpcAsync(release, _frpcExePath, progress);

                _frpcCapabilities = FrpClientCapabilities.Detect(_frpcExePath); // 安装完成后重新检测版本和 STCP 支持情况
                UpdateFrpcVersionDisplay(_frpcCapabilities.VersionText);
                ApplyCapabilityState();
                ConfigStatusTextBlock.Text = $"frpc 已更新为 {_frpcCapabilities.VersionText}。";
                DownloadView.SetDownloadProgress(100, "安装完成");
                DownloadView.SetStatus($"frpc 已更新为 {_frpcCapabilities.VersionText}");
                AppendLog($"frpc 已更新为 {_frpcCapabilities.VersionText}。");
            }
            catch (Exception ex)
            {
                ConfigStatusTextBlock.Text = "下载 frpc 失败。";
                DownloadView.SetDownloadProgress(0, "下载失败");
                DownloadView.SetStatus("下载 frpc 失败。");
                AppendLog($"下载 frpc 失败：{ex.Message}");
                MessageBox.Show($"下载 frpc 失败：{ex.Message}", "下载失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetFrpcDownloadState(isDownloading: false);
            }

            return;
        }

        var downloadConfirmResult = MessageBox.Show(
            $"这个包是 {release.PlatformArchitecture}，不能直接替换当前 Windows 客户端的 frpc.exe。\n\n是否只下载压缩包到 frp 数据目录的 downloads 文件夹？",
            "下载 frp 包",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information);

        if (downloadConfirmResult != MessageBoxResult.OK)
        {
            return;
        }

        SetFrpcDownloadState(isDownloading: true);

        try
        {
            DownloadView.SetStatus($"正在下载 {release.PlatformArchitecture} 包...");
            DownloadView.SetDownloadProgress(0, "准备下载...");
            AppendLog($"开始下载 frp 包 {release.AssetName}。");

            var progress = new Progress<double?>(percent =>
            {
                DownloadView.SetDownloadProgress(
                    percent,
                    percent is null ? "下载中..." : $"下载中 {percent.Value:0}%");
            });

            var packagePath = await DownloadFrpPackageToDownloadsAsync(release, progress);
            DownloadView.SetDownloadProgress(100, "下载完成");
            DownloadView.SetStatus("压缩包已下载");
            AppendLog($"frp 包已下载到：{packagePath}");
        }
        catch (Exception ex)
        {
            DownloadView.SetDownloadProgress(0, "下载失败");
            DownloadView.SetStatus("下载 frp 包失败。");
            AppendLog($"下载 frp 包失败：{ex.Message}");
            MessageBox.Show($"下载 frp 包失败：{ex.Message}", "下载失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetFrpcDownloadState(isDownloading: false);
        }
    }

    private void StopFrpcProcess() // 窗口、托盘停止服务，以及彻底退出客户端共用的停止逻辑
    {
        if (_frpcProcess is null || _frpcProcess.HasExited) // 没有进程，或者进程已经退出
        {
            SetRunningState(false);
            AppendLog("frpc 当前没有运行。");
            return;
        }

        try
        {
            _frpcProcess.Kill(entireProcessTree: true); // 强制结束 frpc 以及它可能创建的子进程
            _frpcProcess.WaitForExit(3000); // 最多等待 3 秒，让进程退出完成
            AppendLog("已发送停止命令。");
        }
        catch (Exception ex) // 停止过程也可能失败，比如进程已经被系统结束
        {
            AppendLog($"停止 frpc 失败：{ex.Message}");
        }
        finally // 无论 try 成功还是 catch 捕获异常，最后都会执行
        {
            SetRunningState(_frpcProcess is { HasExited: false }); // 停止失败时仍显示实际运行状态
        }
    }

    private void SetRunningState(bool isRunning) // 根据运行状态更新界面
    {
        _isRunning = isRunning;
        StartButton.IsEnabled = !isRunning && !_isDownloadingFrpc; // 运行中或下载中不能启动
        StopButton.IsEnabled = isRunning && !_isDownloadingFrpc; // 只有运行中且未下载时允许停止
        DownloadView.SetServiceRunning(isRunning); // 运行中不能替换 frpc.exe
        StatusTextBlock.Text = isRunning ? "运行中" : "未运行"; // 三元表达式：条件 ? 成立时的值 : 不成立时的值
        StatusTextBlock.Foreground = isRunning // 修改状态文字颜色
            ? new SolidColorBrush(Color.FromRgb(26, 127, 55)) // 运行中显示绿色
            : new SolidColorBrush(Color.FromRgb(207, 34, 46)); // 未运行显示红色
        UpdateProfileControlsState();
        UpdateOverview();
    }

    private void UpdateOverview() // 同步首页概览卡片和只读转发表格
    {
        if (OverviewView is null)
        {
            return;
        }

        OverviewView.SetRunningState(_isRunning);

        var serverAddr = ConfigurationView.ServerAddr.Trim();
        var serverPort = ConfigurationView.ServerPort.Trim();
        OverviewServerTextBlock.Text = string.IsNullOrWhiteSpace(serverAddr)
            ? "-"
            : $"{serverAddr}:{serverPort}";
        OverviewServerTextBlock.ToolTip = OverviewServerTextBlock.Text;
        CopyServerIpButton.IsEnabled = serverAddr.Length > 0;

        OverviewView.UpdateProxyCount(_proxies.Count);
        OverviewView.RefreshProxyCards(); // 保存或编辑后刷新首页卡片列表
        UpdateTrayState();
    }

    private void AppendLog(string? message) // 向日志文本框追加一行
    {
        if (_isExiting || Dispatcher.HasShutdownStarted || string.IsNullOrWhiteSpace(message)) // 退出时不再向即将关闭的界面投递日志
        {
            return;
        }

        if (!Dispatcher.CheckAccess()) // 判断当前代码是否运行在 WPF 的 UI 线程
        {
            Dispatcher.BeginInvoke(new Action(() => AppendLog(message))); // 异步切回 UI 线程，避免停止进程时互相等待
            return;
        }

        LogView.AppendLogLine($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}"); // 加时间戳并换行
    }

    private void SetFrpcDownloadState(bool isDownloading) // 下载过程中临时禁用按钮，避免重复点击
    {
        _isDownloadingFrpc = isDownloading;
        DownloadView.SetBusy(isDownloading);
        StartButton.IsEnabled = !isDownloading && !_isRunning;
        StopButton.IsEnabled = !isDownloading && _isRunning;
        UpdateProfileControlsState();
        UpdateTrayState();
    }

    private void UpdateFrpcVersionDisplay(string version) // 同步所有展示 frpc 版本号的位置
    {
        FrpcVersionTextBlock.Text = $"frpc {version}";
        FrpcVersionTextBlock.ToolTip = _frpcCapabilities.DetailText;
        DownloadView.SetCurrentVersion(version);
    }

    private void ApplyCapabilityState() // 把版本检测结果同步到配置页状态标签和类型下拉框
    {
        ConfigurationView.SetCapability(_frpcCapabilities);
        AppendLog($"版本检测：frpc {_frpcCapabilities.VersionText}，STCP {(_frpcCapabilities.SupportsStcp ? "可用" : "不可用")}。");
    }

    private void DetectVersionButton_Click(object sender, RoutedEventArgs e) // 配置页点击“检测版本”
    {
        _frpcCapabilities = FrpClientCapabilities.Detect(_frpcExePath);
        UpdateFrpcVersionDisplay(_frpcCapabilities.VersionText);
        ApplyCapabilityState();
        ConfigStatusTextBlock.Text = _frpcCapabilities.StatusDetail;
        AppendLog(_frpcCapabilities.StatusDetail);

        if (!_frpcCapabilities.SupportsStcp && _proxies.Any(rule => rule.IsStcp))
        {
            AppendLog("配置中的 STCP 规则在当前 frpc 版本下无法启动。");
        }
    }

    private async Task LoadFrpReleasesToDownloadViewAsync() // 从 GitHub 读取可下载版本，并交给下载页显示
    {
        if (_isLoadingFrpReleases)
        {
            return;
        }

        _isLoadingFrpReleases = true;
        DownloadView.SetBusy(true);
        DownloadView.SetStatus("正在读取 GitHub Releases...");
        DownloadView.ResetDownloadProgress();

        try
        {
            var releases = await LoadFrpReleasesAsync();
            DownloadView.SetReleases(releases);
            DownloadView.SetStatus($"已加载 {releases.Count} 个可下载包");
            _hasLoadedFrpReleases = true;
            AppendLog($"已加载 {releases.Count} 个 GitHub Releases 可下载包。");
        }
        catch (Exception ex)
        {
            DownloadView.SetStatus("读取 GitHub Releases 失败。");
            AppendLog($"读取 GitHub Releases 失败：{ex.Message}");
        }
        finally
        {
            _isLoadingFrpReleases = false;
            DownloadView.SetBusy(false);
            DownloadView.SetServiceRunning(_isRunning);
        }
    }

    private static async Task<IReadOnlyList<FrpReleaseInfo>> LoadFrpReleasesAsync() // 读取 GitHub Releases 中所有可下载的 frp 客户端包
    {
        var releases = new List<FrpReleaseInfo>();
        var nextUrl = "https://api.github.com/repos/fatedier/frp/releases?per_page=100";

        for (var page = 0; !string.IsNullOrWhiteSpace(nextUrl) && page < 5; page++)
        {
            using var response = await FrpDownloadHttpClient.GetAsync(nextUrl, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"GitHub API 请求失败，HTTP 状态码：{(int)response.StatusCode}。");
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(responseStream);

            foreach (var releaseElement in document.RootElement.EnumerateArray())
            {
                if (releaseElement.TryGetProperty("draft", out var draftElement) && draftElement.GetBoolean())
                {
                    continue;
                }

                var tagName = GetJsonString(releaseElement, "tag_name");
                var version = NormalizeFrpVersion(tagName);

                if (!IsValidFrpVersion(version))
                {
                    continue;
                }

                var releaseName = GetJsonString(releaseElement, "name");
                var publishedAt = TryReadPublishedAt(releaseElement);

                if (!releaseElement.TryGetProperty("assets", out var assetsElement))
                {
                    continue;
                }

                foreach (var assetElement in assetsElement.EnumerateArray())
                {
                    var assetName = GetJsonString(assetElement, "name");

                    if (!TryParseFrpPackageAsset(assetName, version, out var platform, out var architecture, out var packageExtension))
                    {
                        continue;
                    }

                    releases.Add(new FrpReleaseInfo
                    {
                        Version = version,
                        TagName = tagName,
                        ReleaseName = string.IsNullOrWhiteSpace(releaseName) ? tagName : releaseName,
                        AssetName = assetName,
                        DownloadUrl = GetJsonString(assetElement, "browser_download_url"),
                        Platform = platform,
                        Architecture = architecture,
                        PackageExtension = packageExtension,
                        SizeBytes = TryReadSize(assetElement),
                        PublishedAt = publishedAt
                    });
                }
            }

            nextUrl = GetNextPageUrl(response);
        }

        return releases
            .OrderByDescending(release => release.PublishedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    private static async Task DownloadAndReplaceFrpcAsync(
        FrpReleaseInfo release,
        string targetFrpcExePath,
        IProgress<double?>? progress) // 下载指定版本 frpc，并替换当前使用的 frpc.exe
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "frp-client-download", Guid.NewGuid().ToString("N"));
        var packagePath = Path.Combine(tempDirectory, Path.GetFileName(release.AssetName));
        var extractedFrpcPath = Path.Combine(tempDirectory, "frpc.exe");

        Directory.CreateDirectory(tempDirectory);

        try
        {
            using var response = await FrpDownloadHttpClient.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"GitHub 下载失败，HTTP 状态码：{(int)response.StatusCode}。请确认版本 {release.Version} 是否存在。");
            }

            await DownloadFileWithProgressAsync(response, packagePath, progress);
            progress?.Report(100);

            using (var archive = ZipFile.OpenRead(packagePath))
            {
                var frpcEntry = archive.Entries.FirstOrDefault(entry =>
                    string.Equals(Path.GetFileName(entry.FullName), "frpc.exe", StringComparison.OrdinalIgnoreCase));

                if (frpcEntry is null)
                {
                    throw new InvalidOperationException("下载的压缩包里没有找到 frpc.exe。");
                }

                frpcEntry.ExtractToFile(extractedFrpcPath, overwrite: true);
            }

            var downloadedVersion = GetFrpcVersion(extractedFrpcPath);

            if (!string.Equals(downloadedVersion, release.Version, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"下载到的 frpc 版本是 {downloadedVersion}，不是期望的 {release.Version}。");
            }

            var targetDirectory = Path.GetDirectoryName(targetFrpcExePath);

            if (string.IsNullOrWhiteSpace(targetDirectory))
            {
                throw new InvalidOperationException("无法确定 frpc.exe 的目标目录。");
            }

            Directory.CreateDirectory(targetDirectory);

            var backupPath = Path.Combine(targetDirectory, $"frpc.exe.{DateTime.Now:yyyyMMddHHmmss}.bak");

            if (File.Exists(targetFrpcExePath))
            {
                File.Copy(targetFrpcExePath, backupPath, overwrite: true);
            }

            try
            {
                File.Copy(extractedFrpcPath, targetFrpcExePath, overwrite: true);
            }
            catch
            {
                if (File.Exists(backupPath))
                {
                    File.Copy(backupPath, targetFrpcExePath, overwrite: true);
                }

                throw;
            }
        }
        finally
        {
            try
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
            catch
            {
                // 临时目录删除失败不影响 frpc 更新结果，下次系统清理临时目录即可。
            }
        }
    }

    private static async Task<string> DownloadFrpPackageToDownloadsAsync(
        FrpReleaseInfo release,
        IProgress<double?>? progress) // 非 Windows 包只下载到数据目录，避免替换成本机不能运行的程序
    {
        var downloadsDirectory = Path.Combine(GetUserFrpDirectory(), "downloads");
        Directory.CreateDirectory(downloadsDirectory);

        var packagePath = Path.Combine(downloadsDirectory, Path.GetFileName(release.AssetName));

        using var response = await FrpDownloadHttpClient.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"GitHub 下载失败，HTTP 状态码：{(int)response.StatusCode}。");
        }

        await DownloadFileWithProgressAsync(response, packagePath, progress);
        progress?.Report(100);

        return packagePath;
    }

    private static async Task DownloadFileWithProgressAsync(
        HttpResponseMessage response,
        string targetPath,
        IProgress<double?>? progress)
    {
        var totalBytes = response.Content.Headers.ContentLength;
        var downloadedBytes = 0L;
        var buffer = new byte[81920];

        if (totalBytes is null or <= 0)
        {
            progress?.Report(null);
        }

        await using var sourceStream = await response.Content.ReadAsStreamAsync();
        await using var targetStream = File.Create(targetPath);

        while (true)
        {
            var bytesRead = await sourceStream.ReadAsync(buffer);

            if (bytesRead == 0)
            {
                break;
            }

            await targetStream.WriteAsync(buffer.AsMemory(0, bytesRead));
            downloadedBytes += bytesRead;

            if (totalBytes is > 0)
            {
                progress?.Report(downloadedBytes * 100.0 / totalBytes.Value);
            }
        }
    }

    private static string NormalizeFrpVersion(string version) // 允许用户输入 0.62.1 或 v0.62.1，内部统一去掉 v
    {
        var normalizedVersion = version.Trim();

        return normalizedVersion.StartsWith('v') || normalizedVersion.StartsWith('V')
            ? normalizedVersion[1..]
            : normalizedVersion;
    }

    private static bool IsValidFrpVersion(string version) // 限制版本号字符，避免把用户输入直接拼进下载 URL
    {
        return Regex.IsMatch(version, @"^\d+\.\d+\.\d+(-[A-Za-z0-9.-]+)?$");
    }

    private static bool TryParseFrpPackageAsset(
        string assetName,
        string expectedVersion,
        out string platform,
        out string architecture,
        out string packageExtension) // 解析 frp_版本_系统_架构.zip/tar.gz 这种 GitHub release 文件名
    {
        platform = "";
        architecture = "";
        packageExtension = "";

        var match = Regex.Match(
            assetName,
            @"^frp_(?<version>.+?)_(?<platform>[A-Za-z0-9]+)_(?<architecture>[A-Za-z0-9_]+)\.(?<extension>zip|tar\.gz)$",
            RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            return false;
        }

        var version = match.Groups["version"].Value;

        if (!string.Equals(version, expectedVersion, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        platform = match.Groups["platform"].Value.ToLowerInvariant();
        architecture = match.Groups["architecture"].Value.ToLowerInvariant();
        packageExtension = match.Groups["extension"].Value.ToLowerInvariant();
        return true;
    }

    private static string GetJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? ""
            : "";
    }

    private static long TryReadSize(JsonElement element)
    {
        return element.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var size)
            ? size
            : 0;
    }

    private static DateTimeOffset? TryReadPublishedAt(JsonElement releaseElement)
    {
        var publishedAt = GetJsonString(releaseElement, "published_at");

        return DateTimeOffset.TryParse(publishedAt, out var result) ? result : null;
    }

    private static string? GetNextPageUrl(HttpResponseMessage response) // 解析 GitHub API 的 Link 响应头，拿下一页地址
    {
        if (!response.Headers.TryGetValues("Link", out var linkHeaders))
        {
            return null;
        }

        foreach (var linkPart in string.Join(",", linkHeaders).Split(','))
        {
            var sections = linkPart.Split(';', StringSplitOptions.TrimEntries);

            if (sections.Length < 2 || !sections.Any(section => section.Equals("rel=\"next\"", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var urlPart = sections[0];

            if (urlPart.StartsWith('<') && urlPart.EndsWith('>'))
            {
                return urlPart[1..^1];
            }
        }

        return null;
    }

    private static string GetFrpcVersion(string frpcExePath) => FrpClientCapabilities.ReadFrpcVersion(frpcExePath);

    private static HttpClient CreateFrpDownloadHttpClient() // GitHub 建议请求带 User-Agent，这里统一设置
    {
        var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(120);
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("frp-client-app");
        return httpClient;
    }

    private static string InitializeUserFrpFile(string fileName) // 确保用户数据目录里有 frp 文件，返回真正运行时使用的路径
    {
        var userFrpDirectory = GetUserFrpDirectory();
        Directory.CreateDirectory(userFrpDirectory);

        var userFilePath = Path.Combine(userFrpDirectory, fileName);

        if (File.Exists(userFilePath))
        {
            return userFilePath;
        }

        var bundledFilePath = FindBundledFrpFile(fileName);

        if (File.Exists(bundledFilePath))
        {
            File.Copy(bundledFilePath, userFilePath, overwrite: false);
        }

        return userFilePath;
    }

    private static string GetUserDataDirectory() // Windows 用户数据根目录，例如 C:\Users\name\AppData\Local\FrpClient
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = AppContext.BaseDirectory;
        }

        return Path.Combine(localAppData, "FrpClient");
    }

    private static string GetUserFrpDirectory() // frpc.exe、LICENSE 和下载目录所在位置
    {
        return Path.Combine(GetUserDataDirectory(), "frp");
    }

    private static string GetUserProfilesDirectory() // 多配置方案所在位置
    {
        return Path.Combine(GetUserDataDirectory(), "profiles");
    }

    private static string FindBundledFrpFile(string fileName) // 查找随客户端一起发布的 frp 模板文件
    {
        var bundledPath = Path.Combine(AppContext.BaseDirectory, "frp", fileName); // 发布后文件会在 客户端目录/frp/ 里面

        if (File.Exists(bundledPath)) // 正式发布和正常构建后，优先使用这个模板路径
        {
            return bundledPath;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory); // AppContext.BaseDirectory 是当前程序输出目录

        while (directory is not null) // 开发阶段兜底：一层一层往父目录找原始 frp 文件夹
        {
            var candidatePath = Path.Combine( // 拼出可能的文件路径
                directory.FullName,
                "frp_0.62.1_windows_amd64",
                fileName);

            if (File.Exists(candidatePath)) // 找到了就直接返回
            {
                return candidatePath;
            }

            directory = directory.Parent; // 没找到就去上一层目录继续找
        }

        return bundledPath; // 如果没找到，返回内置路径，后面 File.Exists 会给出错误日志
    }
}

