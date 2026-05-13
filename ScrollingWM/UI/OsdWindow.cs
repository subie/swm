using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using ScrollingWM.Core;

namespace ScrollingWM.UI;

/// <summary>
/// Tiny transient OSD popup that shows "N / M" when the focused window
/// changes. Borderless, topmost, click-through, never takes focus.
/// Renders with GDI+ into a layered window so the background can be
/// semi-transparent with rounded corners and the closing animation can
/// fade alpha smoothly.
/// </summary>
internal sealed class OsdWindow : IDisposable
{
    private const string ClassName = "ScrollingWM_Osd";

    private const float FontSizePt = 36f;
    private const int PaddingX = 28;
    private const int PaddingY = 14;
    private const int CornerRadius = 18;
    private static readonly Color BgColor = Color.FromArgb(220, 28, 28, 28);
    private static readonly Color FgColor = Color.White;

    private const nuint TimerId = 1;
    private const uint TickIntervalMs = 16;
    private const int HoldMs = 700;
    private const int FadeMs = 250;

    private readonly nint _hwnd;
    private readonly Font _font;
    private readonly WndProcDelegate _wndProcDelegate;
    private bool _disposed;

    private long _showStartTicks;
    private byte _currentAlpha;
    private string _currentText = "";
    private (int X, int Y)? _lastPos;

    public OsdWindow()
    {
        _font = new Font("Segoe UI Semibold", FontSizePt, FontStyle.Regular, GraphicsUnit.Point);
        _wndProcDelegate = WndProc;
        var hInstance = GetModuleHandle(null);

        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = hInstance,
            lpszClassName = ClassName,
        };
        RegisterClassEx(ref wc);

        _hwnd = CreateWindowEx(
            WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW,
            ClassName, "swm-osd", WS_POPUP,
            0, 0, 0, 0, 0, 0, hInstance, 0);
    }

    /// <summary>
    /// Show <paramref name="text"/> centered on <paramref name="monitor"/>.
    /// Restarts the hold-then-fade cycle if already visible.
    /// </summary>
    public void Show(string text, Rect monitor)
    {
        if (_disposed) return;
        _currentText = text;
        _currentAlpha = 255;
        _showStartTicks = Environment.TickCount64;

        using var bmp = RenderBitmap(text, alpha: 255);
        var x = monitor.Left + (monitor.Width - bmp.Width) / 2;
        var y = monitor.Top + (monitor.Height - bmp.Height) / 2;
        UpdateLayeredCore(bmp, x, y);

        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);

        SetTimer(_hwnd, TimerId, TickIntervalMs, 0);
    }

    private nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        if (msg == WM_TIMER && (nuint)wParam == TimerId)
        {
            var elapsed = Environment.TickCount64 - _showStartTicks;
            if (elapsed < HoldMs) return 0;

            var fadeElapsed = elapsed - HoldMs;
            if (fadeElapsed >= FadeMs)
            {
                KillTimer(_hwnd, TimerId);
                ShowWindow(_hwnd, SW_HIDE);
                _currentAlpha = 0;
                return 0;
            }
            var newAlpha = (byte)(255 - (255 * fadeElapsed / FadeMs));
            if (newAlpha != _currentAlpha)
            {
                _currentAlpha = newAlpha;
                using var bmp = RenderBitmap(_currentText, newAlpha);
                if (_lastPos is { } p) UpdateLayeredCore(bmp, p.X, p.Y);
            }
            return 0;
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private Bitmap RenderBitmap(string text, byte alpha)
    {
        SizeF measured;
        using (var measureBmp = new Bitmap(1, 1, PixelFormat.Format32bppPArgb))
        using (var g = Graphics.FromImage(measureBmp))
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            measured = g.MeasureString(text, _font);
        }
        var w = (int)Math.Ceiling(measured.Width) + PaddingX * 2;
        var h = (int)Math.Ceiling(measured.Height) + PaddingY * 2;

        var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.CompositingMode = CompositingMode.SourceOver;
            g.Clear(Color.Transparent);

            var bgA = (byte)(BgColor.A * alpha / 255);
            var fgA = (byte)(FgColor.A * alpha / 255);
            using var bgBrush = new SolidBrush(Color.FromArgb(bgA, BgColor.R, BgColor.G, BgColor.B));
            using var fgBrush = new SolidBrush(Color.FromArgb(fgA, FgColor.R, FgColor.G, FgColor.B));

            using var path = RoundedRect(new RectangleF(0, 0, w, h), CornerRadius);
            g.FillPath(bgBrush, path);

            var textPos = new PointF((w - measured.Width) / 2f, (h - measured.Height) / 2f);
            g.DrawString(text, _font, fgBrush, textPos);
        }
        Premultiply(bmp);
        return bmp;
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var d = radius * 2f;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private static void Premultiply(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
        try
        {
            unsafe
            {
                var ptr = (byte*)data.Scan0;
                var stride = data.Stride;
                for (var y = 0; y < bmp.Height; y++)
                {
                    var row = ptr + y * stride;
                    for (var x = 0; x < bmp.Width; x++)
                    {
                        var idx = x * 4;
                        var a = row[idx + 3];
                        if (a == 255) continue;
                        row[idx + 0] = (byte)(row[idx + 0] * a / 255); // B
                        row[idx + 1] = (byte)(row[idx + 1] * a / 255); // G
                        row[idx + 2] = (byte)(row[idx + 2] * a / 255); // R
                    }
                }
            }
        }
        finally { bmp.UnlockBits(data); }
    }

    private void UpdateLayeredCore(Bitmap bmp, int x, int y)
    {
        var screenDc = GetDC(0);
        var memDc = CreateCompatibleDC(screenDc);
        var hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
        var oldBitmap = SelectObject(memDc, hBitmap);
        try
        {
            var size = new SIZE { cx = bmp.Width, cy = bmp.Height };
            var src = new POINT { x = 0, y = 0 };
            var dst = new POINT { x = x, y = y };
            var blend = new BLENDFUNCTION
            {
                BlendOp = AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AC_SRC_ALPHA,
            };
            UpdateLayeredWindow(_hwnd, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, ULW_ALPHA);
            _lastPos = (x, y);
        }
        finally
        {
            SelectObject(memDc, oldBitmap);
            DeleteObject(hBitmap);
            DeleteDC(memDc);
            ReleaseDC(0, screenDc);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        KillTimer(_hwnd, TimerId);
        DestroyWindow(_hwnd);
        _font.Dispose();
    }

    // ---------- P/Invoke ----------
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private static readonly nint HWND_TOPMOST = -1;
    private const int SW_HIDE = 0;
    private const uint WM_TIMER = 0x0113;
    private const uint ULW_ALPHA = 0x00000002;
    private const byte AC_SRC_OVER = 0x00;
    private const byte AC_SRC_ALPHA = 0x01;

    private delegate nint WndProcDelegate(nint hwnd, uint msg, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX wcx);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowEx(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint hInstance, nint param);
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DefWindowProc(nint hwnd, uint msg, nuint wParam, nint lParam);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hwnd, nint hwndInsertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hwnd, int nCmdShow);
    [DllImport("user32.dll")]
    private static extern nuint SetTimer(nint hwnd, nuint id, uint elapse, nint timerProc);
    [DllImport("user32.dll")]
    private static extern bool KillTimer(nint hwnd, nuint id);
    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hwnd, nint hdc);
    [DllImport("user32.dll")]
    private static extern bool UpdateLayeredWindow(nint hwnd, nint hdcDst, ref POINT pptDst, ref SIZE psize,
        nint hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);
    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint hdc);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint hdc);
    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint hdc, nint hObject);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint hObject);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);
}
