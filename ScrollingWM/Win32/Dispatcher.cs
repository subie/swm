using System.Text.Json;
using ScrollingWM.Core;
using ScrollingWM.Rules;
using ScrollingWM.UI;

namespace ScrollingWM.Win32;

public static class StateFile
{
    public static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".swm", "original-rects.json");

    public static void Write(IReadOnlyDictionary<nint, Rect> rects)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(Path)!;
            Directory.CreateDirectory(dir);
            var dto = rects.ToDictionary(
                kv => kv.Key.ToInt64().ToString(),
                kv => new[] { kv.Value.Left, kv.Value.Top, kv.Value.Right, kv.Value.Bottom });
            File.WriteAllText(Path, JsonSerializer.Serialize(dto));
        }
        catch (Exception ex) { Console.Error.WriteLine($"swm: state write failed: {ex.Message}"); }
    }

    public static void Delete()
    {
        try { if (File.Exists(Path)) File.Delete(Path); } catch { }
    }
}

public sealed class Dispatcher
{
    private readonly Config _config;
    private readonly Applier _applier = new();
    private readonly Dictionary<StripKey, Strip> _strips = new();
    private readonly Dictionary<nint, StripKey> _hwndToStrip = new();
    private readonly HashSet<StripKey> _applied = new();
    private readonly Dictionary<nint, Rect> _originalRects = new();
    private readonly string _selfExe;
    private readonly uint? _highlightColor;
    private StripKey? _lastActiveStripKey;
    private nint _draggingHwnd;
    private Rect _dragStartRect;
    private bool _dragStartCaptured;
    private nint _highlighted;
    private StripKey? _lastSeenCurrentDesktop;
    private DateTime _lastHighlightKeepalive = DateTime.MinValue;
    private static readonly TimeSpan HighlightKeepaliveInterval = TimeSpan.FromMilliseconds(200);
    private DateTime _lastForegroundReApply = DateTime.MinValue;
    private static readonly TimeSpan ForegroundReApplyDebounce = TimeSpan.FromMilliseconds(150);
    private DateTime _lastStrayMigration = DateTime.MinValue;
    private static readonly TimeSpan StrayMigrationInterval = TimeSpan.FromMilliseconds(500);
    private readonly OsdWindow _osd = new();
    // Windows whose CREATE/SHOW arrived while the user was holding the left
    // mouse button — almost always a tab tear (browser is mid-drag,
    // repositioning the new window every frame to follow the cursor). We
    // can't track these eagerly: our SetWindowPos on the source tile pulls
    // it out from under the cursor (sometimes re-docking the tab), and our
    // SetWindowPos on the new window races the browser's drag, leaving a
    // gap. Defer until the mouse releases — Poll() drains this set.
    private readonly HashSet<nint> _pendingTearAdopt = new();

    public Dispatcher(Config config, string selfExe)
    {
        _config = config;
        _selfExe = selfExe;
        _highlightColor = Config.ParseColor(config.FocusColor);
    }

    /// <summary>
    /// Show the transient "N / M" OSD centered on the focused window's monitor.
    /// Caller invokes this only on user-driven focus index changes (focus/swap/move
    /// commands, or HandleForeground when a click on a tile shifts the index)
    /// — never from layout-only events like minimize-retile or drag-end.
    /// </summary>
    private void ShowFocusOsd(Strip s)
    {
        var f = s.Focused;
        if (f == null || s.Windows.Count == 0) return;
        // Exclude minimized windows from both numerator and denominator.
        var total = 0;
        var pos = 0;
        for (var i = 0; i < s.Windows.Count; i++)
        {
            if (WindowOps.IsMinimized(s.Windows[i].Hwnd)) continue;
            total++;
            if (i == s.FocusedIndex) pos = total;
        }
        if (total == 0 || pos == 0) return;
        var mon = Monitors.WorkArea(Monitors.MonitorFor(f.Hwnd));
        if (mon.Width <= 0)
        {
            mon = Monitors.PrimaryWorkArea();
            if (mon.Width <= 0) return;
        }
        _osd.Show($"{pos} / {total}", mon);
    }

    private void UpdateHighlight(nint focused)
    {
        if (_highlightColor is not uint color) return;
        if (_highlighted == focused) return;
        if (_highlighted != 0) WindowOps.ClearHighlight(_highlighted);
        if (focused != 0) WindowOps.SetHighlight(focused, color);
        _highlighted = focused;
        // Force the keep-alive in PollForeground to re-paint within 200ms
        // rather than waiting up to a full interval from an arbitrary baseline.
        _lastHighlightKeepalive = DateTime.MinValue;
    }

    /// <summary>
    /// Catches up on foreground changes that didn't fire EVENT_SYSTEM_FOREGROUND.
    /// Win11 silently leaves the prior desktop's window as foreground across a
    /// virtual-desktop switch (no FOREGROUND event), so without polling, the
    /// highlight border stays painted on the (now hidden) prior-desktop window
    /// and the new desktop's focused tile shows no border until focus moves.
    /// Cheap: GetForegroundWindow is a single Win32 call; HandleForeground
    /// is idempotent when nothing changed.
    /// </summary>
    /// <summary>
    /// Per-tick polling for state Windows doesn't notify us about reliably:
    ///   1. Virtual-desktop switches. Win11 sometimes changes the current
    ///      desktop without firing FOREGROUND and without changing the
    ///      foreground hwnd at all, so the only reliable detection is to
    ///      probe IsOnCurrentDesktop on representative tracked windows.
    ///   2. Highlight border decay. Windows clobbers DWMWA_BORDER_COLOR
    ///      across desktop-switch animations and other repaints; periodically
    ///      re-applying it is cheap and keeps it stable.
    /// In-desktop focus changes still come through EVENT_SYSTEM_FOREGROUND,
    /// which HandleForeground processes — no polling needed for that path.
    /// </summary>
    public void Poll()
    {
        // (1) Desktop-change detection.
        StripKey? currentKey = null;
        foreach (var s in _strips.Values)
        {
            if (s.Windows.Count == 0) continue;
            try
            {
                if (VirtualDesktops.IsOnCurrentDesktop(s.Windows[0].Hwnd))
                {
                    currentKey = s.Key;
                    break;
                }
            }
            catch { }
        }
        if (currentKey is StripKey ck && !ck.Equals(_lastSeenCurrentDesktop))
        {
            _lastSeenCurrentDesktop = ck;
            var s = _strips[ck];
            var focusedHwnd = s.Focused?.Hwnd ?? 0;
            if (focusedHwnd != 0)
            {
                _lastActiveStripKey = ck;
                UpdateHighlight(focusedHwnd);
                // bringToFront=true: on a virtual-desktop switch the OS may
                // have foregrounded a different window (or none). We must
                // actively activate our focused tile so it receives input.
                // HandleForeground deliberately won't do this — it assumes the
                // hwnd is already foreground.
                ReApply(s, bringToFront: true);
                _applied.Add(ck);
            }
        }

        // (2) Highlight keep-alive.
        if (_highlighted != 0 && _highlightColor is uint color)
        {
            var now = DateTime.UtcNow;
            if (now - _lastHighlightKeepalive >= HighlightKeepaliveInterval)
            {
                _lastHighlightKeepalive = now;
                try { WindowOps.SetHighlight(_highlighted, color); } catch { }
            }
        }

        // (3) Deferred tear-adoption. Once the user releases the mouse, the
        // browser's drag loop is over and it's safe to track + retile.
        if (_pendingTearAdopt.Count > 0 && !WindowOps.IsLeftMouseDown())
        {
            var pending = _pendingTearAdopt.ToArray();
            _pendingTearAdopt.Clear();
            var toReapply = new HashSet<StripKey>();
            foreach (var hwnd in pending)
            {
                if (TryTrack(hwnd) && _hwndToStrip.TryGetValue(hwnd, out var k))
                    toReapply.Add(k);
            }
            foreach (var k in toReapply) ReApply(_strips[k], bringToFront: false);
        }

        // (4) Stray-window migration. Windows can move between virtual
        // desktops without firing any event we observe (Win+Ctrl+Shift+Arrow,
        // Task View drag, "Move to" context menu, third-party tools). If a
        // tracked window's current desktop no longer matches its strip's key,
        // focus rotation on the old strip would jump us to that hwnd and the
        // OS would switch desktops. Periodically sweep and migrate strays.
        var now2 = DateTime.UtcNow;
        if (now2 - _lastStrayMigration >= StrayMigrationInterval)
        {
            _lastStrayMigration = now2;
            ReapDeadWindows();
            MigrateStrayWindows();
        }
    }

    /// <summary>
    /// Untrack any tracked hwnd that no longer refers to a live window.
    /// EVENT_OBJECT_DESTROY is unreliable when a process exits abruptly or
    /// some Electron close paths skip the per-window destroy notification —
    /// the dead hwnd then lingers in the strip, leaving a phantom slot in
    /// focus rotation and a visible gap in the layout.
    /// </summary>
    private void ReapDeadWindows()
    {
        List<nint>? dead = null;
        foreach (var hwnd in _hwndToStrip.Keys)
        {
            if (!WindowOps.Exists(hwnd)) (dead ??= new()).Add(hwnd);
        }
        if (dead is null) return;
        foreach (var hwnd in dead)
        {
            Console.WriteLine($"swm: reaped dead hwnd 0x{hwnd:X}");
            Untrack(hwnd);
        }
    }

    /// <summary>
    /// Move any tracked window whose actual virtual desktop differs from its
    /// strip's key into the correct strip. Called on a slow timer from Poll();
    /// COM calls into the desktop manager are not free, so we throttle.
    /// </summary>
    private void MigrateStrayWindows()
    {
        // Snapshot to avoid mutating a strip's list while we iterate it.
        var moves = new List<(nint hwnd, StripKey from, StripKey to, bool floated)>();
        foreach (var s in _strips.Values)
        {
            foreach (var w in s.Windows)
            {
                Guid actual;
                try { actual = VirtualDesktops.GetDesktopId(w.Hwnd); }
                catch { continue; }
                if (actual == Guid.Empty) continue;
                if (actual != s.Key.DesktopId)
                    moves.Add((w.Hwnd, s.Key, new StripKey(actual), false));
            }
            foreach (var (hwnd, _) in s.Floated)
            {
                Guid actual;
                try { actual = VirtualDesktops.GetDesktopId(hwnd); }
                catch { continue; }
                if (actual == Guid.Empty) continue;
                if (actual != s.Key.DesktopId)
                    moves.Add((hwnd, s.Key, new StripKey(actual), true));
            }
        }
        if (moves.Count == 0) return;

        var touched = new HashSet<StripKey>();
        foreach (var (hwnd, from, to, floated) in moves)
        {
            var oldStrip = _strips[from];
            ManagedWindow w;
            FloatRecord? fr = null;
            if (floated)
            {
                if (!oldStrip.Floated.TryGetValue(hwnd, out var rec)) continue;
                fr = rec; w = rec.Window;
                oldStrip.Floated.Remove(hwnd);
            }
            else
            {
                var idx = oldStrip.IndexOf(hwnd);
                if (idx < 0) continue;
                w = oldStrip.Windows[idx];
                oldStrip.RemoveAt(idx);
            }
            var newStrip = GetOrCreate(to);
            if (floated && fr is not null)
                newStrip.Floated[hwnd] = fr;
            else
                newStrip.Append(w);
            _hwndToStrip[hwnd] = to;
            touched.Add(from);
            touched.Add(to);
            Console.WriteLine($"swm: migrated 0x{hwnd:X} desk {from.DesktopId} -> {to.DesktopId} (floated={floated})");
        }
        foreach (var k in touched)
            if (_strips.TryGetValue(k, out var s))
                ReApply(s, bringToFront: false);
    }

    /// <summary>
    /// Handle the minimize/restore lifecycle for a tracked window.
    /// On minimize: re-tile so other tiles fill the gap. The DWM border on
    /// the minimized hwnd is gone with the window, so drop it from our cache.
    /// On restore: claim focus back to the restored tile (this is what the
    /// user expects since they explicitly un-minimized it), then re-tile;
    /// the border re-paint happens via UpdateHighlight inside ReApply.
    /// </summary>
    private void HandleMinimize(nint hwnd, bool restored)
    {
        if (!_hwndToStrip.TryGetValue(hwnd, out var key)) return;
        var s = _strips[key];

        if (_highlighted == hwnd) _highlighted = 0;

        if (restored)
        {
            var idx = s.IndexOf(hwnd);
            if (idx >= 0) s.SetFocus(idx);
            ReApply(s);
            if (idx >= 0)
            {
                WindowOps.RaiseAndFocus(hwnd);
                ShowFocusOsd(s);
            }
        }
        else
        {
            ReApply(s);
        }
    }

    public void DiscoverExisting()
    {
        var seen = 0;
        var tracked = 0;
        foreach (var hwnd in WindowOps.EnumerateTopLevel())
        {
            seen++;
            if (TryTrack(hwnd)) tracked++;
        }
        Console.WriteLine($"swm: discovery: {seen} top-level windows, {tracked} tracked, {_strips.Count} strip(s)");
        foreach (var s in _strips.Values)
        {
            Console.WriteLine($"swm: strip desk={s.Key.DesktopId}: {s.Windows.Count} tiled, {s.Floated.Count} floated");
            foreach (var w in s.Windows)
                Console.WriteLine($"swm:   tiled hwnd=0x{w.Hwnd:X} exe={w.ExeName} title='{WindowOps.Title(w.Hwnd)}'");
            foreach (var (h, r) in s.Floated)
                Console.WriteLine($"swm:   float hwnd=0x{h:X} exe={r.Window.ExeName} title='{WindowOps.Title(h)}'");
        }

        // Only apply layout to the strip on the *current* desktop. Other desktops get
        // applied when the user switches to them (HandleForeground).
        var currentDesktop = VirtualDesktops.GetDesktopId(WindowOps.Foreground());
        foreach (var s in _strips.Values)
        {
            if (s.Key.DesktopId == currentDesktop)
            {
                ReApply(s);
                _applied.Add(s.Key);
            }
            else
            {
                Console.WriteLine($"swm: strip desk={s.Key.DesktopId} deferred (not current desktop)");
            }
        }
    }

    // Shell / system UWP surfaces that fire FOREGROUND when activated but must
    // never be tiled. Start menu search, the Start panel itself, action center,
    // text-input host (touch keyboard, candidate window), Win+X menu host, etc.
    // These are all hosted by their own exe (not ApplicationFrameHost), so an
    // exe-name match is exact and stable.
    private static readonly HashSet<string> ShellExeBlocklist = new(StringComparer.OrdinalIgnoreCase)
    {
        "SearchHost.exe",
        "SearchApp.exe",
        "StartMenuExperienceHost.exe",
        "ShellExperienceHost.exe",
        "TextInputHost.exe",
        "LockApp.exe",
    };

    private bool TryTrack(nint hwnd)
    {
        if (_hwndToStrip.ContainsKey(hwnd)) return false;
        if (!WindowOps.LooksManageable(hwnd)) return false;
        var exe = WindowOps.ExeOf(hwnd);
        if (string.Equals(exe, _selfExe, StringComparison.OrdinalIgnoreCase)) return false;
        if (ShellExeBlocklist.Contains(exe)) return false;
        var cls = WindowOps.ClassOf(hwnd);
        var title = WindowOps.Title(hwnd);

        var key = new StripKey(VirtualDesktops.GetDesktopId(hwnd));
        var strip = GetOrCreate(key);
        var rect = WindowOps.GetRect(hwnd);
        _originalRects[hwnd] = rect;
        StateFile.Write(_originalRects);
        var w = new ManagedWindow(hwnd, exe, cls, ResolveWindowWidth(hwnd));
        // Float if (a) a user rule matches, or (b) the window declares itself
        // non-resizable. Non-resizable windows are dialogs / pickers / fixed-
        // size utilities that break when SetWindowPos changes their dimensions
        // (account pickers, file/print dialogs, About boxes, ...). Tiling them
        // would violate the WS_THICKFRAME/WS_MAXIMIZEBOX contract they set.
        if (MatchesFloatRule(exe, cls, title) || !WindowOps.IsResizable(hwnd))
            strip.Floated[hwnd] = new FloatRecord(w, strip.Windows.Count, rect);
        else
            strip.Insert(CursorIndexIn(strip, excludeHwnd: 0), w);
        _hwndToStrip[hwnd] = key;
        return true;
    }

    // Where in `strip` does the cursor want to drop a window?
    //
    // Lays out the strip as if `excludeHwnd` weren't there (excludeHwnd=0
    // means "consider every tile") and finds which laid-out tile the cursor
    // sits over. Returns the post-removal insertion index — i.e., after
    // `RemoveAt(oldIdx)` you can `Insert(thisResult, w)` directly.
    //
    // Falls back to "append" when the cursor isn't over any tile (off-monitor,
    // over a float, on the taskbar). One primitive used by every code path
    // that needs to place a window: tab-tear adoption, intra-strip drag,
    // cross-monitor drag, cross-desktop drag.
    // Per-window tile width. Honors `window_width` from config when set;
    // otherwise divides the work-area width of the monitor the window is on
    // by `tiles_per_monitor`.
    private int ResolveWindowWidth(nint hwnd)
    {
        if (_config.WindowWidth is int w && w > 0) return w;
        var n = Math.Max(1, _config.TilesPerMonitor);
        var mon = Monitors.WorkArea(Monitors.MonitorFor(hwnd));
        var width = mon.Width > 0 ? mon.Width : Monitors.PrimaryWorkArea().Width;
        return width / n;
    }

    private int CursorIndexIn(Strip strip, nint excludeHwnd)
    {
        var skip = new HashSet<nint>();
        if (excludeHwnd != 0) skip.Add(excludeHwnd);
        foreach (var w in strip.Windows)
            if (WindowOps.IsMinimized(w.Hwnd)) skip.Add(w.Hwnd);
        var rects = Layout.Compute(
            strip, OrderedMonitors(),
            new LayoutConfig(0, _config.Gap), skip);
        var (cx, cy) = WindowOps.CursorPos();
        var laidOutIdx = 0;
        for (var i = 0; i < strip.Windows.Count; i++)
        {
            var hwnd = strip.Windows[i].Hwnd;
            if (!rects.TryGetValue(hwnd, out var r)) continue;
            if (cx >= r.Left && cx < r.Right && cy >= r.Top && cy < r.Bottom)
                return laidOutIdx;
            laidOutIdx++;
        }
        return laidOutIdx;
    }

    public void Cleanup()
    {
        Console.WriteLine($"swm: cleanup: restoring {_originalRects.Count} window(s)");
        if (_highlighted != 0) WindowOps.ClearHighlight(_highlighted);
        _applier.ShowAll();
        var monitors = Monitors.AllWorkAreas();
        var primary = Monitors.PrimaryWorkArea();
        foreach (var (hwnd, rect) in _originalRects)
            WindowOps.Move(hwnd, Restore.EnsureOnscreen(rect, monitors, primary));
        StateFile.Delete();
    }

    private bool MatchesFloatRule(string exe, string cls, string title)
    {
        foreach (var r in _config.FloatRule)
            if (r.Matches(exe, cls, title)) return true;
        return false;
    }

    private Strip GetOrCreate(StripKey key)
    {
        if (!_strips.TryGetValue(key, out var s)) { s = new Strip(key); _strips[key] = s; }
        return s;
    }

    public void OnWinEvent(uint type, nint hwnd)
    {
        switch (type)
        {
            case WinEvents.EVENT_OBJECT_SHOW:
            case WinEvents.EVENT_OBJECT_CREATE:
                if (WindowOps.IsLeftMouseDown())
                {
                    // Probable tab tear / drag-spawn. Defer adoption until
                    // the user releases the mouse so we don't race the
                    // browser's drag loop and leave a gap in the tiling.
                    _pendingTearAdopt.Add(hwnd);
                    break;
                }
                if (_hwndToStrip.TryGetValue(hwnd, out var sk))
                {
                    // Already tracked, re-shown (e.g. restored from tray
                    // without firing MINIMIZEEND). Re-tile so it rejoins.
                    ReApply(_strips[sk], bringToFront: false);
                    break;
                }
                if (TryTrack(hwnd) && _hwndToStrip.TryGetValue(hwnd, out var nk))
                    ReApply(_strips[nk], bringToFront: false);
                break;
            case WinEvents.EVENT_OBJECT_DESTROY:
                _pendingTearAdopt.Remove(hwnd);
                Untrack(hwnd);
                break;
            case WinEvents.EVENT_OBJECT_HIDE:
                // Tracked window became invisible without (yet) being
                // destroyed — most commonly Electron-style close-to-tray, or
                // an app that ShowWindow(SW_HIDE)s itself for any reason.
                // Re-tile so the gap closes; ReApply's skip set excludes
                // hidden hwnds. Keep it tracked: a later SHOW (tray restore)
                // brings it back; a real close still fires DESTROY.
                if (_hwndToStrip.TryGetValue(hwnd, out var hk))
                    ReApply(_strips[hk], bringToFront: false);
                break;
            case WinEvents.EVENT_SYSTEM_FOREGROUND:
                HandleForeground(hwnd);
                break;
            case WinEvents.EVENT_SYSTEM_MINIMIZESTART:
                HandleMinimize(hwnd, restored: false);
                break;
            case WinEvents.EVENT_SYSTEM_MINIMIZEEND:
                HandleMinimize(hwnd, restored: true);
                break;
            case WinEvents.EVENT_SYSTEM_MOVESIZESTART:
                _draggingHwnd = hwnd;
                _dragStartRect = WindowOps.GetVisibleRect(hwnd);
                _dragStartCaptured = true;
                break;
            case WinEvents.EVENT_SYSTEM_MOVESIZEEND:
                {
                    var startCaptured = _dragStartCaptured && _draggingHwnd == hwnd;
                    var startRect = _dragStartRect;
                    _draggingHwnd = 0;
                    _dragStartCaptured = false;
                    _dragStartRect = default;
                    HandleMoveSizeEnd(hwnd, startCaptured ? startRect : (Rect?)null);
                }
                break;
        }
    }

    private void Untrack(nint hwnd)
    {
        if (!_hwndToStrip.TryGetValue(hwnd, out var key)) return;
        var s = _strips[key];
        var idx = s.IndexOf(hwnd);
        if (idx >= 0)
        {
            s.RemoveAt(idx);
            _applier.Forget(hwnd);
            ReApply(s);
        }
        else if (s.Floated.ContainsKey(hwnd))
        {
            s.Floated.Remove(hwnd);
        }
        _hwndToStrip.Remove(hwnd);
        if (_originalRects.Remove(hwnd))
            StateFile.Write(_originalRects);
        if (_highlighted == hwnd) _highlighted = 0;
    }

    private void HandleForeground(nint hwnd)
    {
        // UWP apps (Settings, Calculator, ...) and other slow-loading windows
        // often fire EVENT_OBJECT_SHOW while still cloaked or with an empty
        // title, so TryTrack rejects them on first sight and no follow-up SHOW
        // is guaranteed. By the time the user activates one it's fully visible
        // — adopt it here so we don't permanently leak it.
        if (!_hwndToStrip.ContainsKey(hwnd) && WindowOps.LooksManageable(hwnd) && TryTrack(hwnd))
            Console.WriteLine($"swm: HandleForeground adopted late-arriving hwnd=0x{hwnd:X} title='{WindowOps.Title(hwnd)}'");

        if (!_hwndToStrip.TryGetValue(hwnd, out var key)) return;
        var s = _strips[key];
        var idx = s.IndexOf(hwnd);
        if (idx < 0) return;
        // Don't ever accept a minimized window as the focused tile. This can
        // arrive from a stale FOREGROUND event after another window destroys.
        if (WindowOps.IsMinimized(hwnd)) return;
        _lastActiveStripKey = key;
        var focusChanged = idx != s.FocusedIndex;
        var firstVisit = !_applied.Contains(key);
        if (focusChanged) s.SetFocus(idx);
        // Always refresh the border. Desktop switches re-fire FOREGROUND for
        // the same tile that was already focused on this strip; without this
        // call the previous desktop's highlight stays painted on a hidden
        // window and the newly visible focus has no border until the user
        // moves focus off and back.
        UpdateHighlight(hwnd);
        if (focusChanged || firstVisit)
        {
            // Flap protection: if foreground events are arriving faster than the
            // debounce window, an app is fighting us (e.g. Edge during tab tear).
            // Skip the layout pass to avoid amplifying the war; highlight has
            // already been refreshed above. Things settle once flapping stops.
            var now = DateTime.UtcNow;
            if (now - _lastForegroundReApply < ForegroundReApplyDebounce)
            {
                Console.WriteLine($"swm: HandleForeground debounced (flap?) hwnd=0x{hwnd:X}");
                return;
            }
            _lastForegroundReApply = now;
            // bringToFront=false: hwnd is already the foreground (that's why
            // this handler ran). Re-raising would either be a no-op or, if the
            // tracked-focus tile differs from the actual fg (late-adopted
            // float, stale state), would yank activation off the user's window.
            ReApply(s, bringToFront: false);
            _applied.Add(key);
            if (focusChanged) ShowFocusOsd(s);
        }
    }

    private void HandleMoveSizeEnd(nint hwnd, Rect? dragStartRect)
    {
        if (!_hwndToStrip.TryGetValue(hwnd, out var oldKey)) return;
        // Distinguish "user resized" from "user moved" by comparing how each
        // edge shifted between MOVESIZESTART and MOVESIZEEND. A pure move
        // shifts both edges equally; a resize shifts them asymmetrically.
        // Exact, no noise threshold — any legitimate move translates rigidly.
        var rect = WindowOps.GetVisibleRect(hwnd);
        var newKey = new StripKey(VirtualDesktops.GetDesktopId(hwnd));
        var oldStrip = _strips[oldKey];
        bool widthChanged = false;
        if (dragStartRect.HasValue && rect.Width > 0)
        {
            var s0 = dragStartRect.Value;
            widthChanged = (rect.Left - s0.Left) != (rect.Right - s0.Right);
        }

        if (newKey == oldKey)
        {
            var idx = oldStrip.IndexOf(hwnd);
            if (idx >= 0)
            {
                var w = oldStrip.Windows[idx];
                if (widthChanged)
                {
                    // Pure resize: never reorder. The cursor naturally crosses
                    // into a neighbor while dragging an edge — treating that
                    // as a move would swap tiles every resize.
                    w.WidthPx = rect.Width;
                }
                else
                {
                    // Drop where the cursor is. Same rule as tab tear; lets the
                    // user reorder tiles by drag-and-drop and pick the right
                    // monitor by where they release.
                    var target = CursorIndexIn(oldStrip, hwnd);
                    oldStrip.RemoveAt(idx);
                    oldStrip.Insert(target, w);
                    oldStrip.SetFocus(target);
                }
            }
            // bringToFront=false: foreground is already on hwnd (user just
            // released). Re-raising would yank activation off floats.
            ReApply(oldStrip, bringToFront: false);
            return;
        }

        // User dragged window to a different virtual desktop.
        ManagedWindow w2;
        var idxOld = oldStrip.IndexOf(hwnd);
        if (idxOld >= 0) { w2 = oldStrip.Windows[idxOld]; oldStrip.RemoveAt(idxOld); }
        else if (oldStrip.Floated.TryGetValue(hwnd, out var fr)) { w2 = fr.Window; oldStrip.Floated.Remove(hwnd); }
        else return;

        if (widthChanged) w2.WidthPx = rect.Width;
        var newStrip = GetOrCreate(newKey);
        // Cross-desktop with a resize: append rather than place at cursor, so
        // the resize doesn't double as a reorder. (Cross-desktop resize is
        // exotic anyway — typically a virtual-desktop drag is a pure move.)
        var insertAt = widthChanged ? newStrip.Windows.Count : CursorIndexIn(newStrip, hwnd);
        newStrip.Insert(insertAt, w2);
        newStrip.SetFocus(insertAt);
        _hwndToStrip[hwnd] = newKey;
        ReApply(oldStrip, bringToFront: false);
        ReApply(newStrip, bringToFront: false);
    }

    public string OnCommand(string line)
    {
        Console.WriteLine($"swm: cmd <- {line}");
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "err: empty";
        var cmd = parts[0];
        var arg = parts.Length > 1 ? parts[1] : "";
        return cmd switch
        {
            "focus" => DoFocus(arg),
            "swap" => DoSwap(arg),
            "move" => DoMove(arg),
            "float" => DoFloat(arg),
            "fullscreen" => DoFullscreen(arg),
            "tiles" => DoTiles(arg),
            "resize" => DoResize(arg),
            "list" => DoList(),
            "goto" => DoGoto(arg),
            "dump" => DoDump(),
            _ => $"err: unknown command '{cmd}'"
        };
    }

    private string DoDump()
    {
        var sb = new System.Text.StringBuilder();
        var fg = WindowOps.Foreground();
        sb.AppendLine($"foreground: 0x{fg:X} title='{WindowOps.Title(fg)}' tracked={_hwndToStrip.ContainsKey(fg)}");
        sb.AppendLine($"strips: {_strips.Count}, drag: 0x{_draggingHwnd:X}");
        foreach (var s in _strips.Values)
        {
            sb.AppendLine($"  strip desk={s.Key.DesktopId} focus={s.FocusedIndex} scroll={s.ScrollOffsetPx}");
            for (var i = 0; i < s.Windows.Count; i++)
            {
                var w = s.Windows[i];
                sb.AppendLine($"    [{i}] 0x{w.Hwnd:X} {w.ExeName} w={w.WidthPx} title='{WindowOps.Title(w.Hwnd)}'");
            }
            foreach (var (h, r) in s.Floated)
                sb.AppendLine($"    ~  0x{h:X} {r.Window.ExeName} title='{WindowOps.Title(h)}'");
        }
        return sb.ToString().TrimEnd();
    }

    private Strip? ActiveStrip()
    {
        var fg = WindowOps.Foreground();
        // Only honor foreground if it's actually on the current virtual desktop.
        // After AHK-driven desktop switches, Win11 often leaves the prior
        // desktop's window as foreground until something else takes it; trusting
        // it here would yank focus back to the old desktop on every command.
        if (_hwndToStrip.TryGetValue(fg, out var key)
            && _strips.TryGetValue(key, out var s)
            && VirtualDesktops.IsOnCurrentDesktop(fg))
        {
            _lastActiveStripKey = key;
            return s;
        }

        // Foreground isn't on the current desktop (or isn't tracked). Probe each
        // strip with IsOnCurrentDesktop using one of its windows, since fg's
        // desktop id is unreliable here (it's the prior desktop after AHK switch).
        foreach (var strip in _strips.Values)
        {
            var probe = strip.Windows.Count > 0 ? strip.Windows[0].Hwnd
                      : strip.Floated.Count > 0 ? strip.Floated.Keys.First()
                      : 0;
            if (probe != 0 && VirtualDesktops.IsOnCurrentDesktop(probe))
            {
                _lastActiveStripKey = strip.Key;
                Console.WriteLine($"swm: ActiveStrip resolved via probe (fg=0x{fg:X})");
                return strip;
            }
        }

        // No strip's windows live on the current desktop — return null so the
        // caller emits "no active strip" rather than acting on a different desktop.
        Console.WriteLine($"swm: ActiveStrip: no strip on current desktop (fg=0x{fg:X})");
        return null;
    }

    private static bool IsMin(ManagedWindow w) => WindowOps.IsMinimized(w.Hwnd);

    private string DoFocus(string arg)
    {
        var s = ActiveStrip(); if (s == null) return "err: no active strip";
        var before = s.FocusedIndex;
        switch (arg)
        {
            case "left": Commands.FocusLeft(s, IsMin); break;
            case "right": Commands.FocusRight(s, IsMin); break;
            case "home": Commands.FocusHome(s, IsMin); break;
            case "end": Commands.FocusEnd(s, IsMin); break;
            default: return "err: focus arg must be left|right|home|end";
        }
        if (s.FocusedIndex == before) return "ok: no-op (at boundary)";
        ReApply(s);
        WarpCursorToFocused(s);
        ShowFocusOsd(s);
        return "ok";
    }

    private string DoSwap(string arg)
    {
        var s = ActiveStrip(); if (s == null) return "err: no active strip";
        switch (arg)
        {
            case "left": Commands.SwapLeft(s, IsMin); break;
            case "right": Commands.SwapRight(s, IsMin); break;
            case "master":
                Commands.SwapAtMonitorSlot(s, OrderedMonitors(),
                    new LayoutConfig(0, _config.Gap), 0, 1, IsMin);
                break;
            case "secondary":
                Commands.SwapAtMonitorSlot(s, OrderedMonitors(),
                    new LayoutConfig(0, _config.Gap), 1, 0, IsMin);
                break;
            default: return "err: swap arg must be left|right|master|secondary";
        }
        ReApply(s);
        WarpCursorToFocused(s);
        ShowFocusOsd(s);
        return "ok";
    }

    private string DoMove(string arg)
    {
        var s = ActiveStrip(); if (s == null) return "err: no active strip";
        switch (arg)
        {
            case "home": Commands.MoveHome(s); break;
            case "end": Commands.MoveEnd(s); break;
            default: return "err: move arg must be home|end";
        }
        ReApply(s);
        WarpCursorToFocused(s);
        ShowFocusOsd(s);
        return "ok";
    }

    // Warp the mouse cursor to the center of the strip's focused tile. Called
    // from keyboard-driven focus/swap/move so the pointer follows where attention
    // moved, but never from EVENT_SYSTEM_FOREGROUND (click-to-focus would yank
    // the user's own cursor mid-click).
    private static void WarpCursorToFocused(Strip s)
    {
        var hwnd = s.Focused?.Hwnd ?? 0;
        if (hwnd == 0) return;
        WindowOps.WarpCursorToWindow(hwnd);
    }

    private string DoFloat(string arg)
    {
        if (arg != "toggle") return "err: float arg must be toggle";
        var fg = WindowOps.Foreground();
        if (!_hwndToStrip.TryGetValue(fg, out var key)) return "err: focus not tracked";
        var s = _strips[key];
        if (s.IndexOf(fg) >= 0)
        {
            // Tile -> float. The strip's focused index shifts to a neighbor
            // after the float removes itself; suppress raising that neighbor
            // and explicitly activate the just-floated window so it stays on
            // top and keeps input focus.
            Commands.Float(s, WindowOps.GetRect(fg));
            ReApply(s, bringToFront: false);
            WindowOps.RaiseAndFocus(fg);
        }
        else if (s.Floated.ContainsKey(fg))
        {
            Commands.Unfloat(s, fg);
            ReApply(s);
        }
        else return "err: focus not in strip";
        return "ok";
    }

    private string DoFullscreen(string arg)
    {
        if (arg != "toggle") return "err: fullscreen arg must be toggle";
        var s = ActiveStrip(); if (s == null) return "err: no active strip";
        var focusedHwnd = s.Focused?.Hwnd ?? 0;
        var monitor = focusedHwnd != 0
            ? Monitors.WorkArea(Monitors.MonitorFor(focusedHwnd))
            : Monitors.AllWorkAreas().FirstOrDefault();
        Commands.ToggleFullscreen(s, monitor);
        ReApply(s);
        return "ok";
    }

    // Resize the focused tile in monitor-relative steps (1/16th of the
    // focused tile's monitor width, ≥50 px). Doesn't touch other tiles —
    // they shift naturally because layout packs left-to-right.
    private string DoResize(string arg)
    {
        var s = ActiveStrip(); if (s == null) return "err: no active strip";
        var w = s.Focused; if (w == null) return "err: no focused";
        var mon = Monitors.WorkArea(Monitors.MonitorFor(w.Hwnd));
        var monWidth = mon.Width > 0 ? mon.Width : Monitors.PrimaryWorkArea().Width;
        var step = Math.Max(50, monWidth / 16);
        int delta = arg switch
        {
            "grow" => step,
            "shrink" => -step,
            _ => 0
        };
        if (delta == 0) return "err: resize arg must be grow|shrink";
        Commands.ResizeFocused(s, delta);
        ReApply(s);
        return "ok";
    }

    private string DoTiles(string arg)
    {
        var s = ActiveStrip(); if (s == null) return "err: no active strip";
        var primary = Monitors.PrimaryWorkArea();
        if (primary.Width <= 0) return "err: no primary monitor";
        bool includeFullscreen;
        if (string.Equals(arg, "reset", StringComparison.OrdinalIgnoreCase))
        {
            includeFullscreen = true;
        }
        else if (int.TryParse(arg, out var n) && n >= 1 && n <= 16)
        {
            _config.TilesPerMonitor = n;
            includeFullscreen = false;
        }
        else
        {
            return "err: tiles arg must be an int 1..16 or 'reset'";
        }
        Commands.SetAllToTilesPerMonitor(s, OrderedMonitors(), primary,
            _config.TilesPerMonitor, includeFullscreen);
        ReApply(s);
        return "ok";
    }

    private string DoList()
    {
        var items = new List<object>();
        foreach (var s in _strips.Values)
        {
            // Compute once per strip by probing any tracked hwnd. If empty, fall
            // back to false (we'd skip rendering "current" affordance for ghost strips).
            var probe = s.Windows.Count > 0 ? s.Windows[0].Hwnd
                : (s.Floated.Count > 0 ? s.Floated.Keys.First() : 0);
            var onCurrent = probe != 0 && VirtualDesktops.IsOnCurrentDesktop(probe);

            for (var i = 0; i < s.Windows.Count; i++)
            {
                var w = s.Windows[i];
                items.Add(new
                {
                    hwnd = w.Hwnd.ToInt64(),
                    exe = w.ExeName,
                    title = WindowOps.Title(w.Hwnd),
                    desktop = s.Key.DesktopId.ToString(),
                    currentDesktop = onCurrent,
                    floating = false,
                    focused = i == s.FocusedIndex
                });
            }
            foreach (var (hwnd, rec) in s.Floated)
            {
                items.Add(new
                {
                    hwnd = hwnd.ToInt64(),
                    exe = rec.Window.ExeName,
                    title = WindowOps.Title(hwnd),
                    desktop = s.Key.DesktopId.ToString(),
                    currentDesktop = onCurrent,
                    floating = true,
                    focused = false
                });
            }
        }
        return JsonSerializer.Serialize(items);
    }

    private string DoGoto(string arg)
    {
        if (!long.TryParse(arg, out var hLong)) return "err: goto needs numeric hwnd";
        var hwnd = (nint)hLong;
        if (!_hwndToStrip.TryGetValue(hwnd, out var key)) return "err: hwnd not tracked";
        var s = _strips[key];
        var idx = s.IndexOf(hwnd);
        if (idx >= 0)
        {
            s.SetFocus(idx);
            ReApply(s);
        }
        else
        {
            WindowOps.RaiseAndFocus(hwnd);
        }
        return "ok";
    }

    /// <summary>
    /// Lay out the strip. <paramref name="bringToFront"/> controls whether the
    /// focused tile is raised/activated; pass false from event handlers that
    /// run in response to OS-driven focus changes (foreground events, drag
    /// release, late-adoption) so we don't fight the user for activation.
    /// </summary>
    private void ReApply(Strip s, bool bringToFront = true)
    {
        var ordered = OrderedMonitors();
        var cfg = new LayoutConfig(0, _config.Gap);
        var skip = new HashSet<nint>();
        foreach (var w in s.Windows)
        {
            if (WindowOps.IsMinimized(w.Hwnd)) { skip.Add(w.Hwnd); continue; }
            // Hidden but not minimized: app likely close-to-tray'd or
            // self-hid (Electron apps frequently do this on close). Skip from
            // layout so the strip packs tightly. Stays tracked so a later
            // SHOW or restore from tray re-inserts it. A real destroy fires
            // EVENT_OBJECT_DESTROY which Untrack's the hwnd entirely.
            if (!WindowOps.IsVisible(w.Hwnd)) skip.Add(w.Hwnd);
        }
        var rects = Layout.Compute(s, ordered, cfg, skip);
        if (_draggingHwnd != 0) rects.Remove(_draggingHwnd);
        Console.WriteLine($"swm: apply desk={s.Key.DesktopId} rects={rects.Count} focus=0x{(s.Focused?.Hwnd ?? 0):X} scroll={s.ScrollOffsetPx} skipped={skip.Count} bringToFront={bringToFront}");
        _applier.Apply(rects, s.Focused?.Hwnd ?? 0, bringToFront);
        // Floats live above tiles in z-order. Since Apply may have raised the
        // focused tile to HWND_TOP, push every float back above it. NOACTIVATE
        // so input focus is unaffected — the focused tile keeps keystrokes.
        foreach (var hwnd in s.Floated.Keys)
            WindowOps.RaiseZOrder(hwnd);
        UpdateHighlight(s.Focused?.Hwnd ?? 0);
    }

    private List<Rect> OrderedMonitors()
    {
        var all = Monitors.AllWorkAreas();
        var primary = Monitors.PrimaryWorkArea();
        var list = new List<Rect>(all.Count);
        if (primary.Width > 0) list.Add(primary);
        foreach (var m in all) if (m != primary) list.Add(m);
        return list;
    }

    private int MonitorIndexOf(nint hwnd)
    {
        var rect = WindowOps.GetRect(hwnd);
        if (rect.Width == 0) return -1;
        var cx = (rect.Left + rect.Right) / 2;
        var cy = (rect.Top + rect.Bottom) / 2;
        var ordered = OrderedMonitors();
        for (var i = 0; i < ordered.Count; i++)
        {
            var m = ordered[i];
            if (cx >= m.Left && cx <= m.Right && cy >= m.Top && cy <= m.Bottom) return i;
        }
        return -1;
    }
}
