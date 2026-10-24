using Reminders.Core;
using TaskStatus = Reminders.Core.TaskStatus;

namespace Reminders.Windows.Presentation;

internal static class DisplayText
{
    public static string OverlayTitle(TaskStatus status) => status.Task.Schedule.Kind == ScheduleKind.Once
        ? $"{status.Task.Title}  ·  {(status.IsOverdue ? "已逾期" : $"{status.Task.Schedule.DueDate:MM/dd} 截止")}"
        : status.Task.Title;
}
