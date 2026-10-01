using PboSpy.Models;
using PboSpy.Modules.Rtm.ViewModels;

namespace PboSpy.Modules.Rtm;

internal static class PreviewFactories
{
    [Export("FilePreviewFactory")]
    [ExportMetadata("Extensions", new[] { ".rtm" })]
    public static Document PreviewRtm(FileBase entry) => new RtmPreviewViewModel(entry);
}
