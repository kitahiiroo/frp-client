using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;

namespace FrpClient.App;

/// <summary>主窗口的托盘交互、后台隐藏和退出清理逻辑。</summary>
public partial class MainWindow
{
    private NativeTrayIcon? _trayIcon;
    private bool _isExiting;
    private WindowState _lastVisibleWindowState = WindowState.Normal;

    /// <summary>窗口句柄就绪后创建托盘，复用主窗口的 Windows 消息循环。</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Application.Current.SessionEnding += Application_SessionEnding;

        try
        {
            _trayIcon = new NativeTrayIcon(new WindowInteropHelper(this).Handle);
            _trayIcon.ShowRequested += Tray_ShowRequested;
            _trayIcon.StartRequested += Tray_StartRequested;
            _trayIcon.StopRequested += Tray_StopRequested;
            _trayIcon.ExitRequested += Tray_ExitRequested;
            UpdateTrayState();
            AppendLog("关闭窗口或最小化后将在后台运行，可通过右下角托盘菜单控制服务和退出程序。");
        }
        catch (Exception ex)
        {
            _trayIcon?.Dispose();
            _trayIcon = null;
            AppendLog($"托盘图标初始化失败，将保持普通窗口模式：{ex.Message}");
        }
    }

    /// <summary>点击关闭或按 Alt+F4 时隐藏到托盘，明确退出和系统关机时正常关闭。</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!_isExiting && !Dispatcher.HasShutdownStarted && _trayIcon is { IsAvailable: true })
        {
            e.Cancel = true;
            HideToTray();
        }
    }

    /// <summary>最小化时收起到托盘，并记住正常或最大化的窗口状态。</summary>
    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
        {
            HideToTray();
        }
        else
        {
            _lastVisibleWindowState = WindowState;
        }
    }

    /// <summary>只有真正关闭客户端时才停止服务，并释放进程和托盘资源。</summary>
    protected override void OnClosed(EventArgs e)
    {
        _isExiting = true;
        Application.Current.SessionEnding -= Application_SessionEnding;
        _trayIcon?.Dispose();
        _trayIcon = null;
        StopFrpcProcess();
        _frpcProcess?.Dispose();
        _frpcProcess = null;
        base.OnClosed(e);
    }

    /// <summary>用户主动点击“后台运行”时隐藏窗口。</summary>
    private void RunInBackgroundButton_Click(object sender, RoutedEventArgs e)
    {
        HideToTray();
    }

    /// <summary>托盘可用时收起窗口，隐藏窗口不会停止 frpc。</summary>
    private void HideToTray()
    {
        if (_isExiting || !IsVisible || _trayIcon is not { IsAvailable: true })
        {
            return;
        }

        if (WindowState != WindowState.Minimized)
        {
            _lastVisibleWindowState = WindowState;
        }

        Hide();
        AppendLog("客户端已转入后台，服务保持当前状态。");
    }

    /// <summary>显示并激活主窗口，恢复隐藏前的正常或最大化状态。</summary>
    private void ShowMainWindow()
    {
        if (_isExiting)
        {
            return;
        }

        WindowState = _lastVisibleWindowState;
        Show();
        Activate();
    }

    /// <summary>托盘双击或“显示主窗口”菜单打开主界面。</summary>
    private void Tray_ShowRequested(object? sender, EventArgs e)
    {
        ShowMainWindow();
    }

    /// <summary>通过托盘启动当前配置，启动失败时显示窗口以便检查配置和日志。</summary>
    private void Tray_StartRequested(object? sender, EventArgs e)
    {
        if (_isExiting || _isRunning || _isDownloadingFrpc)
        {
            return;
        }

        StartFrpcProcess();
        if (!_isRunning)
        {
            ShowMainWindow();
        }
    }

    /// <summary>通过托盘停止服务，客户端继续保留在后台。</summary>
    private void Tray_StopRequested(object? sender, EventArgs e)
    {
        if (!_isExiting && _isRunning && !_isDownloadingFrpc)
        {
            StopFrpcProcess();
        }
    }

    /// <summary>用户明确选择退出后允许关闭窗口，随后统一停止服务和释放资源。</summary>
    private void Tray_ExitRequested(object? sender, EventArgs e)
    {
        _isExiting = true;
        Close();
    }

    /// <summary>同步托盘状态，配置切换、服务退出和下载状态变化时均会调用。</summary>
    private void UpdateTrayState()
    {
        _trayIcon?.UpdateState(_isRunning, _isDownloadingFrpc, _activeProfile.Name);
        RunInBackgroundButton.IsEnabled = _trayIcon is { IsAvailable: true };
    }

    /// <summary>Windows 注销或关机时允许正常退出，避免被“关闭到托盘”拦截。</summary>
    private void Application_SessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        _isExiting = true;
    }
}
