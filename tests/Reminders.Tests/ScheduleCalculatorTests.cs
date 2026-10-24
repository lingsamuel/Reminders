using Reminders.Core;
using Xunit;

namespace Reminders.Tests;

public sealed class ScheduleCalculatorTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static DateTimeOffset At(string value) => DateTimeOffset.Parse(value);

    [Theory]
    [InlineData(4, "2026-10-24T03:59:00Z", "2026-10-23")]
    [InlineData(4, "2026-10-24T04:00:00Z", "2026-10-24")]
    [InlineData(5, "2026-10-24T04:59:00Z", "2026-10-23")]
    [InlineData(5, "2026-10-24T05:00:00Z", "2026-10-24")]
    public void DailyUsesIndependentInclusiveBoundary(int hour, string now, string expected)
    {
        var period = ScheduleCalculator.Calculate(new() { ResetTime = new(hour, 0) }, At(now), Utc)!;
        Assert.Equal(expected, period.Key);
        Assert.Equal(TimeSpan.FromDays(1), period.End - period.Start);
    }

    [Theory]
    [InlineData("2026-10-21T23:59:00Z", "2026-10-15")]
    [InlineData("2026-10-22T00:00:00Z", "2026-10-22")]
    public void WeeklyUsesThursday(string now, string expected) => Assert.Equal(expected,
        ScheduleCalculator.Calculate(new() { Kind = ScheduleKind.Weekly }, At(now), Utc)!.Key);

    [Theory]
    [InlineData("2024-02-28", "2024-03-01T05:00:00Z", "2024-03-01")]
    [InlineData("2023-12-31", "2024-01-02T04:59:00Z", "2023-12-31")]
    [InlineData("2026-01-31", "2026-02-03T06:00:00Z", "2026-02-02")]
    public void EveryTwoDaysUsesCalendarAnchor(string anchor, string now, string expected) => Assert.Equal(expected,
        ScheduleCalculator.Calculate(new() { Kind = ScheduleKind.EveryNDays, AnchorDate = DateOnly.Parse(anchor), ResetTime = new(5, 0) }, At(now), Utc)!.Key);

    [Fact]
    public void FutureAnchorIsUnavailableUntilItsTime()
    {
        var schedule = new Schedule { Kind = ScheduleKind.EveryNDays, AnchorDate = new(2026, 10, 25), ResetTime = new(5, 0) };
        Assert.Null(ScheduleCalculator.Calculate(schedule, At("2026-10-25T04:59:00Z"), Utc));
        Assert.NotNull(ScheduleCalculator.Calculate(schedule, At("2026-10-25T05:00:00Z"), Utc));
    }

    [Fact]
    public void SpringGapMovesForwardToFirstValidMinute()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        var period = ScheduleCalculator.Calculate(new() { ResetTime = new(2, 30) }, At("2026-03-29T01:00:00Z"), zone)!;
        Assert.Equal("2026-03-29", period.Key);
        Assert.Equal(At("2026-03-29T01:00:00Z"), period.Start);
        Assert.Equal(At("2026-03-30T00:30:00Z"), period.End);
    }

    [Fact]
    public void FallAmbiguityUsesEarlierUtcWithoutSecondReset()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        var schedule = new Schedule { ResetTime = new(2, 30) };
        var first = ScheduleCalculator.Calculate(schedule, At("2026-10-25T00:30:00Z"), zone)!;
        var second = ScheduleCalculator.Calculate(schedule, At("2026-10-25T01:15:00Z"), zone)!;
        Assert.Equal("2026-10-25", second.Key);
        Assert.Equal(first, second);
        Assert.Equal(At("2026-10-25T00:30:00Z"), first.Start);
    }

    [Theory]
    [InlineData(null, "2026-10-25T00:00:00Z")]
    [InlineData("18:30", "2026-10-24T18:30:00Z")]
    public void OnceUsesStableIdentityAndExclusiveDeadline(string? time, string deadline)
    {
        var schedule = new Schedule { Kind = ScheduleKind.Once, DueDate = new(2026, 10, 24), DueTime = time is null ? null : TimeOnly.Parse(time) };
        var first = ScheduleCalculator.Calculate(schedule, At("2026-10-01T00:00:00Z"), Utc)!;
        Assert.Null(first.Start);
        Assert.Equal(At(deadline), first.End);
        Assert.Equal(first, ScheduleCalculator.Calculate(schedule, At("2027-01-01T00:00:00Z"), Utc));
    }

    [Theory]
    [InlineData(ScheduleKind.Daily)]
    [InlineData(ScheduleKind.Weekly)]
    [InlineData(ScheduleKind.EveryNDays)]
    [InlineData(ScheduleKind.Once)]
    public void IdentityIgnoresOnlyUnusedFields(ScheduleKind kind)
    {
        var schedule = new Schedule { Kind = kind, DueDate = new(2026, 10, 24) };
        var unused = kind switch
        {
            ScheduleKind.Daily => schedule with { Weekday = DayOfWeek.Monday, EveryDays = 5, AnchorDate = new(2020, 1, 1), DueDate = new(2027, 1, 1), DueTime = new(18, 0) },
            ScheduleKind.Weekly => schedule with { EveryDays = 5, AnchorDate = new(2020, 1, 1), DueDate = new(2027, 1, 1), DueTime = new(18, 0) },
            ScheduleKind.EveryNDays => schedule with { Weekday = DayOfWeek.Monday, DueDate = new(2027, 1, 1), DueTime = new(18, 0) },
            _ => schedule with { ResetTime = new(9, 0), Weekday = DayOfWeek.Monday, EveryDays = 5, AnchorDate = new(2020, 1, 1), DueDate = new(2027, 1, 1), DueTime = new(18, 0) }
        };
        Assert.False(ScheduleCalculator.ChangesIdentity(schedule, unused));
        Assert.Equal(kind != ScheduleKind.Once, ScheduleCalculator.ChangesIdentity(schedule, schedule with { ResetTime = new(9, 0) }));
        Assert.True(ScheduleCalculator.ChangesIdentity(schedule, schedule with { Kind = kind == ScheduleKind.Once ? ScheduleKind.Daily : ScheduleKind.Once }));
    }

    [Fact]
    public void EveryDaysAcceptsAnyPositiveInteger() => ScheduleCalculator.Validate(new() { Kind = ScheduleKind.EveryNDays, EveryDays = int.MaxValue });

    [Fact]
    public void VeryLongPeriodHasNoRepresentableEndInsteadOfCrashing()
    {
        var period = ScheduleCalculator.Calculate(new() { Kind = ScheduleKind.EveryNDays, AnchorDate = new(2026, 1, 1), EveryDays = int.MaxValue }, At("2026-10-24T12:00:00Z"), Utc)!;
        Assert.Equal("2026-01-01", period.Key);
        Assert.Null(period.End);
    }

    [Fact]
    public void RejectsInvalidActiveRules()
    {
        Assert.Throws<ArgumentException>(() => ScheduleCalculator.Validate(new() { Kind = ScheduleKind.Once }));
        Assert.Throws<ArgumentException>(() => ScheduleCalculator.Validate(new() { Kind = ScheduleKind.EveryNDays, EveryDays = 0 }));
        Assert.Throws<ArgumentException>(() => ScheduleCalculator.Validate(new() { Kind = (ScheduleKind)99 }));
    }
}
