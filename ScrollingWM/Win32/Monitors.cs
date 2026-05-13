using System.Runtime.InteropServices;
using ScrollingWM.Core;

namespace ScrollingWM.Win32;

public static partial class Monitors
{
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint hwnd, uint flags);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromPoint(POINT pt, uint flags);

    private const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    public static Rect PrimaryWorkArea()
    {
        var primary = MonitorFromPoint(new POINT { x = 0, y = 0 }, MONITOR_DEFAULTTOPRIMARY);
        return primary == 0 ? default : WorkArea(primary);
    }

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(nint hMonitor, ref MONITORINFO mi);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc cb, nint data);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate bool MonitorEnumProc(nint hMonitor, nint hdc, nint lprcMonitor, nint dwData);

    public static IReadOnlyList<Rect> AllWorkAreas()
    {
        var list = new List<Rect>();
        MonitorEnumProc cb = (h, _, _, _) => { list.Add(WorkArea(h)); return true; };
        EnumDisplayMonitors(0, 0, cb, 0);
        GC.KeepAlive(cb);
        return list;
    }

    public static nint MonitorFor(nint hwnd) => MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

    public static Rect WorkArea(nint hMonitor)
    {
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(hMonitor, ref mi)) return default;
        var w = mi.rcWork;
        return new Rect(w.left, w.top, w.right, w.bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}
