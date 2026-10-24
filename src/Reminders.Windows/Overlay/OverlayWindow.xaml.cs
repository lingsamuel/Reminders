using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Reminders.Windows.Platform;
using Reminders.Windows.Presentation;
using TaskStatus = Reminders.Core.TaskStatus;

namespace Reminders.Windows.Overlay;

public partial class OverlayWindow : Window
{
    private OverlayWindowBehavior? behavior;
    private bool editing;
    private readonly Dictionary<Guid, RowView> rows = new();
    internal bool AllowClose { get; set; }
    internal List<(Guid TaskId, FrameworkElement Target)> HitTargets { get; } = new();
    internal event Action<Guid>? CompleteRequested;
    internal event Action? GeometryChanged;

    private sealed record RowView(Grid Root, CompletionButton Button, TextBlock Text);

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => behavior = new OverlayWindowBehavior(this);
        DpiChanged += (_, _) => GeometryChanged?.Invoke();
        // Overlay 由托盘生命周期管理；编辑时误按 Alt+F4 不销毁控制器复用的窗口。
        Closing += (_, args) => args.Cancel = !AllowClose;
    }

    internal void Render(IReadOnlyList<TaskStatus> tasks, bool isEditing, double fontSize, int maxRows)
    {
        editing = isEditing;
        behavior?.SetMode(isEditing, !isEditing);
        Surface.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "OverlaySurfaceBrush");
        DragHeader.Visibility = ResizeThumb.Visibility = isEditing ? Visibility.Visible : Visibility.Collapsed;
        TaskScroll.VerticalScrollBarVisibility = isEditing ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        HitTargets.Clear();
        int count = isEditing ? tasks.Count : Math.Min(tasks.Count, maxRows);
        var visible = new HashSet<Guid>();
        var ordered = new List<UIElement>();
        for (int i = 0; i < count; i++)
        {
            var task = tasks[i];
            visible.Add(task.Task.Id);
            if (!rows.TryGetValue(task.Task.Id, out var row))
            {
                row = CreateRow(task.Task.Id);
                rows.Add(task.Task.Id, row);
            }
            row.Root.Height = OverlayLayout.RowHeightFor(fontSize);
            row.Button.IsHitTestVisible = isEditing;
            row.Button.IsPreviewHovered = row.Button.IsPreviewPressed = false;
            AutomationProperties.SetName(row.Button, $"完成 {task.Task.Title}");
            row.Text.Text = DisplayText.OverlayTitle(task);
            row.Text.FontSize = fontSize;
            row.Text.Foreground = task.IsOverdue ? new SolidColorBrush(Color.FromRgb(255, 188, 163))
                : (Brush)FindResource("OverlayTextBrush");
            ordered.Add(row.Root);
            HitTargets.Add((task.Task.Id, row.Button));
        }
        foreach (var id in rows.Keys.Where(id => !visible.Contains(id)).ToArray()) rows.Remove(id);
        if (RowsPanel.Children.Count != ordered.Count || ordered.Where((element, i) => i >= RowsPanel.Children.Count || !ReferenceEquals(element, RowsPanel.Children[i])).Any())
        {
            RowsPanel.Children.Clear();
            foreach (var row in ordered) RowsPanel.Children.Add(row);
        }
        if (isEditing && tasks.Count == 0)
            RowsPanel.Children.Add(new TextBlock { Text = "暂无待完成任务\n可在编辑器创建任务", Foreground = Brushes.LightGray, Margin = new Thickness(4, 14, 0, 0), TextWrapping = TextWrapping.Wrap });
        MoreText.Text = count < tasks.Count ? $"另有 {tasks.Count - count} 项 · 打开编辑器查看" : "";
        MoreText.Visibility = count < tasks.Count ? Visibility.Visible : Visibility.Collapsed;
    }

    private RowView CreateRow(Guid id)
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        root.ColumnDefinitions.Add(new ColumnDefinition());
        var button = new CompletionButton
        {
            Width = 20, Height = 20, Margin = new Thickness(0), Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Focusable = false
        };
        button.Click += (_, _) => CompleteRequested?.Invoke(id);
        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(text, 1);
        root.Children.Add(button);
        root.Children.Add(text);
        return new(root, button, text);
    }

    internal IReadOnlyList<CompletionTarget> GetCompletionTargets()
    {
        var content = (UIElement)Content;
        return HitTargets.Select(hit =>
        {
            var start = hit.Target.TranslatePoint(new Point(0, 0), content);
            return new CompletionTarget(hit.TaskId, new Rect(start, new Size(hit.Target.ActualWidth, hit.Target.ActualHeight)), (CompletionButton)hit.Target);
        }).ToArray();
    }

    private void DragHeader_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!editing || e.ChangedButton != MouseButton.Left) return;
        DragMove();
        GeometryChanged?.Invoke();
    }
    private void Resize_Delta(object sender, DragDeltaEventArgs e)
    {
        Width = Math.Clamp(Width + e.HorizontalChange, MinWidth, 3000);
        Height = Math.Clamp(Height + e.VerticalChange, 120, 3000);
    }
    private void Resize_Completed(object sender, DragCompletedEventArgs e) => GeometryChanged?.Invoke();
}
