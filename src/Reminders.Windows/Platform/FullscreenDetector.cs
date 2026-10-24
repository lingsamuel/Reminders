using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Reminders.Windows.Platform;

internal sealed class FullscreenDetector(int? editorProcessId = null)
{
    public bool ShouldHide(IntPtr overlayHandle)
    {
        int? notificationState = NativeMethods.SHQueryUserNotificationState(out var state) == 0 ? state : null;
        var hwnd = NativeMethods.GetForegroundWindow();
        var excludedForeground = IsExcludedForeground(hwnd);
        // MonitorFromWindow 按窗口与显示器的最大相交面积归属；2 为 MONITOR_DEFAULTTONEAREST。
        var foregroundMonitor = excludedForeground ? IntPtr.Zero : NativeMethods.MonitorFromWindow(hwnd, 2);
        var overlayMonitor = overlayHandle == IntPtr.Zero ? IntPtr.Zero : NativeMethods.MonitorFromWindow(overlayHandle, 2);
        var coversMonitor = foregroundMonitor != IntPtr.Zero && foregroundMonitor == overlayMonitor
            && CoversMonitor(hwnd, foregroundMonitor);
        return FullscreenVisibilityPolicy.ShouldHide(notificationState, excludedForeground,
            foregroundMonitor, overlayMonitor, coversMonitor);
    }

    private bool IsExcludedForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || NativeMethods.IsIconic(hwnd)) return true;
        if (NativeMethods.GetWindowThreadProcessId(hwnd, out var processId) == 0 || processId == 0
            || processId == Environment.ProcessId || processId == editorProcessId) return true;
        var className = new StringBuilder(256);
        if (NativeMethods.GetClassName(hwnd, className, className.Capacity) == 0) return true;
        return className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    private static bool CoversMonitor(IntPtr hwnd, IntPtr monitor)
    {
        if (NativeMethods.DwmGetWindowAttribute(hwnd, 9, out var bounds, Marshal.SizeOf<NativeMethods.Rect>()) != 0
            && !NativeMethods.GetWindowRect(hwnd, out bounds)) return false;
        var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info)) return false;
        // 使用 DWM 可见边界，避免最大化窗口的不可见 resize 边框覆盖任务栏。
        return FullscreenVisibilityPolicy.CoversMonitor(ToPixelBounds(bounds), ToPixelBounds(info.Monitor));
    }

    private static PixelBounds ToPixelBounds(NativeMethods.Rect bounds) =>
        new(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
}
