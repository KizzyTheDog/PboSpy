using PboSpy.Models;
using PboSpy.Modules.Preview.ViewModels;
using PboSpy.Modules.PreviewBinary.Utils;
using PboSpy.Modules.PreviewBinary.ViewModels;

namespace PboSpy.Modules.PreviewBinary;

internal static class PreviewFactories
{
    [Export("FilePreviewFactory")]
    [ExportMetadata("Extensions", new[] { ".bin", ".lip",
        ".fxy", ".wsi", ".shp", ".dbf", ".shx", ".bisurf" })]
    public static Document PreviewGenericBinary(FileBase entry)
    {
        if (entry.IsBinaryConfig())
        {
            var text = entry.GetBinaryConfigAsText();
            return new TextPreviewViewModel(entry, text);
        }
        using (var stream = new System.IO.MemoryStream())
        {
            using (var source = entry.GetStream())
            {
                source.CopyTo(stream);
            }
            stream.Position = 0;
            if (TexHeaders.Is(stream))
            {
                return new TextPreviewViewModel(entry, TexHeaders.ToText(stream));
            }
        }
        return new BinaryPreviewViewModel(entry);
    }
}
