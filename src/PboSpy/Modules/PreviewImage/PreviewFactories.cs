using PboSpy.Models;
using PboSpy.Modules.PreviewImage.ViewModels;
using System.Windows.Media.Imaging;

namespace PboSpy.Modules.PreviewImage;

internal static class PreviewFactories
{
    [Export("FilePreviewFactory")]
    [ExportMetadata("Extensions", new[] { ".jpg", ".jpeg", ".png", ".bmp", ".tga", ".tif", ".tiff", ".gif" })]
    public static Document PreviewImage(FileBase entry)
    {
        return new ImagePreviewViewModel(entry, Load(entry));
    }

    public static BitmapSource Load(FileBase entry)
    {
        using var source = entry.GetStream();
        using var stream = new System.IO.MemoryStream();
        source.CopyTo(stream);
        stream.Position = 0;

        if (entry.Extension == ".tga")
        {
            // WPF has no TGA codec; ImageSharp does.
            using var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Bgra32>(stream);
            var pixels = new byte[image.Width * image.Height * 4];
            image.CopyPixelDataTo(pixels);
            var tga = BitmapSource.Create(image.Width, image.Height, 96, 96,
                System.Windows.Media.PixelFormats.Bgra32, null, pixels, image.Width * 4);
            tga.Freeze();
            return tga;
        }

        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        frame.Freeze();
        return frame;
    }
}
