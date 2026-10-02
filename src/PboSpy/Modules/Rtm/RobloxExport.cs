using PboSpy.Modules.P3d.Scene;
using PboSpy.Services;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace PboSpy.Modules.Rtm;

/// <summary>
/// Arma rigs as skinned FBX (Studio's 3D importer turns them into MeshParts with Bones) and RTMs as KeyframeSequence .rbxmx
/// files that play on them. Bones keep their Arma names under one extra root bone.
/// </summary>
internal static class RobloxExport
{
    public const string RootBone = "ArmaRoot";

    // 1 stud = 28 cm (Roblox's avatar scale). The FBX declares 28 cm units, so the importer lands on studs whether it reads the units or not.
    private const float Studs = 1 / 0.28f;
    private static readonly Matrix4x4 ToRoblox = Matrix4x4.CreateScale(Studs, Studs, -Studs);
    // Studio's importer turns the model 180° about Y (to face -Z), so the animation is worked out in that turned space.
    private static readonly Matrix4x4 ToStudio = ToRoblox * Matrix4x4.CreateScale(-1, 1, -1);

    // Arma's rest pose stands about a metre below the ground the animations use; Roblox gets it standing on the ground,
    // and the animations take that lift back out.
    private static Matrix4x4 Lift(RtmRig rig) => Matrix4x4.CreateTranslation(0, -rig.Triangles.Min(i => rig.Points[i].Y), 0);

    /// <summary>The rig the animation preview uses.</summary>
    public static string[] CurrentRig()
    {
        var saved = AppSettings.Default.RtmRig;
        return string.IsNullOrWhiteSpace(saved) ? RtmRig.BuiltIn[0].Paths : saved.Split('|');
    }

    /// <summary>One MeshPart per texture, named after it.</summary>
    public static string[] MeshNames(RtmRig rig)
    {
        var names = new List<string>();
        foreach (var (texture, _) in rig.Groups)
        {
            var stem = string.IsNullOrWhiteSpace(texture) || TextureResolver.IsProcedural(texture) ? "Body" : Path.GetFileNameWithoutExtension(texture.Replace('\\', '/').Split('/').Last());
            stem = new string(stem.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
            var name = stem;
            for (var n = 2; names.Contains(name, StringComparer.OrdinalIgnoreCase); n++)
            {
                name = stem + "_" + n;
            }
            names.Add(name);
        }
        return names.ToArray();
    }

    /// <summary>
    /// Arma keeps no bone positions, so each bone's head is where its vertices meet its parent's (the joint ring),
    /// else the middle of its own vertices, else its parent's head. Only looks; the animation maths doesn't depend on it.
    /// </summary>
    private static Vector3[] Heads(RtmRig rig)
    {
        var count = rig.BoneNames.Length;
        var parents = Parents(rig);
        var joint = new Vector3[count];
        var jointCount = new int[count];
        var own = new Vector3[count];
        var ownCount = new int[count];
        for (var i = 0; i < rig.Points.Length; i++)
        {
            foreach (var (bone, weight) in rig.Weights[i])
            {
                own[bone] += rig.Points[i];
                ownCount[bone]++;
                if (parents[bone] >= 0 && rig.Weights[i].Any(w => w.Bone == parents[bone] && w.Weight > 0))
                {
                    joint[bone] += rig.Points[i];
                    jointCount[bone]++;
                }
            }
        }
        var heads = new Vector3?[count];
        Vector3 Head(int b) => heads[b] ??= jointCount[b] > 0 ? joint[b] / jointCount[b]
            : ownCount[b] > 0 ? own[b] / ownCount[b]
            : parents[b] >= 0 ? Head(parents[b]) : Vector3.Zero;
        return Enumerable.Range(0, count).Select(Head).ToArray();
    }

    private static int[] Parents(RtmRig rig) => rig.Parents.Select((p, b) =>
    {
        var parent = Array.FindIndex(rig.BoneNames, n => n.Equals(p, StringComparison.OrdinalIgnoreCase));
        return parent == b ? -1 : parent;
    }).ToArray();

    public static void WriteRig(string target, RtmRig rig)
    {
        var place = Lift(rig) * ToRoblox;
        var heads = Heads(rig).Select(h => Vector3.Transform(h, place)).ToArray();
        // Bone 0 is the extra root; Arma bones follow, shifted by one.
        var skin = new ModelExport.Skin
        {
            Bones = rig.BoneNames.Prepend(RootBone).ToArray(),
            Parents = Parents(rig).Select(p => p + 1).Prepend(-1).ToArray(),
            Heads = heads.Select(h => new Vector3D(h.X, h.Y, h.Z)).Prepend(new Vector3D()).ToArray(),
            Weights = new(),
            UnitCentimetres = 28,
        };
        var parts = new List<ModelPart>();
        var names = MeshNames(rig);
        foreach (var ((texture, triangles), name) in rig.Groups.Zip(names))
        {
            var map = new Dictionary<int, int>();
            var used = new List<int>();
            var indices = triangles.Select(t =>
            {
                if (!map.TryGetValue(t, out var local))
                {
                    local = map[t] = used.Count;
                    used.Add(t);
                }
                return local;
            }).ToArray();
            var positions = used.Select(i => Vector3.Transform(rig.Points[i], place)).Select(p => new Point3D(p.X, p.Y, p.Z)).ToList();
            var normals = new Vector3D[positions.Count];
            for (var i = 0; i + 2 < indices.Length; i += 3)
            {
                var n = Vector3D.CrossProduct(positions[indices[i + 1]] - positions[indices[i]], positions[indices[i + 2]] - positions[indices[i]]);
                normals[indices[i]] += n;
                normals[indices[i + 1]] += n;
                normals[indices[i + 2]] += n;
            }
            foreach (ref var n in normals.AsSpan())
            {
                n.Normalize();
            }
            var mesh = new MeshGeometry3D
            {
                Positions = new Point3DCollection(positions),
                Normals = new Vector3DCollection(normals),
                TextureCoordinates = new PointCollection(used.Select(i => new System.Windows.Point(rig.Uvs[i].X, rig.Uvs[i].Y))),
                TriangleIndices = new Int32Collection(indices),
            };
            var part = new ModelPart { Texture = texture, Mesh = mesh, Triangles = indices.Length / 3, Owner = name };
            parts.Add(part);
            skin.Weights[part] = used.Select(i => rig.Weights[i].Select(w => (w.Bone + 1, w.Weight)).ToArray()).ToArray();
        }
        ModelExport.WriteSkinned(target, parts, new TextureResolver(rig.Source, null), skin);
    }

    /// <summary>
    /// A KeyframeSequence for Roblox's Animator. Each pose is the bone's change from its rest CFrame:
    /// world = parent world * rest * pose, and the skinned result must equal Arma's matrix for that bone.
    /// </summary>
    public static void WriteAnimation(string target, RtmAnimation animation, RtmRig rig)
    {
        var lift = Lift(rig);
        var place = lift * ToStudio;
        var heads = Heads(rig).Select(h => Vector3.Transform(h, place)).ToArray();
        var parents = Parents(rig);
        Matrix4x4.Invert(place, out var unplace);
        var poses = animation.Frames.Select(frame =>
        {
            // Rest vertex (placed) -> Arma rest -> Arma pose; the pose is already on the animation's ground, so no lift back.
            var skins = rig.Skin(animation, frame).Select(m => unplace * m * ToStudio).ToArray();
            return skins.Select((s, b) =>
            {
                var parent = parents[b] >= 0 ? skins[parents[b]] : Matrix4x4.Identity;
                Matrix4x4.Invert(parent, out var undo);
                return Matrix4x4.CreateTranslation(heads[b]) * s * undo * Matrix4x4.CreateTranslation(-heads[b]);
            }).ToArray();
        }).ToArray();
        // Only bones that move (and the bones above them) get poses, which keeps the file small.
        var keep = new bool[rig.BoneNames.Length];
        for (var b = 0; b < keep.Length; b++)
        {
            if (poses.Any(f => !Near(f[b], Matrix4x4.Identity)))
            {
                for (var at = b; at >= 0 && !keep[at]; at = parents[at])
                {
                    keep[at] = true;
                }
            }
        }
        // ponytail: Arma's speed comes from the game config, which an RTM doesn't carry; 30 frames a second is a guess, change the length in Roblox.
        var length = Math.Max(1, animation.Frames.Length - 1) / 30.0;
        var xml = new StringBuilder();
        var refs = 0;
        void Open(string type, string name, string properties)
        {
            xml.Append($"<Item class=\"{type}\" referent=\"RBX{refs++}\"><Properties><string name=\"Name\">{System.Security.SecurityElement.Escape(name)}</string>{properties}</Properties>");
        }
        void Pose(Matrix4x4[] frame, int b)
        {
            Open("Pose", rig.BoneNames[b], CFrame(frame[b]) + "<token name=\"EasingDirection\">0</token><token name=\"EasingStyle\">0</token><float name=\"Weight\">1</float>");
            for (var child = 0; child < keep.Length; child++)
            {
                if (keep[child] && parents[child] == b)
                {
                    Pose(frame, child);
                }
            }
            xml.Append("</Item>");
        }
        xml.Append("<roblox xmlns:xmime=\"http://www.w3.org/2005/05/xmlmime\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:noNamespaceSchemaLocation=\"http://www.roblox.com/roblox.xsd\" version=\"4\">");
        Open("KeyframeSequence", Path.GetFileNameWithoutExtension(target), "<bool name=\"Loop\">true</bool><token name=\"Priority\">2</token>");
        var none = CFrame(Matrix4x4.Identity) + "<token name=\"EasingDirection\">0</token><token name=\"EasingStyle\">0</token><float name=\"Weight\">0</float>";
        for (var f = 0; f < poses.Length; f++)
        {
            Open("Keyframe", "Keyframe", $"<float name=\"Time\">{Number(animation.Times[f] * length)}</float>");
            // The importer puts the bones under one of the MeshParts; the Animator ignores the poses for the others.
            foreach (var part in MeshNames(rig))
            {
                Open("Pose", part, none);
                Open("Pose", RootBone, none);
                for (var b = 0; b < keep.Length; b++)
                {
                    if (keep[b] && parents[b] < 0)
                    {
                        Pose(poses[f], b);
                    }
                }
                xml.Append("</Item></Item>");
            }
            xml.Append("</Item>");
        }
        xml.Append("</Item></roblox>");
        File.WriteAllText(target, xml.ToString());
    }

    /// <summary>Asks for a folder, then writes the current rig as FBX and each RTM as .rbxmx next to it.</summary>
    public static async Task Export(IReadOnlyList<PboSpy.Models.FileBase> rtms)
    {
        if (rtms.Count == 0)
        {
            return;
        }
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = PboSpy.Localization.Loc.T("Rtm.Roblox") };
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        var folder = dialog.FolderName;
        var failures = new List<string>();
        await Task.Run(() =>
        {
            var paths = CurrentRig();
            var rig = RtmRig.Load(paths) ?? throw new InvalidOperationException(PboSpy.Localization.Loc.T("Rtm.NoGame"));
            WriteRig(Path.Combine(folder, Path.GetFileNameWithoutExtension(paths[0]) + "_rig.fbx"), rig);
            foreach (var file in rtms)
            {
                try
                {
                    WriteAnimation(Path.Combine(folder, Path.GetFileNameWithoutExtension(file.Name) + ".rbxmx"), RtmAnimation.Read(file), rig);
                }
                catch (Exception ex)
                {
                    failures.Add(file.Name + ": " + ex.Message);
                }
            }
        }).ContinueWith(t => { if (t.Exception != null) failures.Add(t.Exception.InnerException?.Message); });
        if (failures.Count > 0)
        {
            System.Windows.MessageBox.Show(string.Join(Environment.NewLine, failures.Take(10)), PboSpy.Localization.Loc.T("Rtm.Roblox"),
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
        System.Diagnostics.Process.Start("explorer.exe", $"\"{folder}\"");
    }

    private static bool Near(Matrix4x4 a, Matrix4x4 b)
    {
        var d = a - b;
        return Math.Abs(d.M11) + Math.Abs(d.M12) + Math.Abs(d.M13) + Math.Abs(d.M21) + Math.Abs(d.M22) + Math.Abs(d.M23)
            + Math.Abs(d.M31) + Math.Abs(d.M32) + Math.Abs(d.M33) + Math.Abs(d.M41) + Math.Abs(d.M42) + Math.Abs(d.M43) < 1e-4;
    }

    private static string Number(double value) => value.ToString("0.#####", CultureInfo.InvariantCulture);

    // Row-vector matrix to a Roblox CFrame (column vectors), with any scale taken out since CFrames can't hold it.
    private static string CFrame(Matrix4x4 m)
    {
        Matrix4x4.Decompose(m, out _, out var rotation, out var position);
        var r = Matrix4x4.CreateFromQuaternion(rotation);
        return "<CoordinateFrame name=\"CFrame\">" +
            $"<X>{Number(position.X)}</X><Y>{Number(position.Y)}</Y><Z>{Number(position.Z)}</Z>" +
            $"<R00>{Number(r.M11)}</R00><R01>{Number(r.M21)}</R01><R02>{Number(r.M31)}</R02>" +
            $"<R10>{Number(r.M12)}</R10><R11>{Number(r.M22)}</R11><R12>{Number(r.M32)}</R12>" +
            $"<R20>{Number(r.M13)}</R20><R21>{Number(r.M23)}</R21><R22>{Number(r.M33)}</R22></CoordinateFrame>";
    }
}
