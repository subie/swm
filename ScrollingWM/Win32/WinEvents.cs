using System.Runtime.InteropServices;

namespace ScrollingWM.Win32;

public sealed partial class WinEvents : IDisposable
{
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
    public const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    public const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    public const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    public const uint EVENT_OBJECT_CREATE = 0x8000;
    public const uint EVENT_OBJECT_DESTROY = 0x8001;
    public const uint EVENT_OBJECT_SHOW = 0x8002;
    public const uint EVENT_OBJECT_HIDE = 0x8003;
    public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;

    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    private const int OBJID_WINDOW = 0;

    public delegate void WinEventHandler(uint eventType, nint hwnd);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void WinEventProc(nint hWinEventHook, uint eventType, nint hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [LibraryImport("user32.dll")]
    private static partial nint SetWinEventHook(uint eventMin, uint eventMax, nint hmodWinEventProc,
        WinEventProc pfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWinEvent(nint hWinEventHook);

    private readonly List<nint> _hooks = new();
    private readonly WinEventProc _proc; // keep delegate alive
    private readonly WinEventHandler _handler;

    public WinEvents(WinEventHandler handler)
    {
        _handler = handler;
        _proc = OnEvent;
    }

    public void HookRange(uint min, uint max)
    {
        var h = SetWinEventHook(min, max, 0, _proc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        if (h != 0) _hooks.Add(h);
    }

    private void OnEvent(nint hHook, uint eventType, nint hwnd, int idObject, int idChild,
        uint thread, uint time)
    {
        if (idObject != OBJID_WINDOW || hwnd == 0) return;
        _handler(eventType, hwnd);
    }

    public void Dispose()
    {
        foreach (var h in _hooks) UnhookWinEvent(h);
        _hooks.Clear();
    }
}
