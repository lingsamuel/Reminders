using System;
using System.Collections.Generic;
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
    private readonly FullscreenDetector fullscreen;
    private readonly List<CompletionWindow> checkboxes = new();
    private readonly DispatcherTimer timer;
    private string? lastSignature;
    private bool applying, disposed;
    private DateTime nextEditorRefresh = DateTime.MinValue;

    public OverlayController(TaskService service, IEditorSession editor, int? editorProcessId = null)
    {
        this.service = service;
        this.editor = editor;
        fullscreen = new FullscreenDetector(editorProcessId);
        new WindowInteropHelper(window).EnsureHandle();
        service.Changed += StateChanged;
        editor.EditorVisibilityChanged += StateChanged;
        window.CompleteRequested += Complete;
        window.GeometryChanged += SaveGeometry;
        window.LayoutUpdated += (_, _) => AlignCheckboxes();
        SystemEvents.TimeChanged += SystemChanged;
        SystemEvents.DisplaySettingsChanged += SystemChanged;
        SystemEvents.PowerModeChanged += PowerChanged;
        SystemEvents.UserPreferenceChanged += PreferenceChanged;
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => Refresh(), window.Dispatcher);
        timer.Start();
        Refresh();
    }

    private void StateChanged(object? sender, EventArgs e) { lastSignature = null; Refresh(); }
    private void SystemChanged(object? sender, EventArgs e) => RefreshSystem();
    private void PowerChanged(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Resume) RefreshSystem(); }
    private void PreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => RefreshSystem();
    private void RefreshSystem() => window.Dispatcher.BeginInvoke(() =>
    {
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
            // 编辑器时间刷新不能依赖 Overlay 是否重绘、是否全屏隐藏。
            if (editing && DateTime.UtcNow >= nextEditorRefresh)
            {
                editor.RefreshTime();
                nextEditorRefresh = DateTime.UtcNow.AddSeconds(1);
            }
            var tasks = service.GetTasks().Where(x => x.IsAvailable && !x.IsCompleted).ToArray();
            if (fullscreen.ShouldHide() || !settings.Visible || (!editing && tasks.Length == 0))
            {
                Hide();
                return;
            }
            var signature = $"{editing}|{settings}|{string.Join(';', tasks.Select(x => $"{x.Task.Id}:{x.Task.Title}:{x.Period?.Key}:{x.IsOverdue}"))}";
            if (signature == lastSignature && window.IsVisible) return;
            CloseCheckboxes();
            window.Topmost = settings.Topmost;
            window.Opacity = settings.Opacity;
            window.Left = settings.Left;
            window.Top = settings.Top;
            window.Width = settings.Width;
            var work = NativeMethods.GetMonitor(new WindowInteropHelper(window).Handle).Work;
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
            var workHeight = work.Height / dpi.DpiScaleY;
            var maxRows = Math.Max(1, (int)((workHeight - 62) / (settings.FontSize + 18)));
            window.Height = editing ? settings.Height : Math.Max(80, Math.Min(workHeight, Math.Max(settings.Height, tasks.Length * (settings.FontSize + 18) + 40)));
            window.Render(tasks, editing, settings.FontSize, maxRows);
            window.Show();
            KeepOnScreen();
            window.UpdateLayout();
            if (!editing)
            {
                foreach (var (taskId, target) in window.HitTargets)
                {
                    var title = tasks.First(x => x.Task.Id == taskId).Task.Title;
                    var checkbox = new CompletionWindow(window, title, () => Complete(taskId)) { Topmost = settings.Topmost, Opacity = settings.Opacity };
                    checkboxes.Add(checkbox);
                    checkbox.Show();
                    checkbox.Align(target);
                }
            }
            lastSignature = signature;
        }
        catch (Exception ex) { App.ReportError(ex, false); }
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
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, NativeMethods.NoActivate | NativeMethods.NoZOrder);
    }

    private void AlignCheckboxes()
    {
        if (!window.IsVisible) return;
        for (var i = 0; i < Math.Min(checkboxes.Count, window.HitTargets.Count); i++)
            checkboxes[i].Align(window.HitTargets[i].Target);
    }

    private void SaveGeometry()
    {
        if (applying || !editor.IsVisible) return;
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
    private void Hide() { CloseCheckboxes(); window.Hide(); lastSignature = null; }
    private void CloseCheckboxes() { foreach (var checkbox in checkboxes) checkbox.Close(); checkboxes.Clear(); }

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
        CloseCheckboxes();
        window.AllowClose = true;
        window.Close();
    }
}
