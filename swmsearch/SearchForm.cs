using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SwmSearch;

public sealed record WindowItem(long Hwnd, string Exe, string Title, bool Floating, bool Focused, bool CurrentDesktop)
{
    public bool Match(string q) =>
        q.Length == 0 ||
        Exe.Contains(q, StringComparison.OrdinalIgnoreCase) ||
        Title.Contains(q, StringComparison.OrdinalIgnoreCase);
}

public sealed class SearchForm : Form
{
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, uint attr, ref int value, int size);
    private const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    // Light palette inspired by VS Code's light command palette.
    private static readonly Color BgColor       = Color.FromArgb(0xFF, 0xFF, 0xFF);
    private static readonly Color HeaderColor   = Color.FromArgb(0xF3, 0xF3, 0xF3);
    private static readonly Color BorderColor   = Color.FromArgb(0xCC, 0xCC, 0xCC);
    private static readonly Color TextColor     = Color.FromArgb(0x1F, 0x1F, 0x1F);
    private static readonly Color MutedColor    = Color.FromArgb(0x61, 0x61, 0x61);
    private static readonly Color AccentColor   = Color.FromArgb(0x00, 0x67, 0xC0);
    private static readonly Color FocusedExe    = Color.FromArgb(0x00, 0x55, 0xAA);
    private static readonly Color FloatingExe   = Color.FromArgb(0xB5, 0x5E, 0x00);

    private const int ExeColumnWidth = 220;
    private const int RowHeight = 26;
    private const int Pad = 12;

    private readonly List<WindowItem> _all;
    private readonly TextBox _query;
    private readonly ListBox _list;
    private readonly Label _header;
    private bool _allDesktops;
    public long? PickedHwnd { get; private set; }

    public SearchForm(List<WindowItem> items)
    {
        _all = items;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = BgColor;
        ForeColor = TextColor;
        DoubleBuffered = true;
        Padding = new Padding(1);

        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(720, 440);
        var target = Screen.FromHandle(GetForegroundWindow()).WorkingArea;
        Location = new Point(
            target.X + (target.Width - Size.Width) / 2,
            target.Y + (target.Height - Size.Height) / 4);
        KeyPreview = true;

        var headerFont = new Font("Segoe UI", 9f, FontStyle.Regular);
        var queryFont = new Font("Segoe UI", 13f, FontStyle.Regular);
        var listFont = new Font("Cascadia Mono", 10f, FontStyle.Regular,
            GraphicsUnit.Point, 0, gdiVerticalFont: false);
        if (listFont.Name != "Cascadia Mono")
            listFont = new Font("Consolas", 10f, FontStyle.Regular);

        _header = new Label
        {
            Dock = DockStyle.Top,
            Height = 36,
            BackColor = HeaderColor,
            ForeColor = MutedColor,
            Font = headerFont,
            Padding = new System.Windows.Forms.Padding(Pad, 10, Pad, 10),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var queryHost = new Panel
        {
            Dock = DockStyle.Top,
            Height = 48,
            BackColor = BgColor,
            Padding = new System.Windows.Forms.Padding(Pad, 10, Pad, 10),
        };
        _query = new TextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            Font = queryFont,
            BackColor = BgColor,
            ForeColor = TextColor,
        };
        _query.TextChanged += (_, _) => Refilter();
        queryHost.Controls.Add(_query);

        var separator = new Panel
        {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = BorderColor,
        };

        var listHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = BgColor,
            Padding = new System.Windows.Forms.Padding(Pad - 4, 6, Pad - 4, Pad - 4),
        };
        _list = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false,
            Font = listFont,
            BorderStyle = BorderStyle.None,
            BackColor = BgColor,
            ForeColor = TextColor,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = RowHeight,
        };
        _list.DrawItem += DrawListItem;
        _list.DoubleClick += (_, _) => Pick();
        listHost.Controls.Add(_list);

        Controls.Add(listHost);
        Controls.Add(separator);
        Controls.Add(queryHost);
        Controls.Add(_header);

        KeyDown += OnKey;
        Deactivate += (_, _) => Close();
        HandleCreated += (_, _) =>
        {
            int pref = DWMWCP_ROUND;
            DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        };

        UpdateTitle();
        Refilter();
        ActiveControl = _query;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(BorderColor, 1);
        var r = ClientRectangle;
        e.Graphics.DrawRectangle(pen, 0, 0, r.Width - 1, r.Height - 1);
    }

    private void DrawListItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _list.Items.Count) return;
        var w = (WindowItem)_list.Items[e.Index]!;
        var selected = (e.State & DrawItemState.Selected) != 0;
        var g = e.Graphics;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var bg = selected ? AccentColor : BgColor;
        using (var brush = new SolidBrush(bg)) g.FillRectangle(brush, e.Bounds);

        var exeColor = selected ? Color.White
            : w.Focused ? FocusedExe
            : w.Floating ? FloatingExe
            : TextColor;
        var titleColor = selected ? Color.White : MutedColor;

        var marker = w.Focused ? "●" : w.Floating ? "○" : " ";
        var markerColor = selected ? Color.White
            : w.Focused ? FocusedExe
            : w.Floating ? FloatingExe
            : MutedColor;

        const int leftPad = 10;
        const int markerWidth = 18;
        var exeX = e.Bounds.Left + leftPad + markerWidth;
        var titleX = exeX + ExeColumnWidth + 14;
        var y = e.Bounds.Top;
        var rowHeight = e.Bounds.Height;

        var markerRect = new Rectangle(e.Bounds.Left + leftPad, y, markerWidth, rowHeight);
        var exeRect = new Rectangle(exeX, y, ExeColumnWidth, rowHeight);
        var titleRect = new Rectangle(titleX, y, e.Bounds.Right - titleX - leftPad, rowHeight);

        var fmt = TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix
                | TextFormatFlags.SingleLine;

        TextRenderer.DrawText(g, marker, e.Font!, markerRect, markerColor, fmt);
        TextRenderer.DrawText(g, w.Exe, e.Font!, exeRect, exeColor, fmt);

        var title = w.CurrentDesktop ? w.Title : $"{w.Title}  [other desktop]";
        TextRenderer.DrawText(g, title, e.Font!, titleRect, titleColor, fmt);
    }

    private void UpdateTitle()
    {
        _header.Text = _allDesktops
            ? "  swm search   ·   all desktops   ·   Tab: current only   ·   Esc to cancel"
            : "  swm search   ·   current desktop   ·   Tab: all desktops   ·   Esc to cancel";
    }

    private void Refilter()
    {
        var q = _query.Text.Trim();
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var w in _all)
        {
            if (!_allDesktops && !w.CurrentDesktop) continue;
            if (w.Match(q)) _list.Items.Add(w);
        }
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        _list.EndUpdate();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Tab || keyData == (Keys.Tab | Keys.Shift))
        {
            _allDesktops = !_allDesktops;
            UpdateTitle();
            Refilter();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void OnKey(object? s, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Down:
                if (_list.SelectedIndex < _list.Items.Count - 1) _list.SelectedIndex++;
                e.Handled = e.SuppressKeyPress = true;
                break;
            case Keys.Up:
                if (_list.SelectedIndex > 0) _list.SelectedIndex--;
                e.Handled = e.SuppressKeyPress = true;
                break;
            case Keys.Tab:
                // Handled in ProcessCmdKey (Tab is eaten by dialog navigation
                // before reaching KeyDown). Branch kept here as a safety net.
                _allDesktops = !_allDesktops;
                UpdateTitle();
                Refilter();
                e.Handled = e.SuppressKeyPress = true;
                break;
            case Keys.Enter:
                Pick();
                e.Handled = e.SuppressKeyPress = true;
                break;
            case Keys.Escape:
                Close();
                e.Handled = e.SuppressKeyPress = true;
                break;
        }
    }

    private void Pick()
    {
        if (_list.SelectedItem is WindowItem w)
        {
            PickedHwnd = w.Hwnd;
            Close();
        }
    }
}
