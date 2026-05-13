using System.Runtime.InteropServices;
using System.Threading.Channels;
using ScrollingWM.Ipc;
using ScrollingWM.Rules;
using ScrollingWM.Win32;

namespace ScrollingWM;

internal static partial class Program
{
    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessage(out MSG msg, nint hwnd, uint min, uint max, uint remove);

    [LibraryImport("user32.dll", EntryPoint = "TranslateMessage")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(in MSG msg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static partial nint DispatchMessage(in MSG msg);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessDpiAwarenessContext(nint dpiContext);

    // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 (Win10 1703+).
    private static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

    private const uint PM_REMOVE = 0x0001;
    private const uint WM_QUIT = 0x0012;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nuint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    [STAThread]
    private static int Main(string[] args)
    {
        // Must run before ANY Win32 GUI/window/HMONITOR call. Without this,
        // the daemon runs as a non-DPI-aware process and Windows lies to us:
        // GetWindowRect/GetVisibleRect/SetWindowPos coordinates are silently
        // virtualized by the monitor's DPI scale factor, so reading & writing
        // a DPI-aware app's rect amplifies every resize by the scale factor
        // (e.g. 1.5x at 150% scaling).
        try { SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2); } catch { }

        var configPath = args.Length > 0 ? args[0] : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".swm", "config.toml");
        var config = Config.Load(configPath);
        Console.WriteLine($"swm: window_width={config.WindowWidth?.ToString() ?? "auto(50%)"} gap={config.Gap} float_rules={config.FloatRule.Count}");

        var selfExe = Path.GetFileName(Environment.ProcessPath ?? "ScrollingWM.exe");
        var dispatcher = new Dispatcher(config, selfExe);
        dispatcher.DiscoverExisting();
        Console.WriteLine("swm: discovery complete");

        var inbox = Channel.CreateUnbounded<PipeCommand>();
        var pipe = new PipeServer(inbox);
        _ = pipe.RunAsync();

        using var hooks = new WinEvents((type, hwnd) =>
        {
            try { dispatcher.OnWinEvent(type, hwnd); }
            catch (Exception ex) { Console.Error.WriteLine($"swm: hook error: {ex.Message}"); }
        });
        hooks.HookRange(WinEvents.EVENT_SYSTEM_FOREGROUND, WinEvents.EVENT_SYSTEM_FOREGROUND);
        hooks.HookRange(WinEvents.EVENT_SYSTEM_MOVESIZESTART, WinEvents.EVENT_SYSTEM_MOVESIZEEND);
        hooks.HookRange(WinEvents.EVENT_SYSTEM_MINIMIZESTART, WinEvents.EVENT_SYSTEM_MINIMIZEEND);
        hooks.HookRange(WinEvents.EVENT_OBJECT_CREATE, WinEvents.EVENT_OBJECT_HIDE);

        var stop = false;
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop = true; };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { dispatcher.Cleanup(); } catch { } };
        Console.WriteLine($"swm: listening on \\\\.\\pipe\\{PipeServer.PipeName}");

        while (!stop)
        {
            while (PeekMessage(out var msg, 0, 0, 0, PM_REMOVE))
            {
                if (msg.message == WM_QUIT) { stop = true; break; }
                TranslateMessage(in msg);
                DispatchMessage(in msg);
            }
            while (inbox.Reader.TryRead(out var cmd))
            {
                try { cmd.Reply.SetResult(dispatcher.OnCommand(cmd.Line)); }
                catch (Exception ex) { cmd.Reply.SetResult($"err: {ex.Message}"); }
            }
            dispatcher.Poll();
            Thread.Sleep(8);
        }

        dispatcher.Cleanup();
        pipe.Stop();
        return 0;
    }
}
