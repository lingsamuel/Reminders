using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Reminders.Core;
using Reminders.Windows;
using Reminders.Windows.Bridge;
using Reminders.Windows.Overlay;

namespace Reminders.Windows.Tests;

/// <summary>只渲染未显示的 Overlay 控件树；禁止 Show、Activate 或真实输入自动化。</summary>
internal static class Program
{
    private static int passed;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var app = new App();
            app.InitializeComponent();
            var output = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "Reminders-offscreen");
            Directory.CreateDirectory(output);
            var store = new MemoryStore();
            using var session = new BackendSession(store, () => { }, () => false, new FixedTime(), () => TimeZoneInfo.Utc);
            var service = session.Service!;
            service.SaveTask(new() { Title = "领取 Epic 免费游戏", Schedule = new() { Kind = ScheduleKind.Weekly, Weekday = DayOfWeek.Thursday } });
            service.SaveTask(new() { Title = "游戏日常", Schedule = new() { ResetTime = new TimeOnly(5, 0) } });
            service.SaveTask(new() { Title = "检查体力", Schedule = new() { Kind = ScheduleKind.EveryNDays, AnchorDate = new DateOnly(2026, 10, 24) } });
            service.SaveTask(new() { Title = "提交资料", Schedule = new() { Kind = ScheduleKind.Once, DueDate = new DateOnly(2026, 10, 23) } });
            Check(!session.IsVisible, "Backend starts with editor hidden");
            Check(session.Snapshot().Tasks.Count == 4, "Snapshot projects four schedule kinds");
            var pending = service.GetTasks().Where(x => x.IsAvailable && !x.IsCompleted).ToArray();
            var overlay = new OverlayWindow();
            overlay.Render(pending, true, 16, 2);
            Check(overlay.HitTargets.Count == 4, "Edit overlay contains all pending tasks");
            Capture((FrameworkElement)overlay.Content, 340, 260, Path.Combine(output, "overlay-edit.png"));
            overlay.Render(pending, false, 16, 2);
            Check(overlay.HitTargets.Count == 2, "Daily overlay limits interactive rows to available space");
            Check(((TextBlock)overlay.FindName("MoreText")).Text.Contains('2'), "Overflow reports remaining tasks");
            Capture((FrameworkElement)overlay.Content, 340, 160, Path.Combine(output, "overlay-daily.png"));
            var once = pending.Single(x => x.Task.Schedule.Kind == ScheduleKind.Once);
            Check(once.IsOverdue && once.IsAvailable, "Overdue task remains available");
            var daily = pending.Single(x => x.Task.Title == "游戏日常");
            service.Complete(daily.Task.Id);
            Check(session.Snapshot().Tasks.Single(x => x.Task.Id == daily.Task.Id).IsCompleted, "Snapshot reflects persisted completion");
            service.Undo(daily.Task.Id);
            Check(session.Snapshot().Events.Count == 2, "Snapshot retains completion and undo history");
            bool cancelled = false;
            overlay.Closing += (_, e) => cancelled = e.Cancel;
            overlay.Close();
            Check(cancelled, "Accidental Overlay close is cancelled");
            Check(!overlay.IsVisible, "Offscreen rendering never shows Overlay");
            overlay.AllowClose = true;
            overlay.Close();
            Console.WriteLine($"{passed} checks passed. Offscreen images: {output}");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        passed++;
        Console.WriteLine($"PASS: {description}");
    }

    private static void Capture(FrameworkElement content, int width, int height, string path)
    {
        content.Width = width;
        content.Height = height;
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 24, 10, 0, 0, TimeSpan.Zero);
    }
    private sealed class MemoryStore : IStateStore
    {
        private AppState state = new();
        public AppState Load() => state;
        public void Save(AppState value) => state = value;
    }
}
