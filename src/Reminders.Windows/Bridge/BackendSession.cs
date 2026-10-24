using System;
using System.Collections.Generic;
using System.Text.Json;
using Reminders.Core;
using Reminders.Persistence;
using TaskStatus = Reminders.Core.TaskStatus;

namespace Reminders.Windows.Bridge;

internal interface IEditorSession
{
    bool IsVisible { get; }
    event EventHandler? EditorVisibilityChanged;
    void RefreshTime();
    void Refresh();
}

internal sealed record BackendSnapshot
{
    public long Sequence { get; init; }
    public bool Ready { get; init; }
    public string? Error { get; init; }
    public bool CanRestoreBackup { get; init; }
    public IReadOnlyList<TaskStatus> Tasks { get; init; } = Array.Empty<TaskStatus>();
    public IReadOnlyList<TaskDefinition> Definitions { get; init; } = Array.Empty<TaskDefinition>();
    public IReadOnlyList<CompletionEvent> Events { get; init; } = Array.Empty<CompletionEvent>();
    public OverlaySettings Overlay { get; init; } = new();
    public string TimeZoneId { get; init; } = "";
    public string TimeZoneLabel { get; init; } = "";
    public DateTimeOffset Now { get; init; }
}

/// <summary>桥接只投影 Core 状态，不另建完成状态或周期算法。</summary>
internal sealed class BackendSession : IEditorSession, IDisposable
{
    private readonly IStateStore store;
    private readonly Action restoreBackup;
    private readonly Func<bool> hasBackup;
    private readonly TimeProvider clock;
    private readonly Func<TimeZoneInfo> zoneProvider;
    private StateLoadException? loadError;
    private string lastProjection = "";
    private long sequence;

    public BackendSession(IStateStore store, Action restoreBackup, Func<bool> hasBackup, TimeProvider? clock = null, Func<TimeZoneInfo>? zoneProvider = null)
    {
        this.store = store;
        this.restoreBackup = restoreBackup;
        this.hasBackup = hasBackup;
        this.clock = clock ?? TimeProvider.System;
        this.zoneProvider = zoneProvider ?? (() => TimeZoneInfo.Local);
        LoadService();
        lastProjection = Projection();
    }

    public TaskService? Service { get; private set; }
    public bool IsVisible { get; private set; }
    public event EventHandler? EditorVisibilityChanged;
    public event EventHandler? Changed;
    public event EventHandler? ServiceReplaced;

    public BackendSnapshot Snapshot()
    {
        var zone = zoneProvider();
        return new()
        {
            Sequence = sequence,
            Ready = Service is not null,
            Error = loadError?.Message,
            CanRestoreBackup = loadError is { CanRestoreBackup: true } && hasBackup(),
            Tasks = Service?.GetTasks() ?? Array.Empty<TaskStatus>(),
            Definitions = Service?.State.Tasks ?? Array.Empty<TaskDefinition>(),
            Events = Service?.State.Events ?? Array.Empty<CompletionEvent>(),
            Overlay = Service?.State.Overlay ?? new(),
            TimeZoneId = zone.Id,
            TimeZoneLabel = zone.DisplayName,
            Now = clock.GetUtcNow()
        };
    }

    public void Execute(string method, JsonElement payload)
    {
        if (method == "snapshot")
        {
            // 仅未就绪时重新读取主文件；正常快照不替换正在使用的服务。
            if (Service is null) TryReloadService();
            return;
        }
        if (method == "exit") return;
        if (method == "editorVisible")
        {
            var visible = payload.GetProperty("visible").GetBoolean();
            if (visible != IsVisible)
            {
                IsVisible = visible;
                EditorVisibilityChanged?.Invoke(this, EventArgs.Empty);
            }
            return;
        }
        if (method == "restoreBackup")
        {
            if (loadError is null || !loadError.CanRestoreBackup || !hasBackup()) throw new InvalidOperationException("当前没有允许恢复的备份。");
            restoreBackup();
            if (!TryReloadService()) throw new InvalidOperationException(loadError?.Message ?? "恢复失败。");
            return;
        }
        var service = Service ?? throw new InvalidOperationException(loadError?.Message ?? "任务数据尚未就绪。");
        switch (method)
        {
            case "saveTask": service.SaveTask(Read<TaskDefinition>(payload, "task")); break;
            case "complete": service.Complete(Id(payload)); break;
            case "undo": service.Undo(Id(payload)); break;
            case "setEnabled": service.SetEnabled(Id(payload), payload.GetProperty("enabled").GetBoolean()); break;
            case "archive": service.Archive(Id(payload)); break;
            case "move": service.Move(Id(payload), payload.GetProperty("delta").GetInt32()); break;
            case "updateSettings": service.UpdateSettings(Read<OverlaySettings>(payload, "settings")); break;
            default: throw new ArgumentException($"未知后端方法：{method}");
        }
    }

    public void RefreshTime() => Publish(force: false);
    public void Refresh() => Publish(force: false);

    private bool TryReloadService()
    {
        LoadService();
        if (Service is null) return false;
        ServiceReplaced?.Invoke(this, EventArgs.Empty);
        Publish(force: true);
        return true;
    }

    private void LoadService()
    {
        if (Service is not null) Service.Changed -= StateChanged;
        Service = null;
        loadError = null;
        try
        {
            Service = new TaskService(store, clock, zoneProvider);
            Service.Changed += StateChanged;
        }
        catch (StateLoadException error) { loadError = error; }
    }

    private static Guid Id(JsonElement payload) => payload.GetProperty("id").GetGuid();
    private static T Read<T>(JsonElement payload, string property) => payload.GetProperty(property).Deserialize<T>(BridgeJson.Options) ?? throw new ArgumentException($"{property} 不能为空。");
    private void StateChanged(object? sender, EventArgs args) => Publish(force: true);
    private string Projection()
    {
        var snapshot = Snapshot();
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(snapshot.Now, zoneProvider()).DateTime);
        // 时间戳与序号本身不产生变更；本地午夜仍需更新前端日期，即使尚未到任务重置时间。
        return JsonSerializer.Serialize(new
        {
            State = snapshot with { Now = default, Sequence = 0 },
            LocalDate = localDate
        }, BridgeJson.Options);
    }
    private void Publish(bool force)
    {
        var projection = Projection();
        if (!force && projection == lastProjection) return;
        lastProjection = projection;
        sequence++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (Service is not null) Service.Changed -= StateChanged;
    }
}
