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
    public void FocusedSnapsToNearestSlotOnItsMonitor()
    {
        // 4 tiles of 960 packed at 0, 960, 1920, 2880. Focus tile 1 (left=960).
        // Resize to 4 tiles/monitor → newWidth = 480. Slots on primary (mon 0):
        // 0, 480, 960, 1440. Nearest to 960 is 960 itself.
        var s = MakeStrip(4, focused: 1);

        Commands.SetAllToTilesPerMonitor(s, DualMonitor, Primary, tilesPerMonitor: 4, includeFullscreen: false);

        var rects = Layout.Compute(s, DualMonitor, new LayoutConfig(0, 0));
        Assert.Equal(960, rects[s.Windows[1].Hwnd].Left);
        Assert.All(s.Windows, w => Assert.Equal(480, w.WidthPx));
    }

    [Fact]
    public void FocusedSnapsLeftWhenCloserToLeftSlot()
    {
        // Focused tile 1 starts at 960 (focused). Set scroll so it sits at 1000.
        // newWidth=480, slots: 0, 480, 960, 1440. Nearest to 1000 is 960.
        var s = MakeStrip(4, focused: 1);
        s.ScrollOffsetPx = -40; // tile 1 left becomes 1000

        Commands.SetAllToTilesPerMonitor(s, DualMonitor, Primary, tilesPerMonitor: 4, includeFullscreen: false);
        var rects = Layout.Compute(s, DualMonitor, new LayoutConfig(0, 0));
        Assert.Equal(960, rects[s.Windows[1].Hwnd].Left);
    }

    [Fact]
    public void FocusedSnapsRightWhenCloserToRightSlot()
    {
        // Tile 1 at 960; scroll -250 → tile 1 left = 1210. Slots: 0, 480, 960, 1440.
        // 1210 is closer to 1440 than 960 (dist 230 vs 250).
        var s = MakeStrip(4, focused: 1);
        s.ScrollOffsetPx = -250;

        Commands.SetAllToTilesPerMonitor(s, DualMonitor, Primary, tilesPerMonitor: 4, includeFullscreen: false);
        var rects = Layout.Compute(s, DualMonitor, new LayoutConfig(0, 0));
        Assert.Equal(1440, rects[s.Windows[1].Hwnd].Left);
    }

    [Fact]
    public void SnapsToFocusedTilesMonitor_NotPrimary()
    {
        // Focus tile 3 (initially at 2880, sits on monitor 1 = secondary 1920..3840).
        // newWidth=480; slots on secondary: 1920, 2400, 2880, 3360. Tile 3 left = 2880,
        // nearest slot = 2880.
        var s = MakeStrip(4, focused: 3);
        Commands.SetAllToTilesPerMonitor(s, DualMonitor, Primary, tilesPerMonitor: 4, includeFullscreen: false);
        var rects = Layout.Compute(s, DualMonitor, new LayoutConfig(0, 0));
        Assert.Equal(2880, rects[s.Windows[3].Hwnd].Left);
    }

    [Fact]
    public void FullscreenTilePreservedWhenIncludeFullscreenFalse()
    {
        var s = MakeStrip(3);
        s.Windows[1].PreFullscreenWidth = 960;
        s.Windows[1].WidthPx = Primary.Width;
        s.Windows[1].FullscreenMonitorLeft = 0;

        Commands.SetAllToTilesPerMonitor(s, SingleMonitor, Primary, tilesPerMonitor: 2, includeFullscreen: false);

        Assert.Equal(960, s.Windows[0].WidthPx);
        Assert.Equal(Primary.Width, s.Windows[1].WidthPx);
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

        Commands.SetAllToTilesPerMonitor(s, SingleMonitor, Primary, tilesPerMonitor: 2, includeFullscreen: true);

        Assert.All(s.Windows, w => Assert.Equal(960, w.WidthPx));
        Assert.False(s.Windows[1].Fullscreen);
        Assert.Null(s.Windows[1].PreFullscreenWidth);
        Assert.Null(s.Windows[1].FullscreenMonitorLeft);
    }

    [Fact]
    public void EmptyStripIsNoOp()
    {
        var s = new Strip(new StripKey(Guid.Empty));
        Commands.SetAllToTilesPerMonitor(s, SingleMonitor, Primary, tilesPerMonitor: 2, includeFullscreen: false);
        Assert.Empty(s.Windows);
    }

    [Fact]
    public void InvalidNIsNoOp()
    {
        var s = MakeStrip(2);
        Commands.SetAllToTilesPerMonitor(s, SingleMonitor, Primary, tilesPerMonitor: 0, includeFullscreen: false);
        Assert.All(s.Windows, w => Assert.Equal(960, w.WidthPx));
    }
}
