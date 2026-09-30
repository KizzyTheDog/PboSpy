using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.IO;

namespace PboSpy.Modules.Pbr.Core;

public sealed class OutputOptions
{
    public bool FlipGreen { get; set; }
    public bool WriteBaseColor { get; set; } = true;
    public bool WriteOrm { get; set; }
    public bool WriteSeparateMaps { get; set; } = true;
}

/// <summary>
/// Turns Arma's maps into PBR ones:
/// _nohq → normal (rebuilt from DXT5nm when X lives in alpha), _smdi G → metallic, 1 − _smdi B → roughness,
/// _as G → ambient occlusion, _co/_ca → base colour; optionally everything packed as ORM.
/// </summary>
public static class PbrOutput
{
    public static List<string> Write(PbrGroup group, string name, string folder, OutputOptions options)
    {
        Directory.CreateDirectory(folder);
        var written = new List<string>();
        Image<L8> ao = null, roughness = null, metallic = null;
        try
        {
            if (group.Members.TryGetValue(TextureKind.Normal, out var normal))
            {
                using var image = PbrImage.Load(normal.Path);
                using var output = Normal(image, options.FlipGreen);
                written.Add(Save(output, folder, name + "_normal.png"));
            }
            if (group.Members.TryGetValue(TextureKind.Smdi, out var smdi))
            {
                using var image = PbrImage.Load(smdi.Path);
                metallic = Channel(image, p => p.G);
                roughness = Channel(image, p => (byte)(255 - p.B));
                if (options.WriteSeparateMaps)
                {
                    written.Add(Save(metallic, folder, name + "_metallic.png"));
                    written.Add(Save(roughness, folder, name + "_roughness.png"));
                }
            }
            if (group.Members.TryGetValue(TextureKind.Ao, out var aoTexture))
            {
                using var image = PbrImage.Load(aoTexture.Path);
                ao = Channel(image, p => p.G);
                if (options.WriteSeparateMaps)
                {
                    written.Add(Save(ao, folder, name + "_ao.png"));
                }
            }
            if (options.WriteBaseColor && group.Members.TryGetValue(TextureKind.BaseColor, out var baseColor))
            {
                using var image = PbrImage.Load(baseColor.Path);
                var hasAlpha = baseColor.Stem.EndsWith("_ca", StringComparison.OrdinalIgnoreCase);
                if (hasAlpha)
                {
                    written.Add(Save(image, folder, name + "_basecolor.png"));
                }
                else
                {
                    using var rgb = image.CloneAs<Rgb24>();
                    written.Add(Save(rgb, folder, name + "_basecolor.png"));
                }
            }
            if (options.WriteOrm && (ao != null || roughness != null || metallic != null))
            {
                using var orm = Orm(ao, roughness, metallic);
                written.Add(Save(orm, folder, name + "_orm.png"));
            }
        }
        finally
        {
            ao?.Dispose();
            roughness?.Dispose();
            metallic?.Dispose();
        }
        return written;
    }

    private static string Save(Image image, string folder, string fileName)
    {
        var path = Path.Combine(folder, fileName);
        image.SaveAsPng(path);
        return path;
    }

    private static Image<Rgb24> Normal(Image<Rgba32> source, bool flipGreen)
    {
        var swizzled = IsSwizzled(source);
        var output = new Image<Rgb24>(source.Width, source.Height);
        source.ProcessPixelRows(output, (src, dst) =>
        {
            for (var y = 0; y < src.Height; y++)
            {
                var s = src.GetRowSpan(y);
                var d = dst.GetRowSpan(y);
                for (var x = 0; x < s.Length; x++)
                {
                    byte r, g, b;
                    if (swizzled)
                    {
                        var nx = s[x].A / 255f * 2 - 1;
                        var ny = s[x].G / 255f * 2 - 1;
                        var nz = MathF.Sqrt(Math.Clamp(1 - nx * nx - ny * ny, 0, 1));
                        var length = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                        if (length < 1e-6f)
                        {
                            length = 1;
                        }
                        r = ToByte(nx / length * 0.5f + 0.5f);
                        g = ToByte(ny / length * 0.5f + 0.5f);
                        b = ToByte(nz / length * 0.5f + 0.5f);
                    }
                    else
                    {
                        (r, g, b) = (s[x].R, s[x].G, s[x].B);
                    }
                    d[x] = new Rgb24(r, flipGreen ? (byte)(255 - g) : g, b);
                }
            }
        });
        return output;
    }

    /// <summary>DXT5nm: X in alpha, red flat (or unused).</summary>
    private static bool IsSwizzled(Image<Rgba32> image)
    {
        double sumA = 0, sqA = 0, sumR = 0, sqR = 0;
        long count = 0;
        var minAlpha = 255;
        image.ProcessPixelRows(rows =>
        {
            var step = Math.Max(1, rows.Height / 256);
            for (var y = 0; y < rows.Height; y += step)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 0; x < row.Length; x += step)
                {
                    double a = row[x].A / 255.0, r = row[x].R / 255.0;
                    sumA += a; sqA += a * a; sumR += r; sqR += r * r;
                    minAlpha = Math.Min(minAlpha, row[x].A);
                    count++;
                }
            }
        });
        if (count == 0 || minAlpha > 250)
        {
            return false;
        }
        var stdA = Math.Sqrt(Math.Max(0, sqA / count - Math.Pow(sumA / count, 2)));
        var stdR = Math.Sqrt(Math.Max(0, sqR / count - Math.Pow(sumR / count, 2)));
        return stdA > 0.02 && (stdR < 0.02 || stdA > stdR);
    }

    private static Image<L8> Channel(Image<Rgba32> source, Func<Rgba32, byte> pick)
    {
        var output = new Image<L8>(source.Width, source.Height);
        source.ProcessPixelRows(output, (src, dst) =>
        {
            for (var y = 0; y < src.Height; y++)
            {
                var s = src.GetRowSpan(y);
                var d = dst.GetRowSpan(y);
                for (var x = 0; x < s.Length; x++)
                {
                    d[x] = new L8(pick(s[x]));
                }
            }
        });
        return output;
    }

    /// <summary>R = occlusion, G = roughness, B = metallic (glTF / Unreal / Unity HDRP convention).</summary>
    private static Image<Rgb24> Orm(Image<L8> ao, Image<L8> roughness, Image<L8> metallic)
    {
        var width = new[] { ao, roughness, metallic }.Where(i => i != null).Max(i => i.Width);
        var height = new[] { ao, roughness, metallic }.Where(i => i != null).Max(i => i.Height);
        Image<L8> Fit(Image<L8> image) => image == null || (image.Width == width && image.Height == height)
            ? image
            : image.Clone(x => x.Resize(width, height, KnownResamplers.Bicubic));
        var aoImg = Fit(ao);
        var roughImg = Fit(roughness);
        var metalImg = Fit(metallic);
        try
        {
            var output = new Image<Rgb24>(width, height);
            output.ProcessPixelRows(rows =>
            {
                for (var y = 0; y < rows.Height; y++)
                {
                    var row = rows.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        row[x] = new Rgb24(
                            aoImg != null ? aoImg[x, y].PackedValue : (byte)255,
                            roughImg != null ? roughImg[x, y].PackedValue : (byte)128,
                            metalImg != null ? metalImg[x, y].PackedValue : (byte)0);
                    }
                }
            });
            return output;
        }
        finally
        {
            if (aoImg != ao) aoImg?.Dispose();
            if (roughImg != roughness) roughImg?.Dispose();
            if (metalImg != metallic) metalImg?.Dispose();
        }
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)(v * 255 + 0.5f), 0, 255);

    /// <summary>Small preview in BGRA for WPF, showing what each map means rather than its raw channels.</summary>
    public static byte[] MakePreview(float[][] rgba, TextureKind kind, int size, int previewSize)
    {
        var step = size / previewSize;
        var result = new byte[previewSize * previewSize * 4];
        var swizzled = kind == TextureKind.Normal && PbrFeatures.IsSwizzledNormal(rgba);
        for (var y = 0; y < previewSize; y++)
        {
            for (var x = 0; x < previewSize; x++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                for (var dy = 0; dy < step; dy++)
                {
                    for (var dx = 0; dx < step; dx++)
                    {
                        var i = (y * step + dy) * size + x * step + dx;
                        r += rgba[0][i];
                        g += rgba[1][i];
                        b += rgba[2][i];
                        a += rgba[3][i];
                    }
                }
                var n = step * step;
                (r, g, b, a) = (r / n, g / n, b / n, a / n);
                switch (kind)
                {
                    case TextureKind.Normal when swizzled:
                        var nx = a * 2 - 1;
                        var ny = g * 2 - 1;
                        var nz = MathF.Sqrt(Math.Clamp(1 - nx * nx - ny * ny, 0, 1));
                        (r, g, b) = (nx * 0.5f + 0.5f, ny * 0.5f + 0.5f, nz * 0.5f + 0.5f);
                        break;
                    case TextureKind.Ao:
                        r = b = g;
                        break;
                }
                var o = (y * previewSize + x) * 4;
                result[o] = ToByte(b);
                result[o + 1] = ToByte(g);
                result[o + 2] = ToByte(r);
                result[o + 3] = 255;
            }
        }
        return result;
    }
}
