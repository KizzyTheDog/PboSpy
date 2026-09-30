using PboSpy.Models;
using PboSpy.Modules.Audio.Services;
using PboSpy.Modules.Audio.ViewModels;

namespace PboSpy.Modules.Audio;

internal static class PreviewFactories
{
    [Export("FilePreviewFactory")]
    [ExportMetadata("Extensions", new[] { ".wss", ".ogg", ".wav", ".mp3", ".flac" })]
    public static Document PreviewAudio(FileBase entry)
    {
        using var stream = entry.GetStream();
        var audio = AudioDecoder.Decode(stream, entry.Extension);
        return new AudioPreviewViewModel(entry, audio);
    }
}
