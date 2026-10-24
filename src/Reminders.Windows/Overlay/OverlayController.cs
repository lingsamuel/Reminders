using System;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using Reminders.Core;
using Reminders.Windows.Bridge;
using Reminders.Windows.Platform;

namespace Reminders.Windows.Overlay;

internal sealed class OverlayController : IDisposable
{
    private readonly TaskService service;
    private readonly IEditorSession editor;
    private readonly OverlayWindow window = new();
    private readonly CompletionInputWindow input;
    private readonly FullscreenDetector fullscreen;
    private readonly DispatcherTimer timer;
    private string? lastSignature;
    private bool applying, disposed;

    public OverlayController(TaskService service, IEditorSession editor, int? editorProcessId = null)
    {
        this.service = service;
        this.editor = editor;
        fullscreen = new FullscreenDetector(editorProcessId);
        new WindowInteropHelper(window).EnsureHandle();
        input = new CompletionInputWindow(window, Complete);
        service.Changed += StateChanged;
        editor.EditorVisibilityChanged += StateChanged;
        window.CompleteRequested += Complete;
        window.GeometryChanged += GeometryChanged;
        SystemEvents.TimeChanged += SystemChanged;
        SystemEvents.DisplaySettingsChanged += SystemChanged;
        SystemEvents.PowerModeChanged += PowerChanged;
        SystemEvents.UserPreferenceChanged += PreferenceChanged;
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => Refresh(), window.Dispatcher);
        timer.Start();
        Refresh();
    }

    private void StateChanged(object? sender, EventArgs e) => Refresh();
    private void SystemChanged(object? sender, EventArgs e) => RefreshSystem();
    private void PowerChanged(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Resume) RefreshSystem(); }
    private void PreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => RefreshSystem();
    private void RefreshSystem() => window.Dispatcher.BeginInvoke(() =>
    {
        if (disposed) return;
        TimeZoneInfo.ClearCachedData();
        lastSignature = null;
        editor.Refresh();
        Refresh();
    });

    private void Refresh()
    {
        if (disposed || applying) return;
        applying = true;
        try
        {
            var settings = service.State.Overlay;
            var editing = editor.IsVisible;
            var tasks = service.GetTasks().Where(x => x.IsAvailable && !x.IsCompleted).ToArray();
            if (!settings.Visible || (!editing && tasks.Length == 0)) { Conceal(); return; }
            var signature = $"{editing}|{settings}|{string.Join(';', tasks.Select(x => $"{x.Task.Id}:{x.Task.Title}:{x.Period?.Key}:{x.IsOverdue}"))}";
            var changed = signature != lastSignature;
            if (changed)
            {
                input.Conceal();
                window.Topmost = settings.Topmost;
                window.Opacity = settings.Opacity;
                window.Left = settings.Left;
                window.Top = settings.Top;
                window.Width = settings.Width;
                var work = NativeMethods.GetMonitor(new WindowInteropHelper(window).Handle).Work;
                var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
                var layout = OverlayLayout.Calculate(tasks.Length, settings.FontSize, settings.Height, work.Height / dpi.DpiScaleY, editing);
                window.Height = layout.Height;
                window.Render(tasks, editing, settings.FontSize, layout.VisibleRows);
                KeepOnScreen();
                lastSignature = signature;
            }

            // 先应用真实位置再判定屏幕，避免持久化在副屏时按初始主屏 HWND 判错。
            if (fullscreen.ShouldHide(new WindowInteropHelper(window).Handle)) { Conceal(); return; }
            var restored = !window.IsVisible;
            if (restored) window.Show();
            if (changed || restored)
            {
                window.UpdateLayout();
                if (!editing) input.SetTargets(window.GetCompletionTargets());
            }
            if (editing) input.Conceal();
            else input.Present();
        }
        catch (Exception ex)
        {
            // 几何或命中区更新失败时不保留可能错位的输入窗口。
            Conceal();
            lastSignature = null;
            App.ReportError(ex, false);
        }
        finally { applying = false; }
    }

    private void KeepOnScreen()
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var work = NativeMethods.GetMonitor(hwnd).Work;
        if (!NativeMethods.GetWindowRect(hwnd, out var rect)) return;
        var width = Math.Min(rect.Width, work.Width);
        var height = Math.Min(rect.Height, work.Height);
        var x = Math.Clamp(rect.Left, work.Left, Math.Max(work.Left, work.Right - width));
        var y = Math.Clamp(rect.Top, work.Top, Math.Max(work.Top, work.Bottom - height));
        if (!NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, NativeMethods.NoActivate | NativeMethods.NoZOrder))
            throw new InvalidOperationException("无法调整 Overlay 到可见工作区。");
    }

    private void GeometryChanged()
    {
        if (applying || disposed) return;
        lastSignature = null;
        if (!editor.IsVisible) { Refresh(); return; }
        try
        {
            KeepOnScreen();
            service.UpdateSettings(service.State.Overlay with { Left = window.Left, Top = window.Top, Width = window.Width, Height = window.Height });
        }
        catch (Exception ex) { App.ReportError(ex, true); }
    }

    private void Complete(Guid id)
    {
        try { service.Complete(id); }
        catch (Exception ex) { App.ReportError(ex, true); }
    }

    private void Conceal()
    {
        // 隐藏不销毁、不清空行；恢复时所有勾选图形跟随同一视觉窗口显示。
        input.Conceal();
        if (window.IsVisible) window.Hide();
    }

    public void Dispose()
    {
        disposed = true;
        timer.Stop();
        service.Changed -= StateChanged;
        editor.EditorVisibilityChanged -= StateChanged;
        SystemEvents.TimeChanged -= SystemChanged;
        SystemEvents.DisplaySettingsChanged -= SystemChanged;
        SystemEvents.PowerModeChanged -= PowerChanged;
        SystemEvents.UserPreferenceChanged -= PreferenceChanged;
        input.DisposeWindow();
        window.AllowClose = true;
        window.Close();
    }
}
