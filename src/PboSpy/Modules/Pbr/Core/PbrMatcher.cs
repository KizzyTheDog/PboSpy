using PboSpy.Modules.BulkExport.Models;
using PboSpy.Modules.BulkExport.Services;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PboSpy.Modules.Pbr.Core;

public enum TextureKind { Normal, Smdi, Ao, BaseColor }

public enum GroupSource { Rvmat, Name, Visual, Single, Manual }

public sealed class PbrTexture
{
    public string Path { get; init; }
    public string FileName => System.IO.Path.GetFileName(Path);
    public string Stem => System.IO.Path.GetFileNameWithoutExtension(Path);
    public string BaseName { get; init; }
    public TextureKind Kind { get; init; }
    public int Width { get; set; }
    public int Height { get; set; }
    public float[][] Features { get; set; }
    /// <summary>128×128 RGBA preview, already made readable (normals rebuilt, AO as grey).</summary>
    public byte[] Preview { get; set; }
    public string Error { get; set; }
    public override string ToString() => FileName;
}

public sealed class PbrGroup
{
    public Dictionary<TextureKind, PbrTexture> Members { get; } = new();
    public HashSet<TextureKind> Fixed { get; } = new();
    public GroupSource Source { get; set; }
    public double? Confidence { get; set; }
    public string SuggestedName { get; set; }
    public string Rvmat { get; set; }
}

public sealed class MatchOptions
{
    public double Threshold { get; set; } = 0.08;
    public bool UseNames { get; set; } = true;
    public bool UseRvmats { get; set; } = true;
}

/// <summary>
/// Works out which Arma textures belong to the same material / UV layout:
/// 1. RVMATs name the normal, specular and AO maps of a material exactly;
/// 2. otherwise textures sharing a base name ("body_nohq", "body_smdi") go together;
/// 3. whatever is left (obfuscated names, no rvmats) is matched by layout, solving an assignment
///    problem so each texture ends up in at most one set.
/// </summary>
public static class PbrMatcher
{
    private static readonly (string Suffix, TextureKind Kind)[] Suffixes = new (string, TextureKind)[]
    {
        ("_nohq", TextureKind.Normal), ("_novhq", TextureKind.Normal), ("_nofhq", TextureKind.Normal), ("_nof", TextureKind.Normal),
        ("_non", TextureKind.Normal), ("_ns", TextureKind.Normal), ("_no", TextureKind.Normal),
        ("_smdi", TextureKind.Smdi), ("_sm", TextureKind.Smdi),
        ("_adshq", TextureKind.Ao), ("_ads", TextureKind.Ao), ("_as", TextureKind.Ao),
        ("_co", TextureKind.BaseColor), ("_ca", TextureKind.BaseColor)
    }.OrderByDescending(s => s.Item1.Length).ToArray();

    private static readonly string[] Ignored = { "_ti_ca", "_ti_co", "_mc", "_dt", "_mask", "_detail" };

    public static readonly TextureKind[] Kinds = { TextureKind.Normal, TextureKind.Smdi, TextureKind.Ao, TextureKind.BaseColor };

    public static bool TryClassify(string stem, out TextureKind kind, out string baseName)
    {
        kind = default;
        baseName = null;
        var lower = stem.ToLowerInvariant();
        if (Ignored.Any(lower.EndsWith))
        {
            return false;
        }
        foreach (var (suffix, k) in Suffixes)
        {
            if (lower.EndsWith(suffix) && stem.Length > suffix.Length)
            {
                kind = k;
                baseName = stem[..^suffix.Length];
                return true;
            }
        }
        return false;
    }

    public static List<string> FindTextures(string folder, bool recursive) =>
        Directory.EnumerateFiles(folder, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .Where(PbrImage.IsSupported)
            .Where(f => TryClassify(Path.GetFileNameWithoutExtension(f), out _, out _))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static List<string> FindRvmats(string folder, bool recursive) =>
        Directory.EnumerateFiles(folder, "*.rvmat", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).ToList();

    /// <summary>Reads one texture and computes what the matcher needs.</summary>
    public static PbrTexture Analyse(string path)
    {
        TryClassify(Path.GetFileNameWithoutExtension(path), out var kind, out var baseName);
        var texture = new PbrTexture { Path = path, Kind = kind, BaseName = baseName };
        try
        {
            using var image = PbrImage.Load(path, PbrFeatures.Grid);
            var (width, height) = PbrImage.Size(path);
            texture.Width = width > 0 ? width : image.Width;
            texture.Height = height > 0 ? height : image.Height;
            var channels = PbrImage.Channels(image, PbrFeatures.Grid);
            texture.Features = PbrFeatures.Compute(channels, kind);
            texture.Preview = PbrOutput.MakePreview(channels, kind, PbrFeatures.Grid, 128);
        }
        catch (Exception ex)
        {
            texture.Error = ex.Message;
        }
        return texture;
    }

    public static List<PbrGroup> Group(IReadOnlyList<PbrTexture> textures, IEnumerable<string> rvmats, MatchOptions options)
    {
        var items = textures.Where(t => t.Error == null && t.Features != null).ToList();
        var groups = new List<PbrGroup>();
        var taken = new HashSet<PbrTexture>();

        if (options.UseRvmats)
        {
            groups.AddRange(FromRvmats(items, rvmats, taken));
        }
        if (options.UseNames)
        {
            groups.AddRange(FromNames(items, taken, groups));
        }

        groups = SolveVisual(items, groups, options.Threshold);

        foreach (var texture in textures.Where(t => t.Error != null || t.Features == null))
        {
            groups.Add(new PbrGroup { Source = GroupSource.Single, Members = { [texture.Kind] = texture } });
        }

        foreach (var group in groups)
        {
            UpdateConfidence(group);
        }
        return Order(groups);
    }

    public static void UpdateConfidence(PbrGroup group)
    {
        var members = group.Members.Values.Where(m => m.Features != null).ToList();
        if (members.Count < 2)
        {
            group.Confidence = null;
            return;
        }
        var min = double.MaxValue;
        for (var i = 0; i < members.Count; i++)
        {
            for (var j = i + 1; j < members.Count; j++)
            {
                min = Math.Min(min, PbrFeatures.Similarity(members[i].Features, members[j].Features));
            }
        }
        group.Confidence = min;
    }

    private static List<PbrGroup> Order(List<PbrGroup> groups)
    {
        int Rank(PbrGroup g) => g.Members.ContainsKey(TextureKind.Normal) ? 0
            : g.Members.ContainsKey(TextureKind.Smdi) ? 1
            : g.Members.ContainsKey(TextureKind.Ao) ? 2 : 3;
        string First(PbrGroup g) => Kinds.Where(g.Members.ContainsKey).Select(k => g.Members[k].FileName).FirstOrDefault() ?? "";
        return groups
            .OrderBy(g => g.Members.Count <= 1 ? 1 : 0)
            .ThenBy(Rank)
            .ThenBy(First, NaturalComparer.Instance)
            .ToList();
    }

    // ---- rvmats ----------------------------------------------------------

    private static readonly Regex TextTexture = new("texture\\s*=\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] Unwanted = { "damage", "destruct", "dmg", "broken", "wreck", "burn" };

    /// <summary>Every texture path referenced by an rvmat, text or binarized.</summary>
    public static List<string> ReadRvmatTextures(string path)
    {
        var result = new List<string>();
        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch (Exception)
        {
            return result;
        }

        if (data.Length > 4 && data[0] == 0 && data[1] == (byte)'r' && data[2] == (byte)'a' && data[3] == (byte)'P')
        {
            var marker = Encoding.ASCII.GetBytes("texture\0");
            for (var i = 0; i <= data.Length - marker.Length; i++)
            {
                var match = true;
                for (var k = 0; k < marker.Length && match; k++)
                {
                    match = data[i + k] == marker[k];
                }
                if (!match)
                {
                    continue;
                }
                var start = i + marker.Length;
                var end = start;
                while (end < data.Length && data[end] != 0)
                {
                    end++;
                }
                result.Add(Decode(data, start, end - start));
                i = end;
            }
        }
        else
        {
            foreach (Match m in TextTexture.Matches(Decode(data, 0, data.Length)))
            {
                result.Add(m.Groups[1].Value);
            }
        }
        return result.Where(t => !t.StartsWith('#') && t.Length > 0).ToList();
    }

    private static string Decode(byte[] data, int index, int count)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(data, index, count);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(data, index, count);
        }
    }

    /// <summary>File stem variants an rvmat texture path may have been exported as.</summary>
    private static IEnumerable<string> StemKeys(string texturePath)
    {
        var name = texturePath.Replace('/', '\\');
        name = name[(name.LastIndexOf('\\') + 1)..];
        var stem = Path.HasExtension(name) ? name[..name.LastIndexOf('.')] : name;
        yield return stem.ToLowerInvariant();
        foreach (var mode in Enum.GetValues<NameSanitizeMode>())
        {
            yield return BulkExportService.SanitizeSegment(stem, mode).ToLowerInvariant();
        }
    }

    private static IEnumerable<PbrGroup> FromRvmats(List<PbrTexture> items, IEnumerable<string> rvmats, HashSet<PbrTexture> taken)
    {
        var byStem = new Dictionary<string, PbrTexture>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            byStem.TryAdd(item.Stem, item);
        }

        var candidates = new List<(string Rvmat, Dictionary<TextureKind, PbrTexture> Members)>();
        foreach (var rvmat in rvmats ?? Enumerable.Empty<string>())
        {
            var members = new Dictionary<TextureKind, PbrTexture>();
            foreach (var texture in ReadRvmatTextures(rvmat))
            {
                var found = StemKeys(texture).Select(k => byStem.TryGetValue(k, out var t) ? t : null).FirstOrDefault(t => t != null);
                if (found != null && found.Kind != TextureKind.BaseColor)
                {
                    members.TryAdd(found.Kind, found);
                }
            }
            if (members.Count >= 2)
            {
                candidates.Add((rvmat, members));
            }
        }

        // Plain materials first; damage variants reuse the normal map with other specular maps.
        var ordered = candidates
            .OrderBy(c => Unwanted.Any(w => Path.GetFileName(c.Rvmat).Contains(w, StringComparison.OrdinalIgnoreCase)) ? 1 : 0)
            .ThenByDescending(c => c.Members.Count);

        var groups = new List<PbrGroup>();
        foreach (var (rvmat, members) in ordered)
        {
            var existing = groups.FirstOrDefault(g => members.Any(m => g.Members.TryGetValue(m.Key, out var t) && t == m.Value));
            if (existing != null)
            {
                foreach (var (kind, texture) in members)
                {
                    if (!taken.Contains(texture) && !existing.Members.ContainsKey(kind))
                    {
                        existing.Members[kind] = texture;
                        existing.Fixed.Add(kind);
                        taken.Add(texture);
                    }
                }
                continue;
            }
            var free = members.Where(m => !taken.Contains(m.Value)).ToList();
            if (free.Count < 2)
            {
                continue;
            }
            var group = new PbrGroup
            {
                Source = GroupSource.Rvmat,
                Rvmat = rvmat,
                SuggestedName = Path.GetFileNameWithoutExtension(rvmat)
            };
            foreach (var (kind, texture) in free)
            {
                group.Members[kind] = texture;
                group.Fixed.Add(kind);
                taken.Add(texture);
            }
            groups.Add(group);
        }
        return groups;
    }

    // ---- names -----------------------------------------------------------

    private static IEnumerable<PbrGroup> FromNames(List<PbrTexture> items, HashSet<PbrTexture> taken, List<PbrGroup> existing)
    {
        var result = new List<PbrGroup>();

        // Names can complete an rvmat set too (usually with its _co).
        foreach (var group in existing)
        {
            var bases = group.Members.Values.Select(m => m.BaseName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var item in items.Where(i => !taken.Contains(i)).OrderBy(Preference))
            {
                if (group.Members.ContainsKey(item.Kind))
                {
                    continue;
                }
                if (bases.Contains(item.BaseName, StringComparer.OrdinalIgnoreCase))
                {
                    group.Members[item.Kind] = item;
                    group.Fixed.Add(item.Kind);
                    taken.Add(item);
                }
            }
        }

        foreach (var set in items.Where(i => !taken.Contains(i)).GroupBy(i => i.BaseName, StringComparer.OrdinalIgnoreCase))
        {
            var members = new Dictionary<TextureKind, PbrTexture>();
            foreach (var item in set.OrderBy(Preference))
            {
                members.TryAdd(item.Kind, item);
            }
            if (members.Count < 2)
            {
                continue;
            }
            var group = new PbrGroup { Source = GroupSource.Name, SuggestedName = set.Key };
            foreach (var (kind, texture) in members)
            {
                group.Members[kind] = texture;
                group.Fixed.Add(kind);
                taken.Add(texture);
            }
            result.Add(group);
        }
        return result;
    }

    // An opaque _co is the colour map of a material; _ca is usually glass or decals on top.
    private static int Preference(PbrTexture t) => t.Stem.EndsWith("_ca", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

    // ---- layout matching -------------------------------------------------

    private sealed class Slot
    {
        public readonly PbrTexture[] Members = new PbrTexture[4];
        public readonly bool[] Fixed = new bool[4];
        public PbrGroup Origin;
    }

    private static List<PbrGroup> SolveVisual(List<PbrTexture> items, List<PbrGroup> seeded, double threshold)
    {
        var slots = new List<Slot>();
        var placed = new HashSet<PbrTexture>();
        foreach (var group in seeded)
        {
            var slot = new Slot { Origin = group };
            foreach (var (kind, texture) in group.Members)
            {
                slot.Members[(int)kind] = texture;
                slot.Fixed[(int)kind] = true;
                placed.Add(texture);
            }
            slots.Add(slot);
        }

        var free = Kinds.ToDictionary(k => k, k => items.Where(i => i.Kind == k && !placed.Contains(i)).ToList());
        var anchors = new[] { TextureKind.Normal, TextureKind.Smdi, TextureKind.Ao };
        var present = Kinds.Where(k => items.Any(i => i.Kind == k)).ToList();
        if (free.Values.All(f => f.Count == 0) || present.Count < 2)
        {
            return Finish(slots, items, threshold);
        }

        // Every free anchor texture gets a slot to start from; the most common kind seeds them.
        var seedKind = anchors.OrderByDescending(k => free[k].Count).First();
        if (free[seedKind].Count == 0)
        {
            seedKind = TextureKind.BaseColor;
        }
        foreach (var texture in free[seedKind])
        {
            var slot = new Slot();
            slot.Members[(int)seedKind] = texture;
            slots.Add(slot);
        }
        // Leave room for textures that match nothing.
        var extra = anchors.Where(k => k != seedKind).Select(k => free[k].Count).DefaultIfEmpty(0).Max();
        for (var i = 0; i < extra; i++)
        {
            slots.Add(new Slot());
        }

        var cache = new Dictionary<(PbrTexture, PbrTexture), float>();
        float Sim(PbrTexture a, PbrTexture b)
        {
            var key = a.GetHashCode() < b.GetHashCode() ? (a, b) : (b, a);
            if (!cache.TryGetValue(key, out var value))
            {
                value = PbrFeatures.Similarity(a.Features, b.Features);
                cache[key] = value;
            }
            return value;
        }

        double Total()
        {
            double total = 0;
            foreach (var slot in slots)
            {
                var members = slot.Members.Where(m => m != null).ToList();
                for (var i = 0; i < members.Count; i++)
                {
                    for (var j = i + 1; j < members.Count; j++)
                    {
                        total += Sim(members[i], members[j]);
                    }
                }
            }
            return total;
        }

        void Place(TextureKind kind)
        {
            var k = (int)kind;
            var movable = slots.Where(s => !s.Fixed[k]).ToList();
            var candidates = free[kind];
            if (movable.Count == 0 || candidates.Count == 0)
            {
                return;
            }
            var others = present.Count - 1;
            var rows = movable.Count;
            var cols = candidates.Count + movable.Count;
            var cost = new double[rows, cols];
            for (var s = 0; s < rows; s++)
            {
                var slot = movable[s];
                var members = present.Where(o => o != kind).Select(o => slot.Members[(int)o]).Where(m => m != null).ToList();
                for (var c = 0; c < candidates.Count; c++)
                {
                    double score = members.Sum(m => Sim(candidates[c], m)) + threshold * (others - members.Count) * 0.5;
                    cost[s, c] = -score;
                }
                // "Nothing of this kind here" costs the threshold for every partner.
                for (var d = candidates.Count; d < cols; d++)
                {
                    cost[s, d] = -threshold * others;
                }
            }
            var assignment = Hungarian.Solve(cost);
            for (var s = 0; s < rows; s++)
            {
                var column = assignment[s];
                movable[s].Members[k] = column < candidates.Count ? candidates[column] : null;
            }
        }

        foreach (var kind in present.Where(k => k != seedKind))
        {
            Place(kind);
        }
        var previous = Total();
        for (var round = 0; round < 40; round++)
        {
            foreach (var kind in present)
            {
                Place(kind);
            }
            var current = Total();
            if (current <= previous + 1e-9)
            {
                break;
            }
            previous = current;
        }

        return Finish(slots, items, threshold, Sim);
    }

    private static List<PbrGroup> Finish(List<Slot> slots, List<PbrTexture> items, double threshold, Func<PbrTexture, PbrTexture, float> sim = null)
    {
        sim ??= (a, b) => PbrFeatures.Similarity(a.Features, b.Features);
        var groups = new List<PbrGroup>();
        var used = new HashSet<PbrTexture>();

        foreach (var slot in slots)
        {
            var group = slot.Origin ?? new PbrGroup { Source = GroupSource.Visual };
            group.Members.Clear();
            for (var k = 0; k < 4; k++)
            {
                if (slot.Members[k] != null)
                {
                    group.Members[(TextureKind)k] = slot.Members[k];
                }
            }

            // Drop matched members that don't really fit the rest of the set.
            while (group.Members.Count > 1)
            {
                var loose = group.Members.Keys.Where(k => !slot.Fixed[(int)k]).ToList();
                if (loose.Count == 0)
                {
                    break;
                }
                var worst = loose
                    .Select(k => (Kind: k, Mean: group.Members.Where(o => o.Key != k).Average(o => sim(group.Members[k], o.Value))))
                    .OrderBy(x => x.Mean)
                    .First();
                if (worst.Mean >= threshold)
                {
                    break;
                }
                var dropped = group.Members[worst.Kind];
                group.Members.Remove(worst.Kind);
                groups.Add(new PbrGroup { Source = GroupSource.Single, Members = { [worst.Kind] = dropped } });
                used.Add(dropped);
            }

            if (group.Members.Count == 0)
            {
                continue;
            }
            if (group.Members.Count == 1 && slot.Origin == null)
            {
                group.Source = GroupSource.Single;
            }
            foreach (var member in group.Members.Values)
            {
                used.Add(member);
            }
            groups.Add(group);
        }

        foreach (var item in items.Where(i => !used.Contains(i)))
        {
            groups.Add(new PbrGroup { Source = GroupSource.Single, Members = { [item.Kind] = item } });
        }
        return groups;
    }
}

public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string x, string y)
    {
        x ??= "";
        y ??= "";
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                var si = i;
                var sj = j;
                while (i < x.Length && char.IsDigit(x[i])) i++;
                while (j < y.Length && char.IsDigit(y[j])) j++;
                var a = x[si..i].TrimStart('0');
                var b = y[sj..j].TrimStart('0');
                var c = a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
                if (c != 0) return c;
            }
            else
            {
                var c = char.ToLowerInvariant(x[i]).CompareTo(char.ToLowerInvariant(y[j]));
                if (c != 0) return c;
                i++;
                j++;
            }
        }
        return (x.Length - i).CompareTo(y.Length - j);
    }
}
