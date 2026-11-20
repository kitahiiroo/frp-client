using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace FrpClient.App;

/// <summary>Windows 防火墙使用的网络类型，可组合选择。</summary>
[Flags]
internal enum FirewallNetworkProfiles
{
    None = 0,
    Domain = 1,
    Private = 2,
    Public = 4
}

/// <summary>区分授权取消、组织策略限制和规则写入结果。</summary>
internal enum FirewallSetupResult
{
    Success,
    Canceled,
    RestrictedByPolicy,
    Failed
}

/// <summary>读取网络类型，并在用户确认后通过 UAC 为固定路径的 frpc 设置入站规则。</summary>
internal static class WindowsFirewall
{
    private const string ScriptResourceName = "FrpClient.App.ConfigureFrpcFirewall.ps1";

    /// <summary>只读获取当前网络类型；系统不支持或拒绝查询时返回未知状态。</summary>
    public static FirewallNetworkProfiles GetActiveProfiles()
    {
        object? policy = null;
        try
        {
            var policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (policyType is null)
            {
                return FirewallNetworkProfiles.None;
            }

            policy = Activator.CreateInstance(policyType);
            if (policy is null)
            {
                return FirewallNetworkProfiles.None;
            }

            var profiles = (int)((dynamic)policy).CurrentProfileTypes;
            return (FirewallNetworkProfiles)(profiles & 7);
        }
        catch
        {
            // 网络类型仅用于提示；查询失败不应阻止用户打开授权窗口。
            return FirewallNetworkProfiles.None;
        }
        finally
        {
            if (policy is not null && Marshal.IsComObject(policy))
            {
                Marshal.FinalReleaseComObject(policy);
            }
        }
    }

    /// <summary>请求一次管理员授权；仅运行内嵌的规则配置脚本，不提升整个客户端。</summary>
    public static async Task<FirewallSetupResult> ConfigureFrpcAsync(string frpcPath, FirewallNetworkProfiles profiles)
    {
        var allProfiles = FirewallNetworkProfiles.Domain | FirewallNetworkProfiles.Private | FirewallNetworkProfiles.Public;
        if (profiles == FirewallNetworkProfiles.None || (profiles & ~allProfiles) != FirewallNetworkProfiles.None)
        {
            throw new ArgumentOutOfRangeException(nameof(profiles), "请至少选择一种有效的网络类型。");
        }

        var fullPath = Path.GetFullPath(frpcPath);
        if (!Path.GetFileName(fullPath).Equals("frpc.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
        {
            throw new FileNotFoundException("当前 frpc.exe 不存在，请先在下载页安装 frpc。", fullPath);
        }

        using var scriptStream = typeof(WindowsFirewall).Assembly.GetManifestResourceStream(ScriptResourceName)
            ?? throw new InvalidOperationException("发布包缺少防火墙配置脚本，请重新安装客户端。");
        using var reader = new StreamReader(scriptStream, Encoding.UTF8);
        var script = await reader.ReadToEndAsync();
        var command = $"& {{\n{script}\n}} -ProgramPath {QuotePowerShellLiteral(fullPath)} -Profiles {(int)profiles}";
        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
        var powershellPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = powershellPath,
                Arguments = $"-NoLogo -NoProfile -NonInteractive -EncodedCommand {encodedCommand}",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            }) ?? throw new InvalidOperationException("无法启动 Windows 防火墙配置程序。");

            await process.WaitForExitAsync();
            return process.ExitCode switch
            {
                0 => FirewallSetupResult.Success,
                3 => FirewallSetupResult.RestrictedByPolicy,
                _ => FirewallSetupResult.Failed
            };
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return FirewallSetupResult.Canceled;
        }
    }

    /// <summary>将文件路径编码为 PowerShell 单引号字面量，避免特殊字符被当作命令执行。</summary>
    private static string QuotePowerShellLiteral(string value)
    {
        return "'" + value.Replace("'", "''") + "'";
    }
}
