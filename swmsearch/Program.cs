using System.IO.Pipes;
using System.Text.Json;
using System.Windows.Forms;

namespace SwmSearch;

internal static class Program
{
    private const string PipeName = "swm";

    [STAThread]
    private static int Main()
    {
        var json = Send("list");
        if (json == null)
        {
            MessageBox.Show("swm daemon not running.", "swm search",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }

        List<WindowDoc>? docs;
        try { docs = JsonSerializer.Deserialize<List<WindowDoc>>(json, JsonOpts); }
        catch (Exception ex)
        {
            MessageBox.Show($"bad list response: {ex.Message}\n\n{json}", "swm search");
            return 1;
        }
        var items = (docs ?? new()).ConvertAll(d =>
            new WindowItem(d.hwnd, d.exe ?? "", d.title ?? "", d.floating, d.focused, d.currentDesktop));

        ApplicationConfiguration.Initialize();
        using var form = new SearchForm(items);
        Application.Run(form);

        if (form.PickedHwnd is long h) Send($"goto {h}");
        return 0;
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public static string? Send(string line)
    {
        try
        {
            using var c = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
            c.Connect(1000);
            using var w = new StreamWriter(c, leaveOpen: true) { AutoFlush = true };
            using var r = new StreamReader(c, leaveOpen: true);
            w.WriteLine(line);
            return r.ReadLine();
        }
        catch { return null; }
    }

    private sealed record WindowDoc(long hwnd, string? exe, string? title,
        long monitor, string? desktop, bool currentDesktop, bool floating, bool focused);
}
