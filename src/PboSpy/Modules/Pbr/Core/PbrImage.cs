using BIS.PAA;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.IO;

namespace PboSpy.Modules.Pbr.Core;

/// <summary>Loads Arma textures (PAA/PAC or any common image format) as raw RGBA, without the fixes the exporter applies.</summary>
public static class PbrImage
{
    public static readonly string[] Extensions = { ".paa", ".pac", ".png", ".tga", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".webp" };

    public static bool IsSupported(string path) => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Reads the image; for PAA the smallest mipmap that is at least <paramref name="minSize"/> wide is used.</summary>
    public static Image<Rgba32> Load(string path, int minSize = 0)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is ".paa" or ".pac")
        {
            using var stream = File.OpenRead(path);
            var paa = new PAA(stream, extension == ".pac");
            var mipmap = paa[0];
            if (minSize > 0)
            {
                foreach (var candidate in paa.Mipmaps)
                {
                    if (candidate.Width >= minSize && candidate.Height >= minSize && candidate.Width < mipmap.Width)
                    {
                        mipmap = candidate;
                    }
                }
            }
            var bgra = PAA.GetARGB32PixelData(paa, stream, mipmap);
            var image = new Image<Rgba32>(mipmap.Width, mipmap.Height);
            image.ProcessPixelRows(rows =>
            {
                for (var y = 0; y < rows.Height; y++)
                {
                    var row = rows.GetRowSpan(y);
                    var offset = y * mipmap.Width * 4;
                    for (var x = 0; x < row.Length; x++)
                    {
                        var i = offset + x * 4;
                        row[x] = new Rgba32(bgra[i + 2], bgra[i + 1], bgra[i], bgra[i + 3]);
                    }
                }
            });
            return image;
        }
        return Image.Load<Rgba32>(path);
    }

    /// <summary>Original size without decoding the whole picture where possible.</summary>
    public static (int Width, int Height) Size(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is ".paa" or ".pac")
        {
            using var stream = File.OpenRead(path);
            var paa = new PAA(stream, extension == ".pac");
            return (paa[0].Width, paa[0].Height);
        }
        var info = Image.Identify(path);
        return info != null ? (info.Width, info.Height) : (0, 0);
    }

    /// <summary>Resamples to a square grid and returns the four channels as 0..1 floats.</summary>
    public static float[][] Channels(Image<Rgba32> image, int size)
    {
        using var work = image.Clone(x => x.Resize(new ResizeOptions
        {
            Size = new SixLabors.ImageSharp.Size(size, size),
            Mode = ResizeMode.Stretch,
            Sampler = KnownResamplers.Box
        }));
        var channels = new float[4][];
        for (var c = 0; c < 4; c++)
        {
            channels[c] = new float[size * size];
        }
        work.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var i = y * size + x;
                    channels[0][i] = row[x].R / 255f;
                    channels[1][i] = row[x].G / 255f;
                    channels[2][i] = row[x].B / 255f;
                    channels[3][i] = row[x].A / 255f;
                }
            }
        });
        return channels;
    }
}
