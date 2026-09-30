using Gemini.Framework.Themes;
using PboSpy.Services;
using System.Collections;
using System.Windows;
using System.Windows.Media;

namespace PboSpy.Themes;

public static class ThemeService
{
    private static IThemeManager _manager;

    public static IReadOnlyList<ITheme> Themes => _manager?.Themes ?? new List<ITheme>();

    public static ITheme Current => _manager?.CurrentTheme;

    public static event EventHandler Changed;

    public static void Initialize(IThemeManager manager)
    {
        _manager = manager;

        // Gemini's stock themes are replaced by the reworked ones with the same look and names.
        manager.Themes.RemoveAll(t => t.GetType() == typeof(DarkTheme) || t.GetType() == typeof(BlueTheme) || t.GetType() == typeof(LightTheme));
        manager.Themes.Sort((a, b) => Order(a).CompareTo(Order(b)));

        var wanted = Migrate(string.IsNullOrEmpty(AppSettings.Default.Theme) ? GeminiSettings.ThemeName : AppSettings.Default.Theme);
        if (GeminiSettings.ThemeName != wanted)
        {
            GeminiSettings.ThemeName = wanted;
            GeminiSettings.Save();
        }

        manager.CurrentThemeChanged += (_, _) =>
        {
            Recolor(manager.CurrentTheme);
            Changed?.Invoke(null, EventArgs.Empty);
        };
    }

    public static bool IsDark => (Current as PaletteTheme)?.IsDarkSurface ?? true;

    /// <summary>Applies and remembers a theme.</summary>
    public static void SetTheme(ITheme theme)
    {
        if (theme == null)
        {
            return;
        }
        AppSettings.Default.Theme = theme.GetType().Name;
        AppSettings.Default.Save();
        // Gemini re-applies the theme when its setting changes, so this also switches it.
        GeminiSettings.ThemeName = theme.GetType().Name;
        GeminiSettings.Save();
        Preview(theme);
    }

    /// <summary>Applies a theme without remembering it, e.g. while picking one in the settings.</summary>
    public static void Preview(ITheme theme)
    {
        if (theme != null && _manager != null && _manager.CurrentTheme != theme)
        {
            _manager.SetCurrentTheme(theme.GetType().Name);
        }
    }

    private static int Order(ITheme theme) => theme switch
    {
        PboSpyDarkTheme => 0,
        PboSpyLightTheme => 1,
        PboSpyBlueTheme => 2,
        PboSpyMidnightTheme => 3,
        PboSpyArmaTheme => 4,
        PboSpyNordTheme => 5,
        PboSpyDraculaTheme => 6,
        PboSpyEmberTheme => 7,
        _ => 100
    };

    private static string Migrate(string name) => name switch
    {
        null or "" or nameof(DarkTheme) => nameof(PboSpyDarkTheme),
        nameof(BlueTheme) => nameof(PboSpyBlueTheme),
        nameof(LightTheme) => nameof(PboSpyLightTheme),
        _ when _manager.Themes.Any(t => t.GetType().Name == name) => name,
        _ => nameof(PboSpyDarkTheme)
    };

    private static void Recolor(ITheme theme)
    {
        var app = Application.Current;
        if (app == null)
        {
            return;
        }

        if (theme is PaletteTheme palette)
        {
            foreach (var dictionary in Walk(app.Resources))
            {
                Recolor(dictionary, palette);
            }
            if (app.MainWindow != null)
            {
                foreach (var dictionary in Walk(app.MainWindow.Resources))
                {
                    Recolor(dictionary, palette);
                }
            }
            SetBrush(app.Resources, "Explorer.SelectionBrush", palette.Selection);
            SetBrush(app.Resources, "Explorer.SelectionTextBrush", palette.SelectionText);
            SetBrush(app.Resources, "Explorer.FocusBrush", palette.Focus);

            SetBrush(app.Resources, "PboSpy.Surface", palette.Surface);
            SetBrush(app.Resources, "PboSpy.SurfaceAlt", palette.SurfaceAlt);
            SetBrush(app.Resources, "PboSpy.Deep", palette.Deep);
            SetBrush(app.Resources, "PboSpy.Input", palette.InputColor);
            SetBrush(app.Resources, "PboSpy.Border", palette.BorderColor);
            SetBrush(app.Resources, "PboSpy.Hover", palette.HoverColor);
            SetBrush(app.Resources, "PboSpy.Text", palette.TextColor);
            SetBrush(app.Resources, "PboSpy.TextDim", palette.TextDimColor);
            SetBrush(app.Resources, "PboSpy.Accent", palette.AccentColor);
            SetBrush(app.Resources, "PboSpy.AccentHover", palette.AccentHoverColor);
            SetBrush(app.Resources, "PboSpy.Selection", palette.Selection);
            SetBrush(app.Resources, "PboSpy.Success", palette.IsDarkSurface ? Color.FromRgb(0x6C, 0xD9, 0x7E) : Color.FromRgb(0x15, 0x80, 0x3D));
            SetBrush(app.Resources, "PboSpy.Warning", palette.IsDarkSurface ? Color.FromRgb(0xF5, 0xC2, 0x4C) : Color.FromRgb(0xB4, 0x53, 0x09));
            SetBrush(app.Resources, "PboSpy.Error", palette.IsDarkSurface ? Color.FromRgb(0xFF, 0x7B, 0x7B) : Color.FromRgb(0xC0, 0x26, 0x26));
        }
    }

    private static void Recolor(ResourceDictionary dictionary, PaletteTheme palette)
    {
        var updates = new List<(object Key, object Value)>();
        foreach (DictionaryEntry entry in dictionary)
        {
            switch (entry.Value)
            {
                case SolidColorBrush brush when palette.Map(brush.Color) is Color mapped:
                    if (brush.IsFrozen)
                    {
                        var copy = new SolidColorBrush(mapped) { Opacity = brush.Opacity };
                        copy.Freeze();
                        updates.Add((entry.Key, copy));
                    }
                    else
                    {
                        brush.Color = mapped;
                    }
                    break;
                case Color color when palette.Map(color) is Color mappedColor:
                    updates.Add((entry.Key, mappedColor));
                    break;
            }
        }
        foreach (var (key, value) in updates)
        {
            dictionary[key] = value;
        }
    }

    private static void SetBrush(ResourceDictionary resources, string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        resources[key] = brush;
    }

    private static IEnumerable<ResourceDictionary> Walk(ResourceDictionary root)
    {
        yield return root;
        foreach (var merged in root.MergedDictionaries.ToList())
        {
            foreach (var nested in Walk(merged))
            {
                yield return nested;
            }
        }
    }
}
