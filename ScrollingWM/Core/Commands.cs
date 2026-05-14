namespace ScrollingWM.Core;

public static class Commands
{
    private static int NextVisible(Strip s, int from, int dir, Predicate<ManagedWindow>? skip)
    {
        for (var i = from; i >= 0 && i < s.Windows.Count; i += dir)
            if (skip == null || !skip(s.Windows[i])) return i;
        return -1;
    }

    public static void FocusLeft(Strip s, Predicate<ManagedWindow>? skip = null)
    {
        if (s.FocusedIndex <= 0) return;
        var i = NextVisible(s, s.FocusedIndex - 1, -1, skip);
        if (i >= 0) s.SetFocus(i);
    }

    public static void FocusRight(Strip s, Predicate<ManagedWindow>? skip = null)
    {
        if (s.FocusedIndex < 0 || s.FocusedIndex >= s.Windows.Count - 1) return;
        var i = NextVisible(s, s.FocusedIndex + 1, +1, skip);
        if (i >= 0) s.SetFocus(i);
    }

    public static void FocusHome(Strip s, Predicate<ManagedWindow>? skip = null)
    {
        var i = NextVisible(s, 0, +1, skip);
        if (i >= 0) s.SetFocus(i);
    }

    public static void FocusEnd(Strip s, Predicate<ManagedWindow>? skip = null)
    {
        var i = NextVisible(s, s.Windows.Count - 1, -1, skip);
        if (i >= 0) s.SetFocus(i);
    }

    public static void SwapLeft(Strip s, Predicate<ManagedWindow>? skip = null)
    {
        var i = s.FocusedIndex;
        if (i <= 0) return;
        var j = NextVisible(s, i - 1, -1, skip);
        if (j < 0) return;
        (s.Windows[j], s.Windows[i]) = (s.Windows[i], s.Windows[j]);
        s.SetFocus(j);
    }

    public static void SwapRight(Strip s, Predicate<ManagedWindow>? skip = null)
    {
        var i = s.FocusedIndex;
        if (i < 0 || i >= s.Windows.Count - 1) return;
        var j = NextVisible(s, i + 1, +1, skip);
        if (j < 0) return;
        (s.Windows[i], s.Windows[j]) = (s.Windows[j], s.Windows[i]);
        s.SetFocus(j);
    }

    /// <summary>
    /// Swap the focused window with the window currently rendered at
    /// <paramref name="positionOnMonitor"/> on monitor index <paramref name="monitorIndex"/>
    /// (left-to-right, 0-indexed). xmonad-style "swap-master": promote a window
    /// into a fixed visible slot. If fewer windows are on that monitor than the
    /// requested slot, swaps with the last one. No-op when focused is already
    /// the target. ScrollOffsetPx is preserved across the layout probe.
    /// </summary>
    public static void SwapAtMonitorSlot(Strip s, IReadOnlyList<Rect> monitors, LayoutConfig cfg,
        int monitorIndex, int positionOnMonitor, Predicate<ManagedWindow>? skip = null)
    {
        if (s.FocusedIndex < 0) return;
        if (monitorIndex < 0 || monitorIndex >= monitors.Count) return;

        var skipSet = new HashSet<nint>();
        if (skip != null)
            foreach (var w in s.Windows)
                if (skip(w)) skipSet.Add(w.Hwnd);

        var savedScroll = s.ScrollOffsetPx;
        var rects = Layout.Compute(s, monitors, cfg, skipSet);
        s.ScrollOffsetPx = savedScroll;

        var mon = monitors[monitorIndex];
        var onMon = new List<(int idx, int left)>();
        for (var i = 0; i < s.Windows.Count; i++)
        {
            if (!rects.TryGetValue(s.Windows[i].Hwnd, out var r)) continue;
            var cx = (r.Left + r.Right) / 2;
            if (cx >= mon.Left && cx <= mon.Right) onMon.Add((i, r.Left));
        }
        if (onMon.Count == 0) return;
        onMon.Sort((a, b) => a.left.CompareTo(b.left));

        var slot = Math.Min(positionOnMonitor, onMon.Count - 1);
        var targetIdx = onMon[slot].idx;
        if (targetIdx == s.FocusedIndex) return;

        var i0 = s.FocusedIndex;
        (s.Windows[i0], s.Windows[targetIdx]) = (s.Windows[targetIdx], s.Windows[i0]);
        s.SetFocus(targetIdx);
    }

    public static void MoveHome(Strip s)
    {
        var i = s.FocusedIndex;
        if (i <= 0) return;
        var w = s.Windows[i];
        s.Windows.RemoveAt(i);
        s.Windows.Insert(0, w);
        s.SetFocus(0);
    }

    public static void MoveEnd(Strip s)
    {
        var i = s.FocusedIndex;
        if (i < 0 || i == s.Windows.Count - 1) return;
        var w = s.Windows[i];
        s.Windows.RemoveAt(i);
        s.Windows.Add(w);
        s.SetFocus(s.Windows.Count - 1);
    }

    public static void Float(Strip s, Rect currentRect)
    {
        var focused = s.Focused;
        if (focused == null) return;
        var idx = s.FocusedIndex;
        s.RemoveAt(idx);
        s.Floated[focused.Hwnd] = new FloatRecord(focused, idx, currentRect);
    }

    public static void Unfloat(Strip s, nint hwnd)
    {
        if (!s.Floated.TryGetValue(hwnd, out var rec)) return;
        s.Floated.Remove(hwnd);
        s.Insert(rec.PreferredIndex, rec.Window);
        s.SetFocus(s.IndexOf(hwnd));
    }

    public static void ToggleFullscreen(Strip s, Rect monitorRect)
    {
        var w = s.Focused;
        if (w == null) return;
        if (w.Fullscreen)
        {
            w.WidthPx = w.PreFullscreenWidth!.Value;
            w.PreFullscreenWidth = null;
            w.FullscreenMonitorLeft = null;
        }
        else
        {
            w.PreFullscreenWidth = w.WidthPx;
            w.WidthPx = monitorRect.Width;
            w.FullscreenMonitorLeft = monitorRect.Left;
        }
    }

    /// <summary>
    /// Bulk-resize every tile in the strip to <c>primary.Width / tilesPerMonitor</c>,
    /// then nudge <see cref="Strip.ScrollOffsetPx"/> so a chosen anchor tile's
    /// edge stays at the same screen X. Anchor = the rightmost (or leftmost,
    /// per <paramref name="anchor"/>) tile currently overlapping the primary
    /// monitor's work area; falls back to the focused tile if none qualify.
    ///
    /// When <paramref name="includeFullscreen"/> is false, fullscreen tiles
    /// keep their fullscreen state and width. When true, fullscreen state is
    /// cleared on every tile and they're resized like the rest.
    /// </summary>
    public static void SetAllToTilesPerMonitor(Strip s, IReadOnlyList<Rect> monitors, Rect primary,
        int tilesPerMonitor, string anchor, bool includeFullscreen)
    {
        if (s.Windows.Count == 0 || tilesPerMonitor <= 0 || primary.Width <= 0 || monitors.Count == 0) return;
        var newWidth = primary.Width / tilesPerMonitor;
        if (newWidth <= 0) return;
        var anchorRight = !string.Equals(anchor, "left", StringComparison.OrdinalIgnoreCase);

        // 1. Probe current layout to pick an anchor and record its edge X.
        var savedScroll = s.ScrollOffsetPx;
        var rects = Layout.Compute(s, monitors, new LayoutConfig(0, 0));
        s.ScrollOffsetPx = savedScroll;

        int anchorIdx = -1;
        int anchorEdgeX = 0;
        var bestExtreme = anchorRight ? int.MinValue : int.MaxValue;
        for (var i = 0; i < s.Windows.Count; i++)
        {
            if (!rects.TryGetValue(s.Windows[i].Hwnd, out var r)) continue;
            // Tile must overlap primary work area (not just touch).
            if (r.Right <= primary.Left || r.Left >= primary.Right) continue;
            var key = anchorRight ? r.Right : r.Left;
            var better = anchorRight ? key > bestExtreme : key < bestExtreme;
            if (better) { bestExtreme = key; anchorIdx = i; anchorEdgeX = key; }
        }
        if (anchorIdx < 0)
        {
            anchorIdx = s.FocusedIndex;
            if (anchorIdx >= 0 && rects.TryGetValue(s.Windows[anchorIdx].Hwnd, out var fr))
                anchorEdgeX = anchorRight ? fr.Right : fr.Left;
            else
                return; // nothing laid out; bail
        }

        // 2. Resize every tile.
        foreach (var w in s.Windows)
        {
            if (w.Fullscreen && !includeFullscreen) continue;
            if (includeFullscreen && w.Fullscreen)
            {
                w.PreFullscreenWidth = null;
                w.FullscreenMonitorLeft = null;
            }
            w.WidthPx = newWidth;
        }

        // 3. Re-lay-out, then nudge scroll so anchor edge lands at recorded X.
        var savedScroll2 = s.ScrollOffsetPx;
        var rects2 = Layout.Compute(s, monitors, new LayoutConfig(0, 0));
        s.ScrollOffsetPx = savedScroll2;
        if (rects2.TryGetValue(s.Windows[anchorIdx].Hwnd, out var ar))
        {
            var newEdge = anchorRight ? ar.Right : ar.Left;
            s.ScrollOffsetPx += newEdge - anchorEdgeX;
        }
    }
}
