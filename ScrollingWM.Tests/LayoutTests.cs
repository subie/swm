using ScrollingWM.Core;

namespace ScrollingWM.Tests;

public class LayoutTests
{
    private static readonly LayoutConfig Cfg = new(DefaultWindowWidthPx: 1200, GapPx: 12);

    private static Strip MakeStrip(int n, int focused = 0, int width = 1200)
    {
        var s = new Strip(new StripKey(Guid.Empty));
        for (var i = 0; i < n; i++)
            s.Append(new ManagedWindow(i + 1, $"app{i}.exe", "Class", width));
        s.SetFocus(focused);
        return s;
    }

    private static readonly IReadOnlyList<Rect> SingleMonitor =
        new[] { new Rect(0, 0, 1920, 1080) };

    private static readonly IReadOnlyList<Rect> DualMonitor = new[]
    {
        new Rect(0, 0, 1920, 1080),
        new Rect(1920, 0, 3840, 1080),
    };

    [Fact]
    public void Empty_ReturnsEmpty()
    {
        var s = MakeStrip(0);
        Assert.Empty(Layout.Compute(s, SingleMonitor, Cfg));
    }

    [Fact]
    public void Single_PackedFromVirtualLeft()
    {
        var s = MakeStrip(1);
        var rects = Layout.Compute(s, SingleMonitor, Cfg);
        Assert.Equal(0, rects[1].Left);
        Assert.Equal(1080, rects[1].Height);
    }

    [Fact]
    public void Two_PackedFromLeft_NoScroll()
    {
        var s = MakeStrip(2, focused: 0);
        var rects = Layout.Compute(s, SingleMonitor, Cfg);
        Assert.Equal(0, s.ScrollOffsetPx);
        Assert.Equal(0, rects[1].Left);
        Assert.Equal(1212, rects[2].Left);
    }

    [Fact]
    public void Three_FocusOnLast_ScrollsRightSoFocusedRightFlushesVirtualRight()
    {
        var s = MakeStrip(3, focused: 2);
        var rects = Layout.Compute(s, SingleMonitor, Cfg);
        // Naive positions: 0, 1212, 2424. Focused (idx=2) right = 2424+1200 = 3624.
        // virtualRight = 1920 → delta = 3624-1920 = 1704. After scroll: focused
        // right flushes virtualRight at 1920.
        Assert.Equal(1704, s.ScrollOffsetPx);
        Assert.Equal(720, rects[3].Left);
        Assert.Equal(1920, rects[3].Right);
    }

    [Fact]
    public void Three_FocusBackToFirst_ScrollResetsToZero()
    {
        var s = MakeStrip(3, focused: 2);
        Layout.Compute(s, SingleMonitor, Cfg);    // scroll = 1704
        s.SetFocus(0);
        var rects = Layout.Compute(s, SingleMonitor, Cfg);
        Assert.Equal(0, s.ScrollOffsetPx);
        Assert.Equal(0, rects[1].Left);
    }

    [Fact]
    public void TwoMonitors_TwoWindows_PackedFromLeft_NoBezelPush()
    {
        var s = MakeStrip(2, focused: 0);
        var rects = Layout.Compute(s, DualMonitor, Cfg);
        // Straddling allowed: w1 packs at 1212 even though it crosses bezel at 1920.
        Assert.Equal(0, rects[1].Left);
        Assert.Equal(1212, rects[2].Left);
        Assert.Equal(2412, rects[2].Right);
    }

    [Fact]
    public void TwoMonitors_FourWindows_PackedFromLeft_NoBezelPush()
    {
        var s = MakeStrip(4, focused: 0);
        var rects = Layout.Compute(s, DualMonitor, Cfg);
        Assert.Equal(0, s.ScrollOffsetPx);
        Assert.Equal(0, rects[1].Left);
        Assert.Equal(1212, rects[2].Left);
        Assert.Equal(2424, rects[3].Left);
        Assert.Equal(3636, rects[4].Left);
    }

    [Fact]
    public void TwoMonitors_FocusOnSecondWindow_NoScrollWhileVisible()
    {
        var s = MakeStrip(3, focused: 1);
        var rects = Layout.Compute(s, DualMonitor, Cfg);
        // Focused (idx=1) at 1212, right=2412. Visible on virtual desktop (≤3840). No scroll.
        Assert.Equal(0, s.ScrollOffsetPx);
        Assert.Equal(1212, rects[2].Left);
    }

    [Fact]
    public void TwoMonitors_ThreeWindowsFocusOnLast_FitsWithinVirtualDesktop_NoScroll()
    {
        var s = MakeStrip(3, focused: 2);
        var rects = Layout.Compute(s, DualMonitor, Cfg);
        // Focused (idx=2) at 2424, right=3624 ≤ 3840 → no scroll needed.
        Assert.Equal(0, s.ScrollOffsetPx);
        Assert.Equal(0, rects[1].Left);
        Assert.Equal(1212, rects[2].Left);
        Assert.Equal(2424, rects[3].Left);
    }

    [Fact]
    public void TwoMonitors_FocusOnFifthWindow_ScrollsJustEnoughToBringRightEdgeOn()
    {
        var s = MakeStrip(5, focused: 4);
        var rects = Layout.Compute(s, DualMonitor, Cfg);
        // Naive idx=4 left = 4*1212 = 4848, right = 6048. virtualRight = 3840.
        // delta = 6048-3840 = 2208. After scroll: idx=4 right = 3840.
        Assert.Equal(2208, s.ScrollOffsetPx);
        Assert.Equal(2640, rects[5].Left);
        Assert.Equal(3840, rects[5].Right);
    }

    [Fact]
    public void TwoMonitors_FocusBackToFirst_ScrollResetsLeftEdgeFlush()
    {
        var s = MakeStrip(5, focused: 4);
        Layout.Compute(s, DualMonitor, Cfg);   // scroll = 2208
        s.SetFocus(0);
        var rects = Layout.Compute(s, DualMonitor, Cfg);
        Assert.Equal(0, s.ScrollOffsetPx);
        Assert.Equal(0, rects[1].Left);
    }

    [Fact]
    public void Fullscreen_FocusedCoversFirstMonitor()
    {
        var s = MakeStrip(3, focused: 1);
        Commands.ToggleFullscreen(s, SingleMonitor[0]);
        var rects = Layout.Compute(s, SingleMonitor, Cfg);
        Assert.Equal(SingleMonitor[0], rects[2]);
    }

    [Fact]
    public void Fullscreen_PreservesWidthAcrossFocusChange()
    {
        var s = MakeStrip(3, focused: 1);
        Commands.ToggleFullscreen(s, SingleMonitor[0]);
        s.SetFocus(0);
        var rects = Layout.Compute(s, SingleMonitor, Cfg);
        // The (formerly) fullscreen window keeps its monitor-width tile when focus moves away.
        Assert.Equal(1920, rects[2].Width);
        // Focused (idx 0) is at 0 with default width 1200.
        Assert.Equal(0, rects[1].Left);
        Assert.Equal(1200, rects[1].Width);
    }

    [Fact]
    public void Fullscreen_TogglingOffRestoresOriginalWidth()
    {
        var s = MakeStrip(3, focused: 1);
        var orig = s.Focused!.WidthPx;
        Commands.ToggleFullscreen(s, SingleMonitor[0]);
        Commands.ToggleFullscreen(s, SingleMonitor[0]);
        Assert.Equal(orig, s.Focused!.WidthPx);
    }

    [Fact]
    public void Fullscreen_DualMonitor_DoesNotStraddleBezel()
    {
        var s = MakeStrip(4, focused: 2);
        // Use the second monitor's width for the fullscreen tile.
        Commands.ToggleFullscreen(s, DualMonitor[1]);
        var rects = Layout.Compute(s, DualMonitor, Cfg);
        var fl = rects[3].Left; // hwnd 3 is index 2
        var fr = rects[3].Right;
        // Fullscreen window must align flush with one monitor's edges.
        var alignedLeft = fl == DualMonitor[0].Left || fl == DualMonitor[1].Left;
        var alignedRight = fr == DualMonitor[0].Right || fr == DualMonitor[1].Right;
        Assert.True(alignedLeft, $"fullscreen left {fl} did not align with a monitor edge");
        Assert.True(alignedRight, $"fullscreen right {fr} did not align with a monitor edge");
    }

    [Fact]
    public void Fullscreen_RemembersOriginMonitor_NotMisledByExpandedWidth()
    {
        // Window is small and sits on the right side of monitor 0. After fullscreen,
        // its expanded width pushes its center onto monitor 1 — but the user
        // toggled it on monitor 0, so it must snap to monitor 0.
        var s = MakeStrip(2, focused: 1, width: 400);
        // Toggle naming monitor 0 as the origin.
        Commands.ToggleFullscreen(s, DualMonitor[0]);
        var rects = Layout.Compute(s, DualMonitor, Cfg);
        Assert.Equal(DualMonitor[0].Left, rects[2].Left);
        Assert.Equal(DualMonitor[0].Right, rects[2].Right);
    }

    [Fact]
    public void Skipped_WindowsConsumeNoSpace()
    {
        var s = MakeStrip(3, focused: 0);
        var skip = new HashSet<nint> { 2 };
        var rects = Layout.Compute(s, DualMonitor, Cfg, skip);
        Assert.Equal(2, rects.Count);
        Assert.False(rects.ContainsKey(2));
        Assert.Equal(0, rects[1].Left);
        Assert.Equal(1212, rects[3].Left);
    }
}
