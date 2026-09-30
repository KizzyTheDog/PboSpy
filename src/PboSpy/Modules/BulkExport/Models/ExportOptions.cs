using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PboSpy.Modules.BulkExport.Models;

public enum ImageOutputFormat { Png, Jpeg, Bmp, Tiff, Tga }

public enum NameSanitizeMode { PercentEncode, Underscore, Strip }

public enum ExistingFileAction { Overwrite, Skip, Rename }

public enum PaaOutputFormat { Auto, Dxt1, Dxt5 }

public enum ContentPreset { Everything, Textures, Sounds, Configs, Models, Scripts, Custom }

public enum AudioOutputFormat { Wav, Mp3, Ogg, Flac, M4a, Opus, Wss }

public class ExportOptions : INotifyPropertyChanged
{
    private string outputDirectory = "";
    private ContentPreset preset = ContentPreset.Textures;
    private string customExtensions = ".paa, .rvmat, .p3d";
    private bool preserveStructure = true;
    private bool stripPboPrefix;
    private NameSanitizeMode sanitizeMode = NameSanitizeMode.Underscore;
    private ExistingFileAction existingFileAction = ExistingFileAction.Overwrite;
    private bool convertImages = true;
    private ImageOutputFormat imageFormat = ImageOutputFormat.Png;
    private int jpegQuality = 90;
    private int maxSize;
    private bool keepOriginalImages;
    private bool rebuildNormalMaps = true;
    private bool grayscaleDataMaps;
    private bool convertConfigs = true;
    private bool convertAudio = true;
    private bool convertImagesToPaa;
    private PaaOutputFormat paaFormat = PaaOutputFormat.Auto;
    private bool resizeToPowerOfTwo = true;
    private int maxPaaSize = 4096;
    private AudioOutputFormat audioFormat = AudioOutputFormat.Wav;
    private int audioBitrate = 192;
    private int oggQuality = 6;
    private int audioSampleRate;
    private bool audioMono;
    private bool normalizeAudio;
    private bool keepOriginalAudio;
    private bool keepOriginalConfigs;

    public string OutputDirectory { get => outputDirectory; set => Set(ref outputDirectory, value); }

    public ContentPreset Preset
    {
        get => preset;
        set
        {
            if (Set(ref preset, value))
            {
                Notify(nameof(IsCustomPreset));
            }
        }
    }

    public bool IsCustomPreset => preset == ContentPreset.Custom;

    public string CustomExtensions { get => customExtensions; set => Set(ref customExtensions, value); }

    public bool PreserveStructure { get => preserveStructure; set => Set(ref preserveStructure, value); }

    public bool StripPboPrefix { get => stripPboPrefix; set => Set(ref stripPboPrefix, value); }

    public NameSanitizeMode SanitizeMode { get => sanitizeMode; set => Set(ref sanitizeMode, value); }

    public ExistingFileAction ExistingFileAction { get => existingFileAction; set => Set(ref existingFileAction, value); }

    public bool ConvertImages { get => convertImages; set => Set(ref convertImages, value); }

    public ImageOutputFormat ImageFormat
    {
        get => imageFormat;
        set
        {
            if (Set(ref imageFormat, value))
            {
                Notify(nameof(IsLossyFormat));
            }
        }
    }

    public bool IsLossyFormat => imageFormat == ImageOutputFormat.Jpeg;

    public int JpegQuality { get => jpegQuality; set => Set(ref jpegQuality, Math.Clamp(value, 1, 100)); }

    /// <summary>Largest edge in pixels. 0 keeps the full resolution mipmap.</summary>
    public int MaxSize { get => maxSize; set => Set(ref maxSize, Math.Max(0, value)); }

    public bool KeepOriginalImages { get => keepOriginalImages; set => Set(ref keepOriginalImages, value); }

    public bool RebuildNormalMaps { get => rebuildNormalMaps; set => Set(ref rebuildNormalMaps, value); }

    public bool GrayscaleDataMaps { get => grayscaleDataMaps; set => Set(ref grayscaleDataMaps, value); }

    public bool ConvertConfigs { get => convertConfigs; set => Set(ref convertConfigs, value); }

    public bool KeepOriginalConfigs { get => keepOriginalConfigs; set => Set(ref keepOriginalConfigs, value); }

    public bool ConvertAudio { get => convertAudio; set => Set(ref convertAudio, value); }

    /// <summary>The import direction: png / tga / jpg into PAA.</summary>
    public bool ConvertImagesToPaa { get => convertImagesToPaa; set => Set(ref convertImagesToPaa, value); }

    public PaaOutputFormat PaaFormat { get => paaFormat; set => Set(ref paaFormat, value); }

    public bool ResizeToPowerOfTwo { get => resizeToPowerOfTwo; set => Set(ref resizeToPowerOfTwo, value); }

    public int MaxPaaSize { get => maxPaaSize; set => Set(ref maxPaaSize, Math.Max(0, value)); }

    public AudioOutputFormat AudioFormat
    {
        get => audioFormat;
        set
        {
            if (Set(ref audioFormat, value))
            {
                Notify(nameof(UsesBitrate));
                Notify(nameof(UsesOggQuality));
            }
        }
    }

    public bool UsesBitrate => audioFormat is AudioOutputFormat.Mp3 or AudioOutputFormat.M4a or AudioOutputFormat.Opus;

    public bool UsesOggQuality => audioFormat == AudioOutputFormat.Ogg;

    public int AudioBitrate { get => audioBitrate; set => Set(ref audioBitrate, Math.Clamp(value, 32, 320)); }

    /// <summary>Vorbis quality, 0 (smallest) to 10 (best).</summary>
    public int OggQuality { get => oggQuality; set => Set(ref oggQuality, Math.Clamp(value, 0, 10)); }

    /// <summary>0 keeps the source rate.</summary>
    public int AudioSampleRate { get => audioSampleRate; set => Set(ref audioSampleRate, Math.Max(0, value)); }

    public bool AudioMono { get => audioMono; set => Set(ref audioMono, value); }

    public bool NormalizeAudio { get => normalizeAudio; set => Set(ref normalizeAudio, value); }

    public bool KeepOriginalAudio { get => keepOriginalAudio; set => Set(ref keepOriginalAudio, value); }

    public string AudioExtension => audioFormat switch
    {
        AudioOutputFormat.Mp3 => ".mp3",
        AudioOutputFormat.Ogg => ".ogg",
        AudioOutputFormat.Flac => ".flac",
        AudioOutputFormat.M4a => ".m4a",
        AudioOutputFormat.Opus => ".opus",
        AudioOutputFormat.Wss => ".wss",
        _ => ".wav"
    };

    /// <summary>Leading path removed from every entry, used when exporting a sub folder.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string RootPathToStrip { get; set; }

    public string ImageExtension => imageFormat switch
    {
        ImageOutputFormat.Jpeg => ".jpg",
        ImageOutputFormat.Bmp => ".bmp",
        ImageOutputFormat.Tiff => ".tif",
        ImageOutputFormat.Tga => ".tga",
        _ => ".png"
    };

    public IReadOnlyCollection<string> GetExtensionFilter()
    {
        switch (preset)
        {
            case ContentPreset.Everything:
                return null;
            case ContentPreset.Textures:
                return new[] { ".paa", ".pac", ".png", ".tga", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" };
            case ContentPreset.Sounds:
                return new[] { ".wss", ".ogg", ".wav", ".lip" };
            case ContentPreset.Configs:
                return new[] { ".cpp", ".hpp", ".h", ".bin", ".rvmat", ".ext", ".sqm", ".cfg" };
            case ContentPreset.Models:
                return new[] { ".p3d", ".rtm", ".wrp" };
            case ContentPreset.Scripts:
                return new[] { ".sqf", ".sqs", ".fsm", ".sqfc" };
            default:
                var parsed = customExtensions
                    .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(e => e.Trim().ToLowerInvariant())
                    .Select(e => e.StartsWith(".") ? e : "." + e)
                    .Distinct()
                    .ToArray();
                return parsed.Length > 0 ? parsed : null;
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        Notify(name);
        return true;
    }

    private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
