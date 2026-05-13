using ScrollingWM.Core;

namespace ScrollingWM.Win32;

public sealed class Applier
{
    private readonly Dictionary<nint, Rect> _last = new();

    public void Apply(IReadOnlyDictionary<nint, Rect> target, nint focusedHwnd, bool bringToFront = true)
    {
        // Compare against ACTUAL window position, not last-applied. Apps can
        // move their own windows behind our back (browsers during tab tear,
        // Win+Shift+arrow, snap layouts), so a "we already applied this" cache
        // lies. Querying GetVisibleRect per window keeps tiling correct.
        //
        // GetVisibleRect (DWM extended frame) is required here — GetWindowRect
        // includes Win11's invisible ~7px resize-border padding, so it would
        // never match a layout target like (0,0,1536,h) and we'd needlessly
        // re-issue SetWindowPos for every window on every layout pass.
        var changes = new List<(nint Hwnd, Rect Rect)>();
        foreach (var (hwnd, rect) in target)
        {
            var actual = WindowOps.GetVisibleRect(hwnd);
            if (actual != rect) changes.Add((hwnd, rect));
        }

        if (changes.Count > 0)
        {
            // Use BeginDeferWindowPos so all moves are committed atomically by DWM —
            // smoother than per-window SetWindowPos, especially during close animations.
            var batch = WindowOps.BeginBatch(changes.Count);
            var batchOk = batch != 0;
            if (batchOk)
            {
                foreach (var (hwnd, rect) in changes)
                {
                    batch = WindowOps.AddToBatch(batch, hwnd, rect);
                    if (batch == 0) { batchOk = false; break; }
                }
                if (batchOk) batchOk = WindowOps.EndBatch(batch);
            }
            if (!batchOk)
            {
                // Batch failed mid-way; fall back to per-window moves.
                foreach (var (hwnd, rect) in changes) WindowOps.Move(hwnd, rect);
            }
            foreach (var (hwnd, rect) in changes)
                Console.WriteLine($"swm:   move 0x{hwnd:X} -> ({rect.Left},{rect.Top} {rect.Width}x{rect.Height})");
            Console.WriteLine($"swm: applier: {changes.Count} moved (batched={batchOk})");
        }

        // Track every hwnd we've touched (for ShowAll cleanup).
        foreach (var hwnd in target.Keys) _last[hwnd] = target[hwnd];

        if (bringToFront && focusedHwnd != 0 && target.ContainsKey(focusedHwnd))
            WindowOps.RaiseAndFocus(focusedHwnd);
    }

    public void Forget(nint hwnd) => _last.Remove(hwnd);

    /// <summary>For Cleanup: ensure no window stays SW_HIDE'd from a prior daemon version.</summary>
    public void ShowAll()
    {
        foreach (var hwnd in _last.Keys) WindowOps.Show(hwnd);
    }
}
