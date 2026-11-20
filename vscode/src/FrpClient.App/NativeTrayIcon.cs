using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace FrpClient.App;

/// <summary>使用 Windows 原生托盘图标和菜单，不引入额外的界面框架或依赖。</summary>
internal sealed class NativeTrayIcon : IDisposable
{
    private const int CallbackMessage = 0x8001;
    private const int WmContextMenu = 0x007B;
    private const int WmLeftButtonDoubleClick = 0x0203;
    private const int WmRightButtonUp = 0x0205;
    private const int NinKeySelect = 0x0401;
    private const uint NimAdd = 0;
    private const uint NimModify = 1;
    private const uint NimDelete = 2;
    private const uint NimSetFocus = 3;
    private const uint NimSetVersion = 4;
    private const uint MfGrayed = 0x0001;
    private const uint MfSeparator = 0x0800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmNoNotify = 0x0080;
    private const uint TpmReturnCommand = 0x0100;
    private const uint ShowWindowCommand = 1;
    private const uint StartServiceCommand = 2;
    private const uint StopServiceCommand = 3;
    private const uint ExitCommand = 4;

    private readonly HwndSource _windowSource;
    private readonly uint _taskbarCreatedMessage;
    private readonly IntPtr _iconHandle;
    private readonly bool _ownsIcon;
    private NotifyIconData _iconData;
    private bool _usesVersion4;
    private bool _isDisposed;
    private bool _isRunning;
    private bool _isBusy;
    private string _profileName = "默认配置";

    /// <summary>创建托盘图标，并接收所属窗口的 Windows 消息。</summary>
    public NativeTrayIcon(IntPtr windowHandle)
    {
        _windowSource = HwndSource.FromHwnd(windowHandle)
            ?? throw new InvalidOperationException("无法获取托盘图标所属窗口。");
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

        var iconHandle = IntPtr.Zero;
        if (Environment.ProcessPath is { } executablePath)
        {
            ExtractIconEx(executablePath, 0, IntPtr.Zero, out iconHandle, 1);
        }

        _ownsIcon = iconHandle != IntPtr.Zero;
        _iconHandle = _ownsIcon ? iconHandle : LoadIcon(IntPtr.Zero, new IntPtr(32512));
        _iconData = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            WindowHandle = windowHandle,
            Id = 1,
            Flags = 0x0001 | 0x0002 | 0x0004 | 0x0080, // 消息、图标、提示文字和标准悬浮提示
            CallbackMessage = CallbackMessage,
            IconHandle = _iconHandle,
            Tip = "frp 客户端 · 服务未运行",
            Info = "",
            InfoTitle = ""
        };

        _windowSource.AddHook(WindowMessageHook);
        try
        {
            if (_iconHandle == IntPtr.Zero || !AddIcon())
            {
                throw new Win32Exception("无法创建 Windows 托盘图标。");
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>表示托盘图标是否成功注册，避免无图标时隐藏窗口。</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>用户请求打开主窗口。</summary>
    public event EventHandler? ShowRequested;

    /// <summary>用户请求启动当前配置的服务。</summary>
    public event EventHandler? StartRequested;

    /// <summary>用户请求停止服务。</summary>
    public event EventHandler? StopRequested;

    /// <summary>用户请求彻底退出客户端。</summary>
    public event EventHandler? ExitRequested;

    /// <summary>同步服务、下载和配置状态，菜单禁用条件与窗口内按钮保持一致。</summary>
    public void UpdateState(bool isRunning, bool isBusy, string profileName)
    {
        if (_isDisposed || (IsAvailable && _isRunning == isRunning && _isBusy == isBusy && _profileName == profileName))
        {
            return;
        }

        _isRunning = isRunning;
        _isBusy = isBusy;
        _profileName = profileName;
        var status = isBusy ? "正在下载 frpc" : isRunning ? "服务运行中" : "服务未运行";
        _iconData.Tip = $"frp 客户端 · {status}";
        IsAvailable = ShellNotifyIcon(NimModify, ref _iconData);
        if (!IsAvailable)
        {
            if (!AddIcon())
            {
                QueueCommand(ShowWindowCommand); // 托盘不可用时恢复窗口，避免客户端隐藏后无法控制
            }
        }
    }

    /// <summary>注册或在资源管理器重启后恢复图标，并启用现代托盘交互。</summary>
    private bool AddIcon()
    {
        IsAvailable = ShellNotifyIcon(NimAdd, ref _iconData);
        if (IsAvailable)
        {
            _iconData.TimeoutOrVersion = 4;
            _usesVersion4 = ShellNotifyIcon(NimSetVersion, ref _iconData);
        }

        return IsAvailable;
    }

    /// <summary>处理托盘鼠标、键盘事件，以及资源管理器重启通知。</summary>
    private IntPtr WindowMessageHook(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_isDisposed)
        {
            return IntPtr.Zero;
        }

        if (_taskbarCreatedMessage != 0 && (uint)message == _taskbarCreatedMessage)
        {
            if (!AddIcon())
            {
                QueueCommand(ShowWindowCommand);
            }
            return IntPtr.Zero;
        }

        if (message != CallbackMessage)
        {
            return IntPtr.Zero;
        }

        handled = true;
        var trayEvent = _usesVersion4 ? (int)(lParam.ToInt64() & 0xFFFF) : lParam.ToInt32();
        if (trayEvent is WmLeftButtonDoubleClick or NinKeySelect)
        {
            QueueCommand(ShowWindowCommand);
        }
        else if (trayEvent == WmContextMenu || (!_usesVersion4 && trayEvent == WmRightButtonUp))
        {
            ScreenPoint position;
            if (_usesVersion4)
            {
                var coordinates = wParam.ToInt64();
                position = new ScreenPoint
                {
                    X = (short)(coordinates & 0xFFFF),
                    Y = (short)((coordinates >> 16) & 0xFFFF)
                };
            }
            else
            {
                GetCursorPos(out position);
            }

            ShowContextMenu(position);
        }

        return IntPtr.Zero;
    }

    /// <summary>显示原生右键菜单，使用菜单返回值分发操作并及时释放菜单句柄。</summary>
    private void ShowContextMenu(ScreenPoint position)
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            QueueCommand(ShowWindowCommand);
            return;
        }

        try
        {
            AppendMenu(menu, MfGrayed, UIntPtr.Zero, $"配置：{_profileName.Replace("&", "&&")}");
            AppendMenu(menu, MfGrayed, UIntPtr.Zero, _isBusy ? "正在下载 frpc…" : _isRunning ? "服务运行中" : "服务未运行");
            AppendMenu(menu, MfSeparator, UIntPtr.Zero, null);
            AppendMenu(menu, 0, new UIntPtr(ShowWindowCommand), "显示主窗口");
            AppendMenu(menu, _isRunning || _isBusy ? MfGrayed : 0, new UIntPtr(StartServiceCommand), "启动服务");
            AppendMenu(menu, !_isRunning || _isBusy ? MfGrayed : 0, new UIntPtr(StopServiceCommand), "停止服务");
            AppendMenu(menu, MfSeparator, UIntPtr.Zero, null);
            AppendMenu(menu, 0, new UIntPtr(ExitCommand), "退出程序（停止服务）");

            SetForegroundWindow(_windowSource.Handle);
            var command = TrackPopupMenuEx(menu, TpmRightButton | TpmNoNotify | TpmReturnCommand,
                position.X, position.Y, _windowSource.Handle, IntPtr.Zero);
            ShellNotifyIcon(NimSetFocus, ref _iconData); // 将键盘焦点交回托盘，支持继续通过键盘操作
            PostMessage(_windowSource.Handle, 0, IntPtr.Zero, IntPtr.Zero); // 让点击菜单外部能正常关闭菜单
            QueueCommand(command);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    /// <summary>返回 Windows 消息循环后再执行操作，避免关闭窗口时重入消息钩子。</summary>
    private void QueueCommand(uint command)
    {
        if (command == 0 || _windowSource.Dispatcher.HasShutdownStarted)
        {
            return;
        }

        _windowSource.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_isDisposed)
            {
                return;
            }

            switch (command)
            {
                case ShowWindowCommand:
                    ShowRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case StartServiceCommand:
                    StartRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case StopServiceCommand:
                    StopRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case ExitCommand:
                    ExitRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }));
    }

    /// <summary>移除托盘图标和消息钩子，释放应用持有的图标资源。</summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        ShellNotifyIcon(NimDelete, ref _iconData);
        IsAvailable = false;
        if (!_windowSource.IsDisposed)
        {
            _windowSource.RemoveHook(WindowMessageHook);
        }

        if (_ownsIcon)
        {
            DestroyIcon(_iconHandle);
        }
    }

    /// <summary>Windows 托盘图标数据；字符串长度和字段顺序必须与系统结构一致。</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr WindowHandle;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr IconHandle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid GuidItem;
        public IntPtr BalloonIconHandle;
    }

    /// <summary>以屏幕像素表示菜单坐标，保留负数以支持左侧或上方的显示器。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenPoint
    {
        public int X;
        public int Y;
    }

    /// <summary>向系统添加、更新或移除托盘图标。</summary>
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    /// <summary>从当前应用程序提取一个小图标，返回的句柄由应用释放。</summary>
    [DllImport("shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string fileName, int iconIndex, IntPtr largeIcons, out IntPtr smallIcon, uint iconCount);

    /// <summary>读取系统默认图标，作为无法提取应用图标时的后备。</summary>
    [DllImport("user32.dll", EntryPoint = "LoadIconW", ExactSpelling = true)]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

    /// <summary>释放提取出来的应用图标。</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    /// <summary>注册资源管理器重建任务栏时广播的消息。</summary>
    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    /// <summary>创建右键弹出菜单。</summary>
    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    /// <summary>向菜单追加命令、状态文字或分隔线。</summary>
    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string? text);

    /// <summary>弹出菜单并返回被选中的命令编号。</summary>
    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr parameters);

    /// <summary>销毁弹出菜单及其中的菜单项。</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    /// <summary>使托盘所属窗口成为菜单前台窗口，保证鼠标和键盘关闭行为正常。</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    /// <summary>在菜单关闭后向窗口投递空消息。</summary>
    [DllImport("user32.dll", EntryPoint = "PostMessageW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    /// <summary>取得旧版托盘回调需要的鼠标屏幕位置。</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out ScreenPoint position);
}
