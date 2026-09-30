using System.Configuration;

namespace PboSpy.Services;

/// <summary>Gemini keeps its settings class internal; this reaches it through ApplicationSettingsBase.</summary>
internal static class GeminiSettings
{
    private static readonly ApplicationSettingsBase Instance =
        typeof(Gemini.Framework.Themes.ThemeManager).Assembly
            .GetType("Gemini.Properties.Settings")?
            .GetProperty("Default")?
            .GetValue(null) as ApplicationSettingsBase;

    public static string ThemeName
    {
        get => Instance?["ThemeName"] as string ?? "";
        set
        {
            if (Instance != null)
            {
                Instance["ThemeName"] = value;
            }
        }
    }

    public static string LanguageCode
    {
        get => Instance?["LanguageCode"] as string ?? "";
        set
        {
            if (Instance != null)
            {
                Instance["LanguageCode"] = value;
            }
        }
    }

    public static void Save() => Instance?.Save();
}
