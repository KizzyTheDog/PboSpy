using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace PboSpy.Localization;

public record LanguageInfo(string Code, string NativeName);

/// <summary>
/// Runtime string table. Everything bound through <see cref="TrExtension"/> or read through
/// <see cref="T"/> follows <see cref="SetLanguage"/> without restarting.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public const string Fallback = "en";

    public static Loc Instance { get; } = new();

    private readonly Dictionary<string, Dictionary<string, string>> _tables = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _current = new();
    private Dictionary<string, string> _fallback = new();

    public event PropertyChangedEventHandler PropertyChanged;
    public event EventHandler LanguageChanged;

    public string Language { get; private set; } = Fallback;

    public IReadOnlyList<LanguageInfo> Languages { get; private set; } = Array.Empty<LanguageInfo>();

    public static string UserLanguageFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PboSpy", "Languages");

    private Loc()
    {
        LoadTables();
        _fallback = _tables.TryGetValue(Fallback, out var en) ? en : new();
        _current = _fallback;
    }

    public string this[string key] => Get(key);

    public static string T(string key) => Instance.Get(key);

    public static string F(string key, params object[] args)
    {
        try
        {
            return string.Format(CultureInfo.CurrentUICulture, Instance.Get(key), args);
        }
        catch (FormatException)
        {
            return Instance.Get(key);
        }
    }

    public string Get(string key)
    {
        if (key == null)
        {
            return string.Empty;
        }
        if (_current.TryGetValue(key, out var value) || _fallback.TryGetValue(key, out value))
        {
            return value;
        }
        return key;
    }

    public bool TryGet(string key, out string value)
        => _current.TryGetValue(key, out value);

    public void SetLanguage(string code)
    {
        code = string.IsNullOrWhiteSpace(code) ? CultureInfo.InstalledUICulture.Name : code;
        var resolved = Resolve(code);

        Language = resolved;
        _current = _tables.TryGetValue(resolved, out var table) ? table : _fallback;

        try
        {
            var culture = CultureInfo.GetCultureInfo(resolved);
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
        catch (CultureNotFoundException)
        {
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private string Resolve(string code)
    {
        if (_tables.ContainsKey(code))
        {
            return code;
        }
        var neutral = code.Split('-')[0];
        var match = _tables.Keys.FirstOrDefault(k => k.Split('-')[0].Equals(neutral, StringComparison.OrdinalIgnoreCase));
        return match ?? Fallback;
    }

    private void LoadTables()
    {
        var assembly = Assembly.GetExecutingAssembly();
        const string prefix = "PboSpy.Localization.Languages.";
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(prefix) && n.EndsWith(".json")))
        {
            using var stream = assembly.GetManifestResourceStream(name);
            Merge(name[prefix.Length..^5], stream);
        }

        // Drop a <code>.json next to the others to add or patch a language without rebuilding.
        if (Directory.Exists(UserLanguageFolder))
        {
            foreach (var file in Directory.EnumerateFiles(UserLanguageFolder, "*.json"))
            {
                try
                {
                    using var stream = File.OpenRead(file);
                    Merge(Path.GetFileNameWithoutExtension(file), stream);
                }
                catch (Exception)
                {
                }
            }
        }

        Languages = _tables
            .Select(t => new LanguageInfo(t.Key, t.Value.TryGetValue("Language.Name", out var n) ? n : t.Key))
            .OrderBy(l => l.Code == Fallback ? 0 : 1)
            .ThenBy(l => l.NativeName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void Merge(string code, Stream stream)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(stream,
            new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (values == null)
        {
            return;
        }
        if (!_tables.TryGetValue(code, out var table))
        {
            _tables[code] = table = new Dictionary<string, string>(StringComparer.Ordinal);
        }
        foreach (var kv in values)
        {
            table[kv.Key] = kv.Value;
        }
    }
}
