using BIS.P3D;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using MlodLod = BIS.P3D.MLOD.P3DM_LOD;
using OdolLod = BIS.P3D.ODOL.LOD;

namespace PboSpy.Modules.P3d.Scene;

internal sealed class ModelLodOption
{
    public ModelLodOption(ILevelOfDetail lod, int index)
    {
        Lod = lod;
        Index = index;
        Resolution = lod.Resolution;
        Faces = lod.FaceCount;
        Name = lod.Resolution.GetLODName();
    }

    public ILevelOfDetail Lod { get; }
    public int Index { get; }
    public float Resolution { get; }
    public int Faces { get; }
    public string Name { get; }
    public bool IsVisual => BIS.P3D.Resolution.IsVisual(Resolution);

    public override string ToString() => $"{Name}   ({Faces:N0})";
}

/// <summary>Faces of one LOD that share a texture and material, as a frozen WPF mesh.</summary>
internal sealed class ModelPart
{
    public string Texture { get; init; } = "";
    public string Material { get; init; } = "";
    public MeshGeometry3D Mesh { get; init; }
    public int Triangles { get; init; }
    public Vector3D Offset { get; init; }
    public string Owner { get; init; } = "";
}

internal sealed class ModelMesh
{
    public List<ModelPart> Parts { get; } = new();
    public Rect3D Bounds { get; set; } = Rect3D.Empty;
    public int Triangles => Parts.Sum(p => p.Triangles);

    /// <summary>Several models side by side along X, each keeping its own meshes.</summary>
    public static ModelMesh Combine(IReadOnlyList<(string Name, ModelMesh Mesh)> meshes)
    {
        var result = new ModelMesh();
        var x = 0.0;
        foreach (var (name, mesh) in meshes)
        {
            if (mesh.Bounds.IsEmpty)
            {
                continue;
            }
            var b = mesh.Bounds;
            var offset = new Vector3D(x - b.X, 0, 0);
            foreach (var part in mesh.Parts)
            {
                result.Parts.Add(new ModelPart
                {
                    Texture = part.Texture,
                    Material = part.Material,
                    Mesh = part.Mesh,
                    Triangles = part.Triangles,
                    Offset = offset,
                    Owner = name
                });
            }
            var moved = new Rect3D(b.X + offset.X, b.Y, b.Z, b.SizeX, b.SizeY, b.SizeZ);
            result.Bounds = result.Bounds.IsEmpty ? moved : Rect3D.Union(result.Bounds, moved);
            x += b.SizeX + Math.Max(0.5, b.SizeX * 0.15);
        }
        return result;
    }
}

/// <summary>
/// Turns ODOL sections or MLOD faces into meshes. Arma is left handed, so Z is mirrored and the
/// triangle order is swapped to keep faces pointing outwards (the preview culls back faces like the game).
/// </summary>
internal static class ModelMeshBuilder
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ILevelOfDetail, ModelMesh> Built = new();

    public static ModelMesh Build(ILevelOfDetail lod) => Built.GetValue(lod, l => l switch
    {
        OdolLod odol => BuildOdol(odol),
        MlodLod mlod => BuildMlod(mlod),
        _ => new ModelMesh()
    });

    // Boats cut the water surface with these; the game never shows them.
    private static bool IsWaterMask(string texture) => texture.Contains("antiwater", StringComparison.OrdinalIgnoreCase);

    private sealed class PartBuilder
    {
        public readonly Point3DCollection Positions = new();
        public readonly PointCollection Uvs = new();
        public readonly Int32Collection Indices = new();
        public readonly Dictionary<(int, float, float), int> Map = new();
        public string Texture = "";
        public string Material = "";

        public int Add((int, float, float) key, float x, float y, float z, float u, float v)
        {
            if (!Map.TryGetValue(key, out var index))
            {
                index = Positions.Count;
                Map[key] = index;
                Positions.Add(new Point3D(x, y, -z));
                Uvs.Add(new Point(u, v));
            }
            return index;
        }

        public void Triangle(int a, int b, int c)
        {
            Indices.Add(a);
            Indices.Add(b);
            Indices.Add(c);
        }

        public ModelPart Finish(ref Rect3D bounds)
        {
            // WPF would otherwise work the normals out on the UI thread the first time it draws.
            var sums = new Vector3D[Positions.Count];
            for (var i = 0; i + 2 < Indices.Count; i += 3)
            {
                var a = Indices[i];
                var b = Indices[i + 1];
                var c = Indices[i + 2];
                var n = Vector3D.CrossProduct(Positions[b] - Positions[a], Positions[c] - Positions[a]);
                sums[a] += n;
                sums[b] += n;
                sums[c] += n;
            }
            var normals = new Vector3DCollection(sums.Length);
            foreach (var n in sums)
            {
                var copy = n;
                if (copy.LengthSquared > 0)
                {
                    copy.Normalize();
                }
                normals.Add(copy);
            }
            var mesh = new MeshGeometry3D
            {
                Positions = Positions,
                Normals = normals,
                TextureCoordinates = Uvs,
                TriangleIndices = Indices
            };
            mesh.Freeze();
            bounds = Rect3D.Union(bounds, mesh.Bounds);
            return new ModelPart { Texture = Texture, Material = Material, Mesh = mesh, Triangles = Indices.Count / 3 };
        }
    }

    private static ModelMesh BuildOdol(OdolLod lod)
    {
        var result = new ModelMesh();
        var faces = lod.Polygons?.Faces;
        var vertices = lod.Vertices;
        if (faces == null || faces.Length == 0 || vertices == null || vertices.Count == 0)
        {
            return result;
        }
        var uv = lod.UvSets != null && lod.UvSets.Length > 0 ? lod.UvSets[0].GetUV() : null;

        // Proxy markers are triangles the game never draws.
        var proxies = new HashSet<BIS.P3D.ODOL.Polygon>(ReferenceEqualityComparer.Instance);
        foreach (var selection in lod.NamedSelections ?? Array.Empty<BIS.P3D.ODOL.NamedSelection>())
        {
            if (selection.Name != null && selection.Name.StartsWith("proxy:", StringComparison.OrdinalIgnoreCase) && selection.SelectedFaces != null)
            {
                foreach (var index in selection.SelectedFaces)
                {
                    if (index >= 0 && index < faces.Length)
                    {
                        proxies.Add(faces[index]);
                    }
                }
            }
        }

        var parts = new Dictionary<string, PartBuilder>();
        var groups = new List<(string texture, string material, IEnumerable<BIS.P3D.ODOL.Polygon> faces)>();
        if (lod.Sections == null || lod.Sections.Length == 0)
        {
            groups.Add(("", "", faces));
        }
        else
        {
            foreach (var section in lod.Sections)
            {
                var texture = section.TextureIndex >= 0 && lod.Textures != null && section.TextureIndex < lod.Textures.Length
                    ? lod.Textures[section.TextureIndex] ?? "" : "";
                var material = section.MaterialIndex >= 0 && lod.Materials != null && section.MaterialIndex < lod.Materials.Length
                    ? lod.Materials[section.MaterialIndex]?.MaterialName ?? "" : section.Material ?? "";
                // Binarising strips the proxy selections; what is left of the proxy triangles is the
                // one section with neither texture nor material.
                if (texture.Length == 0 && material.Length == 0 || IsWaterMask(texture))
                {
                    continue;
                }
                groups.Add((texture, material, section.GetFaces(faces)));
            }
        }

        foreach (var (texture, material, groupFaces) in groups)
        {
            var key = texture + "|" + material;
            if (!parts.TryGetValue(key, out var part))
            {
                part = new PartBuilder { Texture = texture, Material = material };
                parts[key] = part;
            }
            foreach (var face in groupFaces)
            {
                var indices = face.VertexIndices;
                if (indices == null || indices.Length < 3 || proxies.Contains(face))
                {
                    continue;
                }
                var local = new int[indices.Length];
                var valid = true;
                for (var i = 0; i < indices.Length; i++)
                {
                    var v = indices[i];
                    if (v < 0 || v >= vertices.Count)
                    {
                        valid = false;
                        break;
                    }
                    var p = vertices[v];
                    var t = uv != null && v < uv.Length ? uv[v] : default;
                    local[i] = part.Add((v, 0f, 0f), p.X, p.Y, p.Z, t.X, t.Y);
                }
                if (!valid)
                {
                    continue;
                }
                part.Triangle(local[0], local[2], local[1]);
                if (local.Length == 4)
                {
                    part.Triangle(local[0], local[3], local[2]);
                }
            }
        }

        var bounds = Rect3D.Empty;
        foreach (var part in parts.Values.Where(p => p.Indices.Count > 0))
        {
            result.Parts.Add(part.Finish(ref bounds));
        }
        result.Bounds = bounds;
        return result;
    }

    private static ModelMesh BuildMlod(MlodLod lod)
    {
        var result = new ModelMesh();
        if (lod.Faces == null || lod.Points == null || lod.Points.Length == 0)
        {
            return result;
        }

        var proxies = new bool[lod.Faces.Length];
        foreach (var selection in lod.Taggs.OfType<BIS.P3D.MLOD.NamedSelectionTagg>())
        {
            if (selection.Name != null && selection.Name.StartsWith("proxy:", StringComparison.OrdinalIgnoreCase) && selection.Faces != null)
            {
                for (var i = 0; i < Math.Min(proxies.Length, selection.Faces.Length); i++)
                {
                    proxies[i] |= selection.Faces[i] != 0;
                }
            }
        }

        var parts = new Dictionary<string, PartBuilder>();
        for (var faceIndex = 0; faceIndex < lod.Faces.Length; faceIndex++)
        {
            var face = lod.Faces[faceIndex];
            if (proxies[faceIndex])
            {
                continue;
            }
            var texture = face.Texture ?? "";
            var material = face.Material ?? "";
            if (IsWaterMask(texture))
            {
                continue;
            }
            var key = texture + "|" + material;
            if (!parts.TryGetValue(key, out var part))
            {
                part = new PartBuilder { Texture = texture, Material = material };
                parts[key] = part;
            }
            var count = Math.Min(face.VertexCount, face.Vertices.Length);
            if (count < 3)
            {
                continue;
            }
            var local = new int[count];
            var valid = true;
            for (var i = 0; i < count; i++)
            {
                var vertex = face.Vertices[i];
                if (vertex.PointIndex < 0 || vertex.PointIndex >= lod.Points.Length)
                {
                    valid = false;
                    break;
                }
                var p = lod.Points[vertex.PointIndex];
                // Same point with different UVs must stay split, otherwise texture seams smear.
                local[i] = part.Add((vertex.PointIndex, vertex.U, vertex.V), p.X, p.Y, p.Z, vertex.U, vertex.V);
            }
            if (!valid)
            {
                continue;
            }
            // MLOD stores faces in the opposite order to ODOL; with Z mirrored, this order faces outwards.
            part.Triangle(local[0], local[1], local[2]);
            if (count == 4)
            {
                part.Triangle(local[0], local[2], local[3]);
            }
        }

        var bounds = Rect3D.Empty;
        foreach (var part in parts.Values.Where(p => p.Indices.Count > 0))
        {
            result.Parts.Add(part.Finish(ref bounds));
        }
        result.Bounds = bounds;
        return result;
    }
}
