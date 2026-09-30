namespace PboSpy.Modules.Pbr.Core;

/// <summary>
/// Describes a texture by where its edges are and which way they run. Textures painted on the same
/// UV layout share panel lines, island borders and baked details, whatever the map type, so the
/// orientation-aware edge maps line up even when the colours have nothing in common.
/// </summary>
public static class PbrFeatures
{
    public const int Grid = 512;
    private static readonly int[] Scales = { 128 };
    private const float Sigma = 2f;
    private const float Percentile = 0.95f;

    public static float[][] Compute(float[][] rgba, TextureKind kind)
    {
        var channels = Select(rgba, kind);
        var n = Grid * Grid;
        var jxx = new float[n];
        var jyy = new float[n];
        var jxy = new float[n];
        var gx = new float[n];
        var gy = new float[n];
        var mag = new float[n];

        foreach (var source in channels)
        {
            var channel = Normalize(source);
            if (channel == null)
            {
                continue;
            }
            Sobel(channel, gx, gy);
            for (var i = 0; i < n; i++)
            {
                mag[i] = MathF.Sqrt(gx[i] * gx[i] + gy[i] * gy[i]);
            }
            var p = PercentileOf(mag, Percentile);
            if (p < 1e-6f)
            {
                continue;
            }
            for (var i = 0; i < n; i++)
            {
                var scale = Math.Min(mag[i] / p, 1f) / Math.Max(mag[i], 1e-6f);
                var x = gx[i] * scale;
                var y = gy[i] * scale;
                jxx[i] = Math.Max(jxx[i], x * x);
                jyy[i] = Math.Max(jyy[i], y * y);
                var xy = x * y;
                if (Math.Abs(xy) > Math.Abs(jxy[i]))
                {
                    jxy[i] = xy;
                }
            }
        }

        var maps = new[] { Gaussian(jxx), Gaussian(jyy), Gaussian(jxy) };
        var result = new float[Scales.Length][];
        for (var s = 0; s < Scales.Length; s++)
        {
            var size = Scales[s];
            var vector = new float[size * size * maps.Length];
            for (var m = 0; m < maps.Length; m++)
            {
                BlockMean(maps[m], size, vector, m * size * size);
            }
            result[s] = UnitZeroMean(vector);
        }
        return result;
    }

    public static float Similarity(float[][] a, float[][] b)
    {
        if (a == null || b == null)
        {
            return 0;
        }
        double total = 0;
        for (var s = 0; s < a.Length; s++)
        {
            total += Dot(a[s], b[s]);
        }
        return (float)(total / a.Length);
    }

    private static double Dot(float[] x, float[] y)
    {
        var width = System.Numerics.Vector<float>.Count;
        var acc = System.Numerics.Vector<float>.Zero;
        var i = 0;
        for (; i <= x.Length - width; i += width)
        {
            acc += new System.Numerics.Vector<float>(x, i) * new System.Numerics.Vector<float>(y, i);
        }
        double dot = System.Numerics.Vector.Dot(acc, System.Numerics.Vector<float>.One);
        for (; i < x.Length; i++)
        {
            dot += x[i] * y[i];
        }
        return dot;
    }

    /// <summary>Which channels carry the structure for each Arma map type.</summary>
    private static List<float[]> Select(float[][] rgba, TextureKind kind)
    {
        var (r, g, b, a) = (rgba[0], rgba[1], rgba[2], rgba[3]);
        switch (kind)
        {
            case TextureKind.Normal:
                // DXT5nm keeps X in alpha; a plain RGB normal map has a flat alpha.
                var alphaStd = Std(a);
                var redStd = Std(r);
                return new List<float[]> { alphaStd > 0.02f && (redStd < 0.02f || alphaStd > redStd) ? a : r, g };
            case TextureKind.Smdi:
                return new List<float[]> { g, b };
            case TextureKind.Ao:
                return new List<float[]> { g };
            default:
                var n = r.Length;
                var lum = new float[n];
                var rg = new float[n];
                var bg = new float[n];
                for (var i = 0; i < n; i++)
                {
                    lum[i] = 0.299f * r[i] + 0.587f * g[i] + 0.114f * b[i];
                    rg[i] = r[i] - g[i];
                    bg[i] = b[i] - g[i];
                }
                return new List<float[]> { lum, rg, bg };
        }
    }

    public static bool IsSwizzledNormal(float[][] rgba) => Std(rgba[3]) > 0.02f && (Std(rgba[0]) < 0.02f || Std(rgba[3]) > Std(rgba[0]));

    private static float Std(float[] v)
    {
        double sum = 0, sq = 0;
        foreach (var x in v)
        {
            sum += x;
            sq += x * x;
        }
        var mean = sum / v.Length;
        return (float)Math.Sqrt(Math.Max(0, sq / v.Length - mean * mean));
    }

    private static float[] Normalize(float[] v)
    {
        double sum = 0, sq = 0;
        foreach (var x in v)
        {
            sum += x;
            sq += x * x;
        }
        var mean = sum / v.Length;
        var std = Math.Sqrt(Math.Max(0, sq / v.Length - mean * mean));
        if (std < 1e-3)
        {
            return null;
        }
        var result = new float[v.Length];
        for (var i = 0; i < v.Length; i++)
        {
            result[i] = (float)((v[i] - mean) / std);
        }
        return result;
    }

    // Mirrors the edge pixels (same as scipy's "reflect").
    private static int Reflect(int i, int n) => i < 0 ? -i - 1 : i >= n ? 2 * n - i - 1 : i;

    private static void Sobel(float[] c, float[] gx, float[] gy)
    {
        const int n = Grid;
        for (var y = 0; y < n; y++)
        {
            var ym = Reflect(y - 1, n) * n;
            var y0 = y * n;
            var yp = Reflect(y + 1, n) * n;
            for (var x = 0; x < n; x++)
            {
                var xm = Reflect(x - 1, n);
                var xp = Reflect(x + 1, n);
                var a = c[ym + xm]; var b = c[ym + x]; var d = c[ym + xp];
                var e = c[y0 + xm]; var f = c[y0 + xp];
                var g = c[yp + xm]; var h = c[yp + x]; var k = c[yp + xp];
                gx[y0 + x] = (d - a) + 2 * (f - e) + (k - g);
                gy[y0 + x] = (g - a) + 2 * (h - b) + (k - d);
            }
        }
    }

    private static float PercentileOf(float[] values, float q)
    {
        var copy = (float[])values.Clone();
        Array.Sort(copy);
        var position = q * (copy.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = Math.Min(lower + 1, copy.Length - 1);
        return copy[lower] + (copy[upper] - copy[lower]) * (position - lower);
    }

    private static readonly float[] Kernel = BuildKernel();

    private static float[] BuildKernel()
    {
        var radius = (int)(4 * Sigma + 0.5f);
        var kernel = new float[2 * radius + 1];
        double sum = 0;
        for (var i = -radius; i <= radius; i++)
        {
            kernel[i + radius] = (float)Math.Exp(-0.5 * i * i / (Sigma * Sigma));
            sum += kernel[i + radius];
        }
        for (var i = 0; i < kernel.Length; i++)
        {
            kernel[i] /= (float)sum;
        }
        return kernel;
    }

    private static float[] Gaussian(float[] src)
    {
        const int n = Grid;
        var radius = Kernel.Length / 2;
        var tmp = new float[n * n];
        var dst = new float[n * n];
        for (var y = 0; y < n; y++)
        {
            var row = y * n;
            for (var x = 0; x < n; x++)
            {
                float acc = 0;
                for (var k = -radius; k <= radius; k++)
                {
                    acc += Kernel[k + radius] * src[row + Reflect(x + k, n)];
                }
                tmp[row + x] = acc;
            }
        }
        for (var y = 0; y < n; y++)
        {
            for (var x = 0; x < n; x++)
            {
                float acc = 0;
                for (var k = -radius; k <= radius; k++)
                {
                    acc += Kernel[k + radius] * tmp[Reflect(y + k, n) * n + x];
                }
                dst[y * n + x] = acc;
            }
        }
        return dst;
    }

    private static void BlockMean(float[] src, int size, float[] dst, int offset)
    {
        var block = Grid / size;
        var area = block * block;
        for (var by = 0; by < size; by++)
        {
            for (var bx = 0; bx < size; bx++)
            {
                float acc = 0;
                for (var y = by * block; y < (by + 1) * block; y++)
                {
                    var row = y * Grid;
                    for (var x = bx * block; x < (bx + 1) * block; x++)
                    {
                        acc += src[row + x];
                    }
                }
                dst[offset + by * size + bx] = acc / area;
            }
        }
    }

    private static float[] UnitZeroMean(float[] v)
    {
        double mean = 0;
        foreach (var x in v)
        {
            mean += x;
        }
        mean /= v.Length;
        double norm = 0;
        for (var i = 0; i < v.Length; i++)
        {
            v[i] = (float)(v[i] - mean);
            norm += v[i] * v[i];
        }
        norm = Math.Sqrt(norm);
        if (norm > 1e-6)
        {
            for (var i = 0; i < v.Length; i++)
            {
                v[i] = (float)(v[i] / norm);
            }
        }
        return v;
    }
}
