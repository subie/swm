using ScrollingWM.Core;

namespace ScrollingWM.Tests;

public class RestoreTests
{
    private static readonly Rect Primary = new(0, 0, 1920, 1080);
    private static readonly Rect Secondary = new(1920, 0, 3840, 1080);
    private static readonly IReadOnlyList<Rect> Dual = new[] { Primary, Secondary };

    [Fact]
    public void OnscreenRect_PassesThrough()
    {
        var r = new Rect(100, 100, 800, 600);
        Assert.Equal(r, Restore.EnsureOnscreen(r, Dual, Primary));
    }

    [Fact]
    public void RectOnSecondaryMonitor_PassesThrough()
    {
        var r = new Rect(2100, 200, 2900, 800);
        Assert.Equal(r, Restore.EnsureOnscreen(r, Dual, Primary));
    }

    [Fact]
    public void StraddlingMonitorBoundary_PassesThrough()
    {
        // Spans both monitors; both halves clearly visible.
        var r = new Rect(1500, 100, 2400, 600);
        Assert.Equal(r, Restore.EnsureOnscreen(r, Dual, Primary));
    }

    [Fact]
    public void FarOffscreenRect_RecentersOnPrimary()
    {
        var r = new Rect(5000, 5000, 6000, 6000);
        var clamped = Restore.EnsureOnscreen(r, Dual, Primary);
        Assert.Equal(1000, clamped.Width);
        Assert.Equal(1000, clamped.Height);
        // Centred on primary
        Assert.Equal(Primary.Left + (Primary.Width - 1000) / 2, clamped.Left);
        Assert.Equal(Primary.Top + (Primary.Height - 1000) / 2, clamped.Top);
    }

    [Fact]
    public void NegativeOffscreenRect_RecentersOnPrimary()
    {
        // Hidden far to the left/top (e.g., remembered from a removed monitor).
        var r = new Rect(-3000, -2000, -2000, -1000);
        var clamped = Restore.EnsureOnscreen(r, Dual, Primary);
        Assert.True(clamped.Left >= Primary.Left);
        Assert.True(clamped.Top >= Primary.Top);
        Assert.True(clamped.Right <= Primary.Right);
        Assert.True(clamped.Bottom <= Primary.Bottom);
    }

    [Fact]
    public void BarelyOnscreenBelowThreshold_Recenters()
    {
        // Single-monitor case: only 50 px of the rect overlaps the monitor —
        // below MinVisiblePx (80), so the user couldn't grab the title bar.
        var r = new Rect(1870, 100, 2670, 700);
        var single = new[] { Primary };
        var clamped = Restore.EnsureOnscreen(r, single, Primary);
        Assert.NotEqual(r, clamped);
    }

    [Fact]
    public void RectLargerThanMonitor_ClampedToMonitor()
    {
        var r = new Rect(-5000, -5000, 9000, 9000);
        // Has overlap > MinVisiblePx with both monitors → passes through unchanged.
        // (Verifies we don't try to "shrink to fit" already-visible rects.)
        Assert.Equal(r, Restore.EnsureOnscreen(r, Dual, Primary));
    }

    [Fact]
    public void OffscreenLargerThanMonitor_ShrunkAndCentered()
    {
        var r = new Rect(5000, 5000, 9000, 9000);
        var clamped = Restore.EnsureOnscreen(r, Dual, Primary);
        Assert.True(clamped.Width <= Primary.Width);
        Assert.True(clamped.Height <= Primary.Height);
        Assert.True(clamped.Left >= Primary.Left);
        Assert.True(clamped.Right <= Primary.Right);
    }

    [Fact]
    public void NoMonitors_PassesThrough()
    {
        var r = new Rect(100, 100, 800, 600);
        Assert.Equal(r, Restore.EnsureOnscreen(r, Array.Empty<Rect>(), default));
    }
}
