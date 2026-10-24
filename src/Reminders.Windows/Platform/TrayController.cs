using System;
using System.Drawing;
using System.Windows.Threading;
using Reminders.Core;
using Forms = System.Windows.Forms;

namespace Reminders.Windows.Platform;

internal sealed class TrayController : IDisposable
{
    private readonly Forms.NotifyIcon icon;
    private readonly Icon? ownedIcon;
    private readonly Forms.ContextMenuStrip menu = new();
    private readonly Forms.ToolStripMenuItem visible;
    private readonly TaskService? service;
    private readonly Dispatcher dispatcher;

    public TrayController(TaskService? service, Dispatcher dispatcher, Action openEditor, Action exit)
    {
        this.service = service;
        this.dispatcher = dispatcher;
        menu.Items.Add("打开编辑器", null, (_, _) => dispatcher.Invoke(openEditor));
        visible = new Forms.ToolStripMenuItem("显示 Overlay")
        {
            Checked = service?.State.Overlay.Visible ?? false,
            Enabled = service is not null
        };
        visible.Click += (_, _) => dispatcher.Invoke(() =>
        {
            if (service is null) return;
            try { service.UpdateSettings(service.State.Overlay with { Visible = !service.State.Overlay.Visible }); }
            catch (Exception ex) { App.ReportError(ex, true); }
        });
        menu.Items.Add(visible);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出 Reminders", null, (_, _) => dispatcher.Invoke(exit));
        if (Environment.ProcessPath is { } executable) ownedIcon = Icon.ExtractAssociatedIcon(executable);
        icon = new Forms.NotifyIcon { Icon = ownedIcon ?? SystemIcons.Application, Text = "Reminders · 任务提醒", ContextMenuStrip = menu, Visible = true };
        icon.DoubleClick += (_, _) => dispatcher.Invoke(openEditor);
        if (service is not null) service.Changed += StateChanged;
    }

    private void StateChanged(object? sender, EventArgs e) => dispatcher.Invoke(() => visible.Checked = service?.State.Overlay.Visible ?? false);
    public void Dispose()
    {
        if (service is not null) service.Changed -= StateChanged;
        icon.Visible = false;
        icon.Dispose();
        ownedIcon?.Dispose();
        menu.Dispose();
    }
}
