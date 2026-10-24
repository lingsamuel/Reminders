using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Reminders.Windows.Platform;

namespace Reminders.Windows.Overlay;

internal sealed class CompletionWindow : Window
{
    public CompletionWindow(Window owner, string title, Action complete)
    {
        Title = $"Reminders · 完成 {title}";
        Owner = owner;
        Width = Height = 20;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        var button = new Button
        {
            Content = "", Padding = new Thickness(0), Margin = new Thickness(0),
            Background = new SolidColorBrush(Color.FromArgb(205, 24, 28, 35)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(145, 213, 195)),
            BorderThickness = new Thickness(1), Focusable = false,
            Cursor = System.Windows.Input.Cursors.Hand, ToolTip = $"完成：{title}"
        };
        AutomationProperties.SetName(button, $"完成 {title}");
        button.Click += (_, _) => complete();
        Content = button;
        SourceInitialized += (_, _) => new OverlayWindowBehavior(this).SetMode(false, false);
    }

    public void Align(FrameworkElement target)
    {
        var start = target.PointToScreen(new Point(0, 0));
        var end = target.PointToScreen(new Point(target.ActualWidth, target.ActualHeight));
        // PointToScreen 给出物理像素，直接传给 Win32，避免跨屏 DPI 缩放后勾选框漂移。
        NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero,
            (int)Math.Round(start.X), (int)Math.Round(start.Y),
            Math.Max(1, (int)Math.Round(end.X - start.X)), Math.Max(1, (int)Math.Round(end.Y - start.Y)),
            NativeMethods.NoActivate | NativeMethods.NoZOrder);
    }
}
