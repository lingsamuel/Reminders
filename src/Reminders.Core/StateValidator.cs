using System.Globalization;

namespace Reminders.Core;

/// <summary>统一服务输入与磁盘数据约束，避免两条路径接受不同的状态。</summary>
public static class StateValidator
{
    public static void Validate(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.SchemaVersion != 1) throw new ArgumentException("不支持的数据版本。");
        if (state.Tasks is null || state.Events is null || state.Overlay is null) throw new ArgumentException("数据列表或设置缺失。");
        ValidateSettings(state.Overlay);
        var tasks = new Dictionary<Guid, TaskDefinition>();
        foreach (var task in state.Tasks)
        {
            ValidateTask(task);
            if (!tasks.TryAdd(task.Id, task)) throw new ArgumentException("任务标识重复。");
        }
        var ids = new HashSet<Guid>();
        foreach (var item in state.Events)
        {
            if (item is null || item.Id == Guid.Empty || !ids.Add(item.Id)) throw new ArgumentException("历史标识无效或重复。");
            if (!tasks.TryGetValue(item.TaskId, out var task) || item.Revision < 1 || item.Revision > task.Revision)
                throw new ArgumentException("历史引用的任务或日程版本不存在。");
            if (!Enum.IsDefined(item.Action) || string.IsNullOrWhiteSpace(item.TimeZoneId))
                throw new ArgumentException("历史操作或时区无效。");
            ValidateTitle(item.Title);
            if (item.PeriodStart is { } start && item.PeriodEnd is { } end && start >= end)
                throw new ArgumentException("历史周期范围无效。");
            if (item.PeriodKey == "once")
            {
                if (item.PeriodStart is not null || item.PeriodEnd is null) throw new ArgumentException("一次性历史必须有截止时间且不能有重置起点。");
            }
            else if (item.PeriodStart is null || !DateOnly.TryParseExact(item.PeriodKey, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                throw new ArgumentException("历史周期身份无效。");
        }
    }

    public static void ValidateTask(TaskDefinition task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (task.Id == Guid.Empty || task.Revision < 1) throw new ArgumentException("任务标识或日程版本无效。");
        ValidateTitle(task.Title);
        ScheduleCalculator.Validate(task.Schedule);
    }

    public static void ValidateSettings(OverlaySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!double.IsFinite(settings.Left) || !double.IsFinite(settings.Top)
            || !Positive(settings.Width) || !Positive(settings.Height) || !Positive(settings.FontSize)
            || !double.IsFinite(settings.Opacity) || settings.Opacity <= 0 || settings.Opacity > 1)
            throw new ArgumentException("Overlay 位置、尺寸、字号或透明度无效。");
    }

    private static bool Positive(double value) => double.IsFinite(value) && value > 0;
    private static void ValidateTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 120) throw new ArgumentException("任务名称须为 1 至 120 个字符。");
    }
}
