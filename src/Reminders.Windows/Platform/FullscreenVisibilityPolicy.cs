using System;

namespace Reminders.Windows.Platform;

internal readonly record struct PixelBounds(int Left, int Top, int Right, int Bottom);

internal static class FullscreenVisibilityPolicy
{
    internal static bool ShouldHide(int? notificationState, bool excludedForeground,
        IntPtr foregroundMonitor, IntPtr overlayMonitor, bool coversMonitor)
    {
        // QUNS_NOT_PRESENT 表示锁屏或会话不可用，不依赖此时可能已失效的前台窗口。
        if (notificationState == 1) return true;
        if (excludedForeground || foregroundMonitor == IntPtr.Zero || overlayMonitor == IntPtr.Zero
            || foregroundMonitor != overlayMonitor) return false;

        // 系统通知是全局信号，独占/演示状态必须先归属到当前前台窗口所在屏。
        // QUNS_BUSY 本身不足以证明全屏，仍需窗口实际覆盖显示器。
        return notificationState is 3 or 4 || coversMonitor;
    }

    internal static bool CoversMonitor(PixelBounds window, PixelBounds monitor) =>
        window.Right > window.Left && window.Bottom > window.Top
        && monitor.Right > monitor.Left && monitor.Bottom > monitor.Top
        && (long)window.Left <= (long)monitor.Left + 1 && (long)window.Top <= (long)monitor.Top + 1
        && (long)window.Right >= (long)monitor.Right - 1 && (long)window.Bottom >= (long)monitor.Bottom - 1;
}
