using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SwmSearch;

public sealed record WindowItem(long Hwnd, string Exe, string Title, bool Floating, bool Focused, bool CurrentDesktop)
{
    public override string ToString()
    {
        var marker = Focused ? "* " : (Floating ? "~ " : "  ");
        var scope = CurrentDesktop ? "" : " [other]";
        return $"{marker}{Exe,-28} {Title}{scope}";
    }

    public bool Match(string q) =>
        q.Length == 0 ||
        Exe.Contains(q, StringComparison.OrdinalIgnoreCase) ||
        Title.Contains(q, StringComparison.OrdinalIgnoreCase);
}

public sealed class SearchForm : Form
{
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();

    private readonly List<WindowItem> _all;
    private readonly TextBox _query;
    private readonly ListBox _list;
    private bool _allDesktops;
    public long? PickedHwnd { get; private set; }

    public SearchForm(List<WindowItem> items)
    {
        _all = items;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        ShowInTaskbar = false;
        TopMost = true;
        // Center on the screen that contains the focused window (not the
        // primary screen, and not the screen the mouse happens to be on).
        // Captured *before* this form takes focus.
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(640, 380);
        var target = Screen.FromHandle(GetForegroundWindow()).WorkingArea;
        Location = new Point(
            target.X + (target.Width - Size.Width) / 2,
            target.Y + (target.Height - Size.Height) / 2);
        KeyPreview = true;

        _query = new TextBox { Dock = DockStyle.Top, Font = new Font(FontFamily.GenericSansSerif, 11) };
        _query.TextChanged += (_, _) => Refilter();

        _list = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false,
            Font = new Font(FontFamily.GenericMonospace, 10),
        };
        _list.DoubleClick += (_, _) => Pick();

        Controls.Add(_list);
        Controls.Add(_query);

        KeyDown += OnKey;
        Deactivate += (_, _) => Close();

        UpdateTitle();
        Refilter();
        ActiveControl = _query;
    }

    private void UpdateTitle()
    {
        Text = _allDesktops
            ? "swm search — all desktops  [Tab: current only]"
            : "swm search — current desktop  [Tab: all desktops]";
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
