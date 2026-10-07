using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ElysiaPet.Models;

namespace ElysiaPet.Services;

/// <summary>
/// 窗口层级控制。旧版用 pywin32 的 SetWindowPos 硬改 HWND_TOPMOST，
/// 这里用 WPF 的 Topmost 属性 + 少量 P/Invoke，逻辑更清晰，也不再依赖第三方库。
/// 约定：置顶模式一律让窗口保持 WS_EX_TOOLWINDOW 且不进入 Alt+Tab。
/// </summary>
public static class WindowLevelService
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExAppWindow = 0x00040000;

    private static readonly IntPtr HwndBottom = new(1);
    private static readonly IntPtr HwndTopmost = new(-1);

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    /// <summary>把窗口句柄标记为工具窗口（不出现在 Alt+Tab，符合桌宠的定位）。</summary>
    public static void ApplyToolWindowStyle(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        var exStyle = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        exStyle |= WsExToolWindow;
        exStyle &= ~WsExAppWindow;
        exStyle &= ~WsExNoActivate;
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(exStyle));
    }

    /// <summary>
    /// 推算窗口所在显示器的 DPI 缩放系数（1.0 = 100%）。
    /// 做法是拿窗口的物理宽度除以 WPF 的 DIP 宽度：两个值都来自同一扇窗口，
    /// 因此不受多显示器不同缩放的影响，也不依赖额外的 API 或窗口是否已创建。
    /// </summary>
    public static double GetWindowDpiScale(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect)) return 1.0;

            var physicalWidth = rect.Right - rect.Left;
            var dipWidth = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
            if (physicalWidth <= 0 || dipWidth <= 0) return 1.0;

            var scale = physicalWidth / dipWidth;
            return scale is > 0.5 and < 4.0 ? scale : 1.0;   // 异常值一律按 100% 处理
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return 1.0;
        }
    }

    /// <summary>系统 DPI（LOGPIXELSX，96 = 100% 缩放）。</summary>
    public static int GetSystemDpi()
    {
        try
        {
            var dc = GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return 96;

            try
            {
                var dpi = GetDeviceCaps(dc, LogPixelsX);
                return dpi > 0 ? dpi : 96;
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, dc);
            }
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return 96;
        }
    }

    /// <summary>读取当前光标屏幕坐标（诊断/自检用）。</summary>
    public static bool TryGetCursorPosition(out int x, out int y)
    {
        if (GetCursorPos(out var point))
        {
            x = point.X;
            y = point.Y;
            return true;
        }

        x = 0;
        y = 0;
        return false;
    }

    /// <summary>移动光标到指定屏幕坐标（诊断/自检用）。</summary>
    public static bool TrySetCursorPosition(int x, int y) => SetCursorPos(x, y);

    /// <summary>按配置应用层级：始终置顶 / 非全屏上方 / 桌面层。</summary>
    public static void Apply(Window window, WindowLevel level)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        switch (level)
        {
            case WindowLevel.AlwaysOnTop:
                window.Topmost = true;
                SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0,
                    SwpNoMove | SwpNoSize | SwpNoActivate);
                break;

            case WindowLevel.AboveNormal:
                // 「非全屏应用上方」：保持置顶但允许全屏程序（游戏、播放器）压在它上面。
                // 依赖 Window.Topmost=false + Z 序提升，窗口不会抢焦点。
                window.Topmost = false;
                SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0,
                    SwpNoMove | SwpNoSize | SwpNoActivate);
                break;

            case WindowLevel.DesktopOnly:
                window.Topmost = false;
                SetWindowPos(handle, HwndBottom, 0, 0, 0, 0,
                    SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
                break;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    private const int LogPixelsX = 88;

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int index);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, value) : new IntPtr(SetWindowLong32(hWnd, nIndex, value.ToInt32()));
}
