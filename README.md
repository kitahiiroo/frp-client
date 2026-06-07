# frp-client

一个基于 WPF 开发的 Windows frp 图形客户端，用来更方便地配置和运行 `frpc`。

项目把常用的 frpc 配置操作做成了图形界面，用户可以直接在客户端里修改服务器地址、认证密钥和端口转发规则，也可以启动、停止 frpc 服务并查看运行日志。

## 功能

- 图形化管理 frpc 配置文件
- 添加、删除和查看端口转发规则
- 修改服务器 IP、服务器端口和认证密钥
- 支持 TCP / UDP 转发规则
- 启动和停止 frpc 服务
- 查看 frpc 运行日志
- 显示当前本地 frpc 版本
- 从 GitHub Releases 获取 frp 可下载版本
- 支持按系统架构筛选 frpc 下载包
- 支持下载并安装匹配当前 Windows 架构的 frpc
- 将运行时 frp 文件保存到用户数据目录，避免修改安装目录

## 运行环境

普通用户使用发布包时不需要手动安装 .NET。

如果要从源码运行或开发，需要安装：

- Windows
- .NET 8 SDK
- Visual Studio Code 或 Visual Studio
- C# / C# Dev Kit 插件

## 项目结构

```text
frp-client/
├── frp_0.62.1_windows_amd64/     # 内置 frpc 模板文件
├── vscode/
│   ├── frpclient.sln             # C# 解决方案
│   └── src/
│       └── FrpClient.App/        # WPF 客户端源码
│           ├── Assets/           # 图标资源
│           ├── Resources/        # 全局样式
│           ├── Views/            # 拆分后的界面文件
│           ├── MainWindow.xaml   # 主窗口界面
│           └── MainWindow.xaml.cs # 主窗口逻辑
└── README.md
```

## 从源码运行

进入解决方案目录：

```powershell
cd vscode
```

还原并运行：

```powershell
dotnet run --project src/FrpClient.App/FrpClient.App.csproj
```

也可以在 VS Code 中打开 `vscode` 文件夹，然后使用 C# Dev Kit 运行项目。

## 打包发布

生成 Windows x64 自包含发布包：

```powershell
cd vscode
dotnet publish src/FrpClient.App/FrpClient.App.csproj -c Release -r win-x64 --self-contained true -o publish/frpclient-win-x64 /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:DebugType=None /p:DebugSymbols=false
```

发布目录会生成在：

```text
vscode/publish/frpclient-win-x64/
```

把整个 `frpclient-win-x64` 文件夹压缩后即可发布。用户解压后运行：

```text
FrpClient.App.exe
```

## frp 文件保存位置

程序运行时会把 frpc 和配置文件复制到当前用户的数据目录：

```text
%LocalAppData%\FrpClient\frp
```

这样做的原因是发布目录可能没有写入权限，而配置文件和下载的 frpc 版本需要允许用户修改。

在客户端顶部可以点击“打开目录”快速进入这个文件夹。

## 下载 frpc 版本

客户端会从 fatedier/frp 的 GitHub Releases 读取可下载包。

下载页面会显示不同系统和架构的 frp 包，例如：

- `windows_amd64`
- `windows_arm64`
- `linux_amd64`
- `linux_arm64`
- `darwin_amd64`
- `darwin_arm64`

只有匹配当前 Windows 架构的包会直接安装并替换本机使用的 `frpc.exe`。其它系统或架构的包只会下载到数据目录，不会替换当前客户端，避免下载到无法在本机运行的二进制文件。

## 注意事项

- 当前应用主要面向 Windows 客户端使用。
- 启动 frpc 前，请确认服务器地址、端口、认证密钥和转发规则配置正确。
- 更新 frpc 前需要先停止正在运行的 frpc 服务。
- 发布时不要只复制单独的 `FrpClient.App.exe`，需要保留旁边的 `frp` 文件夹。

## 相关项目

- [fatedier/frp](https://github.com/fatedier/frp)
