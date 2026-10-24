using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Reminders.Windows.Platform;

namespace Reminders.Windows.Overlay;

internal sealed record CompletionTarget(Guid TaskId, Rect Bounds, CompletionButton Visual);

/// <summary>所有任务共用一个输入 HWND；可见图形始终由 OverlayWindow 一次绘制。</summary>
internal sealed class CompletionInputWindow : Window
{
    private readonly OverlayWindow visual;
    private readonly Action<Guid> complete;
    private readonly Canvas canvas = new() { Background = Brushes.Transparent };
    private readonly Dictionary<Guid, Button> buttons = new();
    private IReadOnlyList<CompletionTarget> targets = Array.Empty<CompletionTarget>();
    private readonly Dictionary<Guid, CompletionTarget> byId = new();
    private NativeMethods.Rect previousBounds;
    private DpiScale previousVisualDpi, previousInputDpi;
    private bool geometryValid, allowClose;

    internal CompletionInputWindow(OverlayWindow visual, Action<Guid> complete)
    {
        this.visual = visual;
        this.complete = complete;
        new WindowInteropHelper(visual).EnsureHandle();
        Owner = visual;
        Title = "Reminders Completion Input";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Content = canvas;
        SourceInitialized += (_, _) => new OverlayWindowBehavior(this).SetMode(false, false);
        Closing += (_, args) => args.Cancel = !allowClose;
    }

    internal void SetTargets(IReadOnlyList<CompletionTarget> next)
    {
        ResetInteraction();
        targets = next;
        byId.Clear();
        foreach (var target in next) byId.Add(target.TaskId, target);
        foreach (var id in buttons.Keys.Where(id => !byId.ContainsKey(id)).ToArray())
        {
            canvas.Children.Remove(buttons[id]);
            buttons.Remove(id);
        }
        foreach (var target in next)
        {
            if (!buttons.TryGetValue(target.TaskId, out var button))
            {
                button = CreateButton(target.TaskId);
                buttons.Add(target.TaskId, button);
                canvas.Children.Add(button);
            }
            AutomationProperties.SetName(button, AutomationProperties.GetName(target.Visual));
        }
        PlaceButtons(1, 1);
        geometryValid = false;
    }

    private Button CreateButton(Guid id)
    {
        var button = new Button { Margin = new Thickness(0), Padding = new Thickness(0) };
        button.SetResourceReference(StyleProperty, "OverlayHitButtonStyle");
        void Hover(bool value) { if (byId.TryGetValue(id, out var target)) target.Visual.IsPreviewHovered = value; }
        void Press(bool value) { if (byId.TryGetValue(id, out var target)) target.Visual.IsPreviewPressed = value; }
        button.MouseEnter += (_, _) => Hover(true);
        button.MouseLeave += (_, _) => { Hover(false); Press(false); };
        button.PreviewMouseLeftButtonDown += (_, _) => Press(true);
        button.PreviewMouseLeftButtonUp += (_, _) => Press(false);
        button.LostMouseCapture += (_, _) => Press(false);
        button.Click += (_, _) => complete(id);
        return button;
    }

    internal void Present()
    {
        if (targets.Count == 0) { Conceal(); return; }
        if (Topmost != visual.Topmost) Topmost = visual.Topmost;
        if (!IsVisible)
        {
            Left = visual.Left;
            Top = visual.Top;
            Width = visual.ActualWidth;
            Height = visual.ActualHeight;
            Show();
            geometryValid = false;
        }
        SynchronizeGeometry();
    }

    internal void SynchronizeGeometry()
    {
        if (!IsVisible) return;
        if (!NativeMethods.GetWindowRect(new WindowInteropHelper(visual).Handle, out var bounds))
            throw new InvalidOperationException("无法读取 Overlay 的实际位置。");
        var visualDpi = VisualTreeHelper.GetDpi(visual);
        var inputDpi = VisualTreeHelper.GetDpi(this);
        if (geometryValid && previousBounds.Equals(bounds) && previousVisualDpi.Equals(visualDpi) && previousInputDpi.Equals(inputDpi)) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (!NativeMethods.SetWindowPos(handle, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            NativeMethods.NoActivate | NativeMethods.NoZOrder))
            throw new InvalidOperationException("无法对齐 Overlay 勾选区域。");

        inputDpi = VisualTreeHelper.GetDpi(this);
        // 两个 HWND 跨屏时 DPI 通知可能先后到达，按各自比例校准输入坐标。
        PlaceButtons(visualDpi.DpiScaleX / inputDpi.DpiScaleX, visualDpi.DpiScaleY / inputDpi.DpiScaleY);
        // 首次显示或 DPI 改变后，先提交控件布局再发布原生命中区。
        canvas.UpdateLayout();
        var frame = new Rect(0, 0, bounds.Width, bounds.Height);
        var rectangles = targets.Select(target => Rect.Intersect(frame, new Rect(
            target.Bounds.X * visualDpi.DpiScaleX, target.Bounds.Y * visualDpi.DpiScaleY,
            target.Bounds.Width * visualDpi.DpiScaleX, target.Bounds.Height * visualDpi.DpiScaleY)))
            .Where(rectangle => !rectangle.IsEmpty && rectangle.Width > 0 && rectangle.Height > 0).ToArray();
        CompletionRegion.Apply(handle, rectangles);
        previousBounds = bounds;
        previousVisualDpi = visualDpi;
        previousInputDpi = inputDpi;
        geometryValid = true;
    }

    private void PlaceButtons(double scaleX, double scaleY)
    {
        foreach (var target in targets)
        {
            var button = buttons[target.TaskId];
            Canvas.SetLeft(button, target.Bounds.Left * scaleX);
            Canvas.SetTop(button, target.Bounds.Top * scaleY);
            button.Width = target.Bounds.Width * scaleX;
            button.Height = target.Bounds.Height * scaleY;
        }
    }

    private void ResetInteraction()
    {
        foreach (var target in targets) target.Visual.IsPreviewHovered = target.Visual.IsPreviewPressed = false;
    }

    internal void Conceal() { ResetInteraction(); Hide(); }
    internal void DisposeWindow() { allowClose = true; Close(); }
}
