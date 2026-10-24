using System;

namespace Reminders.Windows.Overlay;

internal readonly record struct OverlayMeasure(double Height, int VisibleRows, double RowHeight);

internal static class OverlayLayout
{
    internal const double VerticalPadding = 26;
    internal const double OverflowHeight = 24;
    internal static double RowHeightFor(double fontSize) => Math.Max(28, fontSize + 16);

    internal static OverlayMeasure Calculate(int taskCount, double fontSize, double savedHeight,
        double availableHeight, bool editing)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(taskCount);
        if (!double.IsFinite(fontSize) || fontSize <= 0 || !double.IsFinite(savedHeight) || savedHeight <= 0
            || !double.IsFinite(availableHeight) || availableHeight <= 0)
            throw new ArgumentException("Overlay 布局需要有效的字号、尺寸与工作区。");
        var rowHeight = RowHeightFor(fontSize);
        if (editing)
            return new(Math.Clamp(savedHeight, Math.Min(120, availableHeight), availableHeight), taskCount, rowHeight);

        int Capacity(double reserved) => (int)Math.Max(0, Math.Floor((availableHeight - VerticalPadding - reserved) / rowHeight));
        var capacity = Capacity(0);
        // 提示行自身也占高度，不能计算完可见行后才挤进已满的面板。
        if (taskCount > capacity) capacity = Capacity(OverflowHeight);
        var rows = Math.Min(taskCount, capacity);
        var height = VerticalPadding + rows * rowHeight + (rows < taskCount ? OverflowHeight : 0);
        return new(Math.Clamp(height, Math.Min(40, availableHeight), availableHeight), rows, rowHeight);
    }
}
