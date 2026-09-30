using BIS.PAA;
using PboSpy.Models;
using PboSpy.Modules.BulkExport.Models;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PboSpy.Modules.BulkExport.Services;

/// <summary>
/// Decodes PAA/PAC into a bitmap, applying the ArmA specific channel conventions
/// that a plain dump of the texture loses.
/// </summary>
public static class PaaImageConverter
{
    private static readonly string[] DataSuffixes = { "_as", "_ads", "_adshq", "_smdi", "_mc", "_dt", "_mask" };

    public static BitmapSource Decode(FileBase file, ExportOptions options)
    {
        using var stream = file.GetStream();
        var isPac = file.Extension == ".pac";
        var paa = new PAA(stream, isPac);

        var mipmap = SelectMipmap(paa, options.MaxSize);
        var pixels = PAA.GetARGB32PixelData(paa, stream, mipmap);

        var baseName = Path.GetFileNameWithoutExtension(file.Name).ToLowerInvariant();

        if (options.RebuildNormalMaps && IsSwizzledNormal(pixels))
        {
            RebuildNormal(pixels);
        }
        else if (options.GrayscaleDataMaps && HasSuffix(baseName, DataSuffixes))
        {
            GreenToGrayscale(pixels);
        }

        var palette = paa.Palette.Colors.Length > 0
            ? new BitmapPalette(paa.Palette.Colors.Select(c => Color.FromRgb(c.R8, c.G8, c.B8)).ToList())
            : null;

        var bitmap = BitmapSource.Create(mipmap.Width, mipmap.Height, 96, 96,
            PixelFormats.Bgra32, palette, pixels, mipmap.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    // WPF has no TGA encoder; uncompressed 32-bit, top-left origin, which Roblox and Blender read.
    private static void WriteTga(BitmapSource bitmap, Stream target)
    {
        var source = bitmap.Format == PixelFormats.Bgra32 ? bitmap : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[source.PixelWidth * source.PixelHeight * 4];
        source.CopyPixels(pixels, source.PixelWidth * 4, 0);
        var header = new byte[18];
        header[2] = 2;
        header[12] = (byte)source.PixelWidth;
        header[13] = (byte)(source.PixelWidth >> 8);
        header[14] = (byte)source.PixelHeight;
        header[15] = (byte)(source.PixelHeight >> 8);
        header[16] = 32;
        header[17] = 0x28;
        target.Write(header);
        target.Write(pixels);
    }

    public static void Encode(BitmapSource bitmap, Stream target, ExportOptions options)
    {
        if (options.ImageFormat == ImageOutputFormat.Tga)
        {
            WriteTga(bitmap, target);
            return;
        }
        BitmapEncoder encoder = options.ImageFormat switch
        {
            ImageOutputFormat.Jpeg => new JpegBitmapEncoder { QualityLevel = options.JpegQuality },
            ImageOutputFormat.Bmp => new BmpBitmapEncoder(),
            ImageOutputFormat.Tiff => new TiffBitmapEncoder(),
            _ => new PngBitmapEncoder()
        };

        var source = bitmap;
        if (options.ImageFormat is ImageOutputFormat.Jpeg or ImageOutputFormat.Bmp)
        {
            // Neither format carries an alpha channel; flattening keeps the colours predictable.
            source = new FormatConvertedBitmap(bitmap, PixelFormats.Bgr24, null, 0);
            source.Freeze();
        }

        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(target);
    }

    private static Mipmap SelectMipmap(PAA paa, int maxSize)
    {
        if (maxSize <= 0)
        {
            return paa[0];
        }

        Mipmap best = null;
        foreach (var mipmap in paa.Mipmaps)
        {
            if (Math.Max(mipmap.Width, mipmap.Height) <= maxSize)
            {
                return mipmap;
            }
            best = mipmap;
        }
        // Every mipmap is larger than the requested size, so the smallest one is the closest match.
        return paa.Mipmaps.Last() ?? best ?? paa[0];
    }

    private static bool HasSuffix(string baseName, string[] suffixes)
        => suffixes.Any(s => baseName.EndsWith(s, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A DXT5nm normal map keeps X in the alpha channel and leaves red at a constant,
    /// so feeding it to a normal map node tilts every face the same way. PAAs that carry
    /// a SWIZ tagg are already unpacked by the decoder, which is why this looks at the
    /// decoded pixels rather than at the file name.
    /// </summary>
    private static bool IsSwizzledNormal(byte[] bgra)
    {
        byte minRed = 255, maxRed = 0, minAlpha = 255, maxAlpha = 0;
        var step = Math.Max(4, (bgra.Length / 4 / 4096) * 4);
        for (var i = 0; i + 3 < bgra.Length; i += step)
        {
            var r = bgra[i + 2];
            var a = bgra[i + 3];
            if (r < minRed) minRed = r;
            if (r > maxRed) maxRed = r;
            if (a < minAlpha) minAlpha = a;
            if (a > maxAlpha) maxAlpha = a;
        }
        return maxRed - minRed < 3 && maxAlpha - minAlpha > 24;
    }

    private static void RebuildNormal(byte[] bgra)
    {
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            var x = bgra[i + 3] / 255.0 * 2.0 - 1.0;
            var y = bgra[i + 1] / 255.0 * 2.0 - 1.0;
            var z = Math.Sqrt(Math.Max(0.0, 1.0 - x * x - y * y));

            bgra[i] = (byte)Math.Clamp((z * 0.5 + 0.5) * 255.0, 0, 255);
            bgra[i + 2] = bgra[i + 3];
            bgra[i + 3] = 255;
        }
    }

    private static void GreenToGrayscale(byte[] bgra)
    {
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            var g = bgra[i + 1];
            bgra[i] = g;
            bgra[i + 2] = g;
            bgra[i + 3] = 255;
        }
    }
}
