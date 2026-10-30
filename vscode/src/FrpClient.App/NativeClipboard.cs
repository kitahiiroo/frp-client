using System.ComponentModel; // 提供 Win32Exception，用来展示系统剪贴板错误
using System.Runtime.InteropServices; // 提供 DllImport、Marshal，用来调用 Win32 剪贴板 API
using System.Text; // 提供 Unicode 编码，用来写入 CF_UNICODETEXT 格式

namespace FrpClient.App;

/// <summary>
/// 用 Win32 API 写入 Windows 剪贴板，避免 WPF Clipboard 封装在部分环境下无法打开剪贴板。
/// </summary>
internal static class NativeClipboard
{
    private const uint CfUnicodeText = 13; // Windows 剪贴板里的 Unicode 文本格式
    private const uint GmemMoveable = 0x0002; // SetClipboardData 要求传入可移动内存块
    private const uint GmemZeroInit = 0x0040; // 分配后清零，保证末尾空字符安全

    /// <summary>
    /// 将文本写入剪贴板；如果剪贴板被其他程序短暂占用，会按指定次数重试。
    /// </summary>
    public static async Task SetTextWithRetryAsync(string text, int retryCount, int retryDelayMilliseconds)
    {
        Exception? lastException = null;

        for (var attempt = 1; attempt <= retryCount; attempt++)
        {
            try
            {
                SetText(text);
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;

                if (attempt < retryCount)
                {
                    await Task.Delay(retryDelayMilliseconds);
                }
            }
        }

        throw lastException ?? new InvalidOperationException("写入剪贴板失败。");
    }

    /// <summary>
    /// 直接调用 Win32 API 写入 Unicode 文本到系统剪贴板。
    /// </summary>
    private static void SetText(string text)
    {
        var clipboardOpened = false;
        var globalTextHandle = IntPtr.Zero;

        try
        {
            if (!OpenClipboard(IntPtr.Zero))
            {
                throw CreateLastWin32Exception("无法打开剪贴板。");
            }

            clipboardOpened = true;

            if (!EmptyClipboard())
            {
                throw CreateLastWin32Exception("无法清空剪贴板。");
            }

            var textBytes = Encoding.Unicode.GetBytes(text + '\0');
            globalTextHandle = GlobalAlloc(GmemMoveable | GmemZeroInit, (UIntPtr)textBytes.Length);

            if (globalTextHandle == IntPtr.Zero)
            {
                throw CreateLastWin32Exception("无法分配剪贴板内存。");
            }

            var lockedMemory = GlobalLock(globalTextHandle);

            if (lockedMemory == IntPtr.Zero)
            {
                throw CreateLastWin32Exception("无法锁定剪贴板内存。");
            }

            try
            {
                Marshal.Copy(textBytes, 0, lockedMemory, textBytes.Length);
            }
            finally
            {
                GlobalUnlock(globalTextHandle);
            }

            if (SetClipboardData(CfUnicodeText, globalTextHandle) == IntPtr.Zero)
            {
                throw CreateLastWin32Exception("无法写入剪贴板。");
            }

            globalTextHandle = IntPtr.Zero; // 写入成功后，内存所有权交给系统剪贴板
        }
        finally
        {
            if (clipboardOpened)
            {
                CloseClipboard();
            }

            if (globalTextHandle != IntPtr.Zero)
            {
                GlobalFree(globalTextHandle);
            }
        }
    }

    /// <summary>
    /// 根据 Win32 最后的错误码生成带上下文的异常。
    /// </summary>
    private static Win32Exception CreateLastWin32Exception(string fallbackMessage)
    {
        var errorCode = Marshal.GetLastWin32Error();
        return errorCode == 0 ? new Win32Exception(fallbackMessage) : new Win32Exception(errorCode, fallbackMessage);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);
}
