using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using Reminders.Windows.Platform;

namespace Reminders.Windows.Overlay;

internal static class CompletionRegion
{
    private const int RegionUnion = 2;
    internal static void Apply(IntPtr handle, IReadOnlyList<Rect> rectangles)
    {
        var combined = NativeMethods.CreateRectRgn(0, 0, 0, 0);
        if (combined == IntPtr.Zero) throw new InvalidOperationException("无法创建勾选命中区域。");
        try
        {
            foreach (var rectangle in rectangles)
            {
                var part = NativeMethods.CreateRectRgn((int)Math.Floor(rectangle.Left), (int)Math.Floor(rectangle.Top),
                    (int)Math.Ceiling(rectangle.Right), (int)Math.Ceiling(rectangle.Bottom));
                if (part == IntPtr.Zero) throw new InvalidOperationException("无法创建任务命中区域。");
                try
                {
                    if (NativeMethods.CombineRgn(combined, combined, part, RegionUnion) == 0)
                        throw new InvalidOperationException("无法合并任务命中区域。");
                }
                finally { NativeMethods.DeleteObject(part); }
            }
            if (NativeMethods.SetWindowRgn(handle, combined, true) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法设置勾选命中区域。");
            // 成功后 HRGN 归系统所有；失败分支仍由本方法释放。
            combined = IntPtr.Zero;
        }
        finally { if (combined != IntPtr.Zero) NativeMethods.DeleteObject(combined); }
    }
}
