using System.Globalization;
using Tomlyn;

namespace ScrollingWM.Rules;

public sealed class Config
{
    /// <summary>Tile width in px. When null, defaults to
    /// <c>monitor.Width / TilesPerMonitor</c> at adoption time.</summary>
    public int? WindowWidth { get; set; }
    public int Gap { get; set; } = 0;
    /// <summary>How many equally-sized tiles fit on one monitor by default.
    /// Drives adoption width and the tiles command. Default 2.</summary>
    public int TilesPerMonitor { get; set; } = 2;
    /// <summary>Hex color like "#FF8C00" tints the focused window's border + title bar. Empty disables.</summary>
    public string FocusColor { get; set; } = "#FF8C00";
    public List<FloatRule> FloatRule { get; set; } = new();

    public static Config Load(string? path)
    {
        if (path == null || !File.Exists(path)) return new Config();
        var text = File.ReadAllText(path);
        return Toml.ToModel<Config>(text);
    }

    /// <summary>Parse "#RRGGBB" into a Win32 COLORREF (0x00BBGGRR). Null on empty/invalid.</summary>
    public static uint? ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var s = hex.TrimStart('#');
        if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) return null;
        var r = (rgb >> 16) & 0xFFu;
        var g = (rgb >> 8) & 0xFFu;
        var b = rgb & 0xFFu;
        return r | (g << 8) | (b << 16);
    }
}

public sealed class FloatRule
{
    public string? Exe { get; set; }
    public string? Class { get; set; }
    public string? Title { get; set; }

    public bool Matches(string exeName, string className, string title)
    {
        if (Exe != null && !MatchGlob(Exe, exeName)) return false;
        if (Class != null && !MatchGlob(Class, className)) return false;
        if (Title != null && !MatchGlob(Title, title)) return false;
        return Exe != null || Class != null || Title != null;
    }

    private static bool MatchGlob(string pattern, string value)
    {
        if (pattern == "*") return true;
        if (pattern.StartsWith("*") && pattern.EndsWith("*"))
            return value.Contains(pattern[1..^1], StringComparison.OrdinalIgnoreCase);
        if (pattern.StartsWith("*"))
            return value.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase);
        if (pattern.EndsWith("*"))
            return value.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);
        return string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase);
    }
}
