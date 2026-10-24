using Reminders.Windows.Platform;
using Xunit;

namespace Reminders.Windows.Tests;

public sealed class FullscreenPolicyTests
{
    [Theory]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(null, true)]
    public void FullscreenOnOverlayMonitorHides(int? notificationState, bool coversMonitor)
    {
        Assert.True(FullscreenVisibilityPolicy.ShouldHide(notificationState, false, new(1), new(1), coversMonitor));
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(null, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    public void FullscreenOnAnotherMonitorKeepsOverlayVisible(int? notificationState, bool coversMonitor)
    {
        Assert.False(FullscreenVisibilityPolicy.ShouldHide(notificationState, false, new(1), new(2), coversMonitor));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void OtherNotificationStatesAloneDoNotHideWindowedApplications(int? notificationState)
    {
        Assert.False(FullscreenVisibilityPolicy.ShouldHide(notificationState, false, new(1), new(1), false));
    }

    [Fact]
    public void BusyStateCanStillHideAnActualBorderlessFullscreenWindow()
    {
        Assert.True(FullscreenVisibilityPolicy.ShouldHide(2, false, new(1), new(1), true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ExcludedForegroundDoesNotHideOverlay(int? notificationState)
    {
        Assert.False(FullscreenVisibilityPolicy.ShouldHide(notificationState, true, new(1), new(1), true));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(0, 0)]
    public void MissingMonitorDoesNotHideOverlay(int foregroundMonitor, int overlayMonitor)
    {
        foreach (var state in new int?[] { null, 3, 4 })
            Assert.False(FullscreenVisibilityPolicy.ShouldHide(state, false, new(foregroundMonitor), new(overlayMonitor), true));
    }

    [Theory]
    [InlineData(false, 1, 1)]
    [InlineData(false, 1, 2)]
    [InlineData(true, 0, 0)]
    public void LockedOrUnavailableSessionHidesGlobally(bool excludedForeground, int foregroundMonitor, int overlayMonitor)
    {
        Assert.True(FullscreenVisibilityPolicy.ShouldHide(1, excludedForeground, new(foregroundMonitor), new(overlayMonitor), false));
    }

    [Theory]
    [InlineData(0, 0, 1920, 1080, 0, 0, 1920, 1080)]
    [InlineData(-1920, -1080, 0, 0, -1920, -1080, 0, 0)]
    [InlineData(-1930, -1090, 10, 10, -1920, -1080, 0, 0)]
    [InlineData(1, 1, 1919, 1079, 0, 0, 1920, 1080)]
    public void FullscreenGeometryCoversMonitorIncludingNegativeCoordinates(
        int left, int top, int right, int bottom, int monitorLeft, int monitorTop, int monitorRight, int monitorBottom)
    {
        Assert.True(FullscreenVisibilityPolicy.CoversMonitor(new(left, top, right, bottom), new(monitorLeft, monitorTop, monitorRight, monitorBottom)));
    }

    [Theory]
    [InlineData(0, 0, 1920, 1040, 0, 0, 1920, 1080)]
    [InlineData(-1920, -1080, 0, -40, -1920, -1080, 0, 0)]
    [InlineData(40, 0, 1920, 1080, 0, 0, 1920, 1080)]
    [InlineData(0, 0, 1880, 1080, 0, 0, 1920, 1080)]
    [InlineData(0, 40, 1920, 1080, 0, 0, 1920, 1080)]
    [InlineData(0, 0, 0, 1080, 0, 0, 1920, 1080)]
    [InlineData(0, 0, 1920, 0, 0, 0, 1920, 1080)]
    [InlineData(0, 0, 1920, 1080, 0, 0, 0, 1080)]
    [InlineData(0, 0, 1920, 1080, 0, 0, 1920, 0)]
    public void OrdinaryMaximizedAndInvalidBoundsDoNotCoverMonitor(
        int left, int top, int right, int bottom, int monitorLeft, int monitorTop, int monitorRight, int monitorBottom)
    {
        var coversMonitor = FullscreenVisibilityPolicy.CoversMonitor(new(left, top, right, bottom), new(monitorLeft, monitorTop, monitorRight, monitorBottom));
        Assert.False(coversMonitor);
        Assert.False(FullscreenVisibilityPolicy.ShouldHide(null, false, new(1), new(1), coversMonitor));
    }
}
