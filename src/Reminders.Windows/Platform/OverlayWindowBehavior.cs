using System;
using System.Windows;
using System.Windows.Interop;

namespace Reminders.Windows.Platform;

internal sealed class OverlayWindowBehavior
{
    private readonly IntPtr handle;
    private bool interactive;

    public OverlayWindowBehavior(Window window)
    {
        handle = new WindowInteropHelper(window).EnsureHandle();
        HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
    }

    public void SetMode(bool allowActivation, bool clickThrough)
    {
        interactive = allowActivation;
        var style = NativeMethods.GetStyle(handle, NativeMethods.GwlExStyle) | NativeMethods.ExToolWindow;
        style = allowActivation ? style & ~NativeMethods.ExNoActivate : style | NativeMethods.ExNoActivate;
        style = clickThrough ? style | NativeMethods.ExTransparent : style & ~NativeMethods.ExTransparent;
        NativeMethods.SetStyle(handle, NativeMethods.GwlExStyle, style);
        NativeMethods.SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.NoActivate | NativeMethods.NoZOrder | NativeMethods.FrameChanged | 0x0001 | 0x0002);
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0021 && !interactive) // WM_MOUSEACTIVATE
        {
            handled = true;
            return new IntPtr(3); // MA_NOACTIVATE：保留点击，但不切走游戏/编辑器焦点。
        }
        return IntPtr.Zero;
    }
}
