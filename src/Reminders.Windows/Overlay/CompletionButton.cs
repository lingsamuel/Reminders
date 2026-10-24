using System.Windows;
using System.Windows.Controls;

namespace Reminders.Windows.Overlay;

internal sealed class CompletionButton : Button
{
    public static readonly DependencyProperty IsPreviewHoveredProperty = DependencyProperty.Register(
        nameof(IsPreviewHovered), typeof(bool), typeof(CompletionButton), new PropertyMetadata(false));

    public static readonly DependencyProperty IsPreviewPressedProperty = DependencyProperty.Register(
        nameof(IsPreviewPressed), typeof(bool), typeof(CompletionButton), new PropertyMetadata(false));

    public bool IsPreviewHovered
    {
        get => (bool)GetValue(IsPreviewHoveredProperty);
        set => SetValue(IsPreviewHoveredProperty, value);
    }

    public bool IsPreviewPressed
    {
        get => (bool)GetValue(IsPreviewPressedProperty);
        set => SetValue(IsPreviewPressedProperty, value);
    }

    public CompletionButton()
    {
        // 日常模式的输入来自独立窗口，转发状态与原生鼠标状态共用同一模板。
        SetResourceReference(StyleProperty, "OverlayCompletionButtonStyle");
    }
}
