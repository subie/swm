namespace ScrollingWM.Core;

public static class Restore
{
    // Make sure a restored rect lands somewhere the user can see and grab.
    // Captured "original" rects are wherever Windows spawned the app — for
    // mid-session windows that's often a position the app remembered from a
    // prior swm session where it was tiled offscreen. Restoring blindly would
    // leave them stranded. If the rect doesn't substantially overlap any
    // monitor's work area, recentre it on `primary` at its original size
    // (clamped to the monitor).
    public const int MinVisiblePx = 80;

    public static Rect EnsureOnscreen(Rect r, IReadOnlyList<Rect> monitors, Rect primary)
    {
        foreach (var m in monitors)
        {
            var ix = Math.Max(0, Math.Min(r.Right, m.Right) - Math.Max(r.Left, m.Left));
            var iy = Math.Max(0, Math.Min(r.Bottom, m.Bottom) - Math.Max(r.Top, m.Top));
            if (ix >= MinVisiblePx && iy >= MinVisiblePx) return r;
        }
        if (primary.Width <= 0 || primary.Height <= 0) return r;
        var w = Math.Min(r.Width > 0 ? r.Width : primary.Width / 2, primary.Width);
        var h = Math.Min(r.Height > 0 ? r.Height : primary.Height / 2, primary.Height);
        var left = primary.Left + (primary.Width - w) / 2;
        var top = primary.Top + (primary.Height - h) / 2;
        return Rect.FromSize(left, top, w, h);
    }
}
