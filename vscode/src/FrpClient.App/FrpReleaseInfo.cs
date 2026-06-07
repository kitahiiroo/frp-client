namespace FrpClient.App;

public class FrpReleaseInfo
{
    public string Version { get; set; } = "";

    public string TagName { get; set; } = "";

    public string ReleaseName { get; set; } = "";

    public string AssetName { get; set; } = "";

    public string DownloadUrl { get; set; } = "";

    public string Platform { get; set; } = "";

    public string Architecture { get; set; } = "";

    public string PackageExtension { get; set; } = "";

    public long SizeBytes { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public string PublishedAtText => PublishedAt?.ToLocalTime().ToString("yyyy-MM-dd") ?? "-";

    public string PlatformArchitecture => string.IsNullOrWhiteSpace(Platform) || string.IsNullOrWhiteSpace(Architecture)
        ? "-"
        : $"{Platform}_{Architecture}";

    public bool CanInstallOnThisClient =>
        Platform.Equals("windows", StringComparison.OrdinalIgnoreCase) &&
        PackageExtension.Equals("zip", StringComparison.OrdinalIgnoreCase) &&
        Architecture.Equals(GetCurrentWindowsFrpArchitecture(), StringComparison.OrdinalIgnoreCase);

    public string ActionText => CanInstallOnThisClient ? "安装" : "下载";

    public string ActionHint => CanInstallOnThisClient
        ? "下载并替换当前客户端使用的 frpc.exe"
        : "下载压缩包到 frp 数据目录，不替换当前 Windows 客户端";

    public string SizeText
    {
        get
        {
            if (SizeBytes <= 0)
            {
                return "-";
            }

            var sizeInMb = SizeBytes / 1024.0 / 1024.0;
            return $"{sizeInMb:F1} MB";
        }
    }

    private static string GetCurrentWindowsFrpArchitecture()
    {
        return System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "amd64",
            System.Runtime.InteropServices.Architecture.X86 => "386",
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            System.Runtime.InteropServices.Architecture.Arm => "arm",
            _ => ""
        };
    }
}

public class FrpReleaseDownloadRequestedEventArgs(FrpReleaseInfo release) : EventArgs
{
    public FrpReleaseInfo Release { get; } = release;
}
