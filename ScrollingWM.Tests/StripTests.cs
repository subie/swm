using ScrollingWM.Core;

namespace ScrollingWM.Tests;

public class StripTests
{
    private static Strip Make(int n)
    {
        var s = new Strip(new StripKey(Guid.Empty));
        for (var i = 0; i < n; i++)
            s.Append(new ManagedWindow(i + 1, $"app{i}.exe", "Class", 1200));
        return s;
    }

    [Fact]
    public void NewStrip_NoFocus()
    {
        var s = new Strip(new StripKey(Guid.Empty));
        Assert.Equal(-1, s.FocusedIndex);
        Assert.Null(s.Focused);
    }

    [Fact]
    public void Append_FirstWindowBecomesFocused()
    {
        var s = new Strip(new StripKey(Guid.Empty));
        s.Append(new ManagedWindow(1, "a", "C", 100));
        Assert.Equal(0, s.FocusedIndex);
    }

    [Fact]
    public void Append_DoesNotChangeFocusOnceSet()
    {
        var s = Make(1);
        s.SetFocus(0);
        s.Append(new ManagedWindow(2, "b", "C", 100));
        Assert.Equal(0, s.FocusedIndex);
    }

    [Fact]
    public void InsertBeforeFocus_ShiftsFocusedIndex()
    {
        var s = Make(3);
        s.SetFocus(2);
        s.Insert(0, new ManagedWindow(99, "x", "C", 100));
        Assert.Equal(3, s.FocusedIndex);
        Assert.Equal((nint)99, s.Windows[0].Hwnd);
    }

    [Fact]
    public void InsertAtFocus_ShiftsFocusedIndex()
    {
        // Insert exactly at FocusedIndex pushes the focused window right by one.
        var s = Make(3);
        s.SetFocus(1);
        s.Insert(1, new ManagedWindow(99, "x", "C", 100));
        Assert.Equal(2, s.FocusedIndex);
    }

    [Fact]
    public void InsertAfterFocus_DoesNotShiftFocusedIndex()
    {
        var s = Make(3);
        s.SetFocus(0);
        s.Insert(2, new ManagedWindow(99, "x", "C", 100));
        Assert.Equal(0, s.FocusedIndex);
    }

    [Fact]
    public void Insert_OutOfRange_Clamps()
    {
        var s = Make(2);
        s.Insert(99, new ManagedWindow(99, "x", "C", 100));
        Assert.Equal(3, s.Windows.Count);
        Assert.Equal((nint)99, s.Windows[2].Hwnd);
    }

    [Fact]
    public void RemoveAtBeforeFocus_DecrementsFocus()
    {
        var s = Make(3);
        s.SetFocus(2);
        s.RemoveAt(0);
        Assert.Equal(1, s.FocusedIndex);
    }

    [Fact]
    public void RemoveAtFocus_StaysAtSameIndex()
    {
        var s = Make(3);
        s.SetFocus(1);
        s.RemoveAt(1);
        // Index 1 now refers to what was previously at index 2.
        Assert.Equal(1, s.FocusedIndex);
    }

    [Fact]
    public void RemoveAtFocus_AtEnd_ClampsToNewLast()
    {
        var s = Make(3);
        s.SetFocus(2);
        s.RemoveAt(2);
        Assert.Equal(1, s.FocusedIndex);
    }

    [Fact]
    public void RemoveAtAfterFocus_DoesNotChangeFocus()
    {
        var s = Make(3);
        s.SetFocus(0);
        s.RemoveAt(2);
        Assert.Equal(0, s.FocusedIndex);
    }

    [Fact]
    public void RemoveAt_LastWindow_ClearsFocus()
    {
        var s = Make(1);
        s.SetFocus(0);
        s.RemoveAt(0);
        Assert.Equal(-1, s.FocusedIndex);
        Assert.Null(s.Focused);
    }

    [Fact]
    public void RemoveAt_OutOfRange_NoOp()
    {
        var s = Make(2);
        s.SetFocus(1);
        s.RemoveAt(99);
        Assert.Equal(2, s.Windows.Count);
        Assert.Equal(1, s.FocusedIndex);
    }

    [Fact]
    public void SetFocus_Clamps()
    {
        var s = Make(3);
        s.SetFocus(99);
        Assert.Equal(2, s.FocusedIndex);
        s.SetFocus(-5);
        Assert.Equal(0, s.FocusedIndex);
    }

    [Fact]
    public void SetFocus_OnEmpty_StaysMinusOne()
    {
        var s = new Strip(new StripKey(Guid.Empty));
        s.SetFocus(0);
        Assert.Equal(-1, s.FocusedIndex);
    }

    [Fact]
    public void IndexOf_FindsHwnd()
    {
        var s = Make(3);
        Assert.Equal(1, s.IndexOf(2));
    }

    [Fact]
    public void IndexOf_MissingHwnd_ReturnsMinusOne()
    {
        var s = Make(3);
        Assert.Equal(-1, s.IndexOf(99));
    }
}
