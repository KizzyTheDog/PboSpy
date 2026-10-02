using PboSpy.Localization;
using BIS.Core.Streams;
using BIS.P3D;
using BIS.RTM;
using PboSpy.Models;
using PboSpy.Modules.P3d.Scene;
using System.IO;
using System.Numerics;
using System.Text;

namespace PboSpy.Modules.Rtm;

/// <summary>An Arma animation (text RTM_0101 or binarised BMTR): per frame, one model-space matrix per bone.</summary>
internal sealed class RtmAnimation
{
    public string Format { get; private init; } = "";
    public string[] Bones { get; private init; } = Array.Empty<string>();
    public float[] Times { get; private init; } = Array.Empty<float>();
    public Matrix4x4[][] Frames { get; private init; } = Array.Empty<Matrix4x4[]>();
    public Vector3 Step { get; private init; }
    public string Details { get; private init; } = "";

    public static RtmAnimation Read(FileBase file)
    {
        using var source = file.GetStream();
        var stream = new MemoryStream();
        source.CopyTo(stream);
        stream.Position = 0;
        var magic = Encoding.ASCII.GetString(stream.ToArray(), 0, Math.Min(8, (int)stream.Length));
        if (magic.StartsWith("BMTR"))
        {
            var rtm = new RTMB(stream);
            var frames = rtm.Phases.Select(phase => phase.Select(t =>
            {
                // Stored relative to the parent bone, and conjugated and turned 180° about Y compared with the text
                // format: checked bone by bone against Mikero's DeRtm output.
                var q = t.Quaternion;
                var v = t.Vector.Vector3;
                var m = Matrix4x4.CreateFromQuaternion(new Quaternion(q.X, -q.Y, q.Z, q.W));
                m.Translation = new Vector3(-v.X, v.Y, -v.Z);
                return m;
            }).ToArray()).ToArray();
            return Make("Binarised (BMTR v" + rtm.Version + ")", rtm.BoneNames, rtm.PhaseTimes, frames, rtm.Step.Vector3,
                rtm.MetaDataValues, rtm.AnimKeyStones?.Select(k => k.ToString()));
        }
        // Text RTMs can start with an RTM_MDAT block (properties) before the RTM_0101 data.
        var raw = stream.ToArray();
        var at = Math.Max(0, Encoding.ASCII.GetString(raw).IndexOf("RTM_0101", StringComparison.Ordinal));
        var text = new RTM(new MemoryStream(raw, at, raw.Length - at));
        var count = text.FrameTimes.Length;
        var bones = text.BoneNames.Select(b => b.Split('\0')[0].Trim()).ToArray();
        var matrices = Enumerable.Range(0, count).Select(f => Enumerable.Range(0, bones.Length).Select(b => text.FrameTransforms[f, b].Matrix).ToArray()).ToArray();
        return Make("RTM_0101 (editable)", bones, text.FrameTimes, matrices, text.Displacement.Vector3, null, null);
    }

    private static RtmAnimation Make(string format, string[] bones, float[] times, Matrix4x4[][] frames, Vector3 step,
        IEnumerable<string> metadata, IEnumerable<string> keys)
    {
        var details = new StringBuilder();
        details.AppendLine($"Format     {format}");
        details.AppendLine($"Bones      {bones.Length}");
        details.AppendLine($"Frames     {frames.Length}");
        details.AppendLine($"Step       {step.X:0.###} {step.Y:0.###} {step.Z:0.###}  (movement per cycle)");
        if (metadata?.Any() == true)
        {
            details.AppendLine("Metadata   " + string.Join(", ", metadata));
        }
        if (keys?.Any() == true)
        {
            details.AppendLine("Keys       " + string.Join(", ", keys));
        }
        details.AppendLine();
        details.AppendLine("Frame times");
        details.AppendLine("  " + string.Join("  ", times.Select(t => t.ToString("0.###"))));
        details.AppendLine();
        details.AppendLine("Bones (frame 0 position)");
        for (var b = 0; b < bones.Length; b++)
        {
            var p = frames.Length > 0 ? frames[0][b].Translation : Vector3.Zero;
            details.AppendLine($"  {b,3}  {bones[b],-28} {p.X,8:0.###} {p.Y,8:0.###} {p.Z,8:0.###}");
        }
        return new RtmAnimation { Format = format, Bones = bones, Times = times, Frames = frames, Step = step, Details = details.ToString() };
    }

    /// <summary>Bone matrices at a phase 0..1, blended between the two nearest frames.</summary>
    public Matrix4x4[] Sample(double phase)
    {
        if (Frames.Length == 0)
        {
            return Array.Empty<Matrix4x4>();
        }
        var next = Array.FindIndex(Times, t => t >= phase);
        if (next <= 0)
        {
            return Frames[Math.Max(0, next)];
        }
        var previous = next - 1;
        var span = Times[next] - Times[previous];
        var f = span > 0 ? (float)((phase - Times[previous]) / span) : 0;
        // ponytail: blending matrices element-wise skews them slightly mid-frame; fine for a preview, slerp if it ever shows.
        return Frames[previous].Zip(Frames[next], (a, b) => Matrix4x4.Lerp(a, b, f)).ToArray();
    }
}

/// <summary>The game's body model with each vertex's bones, used to play animations on.</summary>
internal sealed class RtmRig
{
    /// <summary>Built-in rigs: game character models, each with a head, read from the installed Arma 3.</summary>
    public static readonly (string Key, string[] Paths)[] BuiltIn =
    {
        ("Rtm.Rig.Body", new[] { "a3/characters_f/common/basicbody.p3d", "a3/characters_f/heads/m_white_01.p3d" }),
        ("Rtm.Rig.Nato", new[] { "a3/characters_f/blufor/b_soldier_01.p3d", "a3/characters_f/heads/m_white_01.p3d" }),
        ("Rtm.Rig.Csat", new[] { "a3/characters_f/opfor/o_soldier_01.p3d", "a3/characters_f/heads/m_persian_01.p3d" }),
        ("Rtm.Rig.Civilian", new[] { "a3/characters_f/civil/c_citizen1.p3d", "a3/characters_f/heads/m_greek_01.p3d" }),
        ("Rtm.Rig.Pilot", new[] { "a3/characters_f/common/pilot_f.p3d", "a3/characters_f/heads/m_african_01.p3d" }),
    };

    /// <summary>Model space (bounding centre added back), so bone matrices apply directly.</summary>
    public Vector3[] Points { get; private init; }
    public int[] Triangles { get; private init; }
    public Vector2[] Uvs { get; private init; }
    /// <summary>Triangles per texture (one entry per section), so the view can give each its own material.</summary>
    public (string Texture, int[] Triangles)[] Groups { get; private init; }
    /// <summary>A model the rig came from, so textures are looked up next to it (and in the game).</summary>
    public FileBase Source { get; private init; }
    public string[] BoneNames { get; private init; }
    public string[] Parents { get; private init; }
    public (int Bone, float Weight)[][] Weights { get; private init; }

    private static readonly Dictionary<string, RtmRig> Loaded = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Game paths (a3\...) or files on disk, merged into one rig. Null when none could be read.</summary>
    public static RtmRig Load(string[] paths)
    {
        var key = string.Join("|", paths);
        lock (Loaded)
        {
            if (Loaded.TryGetValue(key, out var hit))
            {
                return hit;
            }
        }
        var points = new List<Vector3>();
        var uvs = new List<Vector2>();
        var groups = new List<(string, List<int>)>();
        FileBase source = null;
        var triangles = new List<int>();
        var weights = new List<(int, float)[]>();
        var bones = new List<string>();
        var parents = new List<string>();
        (BIS.P3D.ODOL.LOD Lod, Vector3 Center, int[] Bones) body = default;
        foreach (var entry in paths)
        {
            // "model.p3d?ucp=oefcp" swaps part of the model's texture names, for the camo a loadout uses.
            var swaps = entry.Split('?', 2).Skip(1).SelectMany(q => q.Split('&')).Select(x => x.Split('=', 2)).Where(x => x.Length == 2).ToList();
            var path = entry.Split('?')[0];
            FileBase file = File.Exists(path) ? new PhysicalFile(path) : GameData.Find(path);
            if (file == null)
            {
                continue;
            }
            source ??= file;
            P3D model;
            using (var stream = file.GetStream())
            {
                model = StreamHelper.Read<P3D>(stream);
            }
            var odol = model.ODOL ?? throw new InvalidOperationException(Loc.T("Rtm.NeedsOdol"));
            var lod = odol.Lods.FirstOrDefault();
            if (lod == null)
            {
                continue;
            }
            var skeleton = odol.ModelInfo.Skeleton?.SkeletonBoneNames ?? Array.Empty<BIS.P3D.ODOL.SkeletonBoneName>();
            // A model that shares no bones with the body (a weapon, with its own bolt and magazine bones or none) is held, not worn.
            var held = body.Lod != null && !skeleton.Any(b => bones.Contains(b.BoneName, StringComparer.OrdinalIgnoreCase));
            if (held)
            {
                skeleton = Array.Empty<BIS.P3D.ODOL.SkeletonBoneName>();
            }
            var boneIndex = skeleton.Select(b =>
            {
                var at = bones.FindIndex(n => n.Equals(b.BoneName, StringComparison.OrdinalIgnoreCase));
                if (at < 0)
                {
                    bones.Add(b.BoneName);
                    parents.Add(b.ParentBoneName);
                    at = bones.Count - 1;
                }
                return at;
            }).ToArray();
            string defaultTexture = null;
            var start = points.Count;
            // ODOL keeps vertices relative to the bounding centre; animations work around the model origin.
            var center = odol.ModelInfo.BoundingCenter.Vector3;
            // A held model hangs on the body's weapon proxy and moves with that proxy's bone, as in the game.
            var place = Matrix4x4.Identity;
            var attach = bones.FindIndex(n => n.Equals("head", StringComparison.OrdinalIgnoreCase));
            if (held)
            {
                var proxy = body.Lod.Proxies.FirstOrDefault(p => Path.GetFileName(p.ProxyModel.Replace('\\', '/')).StartsWith("weapon", StringComparison.OrdinalIgnoreCase));
                if (proxy != null)
                {
                    place = proxy.Transformation.Matrix * Matrix4x4.CreateTranslation(body.Center);
                    attach = proxy.BoneIndex >= 0 && proxy.BoneIndex < body.Bones.Length ? body.Bones[proxy.BoneIndex]
                        : bones.FindIndex(n => n.Equals("weapon", StringComparison.OrdinalIgnoreCase));
                }
            }
            body = body.Lod == null ? (lod, center, boneIndex) : body;
            points.AddRange(lod.Vertices.Select(v => Vector3.Transform(v.Vector3 + center, place)));
            var uv = lod.UvSets != null && lod.UvSets.Length > 0 ? lod.UvSets[0].GetUV() : null;
            uvs.AddRange(Enumerable.Range(0, lod.Vertices.Count).Select(i => uv != null && i < uv.Length ? uv[i] : Vector2.Zero));
            // Sections with neither texture nor material are proxy markers (head, weapon, gear slots), never drawn.
            foreach (var section in lod.Sections.Where(x => x.TextureIndex != -1 || x.MaterialIndex != -1))
            {
                var texture = section.TextureIndex >= 0 && section.TextureIndex < lod.Textures.Length ? lod.Textures[section.TextureIndex] ?? "" : "";
                texture = swaps.Aggregate(texture, (t, x) => t.Replace(x[0], x[1], StringComparison.OrdinalIgnoreCase));
                // Faces and uniforms set by config leave a blank slot in the model; "data\<model>_co.paa" next to it is the usual default.
                if (string.IsNullOrWhiteSpace(texture) || TextureResolver.IsInvisible(texture))
                {
                    texture = defaultTexture ??= DefaultTexture(path);
                }
                var group = groups.FirstOrDefault(g => g.Item1.Equals(texture, StringComparison.OrdinalIgnoreCase)).Item2;
                if (group == null)
                {
                    groups.Add((texture, group = new List<int>()));
                }
                foreach (var face in section.GetFaces(lod.Polygons.Faces))
                {
                    var v = face.VertexIndices;
                    var tri = v.Length == 4
                        ? new[] { start + v[0], start + v[2], start + v[1], start + v[0], start + v[3], start + v[2] }
                        : new[] { start + v[0], start + v[2], start + v[1] };
                    triangles.AddRange(tri);
                    group.AddRange(tri);
                }
            }
            var refs = lod.VertexBoneRef;
            var unweighted = 0;
            for (var i = 0; i < lod.Vertices.Count; i++)
            {
                var list = new List<(int, float)>();
                if (refs != null && i < refs.Count && !held)
                {
                    var r = refs[i];
                    for (var k = 0; k < Math.Min(r.Count, 4); k++)
                    {
                        var sub = r.Data[k * 2];
                        if (sub < lod.SubSkeletonsToSkeleton.Length && lod.SubSkeletonsToSkeleton[sub] < boneIndex.Length)
                        {
                            list.Add((boneIndex[lod.SubSkeletonsToSkeleton[sub]], Math.Max(1, (int)r.Data[k * 2 + 1]) / 255f));
                        }
                    }
                }
                // The game hangs a head model on the body's head bone, so its unweighted parts (eyes, teeth) move with it.
                if (list.Count == 0 && file != source && attach >= 0)
                {
                    list.Add((attach, 1));
                    unweighted++;
                }
                var sum = list.Sum(w => w.Item2);
                weights.Add(list.Select(w => (w.Item1, w.Item2 / sum)).ToArray());
            }
            if (PboSpy.Services.TestMode.On)
                PboSpy.Services.TestMode.Log($"rig: {path} has {unweighted} vertices without bones, tied to bone {attach}");
        }
        if (triangles.Count == 0)
        {
            return null;
        }
        var rig = new RtmRig { Points = points.ToArray(), Triangles = triangles.ToArray(), Uvs = uvs.ToArray(), Source = source,
            Groups = groups.Select(g => (g.Item1, g.Item2.ToArray())).ToArray(), BoneNames = bones.ToArray(), Parents = parents.ToArray(), Weights = weights.ToArray() };
        lock (Loaded)
        {
            Loaded[key] = rig;
        }
        return rig;
    }

    private static string DefaultTexture(string modelPath)
    {
        var candidate = Path.Combine(Path.GetDirectoryName(modelPath.Replace('/', '\\')) ?? "", "data", Path.GetFileNameWithoutExtension(modelPath) + "_co.paa");
        return File.Exists(candidate) || GameData.Find(candidate) != null ? candidate : "";
    }

    /// <summary>Each rig bone's final matrix (rest model space to posed model space) for one frame.</summary>
    public Matrix4x4[] Skin(RtmAnimation animation, Matrix4x4[] matrices)
    {
        var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var b = 0; b < (animation?.Bones.Length ?? 0); b++)
        {
            lookup.TryAdd(animation.Bones[b], b);
        }
        // Binarised animations are relative to the parent bone; text ones already hold the final matrices.
        // ponytail: body RTMs hold placeholders for the face (the game drives it with separate mimic animations), so face bones
        // follow the head instead; read them if face RTMs ever need previewing.
        var animated = BoneNames.Select(n => lookup.TryGetValue(n, out var b) && b < matrices.Length && !IsFace(n)).ToArray();
        var local = BoneNames.Select((n, i) => animated[i] ? matrices[lookup[n]] : Matrix4x4.Identity).ToArray();
        var relative = animation?.Format.StartsWith("Binarised") == true;
        var byBone = new Matrix4x4[local.Length];
        var done = new bool[local.Length];
        Matrix4x4 Chain(int b)
        {
            if (!done[b])
            {
                var parent = Array.FindIndex(BoneNames, n => n.Equals(Parents[b], StringComparison.OrdinalIgnoreCase));
                var hasParent = parent >= 0 && parent != b;
                // A bone the animation leaves out (face bones in most RTMs) follows its parent.
                byBone[b] = hasParent && relative ? local[b] * Chain(parent)
                    : hasParent && !animated[b] ? Chain(parent)
                    : local[b];
                done[b] = true;
            }
            return byBone[b];
        }
        for (var b = 0; b < local.Length; b++)
        {
            Chain(b);
        }
        return byBone;
    }

    private static bool IsFace(string bone) =>
        bone.StartsWith("face_", StringComparison.OrdinalIgnoreCase) || bone.StartsWith("eye", StringComparison.OrdinalIgnoreCase);

    /// <summary>Vertices posed by the animation's bone matrices (bones it doesn't move stay put).</summary>
    public Vector3[] Pose(RtmAnimation animation, Matrix4x4[] matrices)
    {
        var byBone = Skin(animation, matrices);
        var result = new Vector3[Points.Length];
        Parallel.For(0, Points.Length, i =>
        {
            var weights = Weights[i];
            if (weights.Length == 0)
            {
                result[i] = Points[i];
                return;
            }
            var p = Vector3.Zero;
            foreach (var (bone, weight) in weights)
            {
                p += Vector3.Transform(Points[i], bone < byBone.Length ? byBone[bone] : Matrix4x4.Identity) * weight;
            }
            result[i] = p;
        });
        return result;
    }
}
