using Reminders.Windows.Overlay;
using Xunit;

namespace Reminders.Protocol.Tests;

public sealed class OverlayLayoutTests
{
    [Fact]
    public void DailyPanelFitsOneRowInsteadOfRetainingTheEditorHeight()
    {
        var layout = OverlayLayout.Calculate(1, 16, 260, 1080, false);
        Assert.Equal(58, layout.Height);
        Assert.Equal(1, layout.VisibleRows);
    }

    [Fact]
    public void EditorRetainsSavedHeightAndScrollableTasks()
    {
        var layout = OverlayLayout.Calculate(20, 16, 260, 1080, true);
        Assert.Equal(260, layout.Height);
        Assert.Equal(20, layout.VisibleRows);
    }

    [Fact]
    public void OverflowReservesSpaceForItsOwnMessage()
    {
        var layout = OverlayLayout.Calculate(100, 16, 260, 200, false);
        Assert.Equal(4, layout.VisibleRows);
        Assert.Equal(178, layout.Height);
        Assert.True(layout.Height <= 200);
    }

    [Fact]
    public void ExactFitDoesNotReserveAnUnnecessaryOverflowRow()
    {
        var layout = OverlayLayout.Calculate(5, 16, 260, 186, false);
        Assert.Equal(5, layout.VisibleRows);
        Assert.Equal(186, layout.Height);
    }

    [Theory]
    [InlineData(10, 28)]
    [InlineData(16, 32)]
    [InlineData(40, 56)]
    public void RowsRemainLargeEnoughForTheHitTarget(double font, double rowHeight) =>
        Assert.Equal(rowHeight, OverlayLayout.Calculate(1, font, 260, 1080, false).RowHeight);

    [Fact]
    public void TooLittleSpaceShowsOnlyOverflowInsteadOfClippingAHitTarget()
    {
        var layout = OverlayLayout.Calculate(2, 40, 260, 100, false);
        Assert.Equal(0, layout.VisibleRows);
        Assert.Equal(50, layout.Height);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HeightNeverExceedsWorkArea(bool editing)
    {
        var layout = OverlayLayout.Calculate(100, 32, 3000, 500, editing);
        Assert.InRange(layout.Height, 1, 500);
    }

    [Fact]
    public void NoTasksHaveNoInteractiveRows() =>
        Assert.Equal(0, OverlayLayout.Calculate(0, 16, 260, 1080, false).VisibleRows);
}
