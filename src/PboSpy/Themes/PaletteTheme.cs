using Gemini.Framework.Themes;
using PboSpy.Localization;
using System.Windows.Media;

namespace PboSpy.Themes;

/// <summary>
/// A Gemini theme recoloured after it loads. Gemini's own dictionaries stay in charge of the
/// control templates; every brush whose colour belongs to one of the base theme's roles is
/// swapped for this palette's colour for that role.
/// </summary>
public abstract class PaletteTheme : ITheme
{
    protected enum Role
    {
        Deepest, Panel, Chrome, Input, Hover, Border, Accent, AccentLight, AccentDark, Text, TextDim, Disabled, Thumb,
        HighlightSoft, HighlightStrong, HighlightBorder
    }

    private static readonly Dictionary<string, Role> DarkRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["#FF1B1B1C"] = Role.Deepest, ["#FF1E1E1E"] = Role.Deepest, ["#FF1F1F20"] = Role.Deepest,
        ["#FF252526"] = Role.Panel, ["#FF2B2B2B"] = Role.Panel,
        ["#FF2D2D30"] = Role.Chrome,
        ["#FF333337"] = Role.Input, ["#FF333334"] = Role.Input, ["#FF38383B"] = Role.Input,
        ["#FF3E3E40"] = Role.Hover, ["#FF3E3E42"] = Role.Hover, ["#FF393939"] = Role.Hover, ["#FF404044"] = Role.Hover,
        ["#FF3F3F46"] = Role.Border, ["#FF434346"] = Role.Border, ["#FF46464A"] = Role.Border, ["#FF424245"] = Role.Border,
        ["#FF4D4D50"] = Role.Border, ["#FF4E4E50"] = Role.Border, ["#FF4A4A4C"] = Role.Border, ["#FF4D4D51"] = Role.Border,
        ["#FF007ACC"] = Role.Accent,
        ["#FF1C97EA"] = Role.AccentLight, ["#FF0099FF"] = Role.AccentLight, ["#FF0097FB"] = Role.AccentLight,
        ["#FF3399FF"] = Role.AccentLight, ["#FF52B0EF"] = Role.AccentLight, ["#FF55AAFF"] = Role.AccentLight, ["#FF59A8DE"] = Role.AccentLight,
        ["#FF0E6198"] = Role.AccentDark, ["#FF335C87"] = Role.AccentDark,
        ["#FFF1F1F1"] = Role.Text, ["#FFD0D0D0"] = Role.TextDim, ["#FF999999"] = Role.TextDim,
        ["#FF656565"] = Role.Disabled, ["#FF555558"] = Role.Disabled,
        ["#FF686868"] = Role.Thumb
    };

    private static readonly Dictionary<string, Role> LightRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["#FFEEEEF2"] = Role.Chrome, ["#FFF5F5F5"] = Role.Panel, ["#FFF6F6F6"] = Role.Deepest,
        ["#FFCCCEDB"] = Role.Border, ["#FFE0E3E6"] = Role.Border,
        ["#FFC9DEF5"] = Role.Hover, ["#FFE8E8EC"] = Role.Input,
        ["#FF007ACC"] = Role.Accent, ["#FF0E70C0"] = Role.AccentDark, ["#FF0E6198"] = Role.AccentDark,
        ["#FF3399FF"] = Role.AccentLight, ["#FF1C97EA"] = Role.AccentLight, ["#FF52B0EF"] = Role.AccentLight, ["#FF569DE5"] = Role.AccentLight,
        ["#FF1E1E1E"] = Role.Text, ["#FF717171"] = Role.TextDim, ["#FFA2A4A5"] = Role.Disabled
    };

    private static readonly Dictionary<string, Role> BlueRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["#FF293955"] = Role.Deepest, ["#FF364E6F"] = Role.Panel, ["#FF4D6082"] = Role.Chrome, ["#FF465A7D"] = Role.Border,
        ["#FF4B5C74"] = Role.Hover, ["#FF5B7199"] = Role.Hover, ["#FF8E9BBC"] = Role.Border,
        ["#FFD6DBE9"] = Role.Input, ["#FFCFD6E5"] = Role.Input, ["#FFDCE0EC"] = Role.Input,
        ["#FFFDF4BF"] = Role.HighlightSoft, ["#FFFFFCF4"] = Role.HighlightSoft, ["#FFFFF29D"] = Role.HighlightStrong,
        ["#FFFFE8A6"] = Role.HighlightStrong, ["#FFE5C365"] = Role.HighlightBorder, ["#FFE2CD93"] = Role.HighlightBorder,
        ["#FFE0D2AA"] = Role.HighlightSoft, ["#FF75633D"] = Role.TextDim,
        ["#FF007ACC"] = Role.Accent, ["#FF3399FF"] = Role.AccentLight, ["#FF1BA1E2"] = Role.AccentLight, ["#FF1C97EA"] = Role.AccentLight
    };

    public abstract string Key { get; }

    public virtual string Name => Loc.T("Theme." + Key);

    protected abstract ITheme Base { get; }

    protected abstract IReadOnlyDictionary<Role, string> Palette { get; }

    public abstract Color Selection { get; }

    public virtual Color SelectionText => Colors.White;

    public virtual Color Focus => Selection;

    public bool IsDark => Base is DarkTheme;

    // Colours for PboSpy's own windows and pages (start page, P3D tools, PBR maker...).
    public virtual Color Surface => Get(Role.Panel, IsDark ? "#FF1E1F22" : "#FFFFFFFF");
    public virtual Color SurfaceAlt => Get(Role.Chrome, IsDark ? "#FF26272B" : "#FFF1F3F6");
    public virtual Color Deep => Get(Role.Deepest, IsDark ? "#FF16171A" : "#FFF8F9FB");
    public virtual Color InputColor => Get(Role.Input, IsDark ? "#FF2E3035" : "#FFFFFFFF");
    public virtual Color BorderColor => Get(Role.Border, IsDark ? "#FF3A3D43" : "#FFD4D8DE");
    public virtual Color HoverColor => Get(Role.Hover, IsDark ? "#FF383B41" : "#FFDCE8FB");
    public virtual Color TextColor => Get(Role.Text, IsDark ? "#FFE6E8EB" : "#FF111827");
    public virtual Color TextDimColor => Get(Role.TextDim, IsDark ? "#FFA1A6AE" : "#FF5B6472");
    public virtual Color AccentColor => Get(Role.Accent, "#FF3B82F6");
    public virtual Color AccentHoverColor => Get(Role.AccentLight, "#FF60A5FA");

    public bool IsDarkSurface => Surface.R * 0.299 + Surface.G * 0.587 + Surface.B * 0.114 < 128;

    protected Color Get(Role role, string fallback) =>
        Palette.TryGetValue(role, out var hex) ? Parse(hex) : Parse(fallback);

    public IEnumerable<Uri> ApplicationResources => Base.ApplicationResources;

    public IEnumerable<Uri> MainWindowResources => Base.MainWindowResources;

    private Dictionary<Color, Color> _map;

    public Color? Map(Color original)
    {
        _map ??= BuildMap();
        return _map.TryGetValue(original, out var mapped) ? mapped : null;
    }

    private Dictionary<Color, Color> BuildMap()
    {
        var roles = Base switch
        {
            DarkTheme => DarkRoles,
            BlueTheme => BlueRoles,
            _ => LightRoles
        };
        var map = new Dictionary<Color, Color>();
        foreach (var (hex, role) in roles)
        {
            if (Palette.TryGetValue(role, out var target))
            {
                map[Parse(hex)] = Parse(target);
            }
        }
        return map;
    }

    protected static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
