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
        var triangles = new List<int>();
        var weights = new List<(int, float)[]>();
        var bones = new List<string>();
        var parents = new List<string>();
        foreach (var path in paths)
        {
            FileBase file = File.Exists(path) ? new PhysicalFile(path) : GameData.Find(path);
            if (file == null)
            {
                continue;
            }
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
            var start = points.Count;
            // ODOL keeps vertices relative to the bounding centre; animations work around the model origin.
            var center = odol.ModelInfo.BoundingCenter.Vector3;
            points.AddRange(lod.Vertices.Select(v => v.Vector3 + center));
            // Sections with neither texture nor material are proxy markers (head, weapon, gear slots), never drawn.
            foreach (var face in lod.Sections.Where(x => x.TextureIndex != -1 || x.MaterialIndex != -1).SelectMany(x => x.GetFaces(lod.Polygons.Faces)))
            {
                var v = face.VertexIndices;
                triangles.AddRange(new[] { start + v[0], start + v[2], start + v[1] });
                if (v.Length == 4)
                {
                    triangles.AddRange(new[] { start + v[0], start + v[3], start + v[2] });
                }
            }
            var refs = lod.VertexBoneRef;
            for (var i = 0; i < lod.Vertices.Count; i++)
            {
                var list = new List<(int, float)>();
                if (refs != null && i < refs.Count)
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
                var sum = list.Sum(w => w.Item2);
                weights.Add(list.Select(w => (w.Item1, w.Item2 / sum)).ToArray());
            }
        }
        if (triangles.Count == 0)
        {
            return null;
        }
        var rig = new RtmRig { Points = points.ToArray(), Triangles = triangles.ToArray(), BoneNames = bones.ToArray(), Parents = parents.ToArray(), Weights = weights.ToArray() };
        lock (Loaded)
        {
            Loaded[key] = rig;
        }
        return rig;
    }

    /// <summary>Vertices posed by the animation's bone matrices (bones it doesn't move stay put).</summary>
    public Vector3[] Pose(RtmAnimation animation, Matrix4x4[] matrices)
    {
        var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var b = 0; b < (animation?.Bones.Length ?? 0); b++)
        {
            lookup.TryAdd(animation.Bones[b], b);
        }
        // Binarised animations are relative to the parent bone; text ones already hold the final matrices.
        var local = BoneNames.Select(n => lookup.TryGetValue(n, out var b) && b < matrices.Length ? matrices[b] : Matrix4x4.Identity).ToArray();
        var byBone = new Matrix4x4[local.Length];
        var done = new bool[local.Length];
        Matrix4x4 Chain(int b)
        {
            if (!done[b])
            {
                var parent = Array.FindIndex(BoneNames, n => n.Equals(Parents[b], StringComparison.OrdinalIgnoreCase));
                byBone[b] = parent >= 0 && parent != b && animation?.Format.StartsWith("Binarised") == true ? local[b] * Chain(parent) : local[b];
                done[b] = true;
            }
            return byBone[b];
        }
        for (var b = 0; b < local.Length; b++)
        {
            Chain(b);
        }
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
