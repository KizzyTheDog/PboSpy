using BCnEncoder.Shared;
using BIS.Core.Streams;
using BIS.PAA;
using BIS.PAA.Encoder;
using PboSpy.Modules.BulkExport.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.IO;

namespace PboSpy.Modules.BulkExport.Services;

/// <summary>Turns a normal image (png, tga, jpg, …) into an ArmA PAA.</summary>
public static class PaaWriter
{
    public static readonly string[] SourceExtensions = { ".png", ".tga", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".gif" };

    private static readonly string[] Dxt5Suffixes = { "_ca", "_nohq", "_nofhq", "_novhq", "_smdi", "_mask" };

    public static bool CanConvert(string extension) => SourceExtensions.Contains(extension?.ToLowerInvariant());

    public static void Write(Stream source, string fileName, Stream output, ExportOptions options)
    {
        using var image = Image.Load<Rgba32>(source);

        if (options.ResizeToPowerOfTwo)
        {
            var width = PowerOfTwo(image.Width, options.MaxPaaSize);
            var height = PowerOfTwo(image.Height, options.MaxPaaSize);
            if (width != image.Width || height != image.Height)
            {
                image.Mutate(x => x.Resize(width, height, KnownResamplers.Lanczos3));
            }
        }
        else if (!IsPowerOfTwo(image.Width) || !IsPowerOfTwo(image.Height))
        {
            throw new InvalidDataException($"{image.Width}x{image.Height} is not a power of two. Turn on resizing or fix the source.");
        }

        var pixels = new ColorRgba32[image.Height, image.Width];
        var hasAlpha = false;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var p = row[x];
                    pixels[y, x] = new ColorRgba32(p.R, p.G, p.B, p.A);
                    hasAlpha |= p.A < 255;
                }
            }
        });

        var type = ChooseType(Path.GetFileNameWithoutExtension(fileName), hasAlpha, options.PaaFormat);
        using var writer = new BinaryWriterEx(output, true);
        PaaEncoder.WritePAA(writer, pixels, type);
    }

    public static PAAType ChooseType(string baseName, bool hasAlpha, PaaOutputFormat format)
    {
        switch (format)
        {
            case PaaOutputFormat.Dxt1:
                return PAAType.DXT1;
            case PaaOutputFormat.Dxt5:
                return PAAType.DXT5;
        }
        var lower = baseName.ToLowerInvariant();
        if (hasAlpha || Dxt5Suffixes.Any(s => lower.EndsWith(s)))
        {
            return PAAType.DXT5;
        }
        return PAAType.DXT1;
    }

    private static bool IsPowerOfTwo(int v) => v > 0 && (v & (v - 1)) == 0;

    private static int PowerOfTwo(int value, int max)
    {
        var lower = 1;
        while (lower * 2 <= value)
        {
            lower *= 2;
        }
        var upper = lower == value ? value : lower * 2;
        var nearest = value - lower <= upper - value ? lower : upper;
        if (max > 0)
        {
            nearest = Math.Min(nearest, max);
        }
        return Math.Max(4, nearest);
    }
}
