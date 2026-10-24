namespace Reminders.Core;

public enum ScheduleKind { Daily, Weekly, EveryNDays, Once }
public enum CompletionAction { Complete, Undo }

public sealed record Schedule
{
    public ScheduleKind Kind { get; init; }
    public TimeOnly ResetTime { get; init; }
    public DayOfWeek Weekday { get; init; } = DayOfWeek.Thursday;
    public int EveryDays { get; init; } = 2;
    public DateOnly AnchorDate { get; init; } = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly? DueDate { get; init; }
    public TimeOnly? DueTime { get; init; }
}

public sealed record TaskDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Title { get; init; } = "";
    public Schedule Schedule { get; init; } = new();
    public int Revision { get; init; } = 1;
    public int Order { get; init; }
    public bool Enabled { get; init; } = true;
    public bool Archived { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record OverlaySettings
{
    public double Left { get; init; } = 80;
    public double Top { get; init; } = 100;
    public double Width { get; init; } = 340;
    public double Height { get; init; } = 260;
    public double FontSize { get; init; } = 16;
    public double Opacity { get; init; } = 0.88;
    public bool Topmost { get; init; } = true;
    public bool Visible { get; init; } = true;
}

public sealed record AppState
{
    public int SchemaVersion { get; init; } = 1;
    public IReadOnlyList<TaskDefinition> Tasks { get; init; } = Array.Empty<TaskDefinition>();
    public IReadOnlyList<CompletionEvent> Events { get; init; } = Array.Empty<CompletionEvent>();
    public OverlaySettings Overlay { get; init; } = new();
}

public sealed record TaskPeriod
{
    public string Key { get; init; } = "";
    public DateTimeOffset? Start { get; init; }
    public DateTimeOffset? End { get; init; }
}

public sealed record CompletionEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TaskId { get; init; }
    public int Revision { get; init; }
    public string PeriodKey { get; init; } = "";
    public CompletionAction Action { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    public string Title { get; init; } = "";
    public DateTimeOffset? PeriodStart { get; init; }
    public DateTimeOffset? PeriodEnd { get; init; }
    public string TimeZoneId { get; init; } = "";
}

public sealed record TaskStatus
{
    public TaskDefinition Task { get; init; } = new();
    public TaskPeriod? Period { get; init; }
    public bool IsCompleted { get; init; }
    public bool IsOverdue { get; init; }
    public bool IsAvailable { get; init; }
}

public interface IStateStore
{
    AppState Load();
    void Save(AppState state);
}
