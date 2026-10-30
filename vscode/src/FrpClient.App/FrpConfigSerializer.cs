using System.Text;

namespace FrpClient.App;

/// <summary>
/// 读取出来的 frpc.toml 内容：全局服务器配置加上所有转发规则。
/// </summary>
public sealed class FrpConfigDocument
{
    public string ServerAddr { get; set; } = "";

    public string ServerPort { get; set; } = "7000";

    public string AuthToken { get; set; } = "";

    public List<FrpProxyConfig> Rules { get; } = [];
}

/// <summary>
/// 负责 frpc.toml 的解析和生成。
/// 客户端只处理自己支持的字段，其它未知配置在保存时会被忽略，
/// 这与之前版本只重写 tcp/udp 配置的行为保持一致。
/// </summary>
public static class FrpConfigSerializer
{
    private enum SectionKind
    {
        Global,
        Proxies,
        Visitors,
        Other
    }

    public static FrpConfigDocument Parse(string configText, out List<string> warnings)
    {
        var document = new FrpConfigDocument();
        var unsupportedVisitors = new List<string>();
        warnings = [];
        var section = SectionKind.Global;
        FrpProxyConfig? currentRule = null;

        foreach (var line in configText.Split('\n'))
        {
            var trimmedLine = line.Trim();

            if (trimmedLine.Length == 0 || trimmedLine.StartsWith('#'))
            {
                continue;
            }

            if (TryReadSectionHeader(trimmedLine, out var sectionName))
            {
                if (sectionName.Equals("proxies", StringComparison.OrdinalIgnoreCase))
                {
                    section = SectionKind.Proxies;
                    currentRule = new FrpProxyConfig
                    {
                        Type = FrpRuleTypes.Tcp,
                        Role = FrpRuleTypes.ProxyRole
                    };
                    document.Rules.Add(currentRule);
                }
                else if (sectionName.Equals("visitors", StringComparison.OrdinalIgnoreCase))
                {
                    section = SectionKind.Visitors;
                    currentRule = new FrpProxyConfig
                    {
                        Type = FrpRuleTypes.Stcp,
                        Role = FrpRuleTypes.VisitorRole,
                        LocalIP = "",
                        LocalPort = ""
                    };
                    document.Rules.Add(currentRule);
                }
                else
                {
                    // 例如 [webServer]、[[proxies.plugin]] 等客户端不支持的段落，跳过其中的键值。
                    section = SectionKind.Other;
                    currentRule = null;
                }

                continue;
            }

            var equalsIndex = trimmedLine.IndexOf('=');

            if (equalsIndex < 0)
            {
                continue;
            }

            var key = trimmedLine[..equalsIndex].Trim();
            var value = UnquoteTomlValue(trimmedLine[(equalsIndex + 1)..]);

            if (section == SectionKind.Other)
            {
                continue;
            }

            if (currentRule is null)
            {
                switch (key)
                {
                    case "serverAddr":
                        document.ServerAddr = value;
                        break;
                    case "serverPort":
                        document.ServerPort = value;
                        break;
                    case "auth.token":
                        document.AuthToken = value;
                        break;
                }

                continue;
            }

            switch (key)
            {
                case "name":
                    currentRule.Name = value;
                    break;
                case "type":
                    currentRule.Type = value;
                    break;
                case "localIP":
                    currentRule.LocalIP = value;
                    break;
                case "localPort":
                    currentRule.LocalPort = value;
                    break;
                case "remotePort":
                    currentRule.RemotePort = value;
                    break;
                case "secretKey":
                    currentRule.SecretKey = value;
                    break;
                case "serverName":
                    currentRule.ServerName = value;
                    break;
                case "bindAddr":
                    currentRule.BindAddr = value;
                    break;
                case "bindPort":
                    currentRule.BindPort = value;
                    break;
            }
        }

        // 目前只支持 stcp 访问端，xtcp / sudp 的访问端先跳过，避免保存时把不认识的类型写坏。
        foreach (var rule in document.Rules
                     .Where(rule => rule.Role == FrpRuleTypes.VisitorRole &&
                                    !string.Equals(rule.Type, FrpRuleTypes.Stcp, StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            unsupportedVisitors.Add(rule.Name);
            document.Rules.Remove(rule);
        }

        foreach (var name in unsupportedVisitors)
        {
            warnings.Add($"暂不支持访问端类型“{name}”，已忽略该 [[visitors]] 配置。");
        }

        return document;
    }

    public static string Build(FrpConfigDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"serverAddr = \"{EscapeTomlString(document.ServerAddr)}\"");
        builder.AppendLine($"serverPort = {document.ServerPort}");
        builder.AppendLine($"auth.token = \"{EscapeTomlString(document.AuthToken)}\"");
        builder.AppendLine();

        foreach (var rule in document.Rules.Where(rule => !rule.IsStcpVisitor))
        {
            builder.AppendLine("[[proxies]]");
            builder.AppendLine($"name = \"{EscapeTomlString(rule.Name)}\"");
            builder.AppendLine($"type = \"{EscapeTomlString(rule.Type)}\"");

            if (rule.IsStcp)
            {
                builder.AppendLine($"secretKey = \"{EscapeTomlString(rule.SecretKey)}\"");
                builder.AppendLine($"localIP = \"{EscapeTomlString(rule.LocalIP)}\"");
                builder.AppendLine($"localPort = {rule.LocalPort}");
            }
            else
            {
                builder.AppendLine($"localIP = \"{EscapeTomlString(rule.LocalIP)}\"");
                builder.AppendLine($"localPort = {rule.LocalPort}");
                builder.AppendLine($"remotePort = {rule.RemotePort}");
            }

            builder.AppendLine();
        }

        foreach (var rule in document.Rules.Where(rule => rule.IsStcpVisitor))
        {
            builder.AppendLine("[[visitors]]");
            builder.AppendLine($"name = \"{EscapeTomlString(rule.Name)}\"");
            builder.AppendLine("type = \"stcp\"");
            builder.AppendLine($"serverName = \"{EscapeTomlString(rule.ServerName)}\"");
            builder.AppendLine($"secretKey = \"{EscapeTomlString(rule.SecretKey)}\"");
            builder.AppendLine($"bindAddr = \"{EscapeTomlString(rule.BindAddr)}\"");
            builder.AppendLine($"bindPort = {rule.BindPort}");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static bool TryReadSectionHeader(string trimmedLine, out string sectionName)
    {
        sectionName = "";

        if (!trimmedLine.StartsWith("[[", StringComparison.Ordinal) || !trimmedLine.EndsWith("]]", StringComparison.Ordinal))
        {
            return false;
        }

        sectionName = trimmedLine[2..^2].Trim();
        return sectionName.Length > 0;
    }

    private static string UnquoteTomlValue(string value) // 把 TOML 里的 "abc" 或 123 转成界面要显示的 abc / 123
    {
        var trimmedValue = RemoveInlineComment(value).Trim();

        if (trimmedValue.Length >= 2 && trimmedValue.StartsWith('"') && trimmedValue.EndsWith('"'))
        {
            trimmedValue = trimmedValue[1..^1];
        }

        return trimmedValue
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);
    }

    private static string RemoveInlineComment(string value) // 去掉 TOML 行尾注释，但保留字符串里的 # 号
    {
        var isInsideString = false;
        var isEscaped = false;

        for (var index = 0; index < value.Length; index++)
        {
            var currentChar = value[index];

            if (isEscaped)
            {
                isEscaped = false;
                continue;
            }

            if (currentChar == '\\' && isInsideString)
            {
                isEscaped = true;
                continue;
            }

            if (currentChar == '"')
            {
                isInsideString = !isInsideString;
                continue;
            }

            if (currentChar == '#' && !isInsideString)
            {
                return value[..index];
            }
        }

        return value;
    }

    private static string EscapeTomlString(string value) // 写入 TOML 前转义反斜杠和双引号
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
