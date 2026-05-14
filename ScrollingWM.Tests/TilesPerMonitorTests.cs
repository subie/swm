using ScrollingWM.Core;

namespace ScrollingWM.Tests;

public class TilesPerMonitorTests
{
    private static readonly Rect Primary = new(0, 0, 1920, 1080);
    private static readonly IReadOnlyList<Rect> SingleMonitor = new[] { Primary };
    private static readonly IReadOnlyList<Rect> DualMonitor = new[]
    {
        Primary,
        new Rect(1920, 0, 3840, 1080),
    };

    private static Strip MakeStrip(int n, int focused = 0, int width = 960)
    {
        var s = new Strip(new StripKey(Guid.Empty));
        for (var i = 0; i < n; i++)
            s.Append(new ManagedWindow(i + 1, $"app{i}.exe", "Class", width));
        s.SetFocus(focused);
        return s;
    }

    [Fact]
    public void RightAnchorKeepsRightEdgeOfRightmostPrimaryTile()
    {
        // Initial: 4 tiles of 960 on dual monitor, packed at 0, 960, 1920, 2880.
        // Primary spans 0..1920. Tile 0 (0..960) and tile 1 (960..1920) overlap primary.
        // Right anchor → tile 1, right edge at 1920.
        var s = MakeStrip(4);

        Commands.SetAllToTilesPerMonitor(s, DualMonitor, Primary, tilesPerMonitor: 4, anchor: "right", includeFullscreen: false);

        // Each tile now 480. After re-layout, anchor (still index 1) right edge must be at 1920.
        var rects = Layout.Compute(s, DualMonitor, new LayoutConfig(0, 0));
        Assert.Equal(1920, rects[s.Windows[1].Hwnd].Right);
        Assert.All(s.Windows, w => Assert.Equal(480, w.WidthPx));
    }

    [Fact]
    public void LeftAnchorKeepsLeftEdgeOfLeftmostPrimaryTile()
    {
        // Same setup; left anchor → tile 0, left edge at 0.
        var s = MakeStrip(4);

        Commands.SetAllToTilesPerMonitor(s, DualMonitor, Primary, tilesPerMonitor: 4, anchor: "left", includeFullscreen: false);

        var rects = Layout.Compute(s, DualMonitor, new LayoutConfig(0, 0));
        Assert.Equal(0, rects[s.Windows[0].Hwnd].Left);
    }

    [Fact]
    public void FullscreenTilePreservedWhenIncludeFullscreenFalse()
    {
        var s = MakeStrip(3);
        // Fullscreen tile 1.
        s.Windows[1].PreFullscreenWidth = 960;
        s.Windows[1].WidthPx = Primary.Width;
        s.Windows[1].FullscreenMonitorLeft = 0;

        Commands.SetAllToTilesPerMonitor(s, SingleMonitor, Primary, tilesPerMonitor: 2, anchor: "right", includeFullscreen: false);

        Assert.Equal(960, s.Windows[0].WidthPx);
        Assert.Equal(Primary.Width, s.Windows[1].WidthPx); // unchanged
        Assert.True(s.Windows[1].Fullscreen);
        Assert.Equal(960, s.Windows[2].WidthPx);
    }

    [Fact]
    public void FullscreenTileResizedAndStateClearedWhenIncludeFullscreenTrue()
    {
        var s = MakeStrip(3);
        s.Windows[1].PreFullscreenWidth = 960;
        s.Windows[1].WidthPx = Primary.Width;
        s.Windows[1].FullscreenMonitorLeft = 0;

        Commands.SetAllToTilesPerMonitor(s, SingleMonitor, Primary, tilesPerMonitor: 2, anchor: "right", includeFullscreen: true);

        Assert.All(s.Windows, w => Assert.Equal(960, w.WidthPx));
        Assert.False(s.Windows[1].Fullscreen);
        Assert.Null(s.Windows[1].PreFullscreenWidth);
        Assert.Null(s.Windows[1].FullscreenMonitorLeft);
    }

    [Fact]
    public void NoTileOnPrimaryFallsBackToFocused()
    {
        // Build a strip whose tiles all sit on the secondary monitor at start
        // (scrolled far right). Anchor should fall back to focused.
        var s = MakeStrip(2, focused: 0, width: 480);
        s.ScrollOffsetPx = -2000; // pushes everything onto secondary monitor area

        // Probe to confirm tile 0 is off primary.
        var probe = Layout.Compute(s, DualMonitor, new LayoutConfig(0, 0));
        var beforeFocusedLeft = probe[s.Windows[0].Hwnd].Left;
        var beforeFocusedRight = probe[s.Windows[0].Hwnd].Right;
        Assert.True(beforeFocusedLeft >= Primary.Right || beforeFocusedRight <= Primary.Left);

        Commands.SetAllToTilesPerMonitor(s, DualMonitor, Primary, tilesPerMonitor: 4, anchor: "right", includeFullscreen: false);

        // Focused tile (index 0) right edge should land at where it was pre-resize.
        var after = Layout.Compute(s, DualMonitor, new LayoutConfig(0, 0));
        Assert.Equal(beforeFocusedRight, after[s.Windows[0].Hwnd].Right);
    }

    [Fact]
    public void EmptyStripIsNoOp()
    {
        var s = new Strip(new StripKey(Guid.Empty));
        // Should not throw.
        Commands.SetAllToTilesPerMonitor(s, SingleMonitor, Primary, tilesPerMonitor: 2, anchor: "right", includeFullscreen: false);
        Assert.Empty(s.Windows);
    }

    [Fact]
    public void InvalidNIsNoOp()
    {
        var s = MakeStrip(2);
        Commands.SetAllToTilesPerMonitor(s, SingleMonitor, Primary, tilesPerMonitor: 0, anchor: "right", includeFullscreen: false);
        Assert.All(s.Windows, w => Assert.Equal(960, w.WidthPx));
    }
}
