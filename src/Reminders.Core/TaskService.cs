namespace Reminders.Core;

public sealed class TaskService
{
    private readonly IStateStore store;
    private readonly TimeProvider clock;
    private readonly Func<TimeZoneInfo> timeZoneProvider;

    public TaskService(IStateStore store, TimeProvider? timeProvider = null, Func<TimeZoneInfo>? timeZoneProvider = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        clock = timeProvider ?? TimeProvider.System;
        this.timeZoneProvider = timeZoneProvider ?? (() => TimeZoneInfo.Local);
        var state = store.Load();
        StateValidator.Validate(state);
        State = Freeze(state);
    }

    public AppState State { get; private set; }
    public event EventHandler? Changed;

    public IReadOnlyList<TaskStatus> GetTasks()
    {
        var now = clock.GetUtcNow();
        var zone = timeZoneProvider();
        return State.Tasks.Where(x => !x.Archived).OrderBy(x => x.Order)
            .Select(x => GetStatus(x, now, zone)).ToArray();
    }

    public void SaveTask(TaskDefinition task)
    {
        ArgumentNullException.ThrowIfNull(task);
        task = task with { Title = task.Title?.Trim() ?? "" };
        var previous = State.Tasks.FirstOrDefault(x => x.Id == task.Id);
        task = task with
        {
            Revision = previous is null ? 1 : previous.Revision + (ScheduleCalculator.ChangesIdentity(previous.Schedule, task.Schedule) ? 1 : 0),
            CreatedAt = previous?.CreatedAt ?? clock.GetUtcNow(),
            Order = previous?.Order ?? State.Tasks.Count
        };
        StateValidator.ValidateTask(task);
        Commit(State with { Tasks = previous is null ? State.Tasks.Append(task).ToArray() : State.Tasks.Select(x => x.Id == task.Id ? task : x).ToArray() });
    }

    public void Complete(Guid id) => AddEvent(id, CompletionAction.Complete);
    public void Undo(Guid id) => AddEvent(id, CompletionAction.Undo);
    public void SetEnabled(Guid id, bool enabled) => ReplaceTask(id, task => task with { Enabled = enabled });
    public void Archive(Guid id) => ReplaceTask(id, task => task with { Archived = true });

    public void Move(Guid id, int delta)
    {
        var active = State.Tasks.Where(x => !x.Archived).OrderBy(x => x.Order).ToList();
        var index = active.FindIndex(x => x.Id == id);
        if (index < 0) throw new ArgumentException("任务不存在。");
        var target = (int)Math.Clamp((long)index + delta, 0, active.Count - 1);
        if (index == target) return;
        var task = active[index];
        active.RemoveAt(index);
        active.Insert(target, task);
        var order = active.Select((x, i) => (x.Id, i)).ToDictionary(x => x.Id, x => x.i);
        Commit(State with { Tasks = State.Tasks.Select(x => order.TryGetValue(x.Id, out var position) ? x with { Order = position } : x).ToArray() });
    }

    public void UpdateSettings(OverlaySettings settings)
    {
        StateValidator.ValidateSettings(settings);
        Commit(State with { Overlay = settings });
    }

    private TaskStatus GetStatus(TaskDefinition task, DateTimeOffset now, TimeZoneInfo zone)
    {
        var period = ScheduleCalculator.Calculate(task.Schedule, now, zone);
        // 历史按追加顺序求最后操作；系统时钟回拨不能改变操作先后。
        var last = period is null ? null : State.Events.LastOrDefault(x => x.TaskId == task.Id && x.Revision == task.Revision && x.PeriodKey == period.Key);
        var completed = last?.Action == CompletionAction.Complete;
        return new()
        {
            Task = task,
            Period = period,
            IsCompleted = completed,
            IsAvailable = task.Enabled && !task.Archived && period is not null,
            IsOverdue = task.Schedule.Kind == ScheduleKind.Once && !completed && period?.End <= now
        };
    }

    private void AddEvent(Guid id, CompletionAction action)
    {
        var task = Find(id);
        var now = clock.GetUtcNow();
        var zone = timeZoneProvider();
        var status = GetStatus(task, now, zone);
        if (task.Archived || status.Period is null) return;
        if (action == CompletionAction.Complete && (!status.IsAvailable || status.IsCompleted)) return;
        if (action == CompletionAction.Undo && !status.IsCompleted) return;
        var item = new CompletionEvent
        {
            TaskId = task.Id, Revision = task.Revision, PeriodKey = status.Period.Key,
            Action = action, OccurredAt = now.ToUniversalTime(), Title = task.Title,
            PeriodStart = status.Period.Start, PeriodEnd = status.Period.End, TimeZoneId = zone.Id
        };
        Commit(State with { Events = State.Events.Append(item).ToArray() });
    }

    private TaskDefinition Find(Guid id) => State.Tasks.FirstOrDefault(x => x.Id == id) ?? throw new ArgumentException("任务不存在。");
    private void ReplaceTask(Guid id, Func<TaskDefinition, TaskDefinition> update)
    {
        var updated = update(Find(id));
        Commit(State with { Tasks = State.Tasks.Select(x => x.Id == id ? updated : x).ToArray() });
    }

    private void Commit(AppState state)
    {
        StateValidator.Validate(state);
        var frozen = Freeze(state);
        // 先落盘，再发布内存状态和通知。异常时调用方仍看到完整的旧状态。
        store.Save(frozen);
        State = frozen;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static AppState Freeze(AppState state) => state with
    {
        Tasks = Array.AsReadOnly(state.Tasks.ToArray()), Events = Array.AsReadOnly(state.Events.ToArray())
    };

}
