using Reminders.Core;
using Reminders.Windows.Presentation;
using Xunit;
using TaskStatus = Reminders.Core.TaskStatus;

namespace Reminders.Protocol.Tests;

public sealed class DisplayTextTests
{
    [Theory]
    [InlineData("2026-10-25", "2026-10-28", "10/28（3天后）截止")]
    [InlineData("2026-10-27", "2026-10-28", "10/28（1天后）截止")]
    [InlineData("2026-10-28", "2026-10-28", "10/28（今天）截止")]
    [InlineData("2026-10-31", "2026-11-02", "11/02（2天后）截止")]
    [InlineData("2026-12-31", "2027-01-01", "01/01（1天后）截止")]
    [InlineData("2028-02-28", "2028-03-01", "03/01（2天后）截止")]
    public void OnceShowsRemainingCalendarDays(string today, string dueDate, string expected)
    {
        var status = Once(dueDate);
        Assert.Equal($"提交资料  ·  {expected}", DisplayText.OverlayTitle(status, DateOnly.Parse(today)));
    }

    [Fact]
    public void CountdownChangesAfterMidnightWithoutChangingTask()
    {
        var status = Once("2026-10-28");
        Assert.Contains("（3天后）", DisplayText.OverlayTitle(status, new(2026, 10, 25)));
        Assert.Contains("（2天后）", DisplayText.OverlayTitle(status, new(2026, 10, 26)));
    }

    [Fact]
    public void SpecificDueTimeStillUsesCalendarDays()
    {
        var status = Once("2026-10-26");
        status = status with { Task = status.Task with { Schedule = status.Task.Schedule with { DueTime = new(0, 1) } } };
        Assert.Equal("提交资料  ·  10/26（1天后）截止", DisplayText.OverlayTitle(status, new(2026, 10, 25)));
    }

    [Fact]
    public void CoreOverdueStatusTakesPrecedenceEvenOnDueDate()
    {
        var status = Once("2026-10-28") with { IsOverdue = true };
        Assert.Equal("提交资料  ·  已逾期", DisplayText.OverlayTitle(status, new(2026, 10, 28)));
        Assert.Equal("提交资料  ·  已逾期", DisplayText.OverlayTitle(status, new(2026, 10, 29)));
    }

    [Theory]
    [InlineData(ScheduleKind.Daily)]
    [InlineData(ScheduleKind.Weekly)]
    [InlineData(ScheduleKind.EveryNDays)]
    public void RepeatingTasksKeepTheirTitle(ScheduleKind kind)
    {
        var status = new TaskStatus { Task = new() { Title = "日常", Schedule = new() { Kind = kind } } };
        Assert.Equal("日常", DisplayText.OverlayTitle(status, new(2026, 10, 25)));
    }

    private static TaskStatus Once(string dueDate) => new()
    {
        Task = new() { Title = "提交资料", Schedule = new() { Kind = ScheduleKind.Once, DueDate = DateOnly.Parse(dueDate) } },
        IsAvailable = true
    };
}
