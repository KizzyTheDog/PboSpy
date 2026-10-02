using System.IO;
using System.Windows.Media.Media3D;
using System.Text.Json.Nodes;
using System.Windows.Media.Imaging;

namespace PboSpy.Modules.P3d.Scene;

/// <summary>
/// Writes model parts as GLB (PBR materials) or OBJ + MTL, with the textures turned into what those
/// formats expect: _nohq becomes a plain normal map, _smdi a metallic/roughness map (as the PBR Texture Maker does).
/// </summary>
internal static class ModelExport
{
    private sealed class Maps
    {
        public string Name = "";
        public string Base = "";
        public BitmapSource Colour;
        public bool Alpha;
        public BitmapSource Normal;
        public BitmapSource MetalRough;
    }

    public static readonly string[] Formats = { ".glb", ".gltf", ".fbx", ".obj" };

    /// <summary>Bones for a skinned FBX: heads in the file's space, parent index (-1 = none), and per part each vertex's bones.</summary>
    public sealed class Skin
    {
        public string[] Bones;
        public int[] Parents;
        public Vector3D[] Heads;
        public Dictionary<ModelPart, (int Bone, float Weight)[][]> Weights;
        public double UnitCentimetres = 100;
    }

    /// <summary>One skinned FBX object per part, named after part.Owner (Roblox imports it as MeshParts with Bones).</summary>
    public static void WriteSkinned(string target, IReadOnlyList<ModelPart> parts, TextureResolver resolver, Skin skin, int maxSize = 2048)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        WriteFbx(target, parts.Select(p => new List<ModelPart> { p }).ToList(), resolver, maxSize, skin);
    }

    /// <summary>
    /// The extension picks the format. splitAt > 0 cuts any part with more triangles into pieces (Roblox takes 20,000 per MeshPart).
    /// decimate (0..1) keeps that share of the triangles. byMaterial false joins parts into as few objects as the split allows.
    /// </summary>
    public static void Write(string target, IReadOnlyList<ModelPart> parts, TextureResolver resolver, int maxSize = 2048, int splitAt = 0,
        double decimate = 1, bool byMaterial = true, int[] lods = null)
    {
        // Extra lower-detail copies next to the main file: name_lod1, name_lod2... (percent of the main file's triangles).
        foreach (var (percent, level) in (lods ?? Array.Empty<int>()).Where(p => p is > 0 and < 100).Select((p, i) => (p, i + 1)))
        {
            var lodTarget = Path.Combine(Path.GetDirectoryName(target), $"{Path.GetFileNameWithoutExtension(target)}_lod{level}{Path.GetExtension(target)}");
            Write(lodTarget, parts, resolver, maxSize, splitAt, decimate * percent / 100.0, byMaterial);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        parts = parts.Where(p => !TextureResolver.IsInvisible(p.Texture)).ToList();
        if (decimate < 1)
        {
            parts = parts.Select(p => Decimate(p, decimate)).Where(p => p.Triangles > 0).ToList();
        }
        if (splitAt > 0)
        {
            parts = Split(parts, splitAt);
        }
        var objects = Group(parts, byMaterial, splitAt);
        switch (Path.GetExtension(target).ToLowerInvariant())
        {
            case ".obj":
                WriteObj(target, objects, resolver, maxSize);
                break;
            case ".fbx":
                WriteFbx(target, objects, resolver, maxSize);
                break;
            case ".gltf":
                WriteGlb(target, objects, resolver, maxSize, separate: true);
                break;
            default:
                WriteGlb(target, objects, resolver, maxSize, separate: false);
                break;
        }
    }

    // One object per part, or parts packed together while they stay under the split limit.
    private static List<List<ModelPart>> Group(IReadOnlyList<ModelPart> parts, bool byMaterial, int limit)
    {
        if (byMaterial)
        {
            return parts.Select(p => new List<ModelPart> { p }).ToList();
        }
        var groups = new List<List<ModelPart>> { new() };
        var count = 0;
        foreach (var part in parts)
        {
            if (limit > 0 && count + part.Triangles > limit && groups[^1].Count > 0)
            {
                groups.Add(new List<ModelPart>());
                count = 0;
            }
            groups[^1].Add(part);
            count += part.Triangles;
        }
        return groups.Where(g => g.Count > 0).ToList();
    }

    [System.Runtime.InteropServices.DllImport("meshoptimizer", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private static extern nuint meshopt_simplify(uint[] destination, uint[] indices, nuint indexCount, float[] positions, nuint vertexCount,
        nuint stride, nuint targetIndexCount, float targetError, uint options, out float resultError);

    // meshoptimizer's simplifier: collapses edges by least visible change and keeps UV seams and borders.
    private static ModelPart Decimate(ModelPart part, double keep)
    {
        var mesh = part.Mesh;
        var indices = mesh.TriangleIndices.Select(i => (uint)i).ToArray();
        var positions = mesh.Positions.SelectMany(p => new[] { (float)p.X, (float)p.Y, (float)p.Z }).ToArray();
        var target = (nuint)Math.Max(3, (int)(indices.Length * keep) / 3 * 3);
        var result = new uint[indices.Length];
        var count = (int)meshopt_simplify(result, indices, (nuint)indices.Length, positions, (nuint)mesh.Positions.Count, 12, target, 0.05f, 0, out _);
        var map = new Dictionary<uint, int>();
        var newPositions = new System.Windows.Media.Media3D.Point3DCollection();
        var normals = new System.Windows.Media.Media3D.Vector3DCollection();
        var uvs = new System.Windows.Media.PointCollection();
        var newIndices = new System.Windows.Media.Int32Collection(count);
        for (var i = 0; i < count; i++)
        {
            var old = result[i];
            if (!map.TryGetValue(old, out var index))
            {
                index = map[old] = newPositions.Count;
                newPositions.Add(mesh.Positions[(int)old]);
                normals.Add(old < mesh.Normals.Count ? mesh.Normals[(int)old] : default);
                uvs.Add(old < mesh.TextureCoordinates.Count ? mesh.TextureCoordinates[(int)old] : default);
            }
            newIndices.Add(index);
        }
        var simplified = new System.Windows.Media.Media3D.MeshGeometry3D { Positions = newPositions, Normals = normals, TextureCoordinates = uvs, TriangleIndices = newIndices };
        simplified.Freeze();
        return new ModelPart { Texture = part.Texture, Material = part.Material, Mesh = simplified, Triangles = count / 3, Offset = part.Offset, Owner = part.Owner };
    }

    private static string ObjectName(List<ModelPart> group, Dictionary<string, Maps> materials, string stem, int index) =>
        group.Count == 1 ? materials[Key(group[0])].Name : $"{stem}_{index + 1}";

    // Triangles sorted along the part's longest side and cut in runs, so each piece stays in one area.
    private static List<ModelPart> Split(IReadOnlyList<ModelPart> parts, int limit)
    {
        var result = new List<ModelPart>();
        foreach (var part in parts)
        {
            var mesh = part.Mesh;
            var count = mesh.TriangleIndices.Count / 3;
            if (count <= limit)
            {
                result.Add(part);
                continue;
            }
            var b = mesh.Bounds;
            Func<System.Windows.Media.Media3D.Point3D, double> along = b.SizeX >= b.SizeY && b.SizeX >= b.SizeZ ? p => p.X : b.SizeY >= b.SizeZ ? p => p.Y : p => p.Z;
            var idx = mesh.TriangleIndices;
            var order = Enumerable.Range(0, count)
                .OrderBy(t => along(mesh.Positions[idx[t * 3]]) + along(mesh.Positions[idx[t * 3 + 1]]) + along(mesh.Positions[idx[t * 3 + 2]]))
                .ToList();
            for (var start = 0; start < count; start += limit)
            {
                var map = new Dictionary<int, int>();
                var positions = new System.Windows.Media.Media3D.Point3DCollection();
                var normals = new System.Windows.Media.Media3D.Vector3DCollection();
                var uvs = new System.Windows.Media.PointCollection();
                var indices = new System.Windows.Media.Int32Collection();
                foreach (var t in order.Skip(start).Take(limit))
                {
                    for (var k = 0; k < 3; k++)
                    {
                        var old = idx[t * 3 + k];
                        if (!map.TryGetValue(old, out var index))
                        {
                            index = map[old] = positions.Count;
                            positions.Add(mesh.Positions[old]);
                            normals.Add(old < mesh.Normals.Count ? mesh.Normals[old] : default);
                            uvs.Add(old < mesh.TextureCoordinates.Count ? mesh.TextureCoordinates[old] : default);
                        }
                        indices.Add(index);
                    }
                }
                var piece = new System.Windows.Media.Media3D.MeshGeometry3D { Positions = positions, Normals = normals, TextureCoordinates = uvs, TriangleIndices = indices };
                piece.Freeze();
                result.Add(new ModelPart
                {
                    Texture = part.Texture,
                    Material = part.Material,
                    Mesh = piece,
                    Triangles = indices.Count / 3,
                    Offset = part.Offset,
                    Owner = part.Owner
                });
            }
        }
        return result;
    }

    private static Dictionary<string, Maps> Materials(IReadOnlyList<ModelPart> parts, TextureResolver resolver, int maxSize)
    {
        var result = new Dictionary<string, Maps>(StringComparer.OrdinalIgnoreCase);
        var converted = new Dictionary<BitmapSource, BitmapSource>(ReferenceEqualityComparer.Instance);
        BitmapSource Once(BitmapSource source, Func<BitmapSource, BitmapSource> convert)
        {
            if (!converted.TryGetValue(source, out var done))
            {
                done = converted[source] = convert(source);
            }
            return done;
        }
        foreach (var part in parts)
        {
            var key = Key(part);
            if (result.ContainsKey(key))
            {
                continue;
            }
            // Named like the texture ("hull_fwd_co") so the Blender addon and people can match them;
            // scrambled names are decoded first.
            var stem = TextureResolver.IsProcedural(part.Texture) || string.IsNullOrWhiteSpace(part.Texture) ? "procedural"
                : PboSpy.Modules.Deobfuscate.Core.NameRecovery.FixEncoding(Path.GetFileNameWithoutExtension(part.Texture.Replace('\\', '/').Split('/').Last()));
            stem = new string(stem.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_').ToArray());
            var name = stem;
            for (var n = 2; result.Values.Any(m => m.Name == name); n++)
            {
                name = stem + "_" + n;
            }
            var suffix = new[] { "_co", "_ca", "_mc", "_mco" }.FirstOrDefault(x => stem.EndsWith(x, StringComparison.OrdinalIgnoreCase));
            var maps = new Maps { Name = name, Base = (suffix != null ? stem[..^suffix.Length] : stem) + (name == stem ? "" : name[stem.Length..]) };
            var colour = resolver.Resolve(part.Texture, maxSize);
            maps.Colour = colour.Image ?? (colour.Color is System.Windows.Media.Color c ? Solid(c) : null);
            maps.Alpha = TextureResolver.HasAlpha(TextureResolver.Normalize(part.Texture));
            var linked = resolver.ResolveLinked(part.Texture, part.Material, maxSize);
            if (linked.Normal?.Image != null)
            {
                maps.Normal = Once(linked.Normal.Image, FixNormal);
            }
            if (linked.Specular?.Image != null)
            {
                maps.MetalRough = Once(linked.Specular.Image, MetalRoughFromSmdi);
            }
            result[key] = maps;
        }
        return result;
    }

    private static string Key(ModelPart part) => TextureResolver.Normalize(part.Texture) + "|" + TextureResolver.Normalize(part.Material);

    private static BitmapSource Solid(System.Windows.Media.Color c)
    {
        var pixels = new byte[4 * 4 * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = c.B;
            pixels[i + 1] = c.G;
            pixels[i + 2] = c.R;
            pixels[i + 3] = c.A;
        }
        return Frozen(BitmapSource.Create(4, 4, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, 16));
    }

    private static BitmapSource Frozen(BitmapSource image)
    {
        image.Freeze();
        return image;
    }

    private static byte[] Pixels(BitmapSource image)
    {
        var bytes = new byte[image.PixelWidth * image.PixelHeight * 4];
        image.CopyPixels(bytes, image.PixelWidth * 4, 0);
        return bytes;
    }

    // DXT5nm keeps X in alpha; Arma normals are DirectX style, so green is flipped for glTF/Blender.
    private static BitmapSource FixNormal(BitmapSource source)
    {
        var p = Pixels(source);
        byte minA = 255, minR = 255, maxR = 0;
        for (var i = 0; i < p.Length; i += 4 * 17)
        {
            minA = Math.Min(minA, p[i + 3]);
            minR = Math.Min(minR, p[i + 2]);
            maxR = Math.Max(maxR, p[i + 2]);
        }
        var swizzled = minA < 250 && maxR - minR < 24;
        for (var i = 0; i < p.Length; i += 4)
        {
            var x = (swizzled ? p[i + 3] : p[i + 2]) / 127.5 - 1;
            var y = -(p[i + 1] / 127.5 - 1);
            var z = Math.Sqrt(Math.Max(0, 1 - x * x - y * y));
            p[i + 2] = (byte)Math.Clamp((x + 1) * 127.5, 0, 255);
            p[i + 1] = (byte)Math.Clamp((y + 1) * 127.5, 0, 255);
            p[i] = (byte)Math.Clamp((z + 1) * 127.5, 0, 255);
            p[i + 3] = 255;
        }
        return Frozen(BitmapSource.Create(source.PixelWidth, source.PixelHeight, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, p, source.PixelWidth * 4));
    }

    // ORM packing (R occlusion, G roughness, B metallic). _smdi green is how strong the shine is and blue how
    // tight it is; treating green as metal made everything black chrome, so metallic stays 0 and both
    // channels only lower the roughness.
    private static BitmapSource MetalRoughFromSmdi(BitmapSource smdi)
    {
        var p = Pixels(smdi);
        for (var i = 0; i < p.Length; i += 4)
        {
            var shine = p[i + 1] / 255.0 * (p[i] / 255.0);
            p[i + 1] = (byte)(Math.Clamp(1 - shine * 0.75, 0.3, 1) * 255);
            p[i] = 0;
            p[i + 2] = 255;
            p[i + 3] = 255;
        }
        return Frozen(BitmapSource.Create(smdi.PixelWidth, smdi.PixelHeight, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, p, smdi.PixelWidth * 4));
    }

    private static byte[] Png(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void WriteObj(string target, List<List<ModelPart>> objects, TextureResolver resolver, int maxSize)
    {
        var parts = objects.SelectMany(g => g).ToList();
        var folder = Path.GetDirectoryName(target);
        var stem = Path.GetFileNameWithoutExtension(target);
        var textures = Path.Combine(folder, stem + "_textures");
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        var materials = Materials(parts, resolver, maxSize);
        var written = new Dictionary<BitmapSource, string>(ReferenceEqualityComparer.Instance);

        string Save(BitmapSource image, string name)
        {
            if (image == null)
            {
                return null;
            }
            if (written.TryGetValue(image, out var known))
            {
                return known;
            }
            Directory.CreateDirectory(textures);
            File.WriteAllBytes(Path.Combine(textures, name + ".png"), Png(image));
            return written[image] = stem + "_textures/" + name + ".png";
        }

        using (var mtl = new StreamWriter(Path.Combine(folder, stem + ".mtl")))
        {
            foreach (var maps in materials.Values)
            {
                mtl.WriteLine($"newmtl {maps.Name}");
                mtl.WriteLine("Kd 1 1 1");
                var colour = Save(maps.Colour, maps.Name);
                if (colour != null)
                {
                    mtl.WriteLine($"map_Kd {colour}");
                    if (maps.Alpha)
                    {
                        mtl.WriteLine($"map_d {colour}");
                    }
                }
                var normal = Save(maps.Normal, maps.Base + "_normal");
                if (normal != null)
                {
                    mtl.WriteLine($"map_Bump {normal}");
                }
                var metalRough = Save(maps.MetalRough, maps.Base + "_orm");
                if (metalRough != null)
                {
                    mtl.WriteLine($"map_Pr {metalRough}");
                    mtl.WriteLine($"map_Pm {metalRough}");
                }
                mtl.WriteLine();
            }
        }

        using var obj = new StreamWriter(target);
        obj.WriteLine($"mtllib {stem}.mtl");
        var offset = 1;
        foreach (var (group, number) in objects.Select((g, i) => (g, i)))
        {
            obj.WriteLine($"o {ObjectName(group, materials, stem, number)}");
            foreach (var part in group)
            {
            obj.WriteLine($"usemtl {materials[Key(part)].Name}");
            var mesh = part.Mesh;
            foreach (var p in mesh.Positions)
            {
                obj.WriteLine(string.Format(invariant, "v {0:0.######} {1:0.######} {2:0.######}", p.X + part.Offset.X, p.Y + part.Offset.Y, p.Z + part.Offset.Z));
            }
            foreach (var t in mesh.TextureCoordinates)
            {
                obj.WriteLine(string.Format(invariant, "vt {0:0.######} {1:0.######}", t.X, 1 - t.Y));
            }
            foreach (var n in mesh.Normals)
            {
                obj.WriteLine(string.Format(invariant, "vn {0:0.####} {1:0.####} {2:0.####}", n.X, n.Y, n.Z));
            }
            var idx = mesh.TriangleIndices;
            for (var i = 0; i + 2 < idx.Count; i += 3)
            {
                int a = idx[i] + offset, b = idx[i + 1] + offset, c = idx[i + 2] + offset;
                obj.WriteLine($"f {a}/{a}/{a} {b}/{b}/{b} {c}/{c}/{c}");
            }
            offset += mesh.Positions.Count;
            }
        }
    }

    private static void WriteGlb(string target, List<List<ModelPart>> objects, TextureResolver resolver, int maxSize, bool separate)
    {
        var parts = objects.SelectMany(g => g).ToList();
        var stem = Path.GetFileNameWithoutExtension(target);
        var folder = Path.GetDirectoryName(target);
        var materials = Materials(parts, resolver, maxSize);
        var bin = new MemoryStream();
        var views = new JsonArray();
        var accessors = new JsonArray();
        var images = new JsonArray();
        var textures = new JsonArray();
        var gltfMaterials = new JsonArray();
        var meshes = new JsonArray();
        var nodes = new JsonArray();

        int View(byte[] data, int? target = null)
        {
            while (bin.Length % 4 != 0)
            {
                bin.WriteByte(0);
            }
            var view = new JsonObject { ["buffer"] = 0, ["byteOffset"] = bin.Length, ["byteLength"] = data.Length };
            if (target != null)
            {
                view["target"] = target;
            }
            bin.Write(data);
            views.Add(view);
            return views.Count - 1;
        }

        int Accessor(int view, int componentType, int count, string type, JsonArray min = null, JsonArray max = null)
        {
            var accessor = new JsonObject { ["bufferView"] = view, ["componentType"] = componentType, ["count"] = count, ["type"] = type };
            if (min != null)
            {
                accessor["min"] = min;
                accessor["max"] = max;
            }
            accessors.Add(accessor);
            return accessors.Count - 1;
        }

        var embedded = new Dictionary<BitmapSource, int>(ReferenceEqualityComparer.Instance);
        int? Texture(BitmapSource image, string name)
        {
            if (image == null)
            {
                return null;
            }
            if (embedded.TryGetValue(image, out var known))
            {
                return known;
            }
            if (separate)
            {
                Directory.CreateDirectory(Path.Combine(folder, stem + "_textures"));
                File.WriteAllBytes(Path.Combine(folder, stem + "_textures", name + ".png"), Png(image));
                images.Add(new JsonObject { ["uri"] = Uri.EscapeDataString(stem + "_textures") + "/" + Uri.EscapeDataString(name + ".png") });
            }
            else
            {
                images.Add(new JsonObject { ["bufferView"] = View(Png(image)), ["mimeType"] = "image/png" });
            }
            textures.Add(new JsonObject { ["source"] = images.Count - 1, ["sampler"] = 0 });
            return embedded[image] = textures.Count - 1;
        }

        var materialIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, maps) in materials)
        {
            var pbr = new JsonObject { ["metallicFactor"] = 0.0, ["roughnessFactor"] = 0.8 };
            if (Texture(maps.Colour, maps.Name) is int colour)
            {
                pbr["baseColorTexture"] = new JsonObject { ["index"] = colour };
            }
            if (Texture(maps.MetalRough, maps.Base + "_orm") is int metalRough)
            {
                pbr["metallicRoughnessTexture"] = new JsonObject { ["index"] = metalRough };
                pbr["metallicFactor"] = 1.0;
                pbr["roughnessFactor"] = 1.0;
            }
            var material = new JsonObject { ["name"] = maps.Name, ["pbrMetallicRoughness"] = pbr, ["doubleSided"] = true };
            if (Texture(maps.Normal, maps.Base + "_normal") is int normal)
            {
                material["normalTexture"] = new JsonObject { ["index"] = normal };
            }
            if (maps.Alpha)
            {
                material["alphaMode"] = "BLEND";
            }
            gltfMaterials.Add(material);
            materialIndex[key] = gltfMaterials.Count - 1;
        }

        foreach (var (group, number) in objects.Select((g, i) => (g, i)))
        {
            var primitives = new JsonArray();
            foreach (var part in group)
            {
            var mesh = part.Mesh;
            var count = mesh.Positions.Count;
            var positions = new byte[count * 12];
            var normals = new byte[count * 12];
            var uvs = new byte[count * 8];
            float[] min = { float.MaxValue, float.MaxValue, float.MaxValue }, max = { float.MinValue, float.MinValue, float.MinValue };
            for (var i = 0; i < count; i++)
            {
                var p = mesh.Positions[i];
                var v = new[] { (float)(p.X + part.Offset.X), (float)(p.Y + part.Offset.Y), (float)(p.Z + part.Offset.Z) };
                for (var k = 0; k < 3; k++)
                {
                    BitConverter.TryWriteBytes(positions.AsSpan(i * 12 + k * 4), v[k]);
                    min[k] = Math.Min(min[k], v[k]);
                    max[k] = Math.Max(max[k], v[k]);
                }
                var n = i < mesh.Normals.Count ? mesh.Normals[i] : new System.Windows.Media.Media3D.Vector3D(0, 1, 0);
                BitConverter.TryWriteBytes(normals.AsSpan(i * 12), (float)n.X);
                BitConverter.TryWriteBytes(normals.AsSpan(i * 12 + 4), (float)n.Y);
                BitConverter.TryWriteBytes(normals.AsSpan(i * 12 + 8), (float)n.Z);
                var t = i < mesh.TextureCoordinates.Count ? mesh.TextureCoordinates[i] : default;
                BitConverter.TryWriteBytes(uvs.AsSpan(i * 8), (float)t.X);
                BitConverter.TryWriteBytes(uvs.AsSpan(i * 8 + 4), (float)t.Y);
            }
            var indices = new byte[mesh.TriangleIndices.Count * 4];
            for (var i = 0; i < mesh.TriangleIndices.Count; i++)
            {
                BitConverter.TryWriteBytes(indices.AsSpan(i * 4), (uint)mesh.TriangleIndices[i]);
            }
            var position = Accessor(View(positions, 34962), 5126, count, "VEC3", new JsonArray(min[0], min[1], min[2]), new JsonArray(max[0], max[1], max[2]));
            var normal = Accessor(View(normals, 34962), 5126, count, "VEC3");
            var uv = Accessor(View(uvs, 34962), 5126, count, "VEC2");
            var index = Accessor(View(indices, 34963), 5125, mesh.TriangleIndices.Count, "SCALAR");
            primitives.Add(new JsonObject
            {
                ["attributes"] = new JsonObject { ["POSITION"] = position, ["NORMAL"] = normal, ["TEXCOORD_0"] = uv },
                ["indices"] = index,
                ["material"] = materialIndex[Key(part)]
            });
            }
            var name = ObjectName(group, materials, Path.GetFileNameWithoutExtension(target), number);
            meshes.Add(new JsonObject { ["name"] = name, ["primitives"] = primitives });
            nodes.Add(new JsonObject { ["name"] = name, ["mesh"] = meshes.Count - 1 });
        }

        while (bin.Length % 4 != 0)
        {
            bin.WriteByte(0);
        }
        var root = new JsonObject
        {
            ["asset"] = new JsonObject { ["version"] = "2.0", ["generator"] = "PboSpy" },
            ["scene"] = 0,
            ["scenes"] = new JsonArray(new JsonObject { ["nodes"] = new JsonArray(Enumerable.Range(0, nodes.Count).Select(i => (JsonNode)i).ToArray()) }),
            ["nodes"] = nodes,
            ["meshes"] = meshes,
            ["materials"] = gltfMaterials,
            ["accessors"] = accessors,
            ["bufferViews"] = views,
            ["buffers"] = new JsonArray(separate
                ? new JsonObject { ["byteLength"] = bin.Length, ["uri"] = Uri.EscapeDataString(stem + ".bin") }
                : new JsonObject { ["byteLength"] = bin.Length }),
            ["samplers"] = new JsonArray(new JsonObject { ["wrapS"] = 10497, ["wrapT"] = 10497 })
        };
        if (images.Count > 0)
        {
            root["images"] = images;
            root["textures"] = textures;
        }
        if (separate)
        {
            File.WriteAllBytes(Path.Combine(folder, stem + ".bin"), bin.ToArray());
            File.WriteAllText(target, root.ToJsonString());
            return;
        }
        var json = System.Text.Encoding.UTF8.GetBytes(root.ToJsonString());
        var jsonPadded = json.Length + (4 - json.Length % 4) % 4;

        using var file = new BinaryWriter(File.Create(target));
        file.Write(0x46546C67u);
        file.Write(2u);
        file.Write((uint)(12 + 8 + jsonPadded + 8 + bin.Length));
        file.Write((uint)jsonPadded);
        file.Write(0x4E4F534Au);
        file.Write(json);
        for (var i = json.Length; i < jsonPadded; i++)
        {
            file.Write((byte)' ');
        }
        file.Write((uint)bin.Length);
        file.Write(0x004E4942u);
        file.Write(bin.ToArray());
    }

    // Binary FBX 7.4 (Blender and Roblox both read it): one model per part, Y up, metres. Textures are PNGs next to it.
    private sealed class FbxNode
    {
        public string Name;
        public List<object> Props = new();
        public List<FbxNode> Children = new();

        public FbxNode(string name, params object[] props)
        {
            Name = name;
            Props.AddRange(props);
        }

        public FbxNode Add(string name, params object[] props)
        {
            var child = new FbxNode(name, props);
            Children.Add(child);
            return child;
        }
    }

    private static void WriteFbx(string target, List<List<ModelPart>> groups, TextureResolver resolver, int maxSize, Skin skin = null)
    {
        var parts = groups.SelectMany(g => g).ToList();
        var folder = Path.GetDirectoryName(target);
        var stem = Path.GetFileNameWithoutExtension(target);
        var materials = Materials(parts, resolver, maxSize);
        var written = new Dictionary<BitmapSource, string>(ReferenceEqualityComparer.Instance);
        string Save(BitmapSource image, string name)
        {
            if (image == null)
            {
                return null;
            }
            if (!written.TryGetValue(image, out var relative))
            {
                Directory.CreateDirectory(Path.Combine(folder, stem + "_textures"));
                relative = written[image] = Path.Combine(stem + "_textures", name + ".png");
                File.WriteAllBytes(Path.Combine(folder, relative), Png(image));
            }
            return relative;
        }
        static string Named(string name, string type) => name + "\0\u0001" + type;

        var header = new FbxNode("FBXHeaderExtension");
        header.Add("FBXHeaderVersion", 1003);
        header.Add("FBXVersion", 7400);
        header.Add("Creator", "PboSpy");
        var global = new FbxNode("GlobalSettings");
        global.Add("Version", 1000);
        var globalProps = global.Add("Properties70");
        foreach (var (name, value) in new[] { ("UpAxis", 1), ("UpAxisSign", 1), ("FrontAxis", 2), ("FrontAxisSign", 1), ("CoordAxis", 0), ("CoordAxisSign", 1) })
        {
            globalProps.Add("P", name, "int", "Integer", "", value);
        }
        globalProps.Add("P", "UnitScaleFactor", "double", "Number", "", skin?.UnitCentimetres ?? 100.0);

        var objects = new FbxNode("Objects");
        var links = new FbxNode("Connections");
        long next = 1000000;
        static double[] Moved(Vector3D t) => new[] { 1.0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, t.X, t.Y, t.Z, 1 };
        var boneIds = new long[skin?.Bones.Length ?? 0];
        var bindPose = new List<(long Id, double[] Matrix)>();
        for (var b = 0; b < boneIds.Length; b++)
        {
            var id = boneIds[b] = next++;
            var attribute = next++;
            objects.Add("NodeAttribute", attribute, Named(skin.Bones[b], "NodeAttribute"), "LimbNode").Add("TypeFlags", "Skeleton");
            var bone = objects.Add("Model", id, Named(skin.Bones[b], "Model"), "LimbNode");
            bone.Add("Version", 232);
            var parent = skin.Parents[b];
            var local = parent >= 0 ? skin.Heads[b] - skin.Heads[parent] : skin.Heads[b];
            bone.Add("Properties70").Add("P", "Lcl Translation", "Lcl Translation", "", "A", local.X, local.Y, local.Z);
            links.Add("C", "OO", attribute, id);
            bindPose.Add((id, Moved(skin.Heads[b])));
        }
        for (var b = 0; b < boneIds.Length; b++)
        {
            links.Add("C", "OO", boneIds[b], skin.Parents[b] >= 0 ? boneIds[skin.Parents[b]] : 0L);
        }
        var materialIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, maps) in materials)
        {
            var id = next++;
            materialIds[key] = id;
            var material = objects.Add("Material", id, Named(maps.Name, "Material"), "");
            material.Add("Version", 102);
            material.Add("ShadingModel", "phong");
            material.Add("MultiLayer", 0);
            material.Add("Properties70").Add("P", "DiffuseColor", "Color", "", "A", 1.0, 1.0, 1.0);
            foreach (var (image, textureName, slot) in new[] { (maps.Colour, maps.Name, "DiffuseColor"), (maps.Normal, maps.Base + "_normal", "NormalMap") })
            {
                var relative = Save(image, textureName);
                if (relative == null)
                {
                    continue;
                }
                var full = Path.Combine(folder, relative);
                var texture = next++;
                var video = next++;
                var clip = objects.Add("Video", video, Named(textureName, "Video"), "Clip");
                clip.Add("Type", "Clip");
                clip.Add("FileName", full);
                clip.Add("RelativeFilename", relative);
                var tex = objects.Add("Texture", texture, Named(textureName, "Texture"), "");
                tex.Add("Type", "TextureVideoClip");
                tex.Add("Version", 202);
                tex.Add("TextureName", Named(textureName, "Texture"));
                tex.Add("FileName", full);
                tex.Add("RelativeFilename", relative);
                links.Add("C", "OO", video, texture);
                links.Add("C", "OP", texture, id, slot);
            }
        }
        foreach (var (group, number) in groups.Select((g, i) => (g, i)))
        {
            var name = skin != null ? group[0].Owner : ObjectName(group, materials, stem, number) + "_" + number;
            var geometry = next++;
            var model = next++;
            // Joined parts: one mesh, and each triangle says which of the object's materials it uses.
            var used = group.Select(Key).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var vertices = new List<double>();
            var normalData = new List<double>();
            var uvData = new List<double>();
            var polygon = new List<int>();
            var perTriangle = new List<int>();
            foreach (var part in group)
            {
                var mesh = part.Mesh;
                var start = vertices.Count / 3;
                vertices.AddRange(mesh.Positions.SelectMany(p => new[] { p.X + part.Offset.X, p.Y + part.Offset.Y, p.Z + part.Offset.Z }));
                normalData.AddRange(Enumerable.Range(0, mesh.Positions.Count).SelectMany(i => i < mesh.Normals.Count ? new[] { mesh.Normals[i].X, mesh.Normals[i].Y, mesh.Normals[i].Z } : new[] { 0.0, 1, 0 }));
                uvData.AddRange(Enumerable.Range(0, mesh.Positions.Count).SelectMany(i => i < mesh.TextureCoordinates.Count ? new[] { mesh.TextureCoordinates[i].X, 1 - mesh.TextureCoordinates[i].Y } : new[] { 0.0, 0 }));
                var material = used.IndexOf(Key(part));
                for (var i = 0; i + 2 < mesh.TriangleIndices.Count; i += 3)
                {
                    polygon.Add(start + mesh.TriangleIndices[i]);
                    polygon.Add(start + mesh.TriangleIndices[i + 1]);
                    polygon.Add(-(start + mesh.TriangleIndices[i + 2]) - 1);
                    perTriangle.Add(material);
                }
            }
            var uvIndex = polygon.Select(i => i < 0 ? -i - 1 : i).ToArray();
            var geo = objects.Add("Geometry", geometry, Named(name, "Geometry"), "Mesh");
            geo.Add("Vertices", vertices.ToArray());
            geo.Add("PolygonVertexIndex", polygon.ToArray());
            geo.Add("GeometryVersion", 124);
            var normals = geo.Add("LayerElementNormal", 0);
            normals.Add("Version", 101);
            normals.Add("Name", "");
            normals.Add("MappingInformationType", "ByVertice");
            normals.Add("ReferenceInformationType", "Direct");
            normals.Add("Normals", normalData.ToArray());
            var uv = geo.Add("LayerElementUV", 0);
            uv.Add("Version", 101);
            uv.Add("Name", "UVMap");
            uv.Add("MappingInformationType", "ByPolygonVertex");
            uv.Add("ReferenceInformationType", "IndexToDirect");
            uv.Add("UV", uvData.ToArray());
            uv.Add("UVIndex", uvIndex);
            var mat = geo.Add("LayerElementMaterial", 0);
            mat.Add("Version", 101);
            mat.Add("Name", "");
            mat.Add("MappingInformationType", used.Count == 1 ? "AllSame" : "ByPolygon");
            mat.Add("ReferenceInformationType", "IndexToDirect");
            mat.Add("Materials", used.Count == 1 ? new[] { 0 } : perTriangle.ToArray());
            var layer = geo.Add("Layer", 0);
            layer.Add("Version", 100);
            foreach (var type in new[] { "LayerElementNormal", "LayerElementMaterial", "LayerElementUV" })
            {
                var element = layer.Add("LayerElement");
                element.Add("Type", type);
                element.Add("TypedIndex", 0);
            }
            var node = objects.Add("Model", model, Named(name, "Model"), "Mesh");
            node.Add("Version", 232);
            node.Add("Shading", true);
            node.Add("Culling", "CullingOff");
            links.Add("C", "OO", model, 0L);
            links.Add("C", "OO", geometry, model);
            foreach (var key in used)
            {
                links.Add("C", "OO", materialIds[key], model);
            }
            if (skin != null && skin.Weights.TryGetValue(group[0], out var weights))
            {
                bindPose.Add((model, Moved(default)));
                var deformer = next++;
                var skinNode = objects.Add("Deformer", deformer, Named("", "Deformer"), "Skin");
                skinNode.Add("Version", 101);
                skinNode.Add("Link_DeformAcuracy", 50.0);
                links.Add("C", "OO", deformer, geometry);
                for (var b = 0; b < boneIds.Length; b++)
                {
                    var indexes = new List<int>();
                    var amounts = new List<double>();
                    for (var v = 0; v < weights.Length; v++)
                    {
                        foreach (var (bone, weight) in weights[v])
                        {
                            if (bone == b && weight > 0)
                            {
                                indexes.Add(v);
                                amounts.Add(weight);
                            }
                        }
                    }
                    if (indexes.Count == 0)
                    {
                        continue;
                    }
                    var cluster = next++;
                    var clusterNode = objects.Add("Deformer", cluster, Named(skin.Bones[b], "SubDeformer"), "Cluster");
                    clusterNode.Add("Version", 100);
                    clusterNode.Add("UserData", "", "");
                    clusterNode.Add("Indexes", indexes.ToArray());
                    clusterNode.Add("Weights", amounts.ToArray());
                    clusterNode.Add("Transform", Moved(-skin.Heads[b]));
                    clusterNode.Add("TransformLink", Moved(skin.Heads[b]));
                    links.Add("C", "OO", cluster, deformer);
                    links.Add("C", "OO", boneIds[b], cluster);
                }
            }
        }
        if (bindPose.Count > 0)
        {
            var pose = objects.Add("Pose", next++, Named("BindPose", "Pose"), "BindPose");
            pose.Add("Type", "BindPose");
            pose.Add("Version", 100);
            pose.Add("NbPoseNodes", bindPose.Count);
            foreach (var (id, matrix) in bindPose)
            {
                var node = pose.Add("PoseNode");
                node.Add("Node", id);
                node.Add("Matrix", matrix);
            }
        }

        using var file = new BinaryWriter(File.Create(target));
        file.Write(System.Text.Encoding.ASCII.GetBytes("Kaydara FBX Binary  \0"));
        file.Write((byte)0x1A);
        file.Write((byte)0);
        file.Write(7400u);
        // Autodesk's reader (Roblox Studio, Maya) refuses a binary file unless FileId and CreationTime match the footer below;
        // this is the known-good pair Blender writes too.
        var fileId = new FbxNode("FileId", new byte[] { 0x28, 0xb3, 0x2a, 0xeb, 0xb6, 0x24, 0xcc, 0xc2, 0xbf, 0xc8, 0xb0, 0x2a, 0xa9, 0x2b, 0xfc, 0xf1 });
        var created = new FbxNode("CreationTime", "1970-01-01 10:00:00:000");
        var creator = new FbxNode("Creator", "PboSpy");
        var documents = new FbxNode("Documents");
        documents.Add("Count", 1);
        var document = documents.Add("Document", next++, "Scene", "Scene");
        document.Add("RootNode", 0L);
        var definitions = new FbxNode("Definitions");
        definitions.Add("Version", 100);
        var types = objects.Children.GroupBy(c => c.Name).ToList();
        definitions.Add("Count", objects.Children.Count + 1);
        definitions.Add("ObjectType", "GlobalSettings").Add("Count", 1);
        foreach (var type in types)
        {
            definitions.Add("ObjectType", type.Key).Add("Count", type.Count());
        }
        var takes = new FbxNode("Takes");
        takes.Add("Current", "");
        foreach (var root in new[] { header, fileId, created, creator, global, documents, new FbxNode("References"), definitions, objects, links, takes })
        {
            WriteNode(file, root);
        }
        file.Write(new byte[13]);
        file.Write(new byte[] { 0xfa, 0xbc, 0xab, 0x09, 0xd0, 0xc8, 0xd4, 0x66, 0xb1, 0x76, 0xfb, 0x83, 0x1c, 0xf7, 0x26, 0x7e });
        file.Write(new byte[4]);
        var offset = file.BaseStream.Position;
        var pad = ((offset + 15) & ~15) - offset;
        file.Write(new byte[pad == 0 ? 16 : pad]);
        file.Write(7400u);
        file.Write(new byte[120]);
        file.Write(new byte[] { 0xf8, 0x5a, 0x8c, 0x6a, 0xde, 0xf5, 0xd9, 0x7e, 0xec, 0xe9, 0x0c, 0xe3, 0x75, 0x8f, 0x29, 0x0b });
    }

    private static void WriteNode(BinaryWriter file, FbxNode node)
    {
        var start = file.BaseStream.Position;
        file.Write(0u);
        file.Write((uint)node.Props.Count);
        file.Write(0u);
        var name = System.Text.Encoding.ASCII.GetBytes(node.Name);
        file.Write((byte)name.Length);
        file.Write(name);
        var propsStart = file.BaseStream.Position;
        foreach (var prop in node.Props)
        {
            switch (prop)
            {
                case bool b:
                    file.Write((byte)'C');
                    file.Write((byte)(b ? 1 : 0));
                    break;
                case int i:
                    file.Write((byte)'I');
                    file.Write(i);
                    break;
                case long l:
                    file.Write((byte)'L');
                    file.Write(l);
                    break;
                case double d:
                    file.Write((byte)'D');
                    file.Write(d);
                    break;
                case string s:
                    var bytes = System.Text.Encoding.UTF8.GetBytes(s);
                    file.Write((byte)'S');
                    file.Write((uint)bytes.Length);
                    file.Write(bytes);
                    break;
                case double[] doubles:
                    file.Write((byte)'d');
                    file.Write((uint)doubles.Length);
                    file.Write(0u);
                    file.Write((uint)(doubles.Length * 8));
                    foreach (var d in doubles)
                    {
                        file.Write(d);
                    }
                    break;
                case byte[] raw:
                    file.Write((byte)'R');
                    file.Write((uint)raw.Length);
                    file.Write(raw);
                    break;
                case int[] ints:
                    file.Write((byte)'i');
                    file.Write((uint)ints.Length);
                    file.Write(0u);
                    file.Write((uint)(ints.Length * 4));
                    foreach (var i in ints)
                    {
                        file.Write(i);
                    }
                    break;
            }
        }
        var propsLength = file.BaseStream.Position - propsStart;
        if (node.Children.Count > 0)
        {
            foreach (var child in node.Children)
            {
                WriteNode(file, child);
            }
            file.Write(new byte[13]);
        }
        else if (node.Props.Count == 0)
        {
            file.Write(new byte[13]);
        }
        var end = file.BaseStream.Position;
        file.BaseStream.Position = start;
        file.Write((uint)end);
        file.BaseStream.Position = start + 8;
        file.Write((uint)propsLength);
        file.BaseStream.Position = end;
    }
}
