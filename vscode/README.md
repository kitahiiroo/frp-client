# 精简发布

现有 `publish/frpclient-win-x64` 目录约 169.62 MiB，其中主程序约 154.81 MiB，内置 `frpc.exe` 约 14.80 MiB。主程序体积主要来自随包分发的 .NET 桌面运行时。

两种发布方式都保留现有界面、功能和内置 frpc：

| 发布配置 | 运行要求 | 体积优化 |
| --- | --- | --- |
| `Lite-win-x64` | 已安装 .NET 8 Desktop Runtime（Windows x64） | 不捆绑运行时，整个发布目录预计约 16 MiB |
| `Portable-win-x64` | 无需安装 .NET | 压缩内嵌程序集，只保留中英文运行时语言资源 |

以上轻量版大小是估算值，实际大小以重新发布后的目录为准，不包含另行安装的共享运行时。已有发布文件不会因修改配置而自动缩小。

## 轻量版

在当前 `vscode` 目录执行：

```powershell
dotnet publish src/FrpClient.App/FrpClient.App.csproj -c Release -p:PublishProfile=Lite-win-x64
```

输出目录为 `publish/frpclient-win-x64-lite/`。使用前安装 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) 的 Windows x64 版本；仅安装普通 .NET Runtime 不满足 WPF 的运行要求。

## 免安装版

```powershell
dotnet publish src/FrpClient.App/FrpClient.App.csproj -c Release -p:PublishProfile=Portable-win-x64
```

输出目录为 `publish/frpclient-win-x64-portable/`。单文件压缩主要降低解压后程序的体积，ZIP 下载包的大小不一定同比下降；启动时需要解压，启动耗时可能略有增加。

项目也为原有带 `PublishSingleFile=true` 的发布命令启用了压缩。

## 分发说明

- 打包整个发布目录，保留 `FrpClient.App.exe` 旁边的 `frp` 文件夹及其中的许可证。
- 发布配置不输出调试符号、不生成额外的 ReadyToRun 代码，并限制运行时语言资源为简体中文、繁体中文和英文。
- WPF 依赖 XAML 加载和运行时绑定，因此两种配置都关闭程序集裁剪。
- 用户配置仍存放在 `%LocalAppData%\FrpClient`，切换发布版本会继续使用已有配置。
