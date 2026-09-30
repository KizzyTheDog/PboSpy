using Gemini.Framework.Themes;
using System.Windows.Media;

namespace PboSpy.Themes;

[Export(typeof(ITheme))]
public class PboSpyDarkTheme : PaletteTheme
{
    public override string Key => "Dark";
    protected override ITheme Base { get; } = new DarkTheme();
    public override Color Selection => Parse("#FF2F63B8");
    public override Color Focus => Parse("#FF60A5FA");
    protected override IReadOnlyDictionary<Role, string> Palette { get; } = new Dictionary<Role, string>
    {
        [Role.Deepest] = "#FF16171A", [Role.Panel] = "#FF1E1F22", [Role.Chrome] = "#FF26272B", [Role.Input] = "#FF2E3035",
        [Role.Hover] = "#FF383B41", [Role.Border] = "#FF3A3D43", [Role.Accent] = "#FF3B82F6", [Role.AccentLight] = "#FF60A5FA",
        [Role.AccentDark] = "#FF1E4FA8", [Role.Text] = "#FFE6E8EB", [Role.TextDim] = "#FFA1A6AE", [Role.Disabled] = "#FF5E636B",
        [Role.Thumb] = "#FF5A5F68"
    };
}

[Export(typeof(ITheme))]
public class PboSpyMidnightTheme : PaletteTheme
{
    public override string Key => "Midnight";
    protected override ITheme Base { get; } = new DarkTheme();
    public override Color Selection => Parse("#FF0F766E");
    public override Color Focus => Parse("#FF2DD4BF");
    protected override IReadOnlyDictionary<Role, string> Palette { get; } = new Dictionary<Role, string>
    {
        [Role.Deepest] = "#FF000000", [Role.Panel] = "#FF08090A", [Role.Chrome] = "#FF0F1012", [Role.Input] = "#FF16171A",
        [Role.Hover] = "#FF222428", [Role.Border] = "#FF1E2024", [Role.Accent] = "#FF14B8A6", [Role.AccentLight] = "#FF2DD4BF",
        [Role.AccentDark] = "#FF0F766E", [Role.Text] = "#FFE4E6E9", [Role.TextDim] = "#FF9199A3", [Role.Disabled] = "#FF4B5058",
        [Role.Thumb] = "#FF3A3E45"
    };
}

[Export(typeof(ITheme))]
public class PboSpyArmaTheme : PaletteTheme
{
    public override string Key => "Arma";
    protected override ITheme Base { get; } = new DarkTheme();
    public override Color Selection => Parse("#FF8A6D25");
    public override Color Focus => Parse("#FFE0BE6A");
    protected override IReadOnlyDictionary<Role, string> Palette { get; } = new Dictionary<Role, string>
    {
        [Role.Deepest] = "#FF15160F", [Role.Panel] = "#FF1C1E15", [Role.Chrome] = "#FF24271B", [Role.Input] = "#FF2D3022",
        [Role.Hover] = "#FF3A3E2C", [Role.Border] = "#FF3B3F2C", [Role.Accent] = "#FFB8913C", [Role.AccentLight] = "#FFE0BE6A",
        [Role.AccentDark] = "#FF7A5F1F", [Role.Text] = "#FFE9E4D2", [Role.TextDim] = "#FFA9A48E", [Role.Disabled] = "#FF66634F",
        [Role.Thumb] = "#FF5A5C45"
    };
}

[Export(typeof(ITheme))]
public class PboSpyNordTheme : PaletteTheme
{
    public override string Key => "Nord";
    protected override ITheme Base { get; } = new DarkTheme();
    public override Color Selection => Parse("#FF5E81AC");
    public override Color Focus => Parse("#FF88C0D0");
    protected override IReadOnlyDictionary<Role, string> Palette { get; } = new Dictionary<Role, string>
    {
        [Role.Deepest] = "#FF242933", [Role.Panel] = "#FF2B303B", [Role.Chrome] = "#FF2E3440", [Role.Input] = "#FF3B4252",
        [Role.Hover] = "#FF434C5E", [Role.Border] = "#FF3B4252", [Role.Accent] = "#FF5E81AC", [Role.AccentLight] = "#FF88C0D0",
        [Role.AccentDark] = "#FF4C6A92", [Role.Text] = "#FFECEFF4", [Role.TextDim] = "#FFA7B1C2", [Role.Disabled] = "#FF616E88",
        [Role.Thumb] = "#FF4C566A"
    };
}

[Export(typeof(ITheme))]
public class PboSpyDraculaTheme : PaletteTheme
{
    public override string Key => "Dracula";
    protected override ITheme Base { get; } = new DarkTheme();
    public override Color Selection => Parse("#FF6D4AAE");
    public override Color Focus => Parse("#FFBD93F9");
    protected override IReadOnlyDictionary<Role, string> Palette { get; } = new Dictionary<Role, string>
    {
        [Role.Deepest] = "#FF1E1F29", [Role.Panel] = "#FF21222C", [Role.Chrome] = "#FF282A36", [Role.Input] = "#FF343746",
        [Role.Hover] = "#FF44475A", [Role.Border] = "#FF3C3F52", [Role.Accent] = "#FF9D6DF2", [Role.AccentLight] = "#FFBD93F9",
        [Role.AccentDark] = "#FF6D4AAE", [Role.Text] = "#FFF8F8F2", [Role.TextDim] = "#FFA6ACCD", [Role.Disabled] = "#FF6272A4",
        [Role.Thumb] = "#FF535674"
    };
}

[Export(typeof(ITheme))]
public class PboSpyEmberTheme : PaletteTheme
{
    public override string Key => "Ember";
    protected override ITheme Base { get; } = new DarkTheme();
    public override Color Selection => Parse("#FFB4491A");
    public override Color Focus => Parse("#FFFB923C");
    protected override IReadOnlyDictionary<Role, string> Palette { get; } = new Dictionary<Role, string>
    {
        [Role.Deepest] = "#FF171412", [Role.Panel] = "#FF1F1B18", [Role.Chrome] = "#FF28221E", [Role.Input] = "#FF322A25",
        [Role.Hover] = "#FF40362F", [Role.Border] = "#FF3D332D", [Role.Accent] = "#FFEA580C", [Role.AccentLight] = "#FFFB923C",
        [Role.AccentDark] = "#FF9A3412", [Role.Text] = "#FFF1E9E2", [Role.TextDim] = "#FFB0A294", [Role.Disabled] = "#FF6B5F55",
        [Role.Thumb] = "#FF5E524A"
    };
}

[Export(typeof(ITheme))]
public class PboSpyLightTheme : PaletteTheme
{
    public override string Key => "Light";
    protected override ITheme Base { get; } = new LightTheme();
    public override Color Selection => Parse("#FF2563EB");
    public override Color Focus => Parse("#FF1D4ED8");
    protected override IReadOnlyDictionary<Role, string> Palette { get; } = new Dictionary<Role, string>
    {
        [Role.Chrome] = "#FFF1F3F6", [Role.Panel] = "#FFFFFFFF", [Role.Deepest] = "#FFF8F9FB", [Role.Border] = "#FFD4D8DE",
        [Role.Hover] = "#FFDCE8FB", [Role.Input] = "#FFE9ECF1", [Role.Accent] = "#FF2563EB", [Role.AccentDark] = "#FF1D4ED8",
        [Role.AccentLight] = "#FF3B82F6", [Role.Text] = "#FF111827", [Role.TextDim] = "#FF5B6472", [Role.Disabled] = "#FFA3A9B3"
    };
}

[Export(typeof(ITheme))]
public class PboSpyBlueTheme : PaletteTheme
{
    public override string Key => "Blue";
    protected override ITheme Base { get; } = new BlueTheme();
    public override Color Selection => Parse("#FF2F6FD6");
    public override Color Focus => Parse("#FF1E4FA8");
    protected override IReadOnlyDictionary<Role, string> Palette { get; } = new Dictionary<Role, string>
    {
        [Role.Deepest] = "#FF1B3252", [Role.Panel] = "#FF22406A", [Role.Chrome] = "#FF2D4F7E", [Role.Border] = "#FF3A5F92",
        [Role.Hover] = "#FF3B64A0", [Role.Input] = "#FFE3EAF5",
        [Role.HighlightSoft] = "#FFDCEBFF", [Role.HighlightStrong] = "#FFBCD6FA", [Role.HighlightBorder] = "#FF5B8FDB",
        [Role.TextDim] = "#FF3E5270", [Role.Accent] = "#FF2F6FD6", [Role.AccentLight] = "#FF4F8BEA"
    };

    // Gemini's blue theme pairs dark blue chrome with light documents; PboSpy's pages follow the documents.
    public override Color Surface => Parse("#FFF3F6FB");
    public override Color SurfaceAlt => Parse("#FFE3EAF5");
    public override Color Deep => Parse("#FFE9EEF6");
    public override Color InputColor => Parse("#FFFFFFFF");
    public override Color BorderColor => Parse("#FFB8C6DC");
    public override Color HoverColor => Parse("#FFD6E3F7");
    public override Color TextColor => Parse("#FF14233A");
    public override Color TextDimColor => Parse("#FF4A5D7A");
}
