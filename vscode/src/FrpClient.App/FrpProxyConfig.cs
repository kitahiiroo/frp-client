using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FrpClient.App;

/// <summary>
/// frp 规则的类型和角色常量。
/// 表格里的每一行既可能是 [[proxies]]（服务端），也可能是 [[visitors]]（访问端）。
/// </summary>
public static class FrpRuleTypes
{
    public const string Tcp = "tcp";
    public const string Udp = "udp";
    public const string Stcp = "stcp";

    /// <summary>规则写在 [[proxies]] 里，由提供本地服务的一端配置。</summary>
    public const string ProxyRole = "proxy";

    /// <summary>规则写在 [[visitors]] 里，由需要访问服务的一端配置。</summary>
    public const string VisitorRole = "visitor";
}

/// <summary>
/// 表格里的每一行，对应 frpc.toml 里的一个 [[proxies]] 或 [[visitors]]。
/// tcp / udp 只需要本地端口和服务器端口；stcp 还需要密钥，
/// 其中 stcp 服务端使用 [[proxies]]，访问端使用 [[visitors]] 并配置监听端口（bindPort）。
/// </summary>
public class FrpProxyConfig : INotifyPropertyChanged
{
    private string _name = "";
    private string _type = FrpRuleTypes.Tcp;
    private string _role = FrpRuleTypes.ProxyRole;
    private string _localIP = "127.0.0.1";
    private string _localPort = "";
    private string _remotePort = "";
    private string _secretKey = "";
    private string _serverName = "";
    private string _bindAddr = "127.0.0.1";
    private string _bindPort = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string Type
    {
        get => _type;
        set => SetField(ref _type, value);
    }

    /// <summary>proxy 表示 [[proxies]]，visitor 表示 [[visitors]]。</summary>
    public string Role
    {
        get => _role;
        set => SetField(ref _role, value);
    }

    public string LocalIP
    {
        get => _localIP;
        set => SetField(ref _localIP, value);
    }

    public string LocalPort
    {
        get => _localPort;
        set => SetField(ref _localPort, value);
    }

    /// <summary>tcp / udp 的服务器端口，stcp 规则不使用。</summary>
    public string RemotePort
    {
        get => _remotePort;
        set => SetField(ref _remotePort, value);
    }

    /// <summary>stcp 的连接密钥，服务端和访问端必须一致。</summary>
    public string SecretKey
    {
        get => _secretKey;
        set => SetField(ref _secretKey, value);
    }

    /// <summary>stcp 访问端要连接的服务端 proxy 名称。</summary>
    public string ServerName
    {
        get => _serverName;
        set => SetField(ref _serverName, value);
    }

    /// <summary>stcp 访问端监听的地址，默认 127.0.0.1。</summary>
    public string BindAddr
    {
        get => _bindAddr;
        set => SetField(ref _bindAddr, value);
    }

    /// <summary>stcp 访问端监听的端口，也就是用户实际访问的端口。</summary>
    public string BindPort
    {
        get => _bindPort;
        set => SetField(ref _bindPort, value);
    }

    public bool IsStcp => string.Equals(Type, FrpRuleTypes.Stcp, StringComparison.OrdinalIgnoreCase);

    public bool IsStcpVisitor => IsStcp && string.Equals(Role, FrpRuleTypes.VisitorRole, StringComparison.OrdinalIgnoreCase);

    public bool IsStcpProxy => IsStcp && !IsStcpVisitor;

    /// <summary>表格“类型”列和概览卡片标签显示的文字。</summary>
    public string TypeText => !IsStcp
        ? Type
        : IsStcpVisitor ? "stcp 访问端" : "stcp 服务端";

    /// <summary>tcp / udp / stcp 服务端显示本地监听地址，访问端没有本地转发目标。</summary>
    public string LocalEndpointText => IsStcpVisitor ? "-" : $"{LocalIP}:{LocalPort}";

    public string RemotePortText => IsStcp ? "-" : RemotePort;

    /// <summary>清单“端口”列：tcp/udp 显示服务器端口，stcp 访问端显示监听端口，stcp 服务端没有端口。</summary>
    public string PortText => IsStcp ? (IsStcpVisitor ? BindPort : "-") : RemotePort;

    public string BindPortText => IsStcpVisitor ? BindPort : "-";

    public string ServerNameText => IsStcpVisitor ? ServerName : "-";

    public string SecretKeyText => SecretKey.Length == 0 ? "-" : "••••••";

    /// <summary>概览卡片左侧的大号数字：tcp/udp 显示服务器端口，stcp 访问端显示监听端口。</summary>
    public string CardLeftLabel => IsStcp
        ? IsStcpVisitor ? "访问端口" : "本地端口"
        : "服务器端口";

    public string CardLeftValue => IsStcp
        ? IsStcpVisitor ? BindPort : LocalPort
        : RemotePort;

    /// <summary>概览卡片右侧的大号数字：tcp/udp 显示本地端口，stcp 显示服务器名或密钥。</summary>
    public string CardRightLabel => IsStcp
        ? IsStcpVisitor ? "服务器名称" : "密钥"
        : "本地端口";

    public string CardRightValue => IsStcp
        ? IsStcpVisitor ? ServerName : SecretKeyText
        : LocalPort;

    /// <summary>服务器名称是文字而不是端口数字，卡片里用小一号的字，避免超出卡片。</summary>
    public double CardRightValueFontSize => IsStcpVisitor ? 18 : 28;

    /// <summary>访问端没有固定的服务器地址可复制，tcp/udp 和 stcp 访问端才允许复制。</summary>
    public bool CanCopyEndpoint => !IsStcpProxy;

    /// <summary>根据服务器地址拼出卡片“复制地址”要复制的文本。</summary>
    public string GetCopyEndpointText(string serverAddr)
    {
        if (IsStcpVisitor)
        {
            return string.IsNullOrWhiteSpace(BindAddr) ? BindPort : $"{BindAddr.Trim()}:{BindPort}";
        }

        return string.IsNullOrWhiteSpace(serverAddr) ? "" : $"{serverAddr.Trim()}:{RemotePort}";
    }

    public void TrimAll() // 保存前清理表格中用户可能输入的前后空格
    {
        Name = Name.Trim();
        Type = Type.Trim().ToLowerInvariant();
        Role = Role.Trim().ToLowerInvariant();
        LocalIP = LocalIP.Trim();
        LocalPort = LocalPort.Trim();
        RemotePort = RemotePort.Trim();
        SecretKey = SecretKey.Trim();
        ServerName = ServerName.Trim();
        BindAddr = BindAddr.Trim();
        BindPort = BindPort.Trim();
    }

    private void SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;

        // 表格和卡片里大多是计算属性，任何一个字段变化后统一刷新整行的绑定。
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}
