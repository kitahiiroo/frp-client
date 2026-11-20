# 仅由用户确认后的管理员进程调用，为指定 frpc 路径配置所选网络的 TCP/UDP 入站例外。
param(
    [Parameter(Mandatory = $true)]
    [string]$ProgramPath,

    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 7)]
    [int]$Profiles
)

$ErrorActionPreference = 'Stop'

try {
    $frpcPath = [IO.Path]::GetFullPath($ProgramPath)
    if (![IO.File]::Exists($frpcPath) -or [IO.Path]::GetFileName($frpcPath) -ine 'frpc.exe') {
        exit 2
    }

    $policy = New-Object -ComObject HNetCfg.FwPolicy2
    if (($policy.CurrentProfileTypes -band $Profiles) -ne 0 -and $policy.LocalPolicyModifyState -ne 0) {
        # 组织策略或“阻止所有入站连接”优先，不修改这些全局设置。
        exit 3
    }

    # 同一路径使用固定规则名称，更新 frpc 或重复授权时覆盖本客户端的规则。
    $hasher = [Security.Cryptography.SHA256]::Create()
    try {
        $pathHash = [BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($frpcPath.ToLowerInvariant())))
        $pathId = $pathHash.Replace('-', '').Substring(0, 16)
    }
    finally {
        $hasher.Dispose()
    }

    foreach ($protocol in @(6, 17)) {
        $protocolName = if ($protocol -eq 6) { 'TCP' } else { 'UDP' }
        $ruleName = "FrpClient frpc $protocolName ($pathId)"
        $rule = $null
        foreach ($candidate in $policy.Rules) {
            if ($candidate.Name -eq $ruleName) {
                if ($candidate.ApplicationName -ine $frpcPath) { throw 'Firewall rule name conflict.' }
                $rule = $candidate
                break
            }
        }
        $isNewRule = $null -eq $rule
        if ($isNewRule) {
            $rule = New-Object -ComObject HNetCfg.FWRule
            $rule.Name = $ruleName
        }
        $rule.Description = 'Allow the selected FRP client executable on user-selected network profiles.'
        $rule.Grouping = 'FrpClient'
        $rule.ApplicationName = $frpcPath
        $rule.Protocol = $protocol
        $rule.LocalPorts = '*'
        $rule.RemotePorts = '*'
        $rule.LocalAddresses = '*'
        $rule.RemoteAddresses = '*'
        $rule.Direction = 1
        $rule.Action = 1
        $rule.Enabled = $true
        $rule.Profiles = $Profiles
        $rule.InterfaceTypes = 'All'
        $rule.EdgeTraversal = $false
        if ($isNewRule) {
            $policy.Rules.Add($rule)
        }
    }

    # 曾在系统弹窗中拒绝访问会留下显式阻止规则，其优先级高于允许规则。
    # 仅处理同一可执行文件、同一入站方向、所选网络；其余网络的阻止状态继续保留。
    $blockingRules = @()
    foreach ($existing in $policy.Rules) {
        if (!$existing.Enabled -or $existing.Direction -ne 1 -or $existing.Action -ne 0) { continue }
        if ($existing.Protocol -notin @(6, 17, 256)) { continue }
        if ([string]::IsNullOrWhiteSpace($existing.ApplicationName)) { continue }
        try {
            $existingPath = [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($existing.ApplicationName))
        }
        catch {
            continue
        }
        if ($existingPath -ine $frpcPath -or ($existing.Profiles -band $Profiles) -eq 0) { continue }
        $blockingRules += $existing
    }

    foreach ($blockingRule in $blockingRules) {
        $remainingProfiles = ($blockingRule.Profiles -band 7) -band (-bnot $Profiles)
        if ($remainingProfiles -eq 0) {
            # 直接停用具体规则对象，避免按重名规则删除时影响其他路径的程序。
            $blockingRule.Enabled = $false
        }
        else {
            $blockingRule.Profiles = $remainingProfiles
        }
        if ($blockingRule.Enabled -and ($blockingRule.Profiles -band $Profiles) -ne 0) {
            exit 4
        }
    }

    foreach ($protocolName in @('TCP', 'UDP')) {
        $expectedProtocol = if ($protocolName -eq 'TCP') { 6 } else { 17 }
        $installed = $policy.Rules.Item("FrpClient frpc $protocolName ($pathId)")
        if (!$installed.Enabled -or $installed.Action -ne 1 -or $installed.Direction -ne 1 -or
            $installed.Protocol -ne $expectedProtocol -or $installed.ApplicationName -ine $frpcPath -or
            $installed.Profiles -ne $Profiles) {
            exit 4
        }
    }

    exit 0
}
catch {
    # 通过退出码报告失败，不在管理员权限下向用户可写目录生成临时脚本或结果文件。
    exit 1
}
