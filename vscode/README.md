# 客户端使用与发布

## 后台与托盘

- 点击“后台运行”、最小化或关闭窗口，客户端会收起到 Windows 右下角托盘，frpc 保持当前运行状态。
- 图标可能位于任务栏的 `^` 隐藏图标区域；双击图标或右键选择“显示主窗口”可恢复界面。
- 右键托盘图标可以启动当前配置的服务、停止服务，或选择“退出程序（停止服务）”彻底退出。
- 托盘菜单会显示当前配置与服务状态，下载 frpc 时会禁用服务启停，避免替换运行中的文件。
- 关闭窗口保留当前界面内容；启动服务时仍会自动保存并校验配置。
- Windows 注销或关机时正常退出并停止 frpc；托盘初始化失败时继续使用普通窗口模式。

## 精简发布

现有 `publish/frpclient-win-x64` 目录约 169.62 MiB，其中主程序约 154.81 MiB，内置 `frpc.exe` 约 14.80 MiB。主程序体积主要来自随包分发的 .NET 桌面运行时。

两种发布方式都保留现有界面、功能和内置 frpc：

| 发布配置 | 运行要求 | 发布目录 | ZIP 压缩包 |
| --- | --- | --- | --- |
| `Lite-win-x64` | 已安装 .NET 8 Desktop Runtime（Windows x64） | 15.15 MiB | 5.91 MiB |
| `Portable-win-x64` | 无需安装 .NET | 78.66 MiB | 64.40 MiB |

以上为包含新版图标和托盘功能的 Windows x64 发布包实测大小；轻量版不包含另行安装的共享运行时。两份 EXE 均已核对内嵌图标，ZIP 已核对文件清单并逐文件通过 SHA-256 完整性校验。后续修改源码后需要重新发布才能更新程序。

### 轻量版

在当前 `vscode` 目录执行：

```powershell
dotnet publish src/FrpClient.App/FrpClient.App.csproj -c Release -p:PublishProfile=Lite-win-x64
```

输出目录为 `publish/frpclient-win-x64-lite/`。使用前安装 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) 的 Windows x64 版本；仅安装普通 .NET Runtime 不满足 WPF 的运行要求。

### 免安装版

```powershell
dotnet publish src/FrpClient.App/FrpClient.App.csproj -c Release -p:PublishProfile=Portable-win-x64
```

输出目录为 `publish/frpclient-win-x64-portable/`。单文件压缩主要降低解压后程序的体积，ZIP 下载包的大小不一定同比下降；启动时需要解压，启动耗时可能略有增加。

项目也为原有带 `PublishSingleFile=true` 且 `SelfContained=true` 的发布命令启用了压缩。轻量版依赖外部运行时，按 .NET 8 的要求关闭内嵌压缩。

### 分发说明

- 打包整个发布目录，保留 `FrpClient.App.exe` 旁边的 `frp` 文件夹及其中的许可证。
- 发布配置不输出调试符号、不生成额外的 ReadyToRun 代码，并限制运行时语言资源为简体中文、繁体中文和英文。
- WPF 依赖 XAML 加载和运行时绑定，因此两种配置都关闭程序集裁剪。
- 用户配置仍存放在 `%LocalAppData%\FrpClient`，切换发布版本会继续使用已有配置。
