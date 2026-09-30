using Gemini.Framework.Themes;
using Gemini.Modules.Settings;
using Microsoft.Win32;
using PboSpy.Localization;
using PboSpy.Modules.Audio.Services;
using PboSpy.Modules.BulkExport.Models;
using PboSpy.Services;
using PboSpy.Themes;
using System.Diagnostics;
using System.IO;

namespace PboSpy.Modules.Startup.ViewModels;

[Export(typeof(ISettingsEditor))]
[PartCreationPolicy(CreationPolicy.NonShared)]
public class ApplicationSettingsViewModel : PropertyChangedBase, ISettingsEditor
{
    private readonly AppSettings _settings = AppSettings.Default;

    public ApplicationSettingsViewModel()
    {
        ConfirmExit = _settings.ConfirmExit;
        OpenFullscreen = _settings.OpenMaximized;
        ShowStartPage = _settings.ShowStartPage;
        AskAboutStartPage = _settings.AskAboutStartPage;

        Languages = Loc.Instance.Languages;
        _originalLanguage = Loc.Instance.Language;
        _selectedLanguage = Languages.FirstOrDefault(l => l.Code == Loc.Instance.Language) ?? Languages.FirstOrDefault();
        Themes = ThemeService.Themes;
        _originalTheme = ThemeService.Current;
        _selectedTheme = ThemeService.Current;

        RememberExportOptions = _settings.RememberExportOptions;
        ShowFileSizes = _settings.ShowFileSizes;
        SingleClickPreview = _settings.SingleClickPreview;
        AutoUpdate = _settings.AutoUpdate;
        UpdateToken = _settings.UpdateToken;
        HideFilteredFiles = _settings.HideFilteredFiles;
        ConfirmBeforeOverwrite = _settings.ConfirmBeforeOverwrite;
        OpenFolderAfterExport = _settings.OpenFolderAfterExport;
        DragOutConvertsImages = _settings.DragOutConvertsImages;
        RecentFileCount = _settings.RecentFileCount;
        DefaultImageFormat = Enum.TryParse<ImageOutputFormat>(_settings.DefaultImageFormat, out var image) ? image : ImageOutputFormat.Png;
        DefaultAudioFormat = Enum.TryParse<AudioOutputFormat>(_settings.DefaultAudioFormat, out var audio) ? audio : AudioOutputFormat.Wav;
        AudioBitrate = _settings.AudioBitrate;
        AudioQuality = _settings.AudioQuality;
        FfmpegPath = _settings.FfmpegPath;
    }

    private readonly string _originalLanguage;
    private readonly ITheme _originalTheme;
    private LanguageInfo _selectedLanguage;
    private ITheme _selectedTheme;
    private bool _applied;

    public IReadOnlyList<LanguageInfo> Languages { get; }

    /// <summary>Switches straight away so the choice can be seen; Cancel puts the old one back.</summary>
    public LanguageInfo SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            _selectedLanguage = value;
            NotifyOfPropertyChange(nameof(SelectedLanguage));
            if (value != null && value.Code != Loc.Instance.Language)
            {
                Loc.Instance.SetLanguage(value.Code);
            }
        }
    }

    public IReadOnlyList<ITheme> Themes { get; }

    /// <summary>Previewed immediately; only kept when the dialog is closed with OK.</summary>
    public ITheme SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            _selectedTheme = value;
            NotifyOfPropertyChange(nameof(SelectedTheme));
            ThemeService.Preview(value);
        }
    }

    /// <summary>Called when the settings window closes; undoes previews if OK wasn't pressed.</summary>
    public void OnDialogClosed()
    {
        if (_applied)
        {
            return;
        }
        if (_originalTheme != null && ThemeService.Current != _originalTheme)
        {
            ThemeService.Preview(_originalTheme);
        }
        if (Loc.Instance.Language != _originalLanguage)
        {
            Loc.Instance.SetLanguage(_originalLanguage);
        }
    }

    public bool ShowStartPage { get; set; }
    public bool AskAboutStartPage { get; set; }
    public bool ConfirmExit { get; set; }
    public bool OpenFullscreen { get; set; }
    public bool RememberExportOptions { get; set; }
    public bool ShowFileSizes { get; set; }
    public bool SingleClickPreview { get; set; }

    public bool AutoUpdate { get; set; }

    public string UpdateToken { get; set; }

    public string VersionText => Loc.F("Update.Version", PboSpy.Services.Updater.Current.ToString(3));

    public async Task CheckForUpdates() => await PboSpy.Services.Updater.Check(quiet: false);
    public bool HideFilteredFiles { get; set; }
    public bool ConfirmBeforeOverwrite { get; set; }
    public bool OpenFolderAfterExport { get; set; }
    public bool DragOutConvertsImages { get; set; }
    public int RecentFileCount { get; set; }
    public ImageOutputFormat DefaultImageFormat { get; set; }
    public AudioOutputFormat DefaultAudioFormat { get; set; }
    public int AudioBitrate { get; set; }
    public int AudioQuality { get; set; }

    private string _ffmpegPath;
    public string FfmpegPath
    {
        get => _ffmpegPath;
        set
        {
            _ffmpegPath = value;
            NotifyOfPropertyChange(nameof(FfmpegPath));
            NotifyOfPropertyChange(nameof(FfmpegStatus));
        }
    }

    public string FfmpegStatus => AudioEncoder.ResolveFfmpeg(FfmpegPath) is string found
        ? Loc.F("Settings.FfmpegFound", found)
        : Loc.T("Settings.FfmpegMissing");

    public IEnumerable<ImageOutputFormat> ImageFormats => Enum.GetValues<ImageOutputFormat>();
    public IEnumerable<AudioOutputFormat> AudioFormats => Enum.GetValues<AudioOutputFormat>();

    public string SettingsPageName => Loc.T("Settings.Page");

    public string SettingsPagePath => "PboSpy";

    public void BrowseFfmpeg()
    {
        var dialog = new OpenFileDialog { Filter = "ffmpeg.exe|ffmpeg.exe|*.exe|*.exe", Title = "ffmpeg" };
        if (dialog.ShowDialog() == true)
        {
            FfmpegPath = dialog.FileName;
        }
    }

    public void AddToClaude()
    {
        var files = PboSpy.Services.Mcp.ClaudeSetup.ConfigFiles();
        var owner = System.Windows.Application.Current?.Windows.OfType<System.Windows.Window>().FirstOrDefault(w => w.IsActive);
        var message = Loc.F("Settings.McpConfirm", string.Join("\n", files), PboSpy.Services.Mcp.ClaudeSetup.ExePath);
        if (!Windows.MessageDialog.Ask(owner, Loc.T("Settings.McpAdd"), message))
        {
            return;
        }
        try
        {
            foreach (var file in files)
            {
                PboSpy.Services.Mcp.ClaudeSetup.Install(file);
            }
            McpStatus = Loc.T("Settings.McpDone");
        }
        catch (Exception ex)
        {
            McpStatus = ex.Message;
        }
    }

    public void CopyMcpConfig()
    {
        System.Windows.Clipboard.SetText(PboSpy.Services.Mcp.ClaudeSetup.Snippet);
        McpStatus = Loc.T("Settings.McpCopied");
    }

    private string _mcpStatus = "";

    public string McpStatus
    {
        get => _mcpStatus;
        set
        {
            _mcpStatus = value;
            NotifyOfPropertyChange();
        }
    }

    public void OpenInstallFolder()
    {
        Process.Start("explorer.exe", $"\"{AppContext.BaseDirectory.TrimEnd('\\')}\"");
    }

    public void OpenSettingsFolder()
    {
        Directory.CreateDirectory(AppSettings.Folder);
        Process.Start("explorer.exe", $"\"{AppSettings.Folder}\"");
    }

    public void OpenLanguagesFolder()
    {
        Directory.CreateDirectory(Loc.UserLanguageFolder);
        Process.Start("explorer.exe", $"\"{Loc.UserLanguageFolder}\"");
    }

    public void ClearTempFiles()
    {
        var temp = Path.Combine(Path.GetTempPath(), "PboSpy");
        try
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void ClearRecentFiles()
    {
        _settings.RecentFiles = new List<string>();
        _settings.Save();
    }

    public void ApplyChanges()
    {
        _applied = true;
        _settings.ConfirmExit = ConfirmExit;
        _settings.OpenMaximized = OpenFullscreen;
        _settings.ShowStartPage = ShowStartPage;
        _settings.AskAboutStartPage = AskAboutStartPage;

        _settings.RememberExportOptions = RememberExportOptions;
        _settings.ShowFileSizes = ShowFileSizes;
        _settings.SingleClickPreview = SingleClickPreview;
        _settings.AutoUpdate = AutoUpdate;
        _settings.UpdateToken = UpdateToken ?? "";
        _settings.HideFilteredFiles = HideFilteredFiles;
        _settings.ConfirmBeforeOverwrite = ConfirmBeforeOverwrite;
        _settings.OpenFolderAfterExport = OpenFolderAfterExport;
        _settings.DragOutConvertsImages = DragOutConvertsImages;
        _settings.RecentFileCount = RecentFileCount;
        _settings.DefaultImageFormat = DefaultImageFormat.ToString();
        _settings.DefaultAudioFormat = DefaultAudioFormat.ToString();
        _settings.AudioBitrate = AudioBitrate;
        _settings.AudioQuality = AudioQuality;
        _settings.FfmpegPath = FfmpegPath ?? "";
        _settings.Save();

        if (SelectedTheme != null)
        {
            ThemeService.SetTheme(SelectedTheme);
        }
        if (SelectedLanguage != null)
        {
            LocalizationService.SetLanguage(SelectedLanguage.Code);
        }
    }
}
