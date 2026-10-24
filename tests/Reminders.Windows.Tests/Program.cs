using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Reminders.Core;
using Reminders.Windows;
using Reminders.Windows.Bridge;
using Reminders.Windows.Overlay;
using Reminders.Windows.Platform;

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
            var firstButton = (CompletionButton)overlay.HitTargets[0].Target;
            Capture((FrameworkElement)overlay.Content, 340, 260, Path.Combine(output, "overlay-edit.png"));
            var glyph = (System.Windows.Shapes.Path)firstButton.Template.FindName("CheckGlyph", firstButton);
            var box = (Border)firstButton.Template.FindName("Box", firstButton);
            Check(glyph.Visibility == Visibility.Collapsed, "Pending tasks start with an empty checkbox");
            firstButton.IsPreviewHovered = true;
            Capture((FrameworkElement)overlay.Content, 340, 260, Path.Combine(output, "overlay-hover.png"));
            Check(glyph.Visibility == Visibility.Visible, "Hover shows an explicit completion check");
            var hoverFill = ((SolidColorBrush)box.Background).Color;
            Check(Contrast(hoverFill, ((SolidColorBrush)glyph.Stroke).Color) >= 7, "Hover glyph has strong contrast");
            firstButton.IsPreviewPressed = true;
            Capture((FrameworkElement)overlay.Content, 340, 260, Path.Combine(output, "overlay-pressed.png"));
            Check(((SolidColorBrush)box.Background).Color != hoverFill && glyph.Visibility == Visibility.Visible, "Pressed state is distinct and readable");
            overlay.Render(pending, false, 16, 2);
            Check(((Border)overlay.FindName("Surface")).Background is SolidColorBrush { Color.A: >= 200 }, "Daily overlay keeps a readable dark surface");
            Check(ReferenceEquals(firstButton, overlay.HitTargets[0].Target), "Switching modes reuses visual controls");
            Check(firstButton.Opacity == 1 && glyph.Visibility == Visibility.Collapsed, "Daily checkbox is painted by the same visual window");
            Check(overlay.HitTargets.Count == 2, "Daily overlay limits interactive rows to available space");
            Check(((TextBlock)overlay.FindName("MoreText")).Text.Contains('2'), "Overflow reports remaining tasks");
            Capture((FrameworkElement)overlay.Content, 340, (int)OverlayLayout.Calculate(4, 16, 260, 114, false).Height, Path.Combine(output, "overlay-daily.png"));
            VerifyInputLayer(overlay, pending);
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

    private static void VerifyInputLayer(OverlayWindow overlay, Reminders.Core.TaskStatus[] pending)
    {
        Guid? completed = null;
        var windowsBefore = Application.Current.Windows.Count;
        var input = new CompletionInputWindow(overlay, id => completed = id);
        var targets = overlay.GetCompletionTargets();
        input.SetTargets(targets);
        var canvas = (Canvas)input.Content;
        var hit = (Button)canvas.Children[0];
        var visual = targets[0].Visual;
        hit.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
        Check(visual.IsPreviewHovered, "Input hover updates the shared visual control");
        hit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(completed == targets[0].TaskId, "Input routes completion to the matching task");
        input.SetTargets(targets);
        Check(ReferenceEquals(hit, canvas.Children[0]), "Input refresh reuses hit controls");
        Check(!visual.IsPreviewHovered, "Refresh clears obsolete hover state");

        var handle = new WindowInteropHelper(input).EnsureHandle();
        var rectangles = targets.Select(target => target.Bounds).ToArray();
        CompletionRegion.Apply(handle, rectangles);
        var region = NativeMethods.CreateRectRgn(0, 0, 0, 0);
        try
        {
            Check(GetWindowRgn(handle, region) != 0, "Input HWND receives a native hit region");
            var first = rectangles[0];
            Check(PtInRegion(region, (int)(first.X + first.Width / 2), (int)(first.Y + first.Height / 2)), "Native region includes the checkbox");
            Check(!PtInRegion(region, (int)first.Right + 15, (int)(first.Y + first.Height / 2)), "Native region excludes task text");
            Check(!PtInRegion(region, 300, 120), "Native region excludes panel background");
        }
        finally { NativeMethods.DeleteObject(region); }

        var many = Enumerable.Range(0, 120).Select(i => pending[0] with { Task = pending[0].Task with { Id = Guid.NewGuid(), Title = $"任务 {i}" } }).ToArray();
        overlay.Render(many, false, 16, 120);
        Capture((FrameworkElement)overlay.Content, 340, 3866, Path.Combine(Path.GetTempPath(), "reminders-large-offscreen.png"));
        input.SetTargets(overlay.GetCompletionTargets());
        Check(canvas.Children.Count == 120 && Application.Current.Windows.Count == windowsBefore + 1, "120 tasks still use one input window");
        overlay.Render(many, true, 16, 120);
        Capture((FrameworkElement)overlay.Content, 340, 120, Path.Combine(Path.GetTempPath(), "reminders-scroll-offscreen.png"));
        var scroll = (ScrollViewer)overlay.FindName("TaskScroll");
        scroll.ScrollToEnd();
        ((FrameworkElement)overlay.Content).UpdateLayout();
        Check(scroll.VerticalOffset > 0, "Edit overlay can scroll long lists");
        overlay.Render(pending, false, 16, pending.Length);
        Capture((FrameworkElement)overlay.Content, 340, 154, Path.Combine(Path.GetTempPath(), "reminders-restored-offscreen.png"));
        Check(overlay.GetCompletionTargets()[0].Bounds.Top >= 13, "Daily mode resets the editor scroll offset");
        input.Conceal();
        Check(!input.IsVisible && !overlay.IsVisible, "Input and visual tests never show a window");
        input.DisposeWindow();
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowRgn(IntPtr hwnd, IntPtr region);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PtInRegion(IntPtr region, int x, int y);

    private static double Contrast(Color first, Color second)
    {
        static double Linear(byte value) { var s = value / 255d; return s <= .04045 ? s / 12.92 : Math.Pow((s + .055) / 1.055, 2.4); }
        static double Luminance(Color c) => .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B);
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
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
