using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PboSpy.Services;

public sealed class WindowPlacement
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
}

public sealed class RepathRuleSetting
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public bool Regex { get; set; }
}

/// <summary>
/// Settings that are not Gemini's. Stored as JSON in %AppData%\PboSpy so they survive rebuilds
/// and moving the exe (Gemini's own user.config is tied to the exe path).
/// </summary>
public sealed class AppSettings : INotifyPropertyChanged
{
    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PboSpy", TestMode.On ? "Test" : "");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    public static AppSettings Default { get; } = Load();

    private string language = "";
    private string theme = "PboSpyDarkTheme";
    private bool confirmExit;
    private bool openMaximized = true;
    private bool showStartPage = true;
    private bool askAboutStartPage = true;
    private string lastExportFolder = "";
    private string ffmpegPath = "";
    private bool rememberExportOptions = true;
    private bool expandOnSingleClick;
    private bool showFileSizes = true;
    private bool singleClickPreview = true;
    private bool hideFilteredFiles;
    private bool confirmBeforeOverwrite;
    private bool openFolderAfterExport = true;
    private bool selectFilesAfterExport;
    private string modelExportFormat = ".glb";
    private int modelExportMaxTexture = 2048;
    private bool modelExportSplit = true;
    private bool modelExportDecimate;
    private int modelExportKeep = 50;
    private bool modelExportByMaterial = true;
    private bool modelExportLods;
    private string modelExportLodList = "50, 25, 10";
    private int modelExportSplitAt = 20000;
    private string modelRvmatFolder = "";
    private bool autoUpdate = true;
    private bool previewStats = true;
    private string previewShading = "rendered";
    private string updateToken = "";
    private bool dragOutConvertsImages;
    private int recentFileCount = 10;
    private int audioBitrate = 192;
    private int audioQuality = 6;
    private string defaultAudioFormat = "Wav";
    private string defaultImageFormat = "Png";
    private List<string> recentFiles = new();
    private List<string> rtmRigs = new();
    private string rtmRig = "";
    private string steamCmdPath = "";
    private string steamUser = "";
    private List<string> uncheckedExtensions = new();
    private string exportOptionsJson = "";
    private string p3dInput = "";
    private string p3dOutput = "";
    private bool p3dMirrorFolders = true;
    private List<RepathRuleSetting> p3dRecentRules = new();
    private string pbrInput = "";
    private string pbrOutput = "";
    private string pbrPrefix = "UVmap";
    private bool pbrFlipGreen;
    private bool pbrUseNames = true;
    private bool pbrWriteBaseColor = true;
    private bool pbrWriteOrm;
    private double pbrThreshold = 0.2;
    private string modelTextureFolder = "";
    private Dictionary<string, WindowPlacement> windows = new();

    public string Language { get => language; set => Set(ref language, value); }
    public string Theme { get => theme; set => Set(ref theme, value); }
    public bool ConfirmExit { get => confirmExit; set => Set(ref confirmExit, value); }
    public bool OpenMaximized { get => openMaximized; set => Set(ref openMaximized, value); }
    public bool ShowStartPage { get => showStartPage; set => Set(ref showStartPage, value); }
    public bool AskAboutStartPage { get => askAboutStartPage; set => Set(ref askAboutStartPage, value); }
    public string LastExportFolder { get => lastExportFolder; set => Set(ref lastExportFolder, value); }
    public string FfmpegPath { get => ffmpegPath; set => Set(ref ffmpegPath, value); }
    public bool RememberExportOptions { get => rememberExportOptions; set => Set(ref rememberExportOptions, value); }
    public bool ExpandOnSingleClick { get => expandOnSingleClick; set => Set(ref expandOnSingleClick, value); }
    public bool ShowFileSizes { get => showFileSizes; set => Set(ref showFileSizes, value); }
    public bool SingleClickPreview { get => singleClickPreview; set => Set(ref singleClickPreview, value); }
    public bool HideFilteredFiles { get => hideFilteredFiles; set => Set(ref hideFilteredFiles, value); }
    public bool ConfirmBeforeOverwrite { get => confirmBeforeOverwrite; set => Set(ref confirmBeforeOverwrite, value); }
    public bool OpenFolderAfterExport { get => openFolderAfterExport; set => Set(ref openFolderAfterExport, value); }
    public bool SelectFilesAfterExport { get => selectFilesAfterExport; set => Set(ref selectFilesAfterExport, value); }
    public string ModelExportFormat { get => modelExportFormat; set => Set(ref modelExportFormat, value ?? ".glb"); }
    public int ModelExportMaxTexture { get => modelExportMaxTexture; set => Set(ref modelExportMaxTexture, Math.Clamp(value, 64, 8192)); }
    public bool ModelExportSplit { get => modelExportSplit; set => Set(ref modelExportSplit, value); }
    public bool ModelExportDecimate { get => modelExportDecimate; set => Set(ref modelExportDecimate, value); }
    public int ModelExportKeep { get => modelExportKeep; set => Set(ref modelExportKeep, Math.Clamp(value, 5, 100)); }
    public bool ModelExportLods { get => modelExportLods; set => Set(ref modelExportLods, value); }
    public string ModelExportLodList { get => modelExportLodList; set => Set(ref modelExportLodList, value ?? ""); }
    public bool ModelExportByMaterial { get => modelExportByMaterial; set => Set(ref modelExportByMaterial, value); }
    public int ModelExportSplitAt { get => modelExportSplitAt; set => Set(ref modelExportSplitAt, Math.Max(100, value)); }
    public string ModelRvmatFolder { get => modelRvmatFolder; set => Set(ref modelRvmatFolder, value ?? ""); }
    public bool AutoUpdate { get => autoUpdate; set => Set(ref autoUpdate, value); }
    public bool PreviewStats { get => previewStats; set => Set(ref previewStats, value); }
    public string PreviewShading { get => previewShading; set => Set(ref previewShading, value ?? "rendered"); }
    public string UpdateToken { get => updateToken; set => Set(ref updateToken, value ?? ""); }
    public bool DragOutConvertsImages { get => dragOutConvertsImages; set => Set(ref dragOutConvertsImages, value); }
    public int RecentFileCount { get => recentFileCount; set => Set(ref recentFileCount, Math.Clamp(value, 0, 50)); }
    public int AudioBitrate { get => audioBitrate; set => Set(ref audioBitrate, Math.Clamp(value, 32, 320)); }
    public int AudioQuality { get => audioQuality; set => Set(ref audioQuality, Math.Clamp(value, 0, 10)); }
    public string DefaultAudioFormat { get => defaultAudioFormat; set => Set(ref defaultAudioFormat, value); }
    public string DefaultImageFormat { get => defaultImageFormat; set => Set(ref defaultImageFormat, value); }
    /// <summary>Rigs the user added for the animation preview (.p3d files on disk).</summary>
    public List<string> RtmRigs { get => rtmRigs; set => Set(ref rtmRigs, value ?? new()); }
    public string SteamCmdPath { get => steamCmdPath; set => Set(ref steamCmdPath, value ?? ""); }
    /// <summary>Only the account name; steamcmd keeps its own login, PboSpy never handles the password.</summary>
    public string SteamUser { get => steamUser; set => Set(ref steamUser, value ?? ""); }
    public string RtmRig { get => rtmRig; set => Set(ref rtmRig, value ?? ""); }
    public List<string> RecentFiles { get => recentFiles; set => Set(ref recentFiles, value ?? new()); }
    public List<string> UncheckedExtensions { get => uncheckedExtensions; set => Set(ref uncheckedExtensions, value ?? new()); }
    public string ExportOptionsJson { get => exportOptionsJson; set => Set(ref exportOptionsJson, value); }
    public string P3dInput { get => p3dInput; set => Set(ref p3dInput, value ?? ""); }
    public string P3dOutput { get => p3dOutput; set => Set(ref p3dOutput, value ?? ""); }
    public bool P3dMirrorFolders { get => p3dMirrorFolders; set => Set(ref p3dMirrorFolders, value); }
    public List<RepathRuleSetting> P3dRecentRules { get => p3dRecentRules; set => Set(ref p3dRecentRules, value ?? new()); }
    public string PbrInput { get => pbrInput; set => Set(ref pbrInput, value ?? ""); }
    public string PbrOutput { get => pbrOutput; set => Set(ref pbrOutput, value ?? ""); }
    public string PbrPrefix { get => pbrPrefix; set => Set(ref pbrPrefix, string.IsNullOrWhiteSpace(value) ? "UVmap" : value); }
    public bool PbrFlipGreen { get => pbrFlipGreen; set => Set(ref pbrFlipGreen, value); }
    public bool PbrUseNames { get => pbrUseNames; set => Set(ref pbrUseNames, value); }
    public bool PbrWriteBaseColor { get => pbrWriteBaseColor; set => Set(ref pbrWriteBaseColor, value); }
    public bool PbrWriteOrm { get => pbrWriteOrm; set => Set(ref pbrWriteOrm, value); }
    public double PbrThreshold { get => pbrThreshold; set => Set(ref pbrThreshold, Math.Clamp(value, 0, 1)); }
    public string ModelTextureFolder { get => modelTextureFolder; set => Set(ref modelTextureFolder, value ?? ""); }
    public Dictionary<string, WindowPlacement> Windows { get => windows; set => Set(ref windows, value ?? new()); }

    public event PropertyChangedEventHandler PropertyChanged;

    public void AddRecentFile(string path)
    {
        if (RecentFileCount == 0 || string.IsNullOrEmpty(path))
        {
            return;
        }
        // Files under an opened folder used to be added one by one; opening the folder again clears them out.
        var inside = path.TrimEnd('\\') +"\\";
        var list = recentFiles.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase) &&
            !p.StartsWith(inside, StringComparison.OrdinalIgnoreCase)).ToList();
        list.Insert(0, path);
        RecentFiles = list.Take(RecentFileCount).ToList();
        Save();
    }

    private static readonly object SaveLock = new();

    public void Save()
    {
        lock (SaveLock)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(this, Options));
                File.Move(temp, FilePath, true);
            }
            catch (Exception)
            {
            }
        }
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
            }
        }
        catch (Exception)
        {
        }
        return new AppSettings();
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
