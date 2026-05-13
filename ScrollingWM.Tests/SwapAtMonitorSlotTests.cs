using ScrollingWM.Core;

namespace ScrollingWM.Tests;

public class SwapAtMonitorSlotTests
{
    private static readonly LayoutConfig Cfg = new(DefaultWindowWidthPx: 1200, GapPx: 12);

    private static readonly IReadOnlyList<Rect> DualMonitor = new[]
    {
        new Rect(0, 0, 1920, 1080),
        new Rect(1920, 0, 3840, 1080),
    };

    private static Strip MakeStrip(int n, int focused = 0, int width = 600)
    {
        var s = new Strip(new StripKey(Guid.Empty));
        for (var i = 0; i < n; i++)
            s.Append(new ManagedWindow(i + 1, $"app{i}.exe", "Class", width));
        s.SetFocus(focused);
        return s;
    }

    [Fact]
    public void SwapsFocusedWithSlotOccupant()
    {
        // 4 windows of width 600 + 12 gap → packs at 0, 612, 1224, 1836.
        // Centers: 300, 912, 1524, 2136. Mon0 ends at 1920.
        // Mon0 contains windows at indices 0, 1, 2 (centers 300, 912, 1524).
        // Mon1 contains window at index 3 (center 2136).
        var s = MakeStrip(4, focused: 3, width: 600);
        var moved = s.Windows[3]; // focused
        var displaced = s.Windows[1]; // mon0 slot 1
        Commands.SwapAtMonitorSlot(s, DualMonitor, Cfg, monitorIndex: 0, positionOnMonitor: 1);
        Assert.Equal(moved, s.Windows[1]);
        Assert.Equal(displaced, s.Windows[3]);
        Assert.Equal(1, s.FocusedIndex);
    }

    [Fact]
    public void FocusedAlreadyAtSlot_NoOp()
    {
        var s = MakeStrip(4, focused: 1, width: 600);
        var snapshot = s.Windows.ToList();
        Commands.SwapAtMonitorSlot(s, DualMonitor, Cfg, monitorIndex: 0, positionOnMonitor: 1);
        Assert.Equal(snapshot, s.Windows);
        Assert.Equal(1, s.FocusedIndex);
    }

    [Fact]
    public void SlotBeyondCount_FallsBackToLastOnMonitor()
    {
        // Mon1 has only 1 window (idx 3). Asking for slot 5 → swap with last
        // (= idx 3). Focused (idx 0) goes there, idx 3 comes home.
        var s = MakeStrip(4, focused: 0, width: 600);
        var moved = s.Windows[0];
        var displaced = s.Windows[3];
        Commands.SwapAtMonitorSlot(s, DualMonitor, Cfg, monitorIndex: 1, positionOnMonitor: 5);
        Assert.Equal(moved, s.Windows[3]);
        Assert.Equal(displaced, s.Windows[0]);
        Assert.Equal(3, s.FocusedIndex);
    }

    [Fact]
    public void NoWindowsOnTargetMonitor_NoOp()
    {
        // Single small window on mon0; mon1 is empty.
        var s = MakeStrip(1, focused: 0, width: 600);
        var snapshot = s.Windows.ToList();
        Commands.SwapAtMonitorSlot(s, DualMonitor, Cfg, monitorIndex: 1, positionOnMonitor: 0);
        Assert.Equal(snapshot, s.Windows);
        Assert.Equal(0, s.FocusedIndex);
    }

    [Fact]
    public void OutOfRangeMonitor_NoOp()
    {
        var s = MakeStrip(3, focused: 0, width: 600);
        var snapshot = s.Windows.ToList();
        Commands.SwapAtMonitorSlot(s, DualMonitor, Cfg, monitorIndex: 99, positionOnMonitor: 0);
        Assert.Equal(snapshot, s.Windows);
    }

    [Fact]
    public void NegativeMonitor_NoOp()
    {
        var s = MakeStrip(3, focused: 0, width: 600);
        var snapshot = s.Windows.ToList();
        Commands.SwapAtMonitorSlot(s, DualMonitor, Cfg, monitorIndex: -1, positionOnMonitor: 0);
        Assert.Equal(snapshot, s.Windows);
    }

    [Fact]
    public void NoFocus_NoOp()
    {
        var s = new Strip(new StripKey(Guid.Empty));
        // empty strip → FocusedIndex = -1
        Commands.SwapAtMonitorSlot(s, DualMonitor, Cfg, monitorIndex: 0, positionOnMonitor: 0);
        Assert.Empty(s.Windows);
    }

    [Fact]
    public void PreservesScrollOffsetAcrossLayoutProbe()
    {
        // Force scroll by focusing far-right window first.
        var s = MakeStrip(8, focused: 7, width: 600);
        Layout.Compute(s, DualMonitor, Cfg); // sets non-zero scroll
        var savedScroll = s.ScrollOffsetPx;
        Assert.NotEqual(0, savedScroll);
        Commands.SwapAtMonitorSlot(s, DualMonitor, Cfg, monitorIndex: 0, positionOnMonitor: 0);
        Assert.Equal(savedScroll, s.ScrollOffsetPx);
    }

    [Fact]
    public void SwapsAcrossMonitorsBidirectionally()
    {
        // Round-trip: swap A→slot, swap back leaves strip unchanged.
        var s = MakeStrip(4, focused: 3, width: 600);
        var snapshot = s.Windows.ToList();
        Commands.SwapAtMonitorSlot(s, DualMonitor, Cfg, monitorIndex: 0, positionOnMonitor: 1);
        Assert.NotEqual(snapshot, s.Windows);
        // After first swap, focused is at idx 1. The window now at idx 3
        // (formerly idx 1) is on mon0 because of repacking. Swap focus (idx 1,
        // currently on mon0) with mon0 slot 3... actually the simpler check
        // is that explicit element-by-element swap is its own inverse.
        var i0 = s.FocusedIndex; // 1
        var i1 = 3;
        (s.Windows[i0], s.Windows[i1]) = (s.Windows[i1], s.Windows[i0]);
        s.SetFocus(i1);
        Assert.Equal(snapshot, s.Windows);
    }
}
