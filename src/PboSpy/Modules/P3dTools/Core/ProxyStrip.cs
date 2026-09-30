using BIS.P3D.MLOD;
using System.IO;

namespace PboSpy.Modules.P3dTools.Core;

/// <summary>
/// Removes the proxy marker triangles (the lone right triangles with 1:2 legs that mark where
/// crew, weapons or rotors go) from an MLOD, so the model imports into Blender without spikes.
/// The game needs them, so this is meant for a copy.
/// </summary>
public static class ProxyStrip
{
    public static int Strip(string input, string output)
    {
        var mlod = new MLOD(input);
        var removed = 0;
        foreach (var lod in mlod.Lods)
        {
            if (lod.Faces == null || lod.Faces.Length == 0)
            {
                continue;
            }
            var drop = FindProxies(lod);
            var count = drop.Count(d => d);
            if (count == 0)
            {
                continue;
            }
            removed += count;
            var keep = Enumerable.Range(0, lod.Faces.Length).Where(i => !drop[i]).ToArray();
            lod.Faces = keep.Select(i => lod.Faces[i]).ToArray();
            foreach (var tagg in lod.Taggs)
            {
                switch (tagg)
                {
                    case NamedSelectionTagg named:
                        named.Faces = keep.Select(i => named.Faces[i]).ToArray();
                        break;
                    case SelectedTagg selected:
                        selected.Faces = keep.Select(i => selected.Faces[i]).ToArray();
                        break;
                    case LockTagg locked:
                        locked.LockedFaces = keep.Select(i => locked.LockedFaces[i]).ToArray();
                        break;
                    case UVSetTagg uv:
                        uv.FaceUVs = keep.Select(i => uv.FaceUVs[i]).ToArray();
                        break;
                    default:
                        continue;
                }
                tagg.DataSize = tagg.ComputeDataSize();
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
        mlod.WriteToFile(output, true);
        return removed;
    }

    private static bool[] FindProxies(P3DM_LOD lod)
    {
        var drop = new bool[lod.Faces.Length];
        foreach (var tagg in lod.Taggs.OfType<NamedSelectionTagg>())
        {
            if (tagg.Name == null || !tagg.Name.StartsWith("proxy:", StringComparison.OrdinalIgnoreCase) || tagg.Faces == null)
            {
                continue;
            }
            for (var i = 0; i < Math.Min(drop.Length, tagg.Faces.Length); i++)
            {
                drop[i] |= tagg.Faces[i] != 0;
            }
        }

        var uses = new int[lod.Points.Length];
        foreach (var face in lod.Faces)
        {
            foreach (var vertex in face.Vertices.Take(face.VertexCount))
            {
                if (vertex.PointIndex >= 0 && vertex.PointIndex < uses.Length)
                {
                    uses[vertex.PointIndex]++;
                }
            }
        }
        for (var i = 0; i < lod.Faces.Length; i++)
        {
            var face = lod.Faces[i];
            if (drop[i] || face.VertexCount != 3 || !string.IsNullOrEmpty(face.Texture))
            {
                continue;
            }
            var points = face.Vertices.Take(3).Select(v => v.PointIndex).ToArray();
            if (points.Any(p => p < 0 || p >= uses.Length || uses[p] != 1))
            {
                continue;
            }
            drop[i] = IsProxyShape(points.Select(p => lod.Points[p]).ToArray());
        }
        return drop;
    }

    /// <summary>A right angle with one leg twice the other, like the triangle Object Builder places for a proxy.</summary>
    public static bool IsProxyShape(BIS.Core.Math.Vector3P[] p, double tolerance = 0.03)
    {
        for (var i = 0; i < 3; i++)
        {
            var o = p[i];
            var a = p[(i + 1) % 3];
            var b = p[(i + 2) % 3];
            double ax = a.X - o.X, ay = a.Y - o.Y, az = a.Z - o.Z;
            double bx = b.X - o.X, by = b.Y - o.Y, bz = b.Z - o.Z;
            var la = Math.Sqrt(ax * ax + ay * ay + az * az);
            var lb = Math.Sqrt(bx * bx + by * by + bz * bz);
            if (la < 1e-6 || lb < 1e-6)
            {
                continue;
            }
            if (Math.Abs(ax * bx + ay * by + az * bz) / (la * lb) > tolerance)
            {
                continue;
            }
            if (Math.Abs(Math.Max(la, lb) / Math.Min(la, lb) - 2) < 2 * tolerance)
            {
                return true;
            }
        }
        return false;
    }
}
