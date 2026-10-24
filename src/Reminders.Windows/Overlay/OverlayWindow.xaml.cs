using System;
using System.Collections.Generic;
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
    internal bool AllowClose { get; set; }
    internal List<(Guid TaskId, FrameworkElement Target)> HitTargets { get; } = new();
    internal event Action<Guid>? CompleteRequested;
    internal event Action? GeometryChanged;

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
        Surface.Background = isEditing ? new SolidColorBrush(Color.FromArgb(230, 24, 28, 35)) : Brushes.Transparent;
        DragHeader.Visibility = ResizeThumb.Visibility = isEditing ? Visibility.Visible : Visibility.Collapsed;
        TaskScroll.VerticalScrollBarVisibility = isEditing ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        RowsPanel.Children.Clear();
        HitTargets.Clear();
        int count = isEditing ? tasks.Count : Math.Min(tasks.Count, maxRows);
        for (int i = 0; i < count; i++)
        {
            var task = tasks[i];
            var row = new Grid { Height = fontSize + 18 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var button = new Button
            {
                Width = 20, Height = 20, Margin = new Thickness(0), Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                Content = "✓", FontSize = 12, Background = Brushes.Transparent, Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(145, 213, 195)), BorderThickness = new Thickness(1),
                Opacity = isEditing ? 1 : 0, Focusable = false
            };
            AutomationProperties.SetName(button, $"完成 {task.Task.Title}");
            button.Click += (_, _) => CompleteRequested?.Invoke(task.Task.Id);
            row.Children.Add(button);
            HitTargets.Add((task.Task.Id, button));
            var text = new TextBlock
            {
                Text = DisplayText.OverlayTitle(task), FontSize = fontSize,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = task.IsOverdue ? new SolidColorBrush(Color.FromRgb(255, 181, 171)) : Brushes.White,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Opacity = .85 }
            };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            RowsPanel.Children.Add(row);
        }
        if (isEditing && tasks.Count == 0)
            RowsPanel.Children.Add(new TextBlock { Text = "暂无待完成任务\n可在编辑器创建任务", Foreground = Brushes.LightGray, Margin = new Thickness(4, 14, 0, 0), TextWrapping = TextWrapping.Wrap });
        MoreText.Text = count < tasks.Count ? $"另有 {tasks.Count - count} 项 · 打开编辑器查看" : "";
        MoreText.Visibility = count < tasks.Count ? Visibility.Visible : Visibility.Collapsed;
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
