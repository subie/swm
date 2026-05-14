using ScrollingWM.Core;

namespace ScrollingWM.Tests;

public class ResizeFocusedTests
{
    private static Strip MakeStrip(int n, int focused, int width)
    {
        var s = new Strip(new StripKey(Guid.Empty));
        for (var i = 0; i < n; i++)
            s.Append(new ManagedWindow(i + 1, $"app{i}.exe", "Class", width));
        s.SetFocus(focused);
        return s;
    }

    [Fact]
    public void GrowsFocusedByDelta()
    {
        var s = MakeStrip(3, focused: 1, width: 600);
        Commands.ResizeFocused(s, deltaPx: 120);
        Assert.Equal(720, s.Windows[1].WidthPx);
        Assert.Equal(600, s.Windows[0].WidthPx);
        Assert.Equal(600, s.Windows[2].WidthPx);
    }

    [Fact]
    public void ShrinksFocusedByDelta()
    {
        var s = MakeStrip(3, focused: 1, width: 600);
        Commands.ResizeFocused(s, deltaPx: -120);
        Assert.Equal(480, s.Windows[1].WidthPx);
    }

    [Fact]
    public void ClampsToMinWidth()
    {
        var s = MakeStrip(2, focused: 0, width: 400);
        Commands.ResizeFocused(s, deltaPx: -1000, minWidth: 200);
        Assert.Equal(200, s.Windows[0].WidthPx);
    }

    [Fact]
    public void NoOpForFullscreen()
    {
        var s = MakeStrip(2, focused: 0, width: 600);
        s.Windows[0].PreFullscreenWidth = 600;
        s.Windows[0].WidthPx = 1920;
        s.Windows[0].FullscreenMonitorLeft = 0;

        Commands.ResizeFocused(s, deltaPx: 120);
        Assert.Equal(1920, s.Windows[0].WidthPx); // unchanged
        Assert.True(s.Windows[0].Fullscreen);
    }

    [Fact]
    public void NoFocusedIsNoOp()
    {
        var s = new Strip(new StripKey(Guid.Empty));
        Commands.ResizeFocused(s, deltaPx: 100); // no throw
    }
}
