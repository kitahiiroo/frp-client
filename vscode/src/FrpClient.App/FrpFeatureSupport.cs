using System.Diagnostics;
using System.IO;
using System.Text;

namespace FrpClient.App;

/// <summary>客户端用到的 frp 功能，每项功能都有自己的最低版本要求。</summary>
public enum FrpFeature
{
    TomlConfig,
    StcpProxy,
    StcpVisitor
}

/// <summary>各功能所需的最低 frpc 版本。</summary>
public static class FrpFeatureCatalog
{
    /// <summary>frpc 从 0.52.0 开始支持 TOML 配置文件，本客户端生成的就是 TOML。</summary>
    public static readonly FrpVersion TomlConfigMinimum = new(0, 52, 0);

    /// <summary>STCP 代理（[[proxies]] type="stcp"）需要 0.22.0 及以上版本。</summary>
    public static readonly FrpVersion StcpProxyMinimum = new(0, 22, 0);

    /// <summary>STCP 访问端（[[visitors]] type="stcp"）需要 0.22.0 及以上版本。</summary>
    public static readonly FrpVersion StcpVisitorMinimum = new(0, 22, 0);

    public static FrpVersion GetMinimumVersion(FrpFeature feature)
    {
        return feature switch
        {
            FrpFeature.TomlConfig => TomlConfigMinimum,
            FrpFeature.StcpProxy => StcpProxyMinimum,
            FrpFeature.StcpVisitor => StcpVisitorMinimum,
            _ => new FrpVersion(0, 0, 0)
        };
    }

    public static string GetDisplayName(FrpFeature feature)
    {
        return feature switch
        {
            FrpFeature.TomlConfig => "TOML 配置格式",
            FrpFeature.StcpProxy => "STCP 服务端（[[proxies]]）",
            FrpFeature.StcpVisitor => "STCP 访问端（[[visitors]]）",
            _ => feature.ToString()
        };
    }
}

/// <summary>用真实 frpc 的 verify 命令探测 STCP 配置的结果。</summary>
public enum FrpStcpProbeResult
{
    NotRun,
    Supported,
    Unsupported
}

/// <summary>
/// 当前 frpc.exe 的版本与功能支持情况。
/// 判断分两层：先比较版本号，再（在支持 verify 的新版本上）实际让 frpc 校验一段 STCP 配置，
/// 这样即使版本号表过时，也不会把不支持的版本误判为可用。
/// </summary>
public sealed class FrpClientCapabilities
{
    private const string UnknownVersionText = "版本未知";
    private const int ProcessTimeoutMilliseconds = 5000;

    private const string StcpProbeConfigText = """
                                              serverAddr = "127.0.0.1"
                                              serverPort = 7000
                                              auth.token = "stcp-support-probe"

                                              [[proxies]]
                                              name = "stcp-support-probe"
                                              type = "stcp"
                                              secretKey = "stcp-support-probe"
                                              localIP = "127.0.0.1"
                                              localPort = 6000

                                              [[visitors]]
                                              name = "stcp-support-probe-visitor"
                                              type = "stcp"
                                              serverName = "stcp-support-probe"
                                              secretKey = "stcp-support-probe"
                                              bindAddr = "127.0.0.1"
                                              bindPort = 65000

                                              """;

    private FrpClientCapabilities(
        string versionText,
        FrpVersion? version,
        FrpStcpProbeResult stcpProbe,
        string probeDetail)
    {
        VersionText = versionText;
        Version = version;
        StcpProbe = stcpProbe;
        ProbeDetail = probeDetail;
    }

    /// <summary>还没有执行检测时的默认结果。</summary>
    public static FrpClientCapabilities Unknown { get; } =
        new(UnknownVersionText, null, FrpStcpProbeResult.NotRun, "");

    /// <summary>frpc --version 的原始输出，读取失败时是“版本未知”。</summary>
    public string VersionText { get; }

    public FrpVersion? Version { get; }

    public FrpStcpProbeResult StcpProbe { get; }

    public string ProbeDetail { get; }

    public bool HasKnownVersion => Version.HasValue;

    public bool SupportsTomlConfig => IsVersionAtLeast(FrpFeature.TomlConfig);

    public bool SupportsStcpProxy => SupportsTomlConfig && IsVersionAtLeast(FrpFeature.StcpProxy) && StcpProbe != FrpStcpProbeResult.Unsupported;

    public bool SupportsStcpVisitor => SupportsTomlConfig && IsVersionAtLeast(FrpFeature.StcpVisitor) && StcpProbe != FrpStcpProbeResult.Unsupported;

    public bool SupportsStcp => SupportsStcpProxy && SupportsStcpVisitor;

    /// <summary>配置页状态标签和提示里使用的说明文字。</summary>
    public string StatusDetail
    {
        get
        {
            if (!HasKnownVersion)
            {
                return $"未读取到 frpc 版本（{VersionText}），无法确认是否支持 STCP。可在“下载”页安装新版 frpc 后重新检测。";
            }

            if (!SupportsTomlConfig && Version is { } oldVersion)
            {
                return $"frpc {oldVersion} 低于 0.52.0，本客户端使用 TOML 配置，无法启用 STCP（STCP 本身需要 frpc ≥ 0.22.0）。请到“下载”页更新 frpc。";
            }

            if (StcpProbe == FrpStcpProbeResult.Unsupported)
            {
                return $"frpc {VersionText} 不接受 STCP 配置：{ProbeDetail}";
            }

            var probeSuffix = StcpProbe == FrpStcpProbeResult.Supported ? "，并已通过 frpc verify 验证" : "";
            return $"frpc {VersionText}，STCP 服务端与访问端均可用{probeSuffix}。";
        }
    }

    /// <summary>鼠标悬停时显示的完整检测结果。</summary>
    public string DetailText
    {
        get
        {
            var builder = new StringBuilder();
            builder.AppendLine($"frpc 版本：{VersionText}");
            builder.AppendLine($"{FrpFeatureCatalog.GetDisplayName(FrpFeature.TomlConfig)}（≥ {FrpFeatureCatalog.TomlConfigMinimum}）：{DescribeSupport(SupportsTomlConfig)}");
            builder.AppendLine($"{FrpFeatureCatalog.GetDisplayName(FrpFeature.StcpProxy)}（≥ {FrpFeatureCatalog.StcpProxyMinimum}）：{DescribeSupport(SupportsStcpProxy)}");
            builder.AppendLine($"{FrpFeatureCatalog.GetDisplayName(FrpFeature.StcpVisitor)}（≥ {FrpFeatureCatalog.StcpVisitorMinimum}）：{DescribeSupport(SupportsStcpVisitor)}");
            builder.Append($"STCP 配置探测：{StcpProbe switch
            {
                FrpStcpProbeResult.Supported => "通过",
                FrpStcpProbeResult.Unsupported => $"未通过（{ProbeDetail}）",
                _ => "未执行"
            }}");
            return builder.ToString();
        }
    }

    /// <summary>读取 frpc.exe 版本，并检测它是否真的接受 STCP 配置。</summary>
    public static FrpClientCapabilities Detect(string frpcExePath)
    {
        var versionText = ReadFrpcVersion(frpcExePath);

        if (!FrpVersion.TryParse(versionText, out var version))
        {
            return new FrpClientCapabilities(versionText, null, FrpStcpProbeResult.NotRun, "");
        }

        var probeResult = FrpStcpProbeResult.NotRun;
        var probeDetail = "";

        // verify 子命令从 0.52.0（TOML 配置）开始提供，老版本没有这个命令，只能按版本号判断。
        if (version.IsAtLeast(FrpFeatureCatalog.TomlConfigMinimum))
        {
            probeResult = RunStcpConfigProbe(frpcExePath, out probeDetail);
        }

        return new FrpClientCapabilities(versionText, version, probeResult, probeDetail);
    }

    /// <summary>调用 frpc --version 读取版本号，失败时返回“版本未知”。</summary>
    public static string ReadFrpcVersion(string frpcExePath)
    {
        if (!File.Exists(frpcExePath))
        {
            return UnknownVersionText;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = frpcExePath,
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);

            if (process is null)
            {
                return UnknownVersionText;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(ProcessTimeoutMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                return UnknownVersionText;
            }

            var output = outputTask.GetAwaiter().GetResult().Trim();

            if (output.Length == 0)
            {
                // 个别版本把版本号写到标准错误里。
                output = errorTask.GetAwaiter().GetResult().Trim();
            }

            return output.Length == 0 ? UnknownVersionText : output.Split('\n')[0].Trim();
        }
        catch
        {
            return UnknownVersionText;
        }
    }

    private static FrpStcpProbeResult RunStcpConfigProbe(string frpcExePath, out string detail)
    {
        detail = "";

        // 探测用的临时配置优先放在 frpc.exe 旁边（程序更新 frpc 时本来就在写这个目录），
        // 放不下时再依次尝试程序目录和系统临时目录，避免因目录不可写而跳过探测。
        var configPath = TryCreateProbeConfigFile(frpcExePath);

        if (configPath is null)
        {
            detail = "无法创建探测用的临时配置文件";
            return FrpStcpProbeResult.NotRun;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = frpcExePath,
                Arguments = $"verify -c \"{configPath}\"",
                WorkingDirectory = Path.GetDirectoryName(frpcExePath) ?? AppContext.BaseDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);

            if (process is null)
            {
                detail = "无法启动 frpc.exe";
                return FrpStcpProbeResult.NotRun;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(ProcessTimeoutMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                detail = "frpc verify 执行超时";
                return FrpStcpProbeResult.NotRun;
            }

            if (process.ExitCode == 0)
            {
                return FrpStcpProbeResult.Supported;
            }

            var message = $"{errorTask.GetAwaiter().GetResult()} {outputTask.GetAwaiter().GetResult()}".Trim();
            detail = SummarizeMessage(message);

            // 如果这个版本根本没有 verify 子命令，探测本身不可用，交给版本号判断。
            return message.Contains("unknown command", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("unknown flag", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("flag provided but not defined", StringComparison.OrdinalIgnoreCase)
                ? FrpStcpProbeResult.NotRun
                : FrpStcpProbeResult.Unsupported;
        }
        catch (Exception ex)
        {
            detail = SummarizeMessage(ex.Message);
            return FrpStcpProbeResult.NotRun;
        }
        finally
        {
            TryDeleteFile(configPath);
        }
    }

    private static string? TryCreateProbeConfigFile(string frpcExePath)
    {
        foreach (var directory in new[] { Path.GetDirectoryName(frpcExePath), AppContext.BaseDirectory, Path.GetTempPath() })
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                continue;
            }

            var configPath = Path.Combine(directory, $".stcp-probe-{Guid.NewGuid():N}.toml");

            try
            {
                File.WriteAllText(configPath, StcpProbeConfigText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                return configPath;
            }
            catch
            {
                // 换下一个目录继续尝试。
            }
        }

        return null;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 临时文件删除失败不影响检测结果，交给系统清理。
        }
    }

    private static string SummarizeMessage(string message)
    {
        var firstLine = message
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "";

        return firstLine.Length <= 240 ? firstLine : $"{firstLine[..240]}…";
    }

    private bool IsVersionAtLeast(FrpFeature feature)
    {
        return Version is { } version && version.IsAtLeast(FrpFeatureCatalog.GetMinimumVersion(feature));
    }

    private static string DescribeSupport(bool supported) => supported ? "支持" : "不支持";
}
