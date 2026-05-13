namespace ScrollingWM.Core;

public readonly record struct StripKey(Guid DesktopId);

public sealed record FloatRecord(ManagedWindow Window, int PreferredIndex, Rect FloatRect);

public sealed class Strip
{
    public StripKey Key { get; }
    public List<ManagedWindow> Windows { get; } = new();
    public Dictionary<nint, FloatRecord> Floated { get; } = new();
    public int FocusedIndex { get; private set; } = -1;
    public int ScrollOffsetPx { get; set; }

    public Strip(StripKey key) { Key = key; }

    public ManagedWindow? Focused =>
        FocusedIndex >= 0 && FocusedIndex < Windows.Count ? Windows[FocusedIndex] : null;

    public int IndexOf(nint hwnd)
    {
        for (var i = 0; i < Windows.Count; i++)
            if (Windows[i].Hwnd == hwnd) return i;
        return -1;
    }

    public void SetFocus(int index)
    {
        if (Windows.Count == 0) { FocusedIndex = -1; return; }
        FocusedIndex = Math.Clamp(index, 0, Windows.Count - 1);
    }

    public void Insert(int index, ManagedWindow w)
    {
        var clamped = Math.Clamp(index, 0, Windows.Count);
        Windows.Insert(clamped, w);
        if (FocusedIndex < 0) FocusedIndex = clamped;
        else if (clamped <= FocusedIndex) FocusedIndex++;
    }

    public void Append(ManagedWindow w) => Insert(Windows.Count, w);

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= Windows.Count) return;
        Windows.RemoveAt(index);
        if (Windows.Count == 0) { FocusedIndex = -1; return; }
        if (index < FocusedIndex) FocusedIndex--;
        else if (index == FocusedIndex) FocusedIndex = Math.Min(FocusedIndex, Windows.Count - 1);
    }
}
