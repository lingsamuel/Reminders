using System;
using Reminders.Core;
using TaskStatus = Reminders.Core.TaskStatus;

namespace Reminders.Windows.Presentation;

internal static class DisplayText
{
    public static string OverlayTitle(TaskStatus status, DateOnly? today = null)
    {
        if (status.Task.Schedule.Kind != ScheduleKind.Once) return status.Task.Title;
        if (status.IsOverdue) return $"{status.Task.Title}  ·  已逾期";

        var dueDate = status.Task.Schedule.DueDate!.Value;
        // 展示日历天数；不使用截止边界，避免“当天结束”的次日零点多算一天。
        var days = dueDate.DayNumber - (today ?? DateOnly.FromDateTime(DateTime.Today)).DayNumber;
        var countdown = days switch
        {
            > 0 => $"（{days}天后）",
            0 => "（今天）",
            _ => ""
        };
        return $"{status.Task.Title}  ·  {dueDate:MM/dd}{countdown}截止";
    }
}
