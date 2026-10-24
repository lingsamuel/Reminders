using System.Globalization;

namespace Reminders.Core;

public static class ScheduleCalculator
{
    public static bool ChangesIdentity(Schedule before, Schedule after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (before.Kind != after.Kind) return true;
        return before.Kind switch
        {
            ScheduleKind.Daily => before.ResetTime != after.ResetTime,
            ScheduleKind.Weekly => before.ResetTime != after.ResetTime || before.Weekday != after.Weekday,
            ScheduleKind.EveryNDays => before.ResetTime != after.ResetTime || before.EveryDays != after.EveryDays || before.AnchorDate != after.AnchorDate,
            ScheduleKind.Once => false,
            _ => throw new ArgumentException("未知任务类型。")
        };
    }

    public static TaskPeriod? Calculate(Schedule schedule, DateTimeOffset now, TimeZoneInfo zone)
    {
        Validate(schedule);
        ArgumentNullException.ThrowIfNull(zone);
        if (schedule.Kind == ScheduleKind.Once)
        {
            var date = schedule.DueDate!.Value;
            var deadline = schedule.DueTime is { } time
                ? Resolve(date, time, zone)
                : Resolve(date.AddDays(1), TimeOnly.MinValue, zone);
            return new() { Key = "once", End = deadline };
        }

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var interval = schedule.Kind switch
        {
            ScheduleKind.Weekly => 7,
            ScheduleKind.EveryNDays => schedule.EveryDays,
            _ => 1
        };
        var startDate = today;
        if (schedule.Kind == ScheduleKind.Weekly)
            startDate = today.AddDays(-((7 + (int)today.DayOfWeek - (int)schedule.Weekday) % 7));
        else if (schedule.Kind == ScheduleKind.EveryNDays)
        {
            var elapsed = today.DayNumber - schedule.AnchorDate.DayNumber;
            if (elapsed < 0) return null;
            startDate = schedule.AnchorDate.AddDays(elapsed / interval * interval);
        }

        // 比较实际 UTC 边界，秋季重复时段只在第一次墙上时间到达时重置。
        var start = Resolve(startDate, schedule.ResetTime, zone);
        if (start > now)
        {
            if (schedule.Kind == ScheduleKind.EveryNDays && startDate == schedule.AnchorDate) return null;
            startDate = startDate.AddDays(-interval);
            start = Resolve(startDate, schedule.ResetTime, zone);
        }
        return new()
        {
            Key = startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Start = start,
            End = (long)startDate.DayNumber + interval <= DateOnly.MaxValue.DayNumber
                ? Resolve(startDate.AddDays(interval), schedule.ResetTime, zone)
                : null
        };
    }

    public static void Validate(Schedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (!Enum.IsDefined(schedule.Kind)) throw new ArgumentException("未知任务类型。");
        if (schedule.Kind == ScheduleKind.Weekly && !Enum.IsDefined(schedule.Weekday))
            throw new ArgumentException("请选择有效星期。");
        if (schedule.Kind == ScheduleKind.EveryNDays && schedule.EveryDays < 1)
            throw new ArgumentException("重复天数必须为正整数。");
        if (schedule.Kind == ScheduleKind.Once && (schedule.DueDate is null || (schedule.DueDate == DateOnly.MaxValue && schedule.DueTime is null)))
            throw new ArgumentException("请选择有效截止日期。");
    }

    private static DateTimeOffset Resolve(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        // 夏令时跳时不沿用旧偏移，而是前进至首个有效分钟。
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        var offset = zone.IsAmbiguousTime(local)
            ? zone.GetAmbiguousTimeOffsets(local).Max()
            : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
