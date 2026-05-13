namespace ScrollingWM.Core;

public sealed record LayoutConfig(int DefaultWindowWidthPx, int GapPx);

public static class Layout
{
    /// <summary>
    /// Single-reel layout. Windows are packed left-to-right at their declared
    /// <see cref="ManagedWindow.WidthPx"/> starting at <c>virtualLeft - scrollOffset</c>.
    /// Windows may straddle monitor bezels — that's expected and accepted.
    ///
    /// Scroll offset is only adjusted to keep the focused window visible somewhere
    /// on the virtual desktop (i.e. its rect overlaps the union of monitor work
    /// areas). No monitor-edge snapping, no bezel push.
    ///
    /// Skipped windows (e.g. minimized) consume no horizontal space but are not
    /// included in the result. Mutates <c>s.ScrollOffsetPx</c>.
    /// </summary>
    public static Dictionary<nint, Rect> Compute(Strip s, IReadOnlyList<Rect> monitors, LayoutConfig cfg, IReadOnlySet<nint>? skipHwnds = null)
    {
        var result = new Dictionary<nint, Rect>(s.Windows.Count);
        if (s.Windows.Count == 0 || monitors.Count == 0) return result;

        var skipped = new bool[s.Windows.Count];
        var visibleCount = 0;
        for (var i = 0; i < s.Windows.Count; i++)
        {
            skipped[i] = skipHwnds != null && skipHwnds.Contains(s.Windows[i].Hwnd);
            if (!skipped[i]) visibleCount++;
        }
        if (visibleCount == 0) return result;

        int minLeft = int.MaxValue, maxRight = int.MinValue;
        int minTop = int.MaxValue, maxBottom = int.MinValue;
        foreach (var m in monitors)
        {
            if (m.Left < minLeft) minLeft = m.Left;
            if (m.Right > maxRight) maxRight = m.Right;
            if (m.Top < minTop) minTop = m.Top;
            if (m.Bottom > maxBottom) maxBottom = m.Bottom;
        }
        var virtualLeft = minLeft;
        var virtualRight = maxRight;
        var virtualTop = minTop;
        var virtualHeight = maxBottom - minTop;

        var focused = s.Focused;

        int scrollOffset = s.ScrollOffsetPx;
        var fIdx = s.FocusedIndex;
        var focusedSkipped = fIdx < 0 || skipped[fIdx];
        var focusedWidth = focusedSkipped ? 0 : s.Windows[fIdx].WidthPx;

        var positions = new int[s.Windows.Count];

        void Pack()
        {
            var x = virtualLeft - scrollOffset;
            for (var i = 0; i < s.Windows.Count; i++)
            {
                positions[i] = x;
                if (skipped[i]) continue;
                x += s.Windows[i].WidthPx + cfg.GapPx;
            }
        }

        Pack();

        // Keep focused on the virtual desktop. If it falls off the right edge,
        // scroll right just enough to bring its right edge flush with virtualRight;
        // off the left edge, scroll left so its left edge flushes virtualLeft.
        // No monitor-edge snapping — straddling is fine.
        if (!focusedSkipped)
        {
            var fl = positions[fIdx];
            var fr = fl + focusedWidth;
            int delta = 0;
            if (fr > virtualRight) delta = fr - virtualRight;
            else if (fl < virtualLeft) delta = fl - virtualLeft;
            if (delta != 0)
            {
                scrollOffset += delta;
                Pack();
            }
        }

        // If the focused window is fullscreen, snap scroll so its left edge
        // aligns with the monitor it was fullscreened onto. Guarantees a
        // fullscreen window never straddles bezels and stays on its origin
        // monitor regardless of how its naive packed position landed.
        if (!focusedSkipped && focused != null && focused.Fullscreen && focused.FullscreenMonitorLeft.HasValue)
        {
            var fl = positions[fIdx];
            var snapDelta = fl - focused.FullscreenMonitorLeft.Value;
            if (snapDelta != 0)
            {
                scrollOffset += snapDelta;
                Pack();
            }
        }

        s.ScrollOffsetPx = scrollOffset;

        for (var i = 0; i < s.Windows.Count; i++)
        {
            if (skipped[i]) continue;
            var w = s.Windows[i];
            result[w.Hwnd] = Rect.FromSize(positions[i], virtualTop, w.WidthPx, virtualHeight);
        }
        return result;
    }
}
