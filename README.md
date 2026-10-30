# frp-client

一个基于 WPF 开发的 Windows frp 图形客户端，用来更方便地配置和运行 `frpc`。

项目把常用的 frpc 配置操作做成了图形界面，用户可以直接在客户端里修改服务器地址、认证密钥和端口转发规则，也可以启动、停止 frpc 服务并查看运行日志。

## 功能

- 图形化管理 frpc 配置文件
- 添加、删除和查看端口转发规则
- 支持 TCP / UDP 转发规则
- 支持 STCP：可添加 stcp 服务端（`[[proxies]]`）和 stcp 访问端（`[[visitors]]`，含监听端口 bindPort）
- 支持编辑选中的转发规则
- 版本检测：检测当前 frpc 是否支持 STCP，不支持时禁用相关选项并阻止启动
- 修改服务器 IP、服务器端口和认证密钥
- 启动和停止 frpc 服务
- 查看 frpc 运行日志
- 显示当前本地 frpc 版本
- 从 GitHub Releases 获取 frp 可下载版本
- 支持按系统架构筛选 frpc 下载包
- 支持下载并安装匹配当前 Windows 架构的 frpc
- 支持多个配置方案，可新建、复制、重命名和删除配置
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
│           ├── FrpVersion.cs      # frp 版本号解析与比较
│           ├── FrpFeatureSupport.cs # 版本检测、STCP 支持判断
│           ├── FrpConfigSerializer.cs # frpc.toml 读写
│           ├── FrpProxyConfig.cs  # 转发规则模型（tcp/udp/stcp）
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
%LocalAppData%\FrpClient
```

这样做的原因是发布目录可能没有写入权限，而配置文件和下载的 frpc 版本需要允许用户修改。

在客户端顶部可以点击“打开目录”快速进入这个文件夹。

多配置功能启用后，目录大致如下：

```text
%LocalAppData%\FrpClient\
├── frp\
│   ├── frpc.exe
│   ├── LICENSE
│   └── downloads\
└── profiles\
    ├── profiles.json
    ├── default\
    │   └── frpc.toml
    └── profile-2\
        └── frpc.toml
```

`frp/frpc.exe` 是所有配置共用的 frpc 程序；`profiles/*/frpc.toml` 是每个配置方案自己的配置文件。

## 使用 STCP

STCP（Secret TCP）需要“服务端”和“访问端”两端配合，两端的密钥必须相同：

1. 在提供服务的机器上添加类型为 `stcp 服务端` 的规则，填写名称、密钥、本地 IP 和本地端口。
2. 在需要访问的机器上添加类型为 `stcp 访问端` 的规则，填写：
   - 服务器名称：服务端那条 stcp 规则的 `name`（不是服务器 IP），两端名称必须对应
   - 密钥：与服务端一致
   - 监听地址 / 监听端口（bindPort）：访问端在本机监听的地址和端口
3. 连接访问端的 `监听地址:监听端口`，即可访问到服务端的本地服务。

生成的配置大致如下：

```toml
# 服务端
[[proxies]]
name = "secret_web"
type = "stcp"
secretKey = "abc123"
localIP = "127.0.0.1"
localPort = 8080

# 访问端
[[visitors]]
name = "secret_web_visitor"
type = "stcp"
serverName = "secret_web"
secretKey = "abc123"
bindAddr = "127.0.0.1"
bindPort = 9000
```

## 版本检测

配置页“已有转发规则”标题旁边会显示当前 frpc 的 STCP 支持状态（例如“STCP 可用 · frpc 0.62.1”），鼠标悬停可以看到完整检测结果；点击“检测版本”可以重新检测。检测分两步：

- 比较 `frpc --version` 输出：STCP 需要 frpc 0.22.0 及以上；本客户端使用 TOML 配置，实际需要 0.52.0 及以上。
- 在 0.52.0 及以上版本上，再调用 `frpc verify` 实际校验一段 STCP 配置，避免版本号判断过时导致误判。

当版本不支持时：

- 配置页的 STCP 选项会变灰，无法新增 STCP 规则；
- 启动包含 STCP 规则的配置会被拦截，并提示先升级 frpc；
- frpc 低于 0.52.0 时，启动任何规则都会被拦截（旧版本无法读取 TOML 配置）。

升级 frpc 后，回到配置页点击“检测版本”即可恢复。

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
- 转发规则支持 tcp / udp / stcp；访问端目前只支持 stcp 类型，如果 `frpc.toml` 里存在 xtcp / sudp 的 `[[visitors]]`，保存时会被忽略，并在日志中给出提示。

## 相关项目

- [fatedier/frp](https://github.com/fatedier/frp)
