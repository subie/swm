using ScrollingWM.Core;

namespace ScrollingWM.Tests;

public class CommandTests
{
    private static Strip MakeStrip(int n, int focused = 0)
    {
        var s = new Strip(new StripKey(Guid.Empty));
        for (var i = 0; i < n; i++)
            s.Append(new ManagedWindow(i + 1, $"app{i}.exe", "Class", 1200));
        s.SetFocus(focused);
        return s;
    }

    [Fact]
    public void FocusLeft_DecrementsIndex()
    {
        var s = MakeStrip(3, focused: 2);
        Commands.FocusLeft(s);
        Assert.Equal(1, s.FocusedIndex);
    }

    [Fact]
    public void FocusLeft_AtHome_NoOp()
    {
        var s = MakeStrip(3, focused: 0);
        Commands.FocusLeft(s);
        Assert.Equal(0, s.FocusedIndex);
    }

    [Fact]
    public void FocusRight_AtEnd_NoOp()
    {
        var s = MakeStrip(3, focused: 2);
        Commands.FocusRight(s);
        Assert.Equal(2, s.FocusedIndex);
    }

    [Fact]
    public void SwapRight_MovesWindowAndKeepsFocusOnIt()
    {
        var s = MakeStrip(3, focused: 0);
        var moved = s.Windows[0];
        Commands.SwapRight(s);
        Assert.Equal(moved, s.Windows[1]);
        Assert.Equal(1, s.FocusedIndex);
    }

    [Fact]
    public void MoveEnd_MovesFocusedToEnd()
    {
        var s = MakeStrip(4, focused: 1);
        var moved = s.Windows[1];
        Commands.MoveEnd(s);
        Assert.Equal(moved, s.Windows[3]);
        Assert.Equal(3, s.FocusedIndex);
    }

    [Fact]
    public void MoveHome_MovesFocusedToStart()
    {
        var s = MakeStrip(4, focused: 2);
        var moved = s.Windows[2];
        Commands.MoveHome(s);
        Assert.Equal(moved, s.Windows[0]);
        Assert.Equal(0, s.FocusedIndex);
    }

    [Fact]
    public void Float_RemovesFromTiledAndRecordsSlot()
    {
        var s = MakeStrip(3, focused: 1);
        var w = s.Windows[1];
        Commands.Float(s, new Rect(10, 10, 110, 110));
        Assert.Equal(2, s.Windows.Count);
        Assert.True(s.Floated.ContainsKey(w.Hwnd));
        Assert.Equal(1, s.Floated[w.Hwnd].PreferredIndex);
    }

    [Fact]
    public void Unfloat_RestoresAtPreferredIndex()
    {
        var s = MakeStrip(3, focused: 1);
        var w = s.Windows[1];
        Commands.Float(s, new Rect(0, 0, 100, 100));
        Commands.Unfloat(s, w.Hwnd);
        Assert.Equal(3, s.Windows.Count);
        Assert.Equal(w, s.Windows[1]);
        Assert.Equal(1, s.FocusedIndex);
        Assert.False(s.Floated.ContainsKey(w.Hwnd));
    }

    [Fact]
    public void ToggleFullscreen_StoresAndClearsRect()
    {
        var s = MakeStrip(2, focused: 0);
        var w = s.Focused!;
        Commands.ToggleFullscreen(s, new Rect(0, 0, 1920, 1080));
        Assert.True(w.Fullscreen);
        Commands.ToggleFullscreen(s, new Rect(0, 0, 1920, 1080));
        Assert.False(w.Fullscreen);
    }

    [Fact]
    public void FocusRight_FromMiddle_Increments()
    {
        var s = MakeStrip(3, focused: 1);
        Commands.FocusRight(s);
        Assert.Equal(2, s.FocusedIndex);
    }

    [Fact]
    public void FocusHome_JumpsToFirst()
    {
        var s = MakeStrip(4, focused: 3);
        Commands.FocusHome(s);
        Assert.Equal(0, s.FocusedIndex);
    }

    [Fact]
    public void FocusEnd_JumpsToLast()
    {
        var s = MakeStrip(4, focused: 0);
        Commands.FocusEnd(s);
        Assert.Equal(3, s.FocusedIndex);
    }

    [Fact]
    public void FocusHome_RespectsSkipPredicate()
    {
        var s = MakeStrip(4, focused: 3);
        // Skip indices 0 and 1 (hwnds 1 and 2).
        Commands.FocusHome(s, w => w.Hwnd <= 2);
        Assert.Equal(2, s.FocusedIndex);
    }

    [Fact]
    public void FocusEnd_RespectsSkipPredicate()
    {
        var s = MakeStrip(4, focused: 0);
        // Skip indices 2 and 3 (hwnds 3 and 4).
        Commands.FocusEnd(s, w => w.Hwnd >= 3);
        Assert.Equal(1, s.FocusedIndex);
    }

    [Fact]
    public void FocusRight_SkipsOverHiddenWindows()
    {
        var s = MakeStrip(4, focused: 0);
        // From idx 0, skip idx 1 → land on idx 2.
        Commands.FocusRight(s, w => w.Hwnd == 2);
        Assert.Equal(2, s.FocusedIndex);
    }

    [Fact]
    public void FocusLeft_SkipsOverHiddenWindows()
    {
        var s = MakeStrip(4, focused: 3);
        Commands.FocusLeft(s, w => w.Hwnd == 3);
        Assert.Equal(1, s.FocusedIndex);
    }

    [Fact]
    public void SwapLeft_SwapsAndKeepsFocus()
    {
        var s = MakeStrip(3, focused: 2);
        var moved = s.Windows[2];
        Commands.SwapLeft(s);
        Assert.Equal(moved, s.Windows[1]);
        Assert.Equal(1, s.FocusedIndex);
    }

    [Fact]
    public void SwapLeft_AtHome_NoOp()
    {
        var s = MakeStrip(3, focused: 0);
        var snapshot = s.Windows.ToList();
        Commands.SwapLeft(s);
        Assert.Equal(snapshot, s.Windows);
        Assert.Equal(0, s.FocusedIndex);
    }

    [Fact]
    public void SwapRight_AtEnd_NoOp()
    {
        var s = MakeStrip(3, focused: 2);
        var snapshot = s.Windows.ToList();
        Commands.SwapRight(s);
        Assert.Equal(snapshot, s.Windows);
        Assert.Equal(2, s.FocusedIndex);
    }

    [Fact]
    public void MoveHome_AlreadyAtHome_NoOp()
    {
        var s = MakeStrip(3, focused: 0);
        var snapshot = s.Windows.ToList();
        Commands.MoveHome(s);
        Assert.Equal(snapshot, s.Windows);
        Assert.Equal(0, s.FocusedIndex);
    }

    [Fact]
    public void MoveEnd_AlreadyAtEnd_NoOp()
    {
        var s = MakeStrip(3, focused: 2);
        var snapshot = s.Windows.ToList();
        Commands.MoveEnd(s);
        Assert.Equal(snapshot, s.Windows);
        Assert.Equal(2, s.FocusedIndex);
    }

    [Fact]
    public void Float_NoFocus_NoOp()
    {
        var s = new Strip(new StripKey(Guid.Empty));
        Commands.Float(s, new Rect(0, 0, 100, 100));
        Assert.Empty(s.Floated);
    }

    [Fact]
    public void Unfloat_UnknownHwnd_NoOp()
    {
        var s = MakeStrip(2, focused: 0);
        var snapshot = s.Windows.ToList();
        Commands.Unfloat(s, hwnd: 999);
        Assert.Equal(snapshot, s.Windows);
    }

    [Fact]
    public void Unfloat_PreservesPreferredIndex_EvenIfStripGrew()
    {
        var s = MakeStrip(3, focused: 1);
        var w = s.Windows[1];
        Commands.Float(s, new Rect(0, 0, 100, 100));
        // Append two more after floating; preferred index should still be 1.
        s.Append(new ManagedWindow(10, "x", "C", 100));
        s.Append(new ManagedWindow(11, "y", "C", 100));
        Commands.Unfloat(s, w.Hwnd);
        Assert.Equal(w, s.Windows[1]);
        Assert.Equal(1, s.FocusedIndex);
    }
}
