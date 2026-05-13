namespace ScrollingWM.Core;

public sealed class ManagedWindow
{
    public nint Hwnd { get; }
    public string ExeName { get; }
    public string ClassName { get; }
    public int WidthPx { get; set; }
    /// <summary>
    /// Pre-fullscreen WidthPx. When non-null, this window is in fullscreen mode
    /// (its WidthPx has been swapped to the focused monitor's width). Toggling
    /// off restores WidthPx from this value and clears it.
    /// </summary>
    public int? PreFullscreenWidth { get; set; }
    /// <summary>
    /// X coordinate (virtual desktop space) of the left edge of the monitor this
    /// window was fullscreened onto. Used by Layout to snap scroll so the window
    /// sits flush on that monitor and never straddles bezels. Null when not
    /// fullscreen.
    /// </summary>
    public int? FullscreenMonitorLeft { get; set; }
    public bool Fullscreen => PreFullscreenWidth.HasValue;

    public ManagedWindow(nint hwnd, string exeName, string className, int widthPx)
    {
        Hwnd = hwnd;
        ExeName = exeName;
        ClassName = className;
        WidthPx = widthPx;
    }
}
