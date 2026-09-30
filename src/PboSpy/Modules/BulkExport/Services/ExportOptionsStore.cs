using PboSpy.Modules.BulkExport.Models;
using PboSpy.Services;
using System.Text.Json;

namespace PboSpy.Modules.BulkExport.Services;

public static class ExportOptionsStore
{
    public static ExportOptions Load()
    {
        var settings = AppSettings.Default;
        ExportOptions options = null;
        if (settings.RememberExportOptions && !string.IsNullOrEmpty(settings.ExportOptionsJson))
        {
            try
            {
                options = JsonSerializer.Deserialize<ExportOptions>(settings.ExportOptionsJson);
            }
            catch (Exception)
            {
            }
        }

        options ??= new ExportOptions();
        if (string.IsNullOrEmpty(options.OutputDirectory))
        {
            options.OutputDirectory = settings.LastExportFolder;
        }
        if (Enum.TryParse<ImageOutputFormat>(settings.DefaultImageFormat, out var image) && string.IsNullOrEmpty(settings.ExportOptionsJson))
        {
            options.ImageFormat = image;
        }
        if (Enum.TryParse<AudioOutputFormat>(settings.DefaultAudioFormat, out var audio) && string.IsNullOrEmpty(settings.ExportOptionsJson))
        {
            options.AudioFormat = audio;
        }
        return options;
    }

    public static void Save(ExportOptions options)
    {
        var settings = AppSettings.Default;
        settings.LastExportFolder = options.OutputDirectory;
        if (settings.RememberExportOptions)
        {
            settings.ExportOptionsJson = JsonSerializer.Serialize(options);
        }
        settings.Save();
    }
}
