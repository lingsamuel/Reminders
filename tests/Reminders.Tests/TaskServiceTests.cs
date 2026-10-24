using Reminders.Core;
using Xunit;

namespace Reminders.Tests;

public sealed class TaskServiceTests
{
    private readonly MemoryStore store = new();
    private readonly ManualClock clock = new();
    private TaskService Create() => new(store, clock, () => TimeZoneInfo.Utc);
    private static TaskDefinition Daily(string title = "日常") => new() { Title = title, Schedule = new() { ResetTime = new(5, 0) } };

    [Fact]
    public void CompletionPersistsBeforeNotificationAndDoesNotDuplicate()
    {
        var service = Create();
        var task = Daily();
        service.SaveTask(task);
        var notifications = 0;
        service.Changed += (_, _) => { Assert.Single(store.State.Events); notifications++; };
        service.Complete(task.Id);
        service.Complete(task.Id);
        Assert.Equal(1, notifications);
        Assert.Single(service.State.Events);
        Assert.True(Create().GetTasks()[0].IsCompleted);
    }

    [Fact]
    public void SaveFailureDoesNotChangeMemoryOrPublish()
    {
        var service = Create();
        var task = Daily();
        service.SaveTask(task);
        var before = service.State;
        var notifications = 0;
        service.Changed += (_, _) => notifications++;
        store.Fail = true;
        Assert.Throws<IOException>(() => service.Complete(task.Id));
        Assert.Same(before, service.State);
        Assert.False(service.GetTasks()[0].IsCompleted);
        Assert.Equal(0, notifications);
    }

    [Fact]
    public void UndoUsesAppendOrderEvenWhenClockMovesBack()
    {
        var service = Create();
        var task = Daily();
        service.SaveTask(task);
        service.Complete(task.Id);
        clock.Now = clock.Now.AddHours(-1);
        service.Undo(task.Id);
        Assert.False(service.GetTasks()[0].IsCompleted);
        service.Complete(task.Id);
        Assert.True(service.GetTasks()[0].IsCompleted);
        Assert.Equal(3, service.State.Events.Count);
    }

    [Fact]
    public void NextPeriodDoesNotCarryCompletionOrAllowHistoricalUndo()
    {
        var service = Create();
        var task = Daily();
        service.SaveTask(task);
        service.Complete(task.Id);
        clock.Now = clock.Now.AddDays(20);
        Assert.False(service.GetTasks()[0].IsCompleted);
        service.Undo(task.Id);
        Assert.Single(service.State.Events);
    }

    [Fact]
    public void RecurrenceChangesIncrementRevisionButUnusedFieldsDoNot()
    {
        var service = Create();
        var task = Daily();
        service.SaveTask(task);
        service.Complete(task.Id);
        service.SaveTask(task with { Title = "新名字", Revision = 99, Schedule = task.Schedule with { EveryDays = 9, AnchorDate = new(2040, 1, 1), DueDate = new(2040, 1, 1), Weekday = DayOfWeek.Monday } });
        Assert.Equal(1, service.State.Tasks[0].Revision);
        Assert.True(service.GetTasks()[0].IsCompleted);
        service.SaveTask(service.State.Tasks[0] with { Schedule = task.Schedule with { ResetTime = new(4, 0) } });
        Assert.Equal(2, service.State.Tasks[0].Revision);
        Assert.False(service.GetTasks()[0].IsCompleted);
        Assert.Equal("日常", service.State.Events[0].Title);
        Assert.Equal("UTC", service.State.Events[0].TimeZoneId);
        Assert.NotNull(service.State.Events[0].PeriodStart);
        Assert.NotNull(service.State.Events[0].PeriodEnd);
    }

    [Fact]
    public void OnceCompletionSurvivesDaysAndDeadlineEdits()
    {
        var service = Create();
        var task = new TaskDefinition { Title = "资料", Schedule = new() { Kind = ScheduleKind.Once, DueDate = new(2026, 10, 24) } };
        service.SaveTask(task);
        service.Complete(task.Id);
        clock.Now = clock.Now.AddDays(30);
        service.SaveTask(task with { Schedule = task.Schedule with { DueDate = new(2026, 12, 31), DueTime = new(18, 0) } });
        Assert.True(service.GetTasks()[0].IsCompleted);
        Assert.Equal(1, service.State.Tasks[0].Revision);
        Assert.Equal(DateTimeOffset.Parse("2026-10-25T00:00:00Z"), service.State.Events[0].PeriodEnd);
        service.Undo(task.Id);
        Assert.False(service.GetTasks()[0].IsCompleted);
        Assert.Null(service.State.Events[1].PeriodStart);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverdueOnceRemainsAvailable(bool specificTime)
    {
        var service = Create();
        var schedule = new Schedule { Kind = ScheduleKind.Once, DueDate = new(2026, 10, 23), DueTime = specificTime ? new(18, 0) : null };
        var task = new TaskDefinition { Title = "已到期", Schedule = schedule };
        service.SaveTask(task);
        var status = Assert.Single(service.GetTasks());
        Assert.True(status.IsOverdue);
        Assert.True(status.IsAvailable);
        service.Complete(task.Id);
        Assert.False(service.GetTasks()[0].IsOverdue);
    }

    [Fact]
    public void DisabledRetainsCompletionAndArchiveRetainsHistory()
    {
        var service = Create();
        var task = Daily();
        service.SaveTask(task);
        service.SetEnabled(task.Id, false);
        service.Complete(task.Id);
        Assert.Empty(service.State.Events);
        Assert.False(service.GetTasks()[0].IsAvailable);
        service.SetEnabled(task.Id, true);
        service.Complete(task.Id);
        service.SetEnabled(task.Id, false);
        service.SetEnabled(task.Id, true);
        Assert.True(service.GetTasks()[0].IsCompleted);
        service.Archive(task.Id);
        Assert.Empty(service.GetTasks());
        Assert.Single(service.State.Events);
    }

    [Fact]
    public void FutureTaskCannotBeCompleted()
    {
        var service = Create();
        var task = Daily() with { Schedule = new() { Kind = ScheduleKind.EveryNDays, AnchorDate = new(2030, 1, 1) } };
        service.SaveTask(task);
        service.Complete(task.Id);
        Assert.False(service.GetTasks()[0].IsAvailable);
        Assert.Empty(service.State.Events);
    }

    [Fact]
    public void VeryLongIntervalCanBeCompletedAndStored()
    {
        var service = Create();
        var task = Daily() with { Schedule = new() { Kind = ScheduleKind.EveryNDays, AnchorDate = new(2026, 1, 1), EveryDays = int.MaxValue } };
        service.SaveTask(task);
        service.Complete(task.Id);
        Assert.True(Create().GetTasks()[0].IsCompleted);
        Assert.Null(service.State.Events[0].PeriodEnd);
    }

    [Fact]
    public void MoveAndSettingsPersistAndTitleIsTrimmed()
    {
        var service = Create();
        var a = Daily("  A  ");
        var b = Daily("B");
        service.SaveTask(a);
        service.SaveTask(b);
        service.Move(b.Id, -1);
        Assert.Equal(b.Id, service.GetTasks()[0].Task.Id);
        Assert.Equal("A", service.State.Tasks.Single(x => x.Id == a.Id).Title);
        service.UpdateSettings(new() { Left = -100, FontSize = 20 });
        Assert.Equal(-100, Create().State.Overlay.Left);
        Assert.Throws<ArgumentException>(() => service.SaveTask(Daily("  ")));
        Assert.Throws<ArgumentException>(() => service.SaveTask(Daily(new string('a', 121))));
        Assert.Throws<ArgumentException>(() => service.UpdateSettings(new() { Width = double.NaN }));
    }

    private sealed class MemoryStore : IStateStore
    {
        public AppState State { get; private set; } = new();
        public bool Fail { get; set; }
        public AppState Load() => State;
        public void Save(AppState state)
        {
            if (Fail) throw new IOException("模拟写入失败");
            State = state;
        }
    }
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-10-24T12:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
