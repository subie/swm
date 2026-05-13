using System.Runtime.InteropServices;

namespace ScrollingWM.Win32;

public static class VirtualDesktops
{
    private static readonly Guid CLSID_VirtualDesktopManager = new("aa509086-5ca9-4c25-8f95-589d3c07b48a");
    private static readonly IVirtualDesktopManager? _vdm = TryCreate();

    public static Guid GetDesktopId(nint hwnd)
    {
        if (_vdm == null) return Guid.Empty;
        try { _vdm.GetWindowDesktopId(hwnd, out var id); return id; }
        catch { return Guid.Empty; }
    }

    public static bool IsOnCurrentDesktop(nint hwnd)
    {
        if (_vdm == null) return true;
        try { _vdm.IsWindowOnCurrentVirtualDesktop(hwnd, out var on); return on; }
        catch { return true; }
    }

    public static bool MoveToDesktop(nint hwnd, Guid desktopId)
    {
        if (_vdm == null) return false;
        try { _vdm.MoveWindowToDesktop(hwnd, ref desktopId); return true; }
        catch { return false; }
    }

    private static IVirtualDesktopManager? TryCreate()
    {
        try
        {
            var t = Type.GetTypeFromCLSID(CLSID_VirtualDesktopManager);
            return t == null ? null : (IVirtualDesktopManager?)Activator.CreateInstance(t);
        }
        catch { return null; }
    }

    [ComImport, Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        void IsWindowOnCurrentVirtualDesktop(nint hwnd, [MarshalAs(UnmanagedType.Bool)] out bool isOnCurrent);
        void GetWindowDesktopId(nint hwnd, out Guid desktopId);
        void MoveWindowToDesktop(nint hwnd, ref Guid desktopId);
    }
}
