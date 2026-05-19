using System.Diagnostics;
using System.Runtime.InteropServices;
using ScrollingWM.Core;

namespace ScrollingWM.Win32;

public static partial class WindowOps
{
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hwnd, out RECT rect);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hwnd, nint hwndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetWindowText(nint hwnd, [Out] char[] str, int maxCount);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetClassName(nint hwnd, [Out] char[] str, int maxCount);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(nint hwnd);

    public static bool Exists(nint hwnd) => IsWindow(hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hwnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int vKey);

    private const int VK_LBUTTON = 0x01;

    public static bool IsLeftMouseDown() => (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT lpPoint);

    public static (int X, int Y) CursorPos()
    {
        return GetCursorPos(out var p) ? (p.X, p.Y) : (0, 0);
    }

    public static void WarpCursorToWindow(nint hwnd)
    {
        var r = GetVisibleRect(hwnd);
        if (r.Width <= 0 || r.Height <= 0) return;
        SetCursorPos((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
    }

    private const int SW_HIDE = 0;
    private const int SW_SHOWNA = 8;

    public static bool Hide(nint hwnd) => ShowWindow(hwnd, SW_HIDE);
    public static bool Show(nint hwnd) => ShowWindow(hwnd, SW_SHOWNA);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial nint GetAncestor(nint hwnd, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(nint hwnd, int nIndex);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(nint hwnd, uint dwAttribute, out int pvAttribute, int cbAttribute);

    [LibraryImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static partial int DwmGetWindowAttributeRect(nint hwnd, uint dwAttribute, out RECT pvAttribute, int cbAttribute);

    private const uint DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, uint dwAttribute, ref uint pvAttribute, uint cbAttribute);

    private const uint DWMWA_BORDER_COLOR = 34;
    private const uint DWMWA_CAPTION_COLOR = 35;
    private const uint DWMWA_COLOR_DEFAULT = 0xFFFFFFFF;

    /// <summary>Tints border + title bar with the given COLORREF (0x00BBGGRR). Win11 22H2+.</summary>
    public static void SetHighlight(nint hwnd, uint colorref)
    {
        var c = colorref;
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref c, sizeof(uint));
        DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref c, sizeof(uint));
    }

    public static void ClearHighlight(nint hwnd)
    {
        var def = DWMWA_COLOR_DEFAULT;
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref def, sizeof(uint));
        DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref def, sizeof(uint));
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint BeginDeferWindowPos(int nNumWindows);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint DeferWindowPos(nint hWinPosInfo, nint hwnd, nint hwndInsertAfter,
        int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EndDeferWindowPos(nint hWinPosInfo);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    private static partial nint SetFocus(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BringWindowToTop(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial void SwitchToThisWindow(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool fAltTab);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial nint SendMessage(nint hwnd, uint msg, nint wParam, nint lParam);

    private const uint WM_ACTIVATE = 0x0006;
    private const int WA_CLICKACTIVE = 2;

    private const int GWL_EXSTYLE = -20;
    private const int GWL_STYLE = -16;
    private const uint GA_ROOT = 2;
    private const uint DWMWA_CLOAKED = 14;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;
    private const long WS_EX_APPWINDOW = 0x00040000L;
    private const long WS_EX_NOACTIVATE = 0x08000000L;
    private const long WS_THICKFRAME = 0x00040000L;
    private const long WS_MAXIMIZEBOX = 0x00010000L;

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const long WS_EX_LAYERED = 0x00080000L;
    private const long WS_EX_TRANSPARENT = 0x00000020L;
    private const long WS_EX_NOREDIRECTIONBITMAP = 0x00200000L;

    [StructLayout(LayoutKind.Sequential)]
    private struct TITLEBARINFO
    {
        public uint cbSize;
        public RECT rcTitleBar;
        public uint rgstate0; public uint rgstate1; public uint rgstate2;
        public uint rgstate3; public uint rgstate4; public uint rgstate5;
    }
    private const uint STATE_SYSTEM_INVISIBLE = 0x00008000;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTitleBarInfo(nint hwnd, ref TITLEBARINFO pti);

    /// <summary>
    /// True when the OS reports the window's title bar as invisible. Renderless
    /// helper windows (Teams' background WebView2 hosts, some Electron splash
    /// surrogates) keep WS_VISIBLE set and report a full DWM frame, but their
    /// title bar carries STATE_SYSTEM_INVISIBLE — the same signal Microsoft's
    /// "is alt-tab visible?" recipe uses to skip them. Catches the
    /// post-tile phantoms that pass every other liveness check.
    /// </summary>
    public static bool TitleBarInvisible(nint hwnd)
    {
        var ti = new TITLEBARINFO { cbSize = (uint)Marshal.SizeOf<TITLEBARINFO>() };
        if (!GetTitleBarInfo(hwnd, ref ti)) return false;
        return (ti.rgstate0 & STATE_SYSTEM_INVISIBLE) != 0;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLayeredWindowAttributes(nint hwnd, out uint pcrKey, out byte pbAlpha, out uint pdwFlags);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindow(nint hwnd, uint cmd);
    private const uint GW_OWNER = 4;

    /// <summary>
    /// Compact descriptor for diagnostics: which style bits are set on the window,
    /// owner handle, layered alpha if applicable. Lets us tell phantom helper
    /// windows apart from legit main windows when they otherwise look identical
    /// to LooksManageable / ShouldSkip.
    /// </summary>
    public static string DescribeStyle(nint hwnd)
    {
        var ex = ExStyle(hwnd);
        var owner = GetWindow(hwnd, GW_OWNER);
        string layered = "";
        if ((ex & WS_EX_LAYERED) != 0)
        {
            if (GetLayeredWindowAttributes(hwnd, out _, out var alpha, out var flags))
                layered = $" layered(alpha={alpha} flags={flags:X})";
            else
                layered = " layered(noattr)";
        }
        var flagsList = new List<string>();
        if ((ex & WS_EX_LAYERED) != 0) flagsList.Add("LAYERED");
        if ((ex & WS_EX_TRANSPARENT) != 0) flagsList.Add("TRANSPARENT");
        if ((ex & WS_EX_NOREDIRECTIONBITMAP) != 0) flagsList.Add("NOREDIR");
        if ((ex & WS_EX_TOOLWINDOW) != 0) flagsList.Add("TOOL");
        if ((ex & WS_EX_APPWINDOW) != 0) flagsList.Add("APP");
        if ((ex & WS_EX_NOACTIVATE) != 0) flagsList.Add("NOACTIVATE");
        if (TitleBarInvisible(hwnd)) flagsList.Add("TB-INVIS");
        return $"ex={string.Join("|", flagsList)} owner=0x{owner:X}{layered}";
    }

    private const uint SWP_NOSENDCHANGING = 0x0400;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private static readonly nint HWND_TOP = 0;

    public static Rect GetRect(nint hwnd)
    {
        if (!GetWindowRect(hwnd, out var r)) return default;
        return new Rect(r.left, r.top, r.right, r.bottom);
    }

    /// <summary>
    /// Returns the window's visible bounds (DWM extended frame), excluding the
    /// invisible drop-shadow padding that <c>GetWindowRect</c> includes on Win11.
    /// Use this for highlight/overlay positioning so the overlay sits flush
    /// against the window's visible edge.
    /// </summary>
    public static Rect GetVisibleRect(nint hwnd)
    {
        if (DwmGetWindowAttributeRect(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var r,
            System.Runtime.InteropServices.Marshal.SizeOf<RECT>()) == 0)
            return new Rect(r.left, r.top, r.right, r.bottom);
        return GetRect(hwnd);
    }

    public static bool Move(nint hwnd, Rect r) =>
        SetWindowPos(hwnd, 0, r.Left, r.Top, r.Width, r.Height,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSENDCHANGING);

    public static nint BeginBatch(int n) => BeginDeferWindowPos(n);
    public static nint AddToBatch(nint batch, nint hwnd, Rect r) =>
        DeferWindowPos(batch, hwnd, 0, r.Left, r.Top, r.Width, r.Height,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSENDCHANGING);
    public static bool EndBatch(nint batch) => EndDeferWindowPos(batch);

    /// <summary>Raise <paramref name="hwnd"/> to the top of the z-order
    /// without changing keyboard activation. Useful for keeping floats above
    /// tiles after we've activated some other window.</summary>
    public static void RaiseZOrder(nint hwnd) =>
        SetWindowPos(hwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    public static void RaiseAndFocus(nint hwnd)
    {
        SetWindowPos(hwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        var fgBefore = GetForegroundWindow();
        var myThread = GetCurrentThreadId();
        var fgThread = fgBefore == 0 ? 0 : GetWindowThreadProcessId(fgBefore, out _);
        var targetThread = GetWindowThreadProcessId(hwnd, out _);

        // Attach to BOTH the foreground thread (for SetForegroundWindow rights) and
        // the target window's thread (for SetFocus rights on its windows).
        // Without the second attachment, keyboard input often stays with the prior window
        // even after foreground switches.
        var attachedThreads = new List<uint>();
        if (fgThread != 0 && fgThread != myThread && AttachThreadInput(myThread, fgThread, true))
            attachedThreads.Add(fgThread);
        if (targetThread != 0 && targetThread != myThread && targetThread != fgThread
            && AttachThreadInput(myThread, targetThread, true))
            attachedThreads.Add(targetThread);

        bool sfwOk = false;
        try
        {
            BringWindowToTop(hwnd);
            sfwOk = SetForegroundWindow(hwnd);
            SetFocus(hwnd);
            // SwitchToThisWindow is more forceful than SetForegroundWindow and helps
            // when the foreground lock or another app is fighting us.
            SwitchToThisWindow(hwnd, true);
            // WebView2-based hosts (notably Microsoft Teams) only forward keyboard
            // input to their inner Chromium child window when they receive a
            // click-style activation. SetForegroundWindow alone leaves the host's
            // outer HWND focused, so typing goes nowhere until the user clicks.
            // WM_ACTIVATE with WA_CLICKACTIVE makes the host route focus to its
            // child the same way a real mouse click would.
            SendMessage(hwnd, WM_ACTIVATE, (nint)WA_CLICKACTIVE, hwnd);
        }
        finally
        {
            foreach (var t in attachedThreads) AttachThreadInput(myThread, t, false);
        }
        var fgAfter = GetForegroundWindow();
        Console.WriteLine($"swm:     raise 0x{hwnd:X} sfw={sfwOk} attachedTo={attachedThreads.Count} fgBefore=0x{fgBefore:X} fgAfter=0x{fgAfter:X}");
    }

    public static nint Foreground() => GetForegroundWindow();
    public static bool IsVisible(nint hwnd) => IsWindowVisible(hwnd);
    public static bool IsMinimized(nint hwnd) => IsIconic(hwnd);

    public static string Title(nint hwnd)
    {
        var buf = new char[256];
        var n = GetWindowText(hwnd, buf, buf.Length);
        return n > 0 ? new string(buf, 0, n) : "";
    }

    public static string ClassOf(nint hwnd)
    {
        var buf = new char[256];
        var n = GetClassName(hwnd, buf, buf.Length);
        return n > 0 ? new string(buf, 0, n) : "";
    }

    public static string ExeOf(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        try { return Path.GetFileName(Process.GetProcessById((int)pid).MainModule?.FileName ?? "") ?? ""; }
        catch { return ""; }
    }

    public static long ExStyle(nint hwnd) => GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
    public static long Style(nint hwnd) => GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();

    /// <summary>
    /// True when the window's style declares it is a normal resizable top-level
    /// window (has both a sizing border and a maximize box). Fixed-size dialogs
    /// — modal popups, account pickers, "About" boxes, file/print dialogs — lack
    /// these. They have explicitly told the OS not to resize them; tiling them
    /// breaks their content (e.g. WAM "Sign in" popup cancels its async account
    /// fetch the moment we SetWindowPos it). Auto-route such windows to floating.
    /// </summary>
    public static bool IsResizable(nint hwnd)
    {
        var s = Style(hwnd);
        return (s & WS_THICKFRAME) != 0 && (s & WS_MAXIMIZEBOX) != 0;
    }
    public static bool IsTopLevel(nint hwnd) => GetAncestor(hwnd, GA_ROOT) == hwnd;

    // DWM cloak reasons
    private const int DWM_CLOAKED_APP = 0x00000001;
    private const int DWM_CLOAKED_SHELL = 0x00000002;     // virtual desktop -> still trackable
    private const int DWM_CLOAKED_INHERITED = 0x00000004;

    public static bool IsCloakedByApp(nint hwnd)
    {
        var hr = DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int));
        if (hr != 0) return false;
        // Filter only if cloaked for a reason other than virtual-desktop shell.
        return (cloaked & ~DWM_CLOAKED_SHELL) != 0;
    }

    public static bool LooksManageable(nint hwnd)
    {
        if (!IsWindowVisible(hwnd)) return false;
        // Minimized windows are technically "visible" (WS_VISIBLE set) but Windows
        // has them hidden — they shouldn't occupy a tile.
        if (IsIconic(hwnd)) return false;
        if (!IsTopLevel(hwnd)) return false;
        if (IsCloakedByApp(hwnd)) return false;
        var ex = ExStyle(hwnd);
        if ((ex & WS_EX_NOACTIVATE) != 0) return false;
        if ((ex & WS_EX_TOOLWINDOW) != 0 && (ex & WS_EX_APPWINDOW) == 0) return false;
        if (Title(hwnd).Length == 0) return false;
        // Reject phantom-sized windows. Real app windows are at least a few
        // hundred px in both dimensions. Teams (and other Electron-style
        // apps) sometimes spawn invisible 1x1 or zero-sized helper hwnds
        // that pass every other check; adopting them creates blank tiles
        // that never go away because the hwnd technically stays "visible".
        var r = GetRect(hwnd);
        if (r.Width < 100 || r.Height < 100) return false;
        if (TitleBarInvisible(hwnd)) return false;
        return true;
    }

    public static IEnumerable<nint> EnumerateTopLevel()
    {
        var list = new List<nint>();
        EnumWindowsProc cb = (h, _) => { list.Add(h); return true; };
        EnumWindows(cb, 0);
        GC.KeepAlive(cb);
        return list;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }
}
