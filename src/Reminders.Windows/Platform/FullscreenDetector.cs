using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Reminders.Windows.Platform;

internal sealed class FullscreenDetector(int? editorProcessId = null)
{
    public bool ShouldHide()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || NativeMethods.IsIconic(hwnd)) return false;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == Environment.ProcessId || processId == editorProcessId) return false;
        // 先排除编辑器与自身，再应用系统全屏通知，编辑窗口不应隐藏可调整的 Overlay。
        if (NativeMethods.SHQueryUserNotificationState(out var state) == 0 && state is 1 or 3 or 4)
            return true;
        var className = new StringBuilder(256);
        NativeMethods.GetClassName(hwnd, className, className.Capacity);
        if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            return false;

        if (NativeMethods.DwmGetWindowAttribute(hwnd, 9, out var bounds, Marshal.SizeOf<NativeMethods.Rect>()) != 0
            && !NativeMethods.GetWindowRect(hwnd, out bounds)) return false;
        var monitor = NativeMethods.GetMonitor(hwnd).Monitor;
        // 使用 DWM 可见边界，避免最大化窗口的不可见 resize 边框覆盖任务栏。
        return bounds.Width > 0 && bounds.Height > 0
            && bounds.Left <= monitor.Left + 1 && bounds.Top <= monitor.Top + 1
            && bounds.Right >= monitor.Right - 1 && bounds.Bottom >= monitor.Bottom - 1;
    }
}
