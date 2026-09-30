using BIS.Core.Config;
using BIS.Core.Streams;
using BIS.P3D;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PboSpy.Modules.Deobfuscate.Core;

public enum NameSource { None, Config, Pair, Material, Selection, Model, Script, Fallback }

public sealed class NameInput
{
    public NameInput(string path, long size, Func<Stream> open)
    {
        Path = path;
        Size = size;
        Open = open;
    }

    /// <summary>Path inside the PBO exactly as stored (Latin-1 decoded bytes).</summary>
    public string Path { get; }
    public long Size { get; }
    public Func<Stream> Open { get; }
}

public sealed class NameSuggestion
{
    public NameInput Input { get; init; }
    public string Current { get; init; } = "";
    public string Suggested { get; set; } = "";
    public string Reason { get; set; } = "";
    public NameSource Source { get; set; }
}

public sealed class NameReport
{
    public List<NameSuggestion> Items { get; } = new();
    public int Total { get; set; }
    public int Decoys { get; set; }
    public List<string> DecoyNames { get; } = new();
    public string ObfuscatorTag { get; set; } = "";
    public string PackedWith { get; set; } = "";
    public int MinLength { get; set; }
    public int MaxLength { get; set; }
    public int WithWildcard { get; set; }
    public Dictionary<NameSource, int> BySource { get; } = new();
}

/// <summary>
/// Scrambled PBOs replace file names with random letters plus a '*' or '?' so Windows can't create
/// the files. The originals are gone, but most files are referenced from somewhere (config classes,
/// model sections, rvmat stages, scripts), and those references say what the file is for.
/// </summary>
public static class NameRecovery
{
    private static readonly string[] Suffixes =
    {
        "_ti_ca", "_nohq", "_smdi", "_mask", "_mco", "_cdt", "_dtsmdi", "_sm", "_as", "_co", "_ca", "_mc", "_dt", "_no", "_ns", "_nopx", "_ads", "_lco", "_adshq", "_pr"
    };

    private static readonly HashSet<string> GenericProps = new(StringComparer.OrdinalIgnoreCase)
    {
        "file", "model", "sound", "texture", "text", "samples", "sample", "filename", "path", "name", "soundset", "sounds",
        "sound1", "sound2", "sound3", "begin1", "begin2", "begin3", "begin4", "soundbegin", "soundend", "hiddenselectionstextures"
    };

    private static readonly HashSet<string> SuffixProps = new(StringComparer.OrdinalIgnoreCase)
    {
        "icon", "picture", "uipicture", "editorpreview", "logo", "gunneropticsmodel", "modeloptics", "turretinfotype", "wreck", "texturesource"
    };

    private static readonly HashSet<string> GenericClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "draw", "controls", "opticsin", "sounds", "soundsext", "states", "items", "g", "texturesources", "hitpoints", "turrets", "mfd"
    };

    private static readonly HashSet<string> GenericSelections = new(StringComparer.OrdinalIgnoreCase)
    {
        "trup", "zbytek", "body", "otocvez", "otochlaven", "damage", "camo", "clan", "clan_sign", "light"
    };

    private static readonly Regex QuotedPath = new("\"([^\"\\r\\n]{3,260})\"", RegexOptions.Compiled);
    private static readonly Regex StageTexture = new("texture\\s*=\\s*\"([^\"]+)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>Stored names are raw bytes; scrambled ones are usually UTF-8 Cyrillic.</summary>
    public static string FixEncoding(string value)
    {
        if (string.IsNullOrEmpty(value) || value.All(c => c < 128))
        {
            return value;
        }
        if (value.Any(c => c > 255))
        {
            return value;
        }
        try
        {
            return StrictUtf8.GetString(Encoding.Latin1.GetBytes(value));
        }
        catch (DecoderFallbackException)
        {
            return value;
        }
    }

    public static bool IsScrambled(string fileName)
    {
        var name = StemOf(FixEncoding(fileName) ?? "");
        return name.Length > 0 && name.Any(c => c == '*' || c == '?' || c > 127 && char.IsLetter(c));
    }

    public static bool IsDecoy(string path, long size) =>
        size == 0 && (path.Trim().Length == 0 || path.Any(c => c < 32 || c == '*' || c == '?') || path.Trim('\\', '/').Length == 0);

    // Stored paths use '\\' whatever the OS, so System.IO.Path can't be trusted with them.
    private static string NameOf(string path)
    {
        var cut = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
        return cut >= 0 ? path[(cut + 1)..] : path;
    }

    private static string FolderOf(string path)
    {
        var cut = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
        return cut >= 0 ? path[..cut] : "";
    }

    private static string ExtensionOf(string path)
    {
        var name = NameOf(path);
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[dot..] : "";
    }

    private static string StemOf(string path)
    {
        var name = NameOf(path);
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }

    // Loose on purpose: unpacked copies on disk have '_' or %2a where the PBO had '*' and '?'.
    private static string Canon(string path) => Loose((path ?? "").Trim());

    public static string Loose(string path) =>
        (path ?? "").Replace('/', '\\').TrimStart('\\').ToLowerInvariant()
            .Replace("%2a", "_").Replace("%3f", "_").Replace('*', '_').Replace('?', '_');

    private static (string Stem, string Suffix) SplitSuffix(string stem)
    {
        var lower = stem.ToLowerInvariant();
        foreach (var suffix in Suffixes)
        {
            if (lower.EndsWith(suffix) && lower.Length > suffix.Length)
            {
                return (stem[..^suffix.Length], stem[^suffix.Length..]);
            }
        }
        return (stem, "");
    }

    private sealed class Node
    {
        public NameInput Input;
        public string Fixed;
        public string Key;
        public bool Scrambled;
        public string Suffix = "";
        public string Base;
        public NameSource Source;
        public string Reason = "";
        public int Priority = int.MaxValue;
        public string KnownBase => Scrambled ? Base : SplitSuffix(StemOf(Fixed)).Stem;
    }

    public static NameReport Analyse(string prefix, IReadOnlyList<NameInput> inputs, IEnumerable<KeyValuePair<string, string>> properties = null,
        Action<string> progress = null, CancellationToken cancel = default)
    {
        var report = new NameReport { Total = inputs.Count };
        foreach (var property in properties ?? Enumerable.Empty<KeyValuePair<string, string>>())
        {
            if (property.Key.Equals("obfuscated", StringComparison.OrdinalIgnoreCase))
            {
                report.ObfuscatorTag = (report.ObfuscatorTag.Length > 0 ? report.ObfuscatorTag + "\", \"" : "") + FixEncoding(property.Value);
            }
            else if (!property.Key.Equals("prefix", StringComparison.OrdinalIgnoreCase) && !property.Key.Equals("version", StringComparison.OrdinalIgnoreCase))
            {
                report.PackedWith = (report.PackedWith.Length > 0 ? report.PackedWith + ", " : "") + property.Key + " " + property.Value;
            }
        }

        var prefixKey = Canon(prefix);
        var nodes = new Dictionary<string, Node>();
        foreach (var input in inputs)
        {
            if (IsDecoy(input.Path, input.Size))
            {
                report.Decoys++;
                report.DecoyNames.Add(input.Path);
                continue;
            }
            var fixedPath = FixEncoding(input.Path).Replace('/', '\\');
            var node = new Node
            {
                Input = input,
                Fixed = fixedPath,
                Key = Canon(prefixKey.Length > 0 ? prefixKey + "\\" + fixedPath : fixedPath),
                Scrambled = IsScrambled(fixedPath)
            };
            if (node.Scrambled)
            {
                node.Suffix = SplitSuffix(StemOf(fixedPath)).Suffix;
            }
            nodes.TryAdd(node.Key, node);
        }

        var scrambled = nodes.Values.Where(n => n.Scrambled).ToList();
        if (scrambled.Count > 0)
        {
            var stems = scrambled.Select(n => SplitSuffix(StemOf(n.Fixed)).Stem).ToList();
            report.MinLength = stems.Min(s => s.Length);
            report.MaxLength = stems.Max(s => s.Length);
            report.WithWildcard = stems.Count(s => s.Contains('*') || s.Contains('?'));
        }

        Node Find(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                return null;
            }
            var key = Canon(FixEncoding(reference));
            if (nodes.TryGetValue(key, out var node))
            {
                return node;
            }
            if (prefixKey.Length > 0 && !key.StartsWith(prefixKey + "\\") && nodes.TryGetValue(prefixKey + "\\" + key, out node))
            {
                return node;
            }
            if (ExtensionOf(key).Length == 0)
            {
                foreach (var extension in new[] { ".p3d", ".paa", ".wss", ".ogg", ".wav", ".rtm", ".sqf", ".rvmat" })
                {
                    if (nodes.TryGetValue(key + extension, out node) || prefixKey.Length > 0 && nodes.TryGetValue(prefixKey + "\\" + key + extension, out node))
                    {
                        return node;
                    }
                }
            }
            return null;
        }

        bool Assign(Node node, string name, NameSource source, string reason, int priority)
        {
            if (node == null || !node.Scrambled)
            {
                return false;
            }
            name = Clean(name);
            if (name.Length == 0 || priority >= node.Priority)
            {
                return false;
            }
            node.Base = node.Suffix.Length > 0 && name.Length > node.Suffix.Length &&
                        name.EndsWith(node.Suffix, StringComparison.OrdinalIgnoreCase) ? name[..^node.Suffix.Length] : name;
            node.Source = source;
            node.Reason = reason;
            node.Priority = priority;
            return true;
        }

        // 1. Config references.
        progress?.Invoke("config");
        var configRefs = new Dictionary<Node, List<(string Name, string Where)>>();
        foreach (var node in nodes.Values.Where(n => n.Fixed.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) ||
                                                     n.Fixed.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase) ||
                                                     n.Fixed.EndsWith(".hpp", StringComparison.OrdinalIgnoreCase)))
        {
            cancel.ThrowIfCancellationRequested();
            try
            {
                using var stream = Buffer(node.Input.Open());
                if (IsRap(stream))
                {
                    var file = new ParamFile(stream);
                    WalkConfig(file.Root, new List<string>(), (value, classes, property, index, owner) =>
                    {
                        var target = Find(value);
                        if (target == null || !target.Scrambled)
                        {
                            return;
                        }
                        var name = NameFromConfig(classes, property, index, owner);
                        if (name.Length == 0)
                        {
                            return;
                        }
                        if (!configRefs.TryGetValue(target, out var list))
                        {
                            configRefs[target] = list = new();
                        }
                        list.Add((name, string.Join("/", classes) + (property.Length > 0 ? "." + property : "")));
                    });
                }
                else if (!node.Fixed.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                {
                    ScanText(stream, node, Find, (target, name) => Assign(target, name, NameSource.Script, node.Fixed, 60));
                }
            }
            catch (Exception) when (!cancel.IsCancellationRequested)
            {
            }
        }
        foreach (var (target, list) in configRefs)
        {
            var best = list.GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key.Length).First();
            Assign(target, best.Key, NameSource.Config, best.First().Where, 10);
        }

        // 2. Models: what each section draws with, and which selections it belongs to.
        progress?.Invoke("models");
        var pairs = new List<(Node Texture, Node Material, string TextureRef, string MaterialRef, List<string> Selections, Node Model, int Index)>();
        var proxies = new List<(Node Parent, string Child)>();
        foreach (var node in nodes.Values.Where(n => n.Fixed.EndsWith(".p3d", StringComparison.OrdinalIgnoreCase)))
        {
            cancel.ThrowIfCancellationRequested();
            try
            {
                using var stream = Buffer(node.Input.Open());
                if (prefixKey.Length > 0)
                {
                    var raw = Encoding.Latin1.GetString(((MemoryStream)stream).ToArray());
                    foreach (Match match in Regex.Matches(raw, "proxy:\\\\?(" + Regex.Escape(prefixKey) + "\\\\[^\\0]{1,240}?)(\\.\\d{3})?(?=\\0)", RegexOptions.IgnoreCase))
                    {
                        proxies.Add((node, match.Groups[1].Value));
                    }
                }
                var p3d = StreamHelper.Read<P3D>(stream);
                var index = 0;
                foreach (var lod in p3d.LODs.Where(l => BIS.P3D.Resolution.IsVisual(l.Resolution)))
                {
                    foreach (var (texture, material, selections) in Sections(lod))
                    {
                        pairs.Add((Find(texture), Find(material), texture, material, selections, node, index++));
                    }
                }
            }
            catch (Exception) when (!cancel.IsCancellationRequested)
            {
            }
        }

        // Models only placed inside other models (rotors, weapons, lights) are named after their host.
        foreach (var (parent, child) in proxies)
        {
            var target = Find(child);
            if (target is { Scrambled: true } && parent.KnownBase is { } host)
            {
                Assign(target, host + "_proxy", NameSource.Model, parent.Fixed, 45);
            }
        }

        // 3. Materials: the textures in their stages.
        progress?.Invoke("materials");
        var stages = new Dictionary<Node, List<Node>>();
        foreach (var node in nodes.Values.Where(n => n.Fixed.EndsWith(".rvmat", StringComparison.OrdinalIgnoreCase)))
        {
            cancel.ThrowIfCancellationRequested();
            try
            {
                using var stream = Buffer(node.Input.Open());
                stages[node] = RvmatTextures(stream).Select(Find).Where(n => n != null).Distinct().ToList();
            }
            catch (Exception) when (!cancel.IsCancellationRequested)
            {
            }
        }

        // 4. Spread known names across texture/material pairs and material stages until nothing changes.
        for (var pass = 0; pass < 6; pass++)
        {
            var changed = false;
            foreach (var pair in pairs)
            {
                var textureBase = pair.Texture?.KnownBase;
                var materialBase = pair.Material?.KnownBase;
                if (textureBase != null && pair.Material is { Scrambled: true })
                {
                    changed |= Assign(pair.Material, textureBase, NameSource.Pair, pair.Texture.Fixed, 20 + pass);
                }
                if (materialBase != null && pair.Texture is { Scrambled: true })
                {
                    changed |= Assign(pair.Texture, materialBase, NameSource.Pair, pair.Material.Fixed, 20 + pass);
                }
            }
            foreach (var (material, textures) in stages)
            {
                var materialBase = material.KnownBase;
                if (materialBase != null)
                {
                    foreach (var texture in textures.Where(t => t.Scrambled))
                    {
                        changed |= Assign(texture, materialBase, NameSource.Material, material.Fixed, 30 + pass);
                    }
                }
                else
                {
                    var known = textures.FirstOrDefault(t => !t.Scrambled || t.Base != null);
                    if (known != null)
                    {
                        changed |= Assign(material, known.KnownBase, NameSource.Material, known.Fixed, 30 + pass);
                    }
                }
            }
            if (!changed)
            {
                break;
            }
        }

        // 5. Still unnamed: the selection the faces belong to, or the model they are on.
        var parts = new Dictionary<Node, int>();
        var partTotals = pairs
            .Where(p => p.Model != null && (p.Texture is { Scrambled: true, Base: null } || p.Material is { Scrambled: true, Base: null }))
            .GroupBy(p => p.Model).ToDictionary(g => g.Key, g => g.Count());
        foreach (var pair in pairs)
        {
            var selection = pair.Selections
                .Where(s => !GenericSelections.Contains(s) && !s.StartsWith("proxy:", StringComparison.OrdinalIgnoreCase) && !s.StartsWith("-"))
                .OrderBy(s => s.Length).FirstOrDefault();
            string fallback = selection;
            if (fallback == null && pair.Model?.KnownBase is { } model && (pair.Texture is { Scrambled: true, Base: null } || pair.Material is { Scrambled: true, Base: null }))
            {
                parts[pair.Model] = System.Collections.Generic.CollectionExtensions.GetValueOrDefault(parts, pair.Model) + 1;
                fallback = partTotals[pair.Model] > 1 ? model + "_part" + parts[pair.Model].ToString("00") : model;
            }
            if (fallback == null)
            {
                continue;
            }
            var source = selection != null ? NameSource.Selection : NameSource.Model;
            var reason = pair.Model.Fixed + (selection != null ? " : " + selection : "");
            Assign(pair.Texture, fallback, source, reason, 50);
            Assign(pair.Material, fallback, source, reason, 50);
        }
        foreach (var (material, textures) in stages.Where(s => s.Key.Base != null))
        {
            foreach (var texture in textures)
            {
                Assign(texture, material.Base, NameSource.Material, material.Fixed, 55);
            }
        }

        // 6. Scripts and other text files.
        progress?.Invoke("scripts");
        foreach (var node in nodes.Values.Where(n => n.Fixed.EndsWith(".sqf", StringComparison.OrdinalIgnoreCase) ||
                                                     n.Fixed.EndsWith(".sqs", StringComparison.OrdinalIgnoreCase) ||
                                                     n.Fixed.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                                                     n.Fixed.EndsWith(".ext", StringComparison.OrdinalIgnoreCase) ||
                                                     n.Fixed.EndsWith(".fsm", StringComparison.OrdinalIgnoreCase)))
        {
            cancel.ThrowIfCancellationRequested();
            try
            {
                using var stream = Buffer(node.Input.Open());
                ScanText(stream, node, Find, (target, name) => Assign(target, name, NameSource.Script, node.Fixed, 60));
            }
            catch (Exception) when (!cancel.IsCancellationRequested)
            {
            }
        }

        // 7. Build unique names per folder.
        var taken = new HashSet<string>(nodes.Values.Where(n => !n.Scrambled).Select(n => n.Fixed.ToLowerInvariant()));
        var unnamed = 0;
        foreach (var node in scrambled.OrderBy(n => n.Priority).ThenBy(n => n.Fixed, StringComparer.Ordinal))
        {
            var folder = FolderOf(node.Fixed) ?? "";
            var extension = ExtensionOf(node.Fixed);
            if (extension.Contains('*') || extension.Contains('?'))
            {
                extension = "";
            }
            string baseName;
            if (node.Base == null)
            {
                baseName = "unnamed_" + (++unnamed).ToString("000");
                node.Source = NameSource.Fallback;
            }
            else
            {
                baseName = node.Base;
            }
            var candidate = baseName + node.Suffix + extension;
            var n = 2;
            while (!taken.Add(Combine(folder, candidate).ToLowerInvariant()))
            {
                candidate = baseName + "_" + n++ + node.Suffix + extension;
            }
            report.Items.Add(new NameSuggestion
            {
                Input = node.Input,
                Current = node.Fixed,
                Suggested = Combine(folder, candidate),
                Reason = node.Reason,
                Source = node.Source
            });
            report.BySource[node.Source] = System.Collections.Generic.CollectionExtensions.GetValueOrDefault(report.BySource, node.Source) + 1;
        }
        report.Items.Sort((a, b) => string.Compare(a.Current, b.Current, StringComparison.OrdinalIgnoreCase));
        return report;
    }

    private static string Combine(string folder, string name) => string.IsNullOrEmpty(folder) ? name : folder + "\\" + name;

    private static string Clean(string name)
    {
        var builder = new StringBuilder();
        foreach (var c in name ?? "")
        {
            builder.Append(c < 128 && (char.IsLetterOrDigit(c) || c == '_' || c == '-') ? c : '_');
        }
        var result = Regex.Replace(builder.ToString(), "_{2,}", "_").Trim('_');
        return result.Length > 64 ? result[..64].TrimEnd('_') : result;
    }

    private static Stream Buffer(Stream stream)
    {
        if (stream is MemoryStream)
        {
            return stream;
        }
        var memory = new MemoryStream();
        using (stream)
        {
            stream.CopyTo(memory);
        }
        memory.Position = 0;
        return memory;
    }

    private static bool IsRap(Stream stream)
    {
        var head = new byte[4];
        var read = stream.Read(head, 0, 4);
        stream.Position = 0;
        return read == 4 && head[0] == 0 && head[1] == (byte)'r' && head[2] == (byte)'a' && head[3] == (byte)'P';
    }

    private delegate void ConfigVisitor(string value, List<string> classes, string property, int index, ParamClass owner);

    private static void WalkConfig(ParamClass cls, List<string> classes, ConfigVisitor visit)
    {
        foreach (var entry in cls.Entries)
        {
            switch (entry)
            {
                case ParamClass child:
                    classes.Add(child.Name);
                    WalkConfig(child, classes, visit);
                    classes.RemoveAt(classes.Count - 1);
                    break;
                case ParamValue value:
                    Visit(value.Value, classes, value.Name, -1, cls, visit);
                    break;
                case ParamArray array:
                    VisitArray(array.Array, classes, array.Name, cls, visit);
                    break;
                case ParamArraySpec array:
                    VisitArray(array.Array, classes, array.Name, cls, visit);
                    break;
            }
        }
    }

    private static void VisitArray(RawArray array, List<string> classes, string property, ParamClass owner, ConfigVisitor visit)
    {
        for (var i = 0; i < array.Entries.Count; i++)
        {
            Visit(array.Entries[i], classes, property, i, owner, visit);
        }
    }

    private static void Visit(RawValue value, List<string> classes, string property, int index, ParamClass owner, ConfigVisitor visit)
    {
        switch (value?.Value)
        {
            case RawArray nested:
                for (var i = 0; i < nested.Entries.Count; i++)
                {
                    Visit(nested.Entries[i], classes, property, index < 0 ? i : index, owner, visit);
                }
                break;
            case string text when text.Length > 2 && (text.Contains('\\') || text.Contains('/') || text.Contains('.')):
                visit(text, classes, property, index, owner);
                break;
        }
    }

    private static string NameFromConfig(List<string> classes, string property, int index, ParamClass owner)
    {
        if (classes.Count == 0)
        {
            return property;
        }
        var cls = classes[^1];
        if (property.Equals("hiddenSelectionsTextures", StringComparison.OrdinalIgnoreCase) && index >= 0)
        {
            var selections = owner.Entries.OfType<ParamArray>().FirstOrDefault(a => a.Name.Equals("hiddenSelections", StringComparison.OrdinalIgnoreCase));
            if (selections != null && index < selections.Array.Entries.Count && selections.Array.Entries[index].Value is string selection)
            {
                return cls + "_" + selection;
            }
        }
        if (GenericClasses.Contains(cls) || cls.Length <= 2)
        {
            cls = classes.Count > 1 ? classes[^2] : cls;
        }
        if (GenericProps.Contains(property) || Regex.IsMatch(property, "^(sound|begin|end)\\d*$", RegexOptions.IgnoreCase))
        {
            return cls;
        }
        if (SuffixProps.Contains(property))
        {
            return cls + "_" + property;
        }
        return classes.Count <= 2 ? property : cls + "_" + property;
    }

    private static IEnumerable<(string Texture, string Material, List<string> Selections)> Sections(ILevelOfDetail lod)
    {
        switch (lod)
        {
            case BIS.P3D.ODOL.LOD odol when odol.Sections != null:
                var bySection = new Dictionary<int, List<string>>();
                foreach (var selection in odol.NamedSelections ?? Array.Empty<BIS.P3D.ODOL.NamedSelection>())
                {
                    if (!selection.IsSectional || selection.Sections == null)
                    {
                        continue;
                    }
                    foreach (var section in selection.Sections)
                    {
                        if (!bySection.TryGetValue(section, out var list))
                        {
                            bySection[section] = list = new();
                        }
                        list.Add(selection.Name);
                    }
                }
                for (var i = 0; i < odol.Sections.Length; i++)
                {
                    var section = odol.Sections[i];
                    var texture = section.TextureIndex >= 0 && section.TextureIndex < (odol.Textures?.Length ?? 0) ? odol.Textures[section.TextureIndex] : "";
                    var material = section.MaterialIndex >= 0 && section.MaterialIndex < (odol.Materials?.Length ?? 0)
                        ? odol.Materials[section.MaterialIndex]?.MaterialName ?? "" : section.Material ?? "";
                    yield return (texture, material, System.Collections.Generic.CollectionExtensions.GetValueOrDefault(bySection, i) ?? new List<string>());
                }
                break;
            case BIS.P3D.MLOD.P3DM_LOD mlod when mlod.Faces != null:
                var faceSelections = new List<string>[mlod.Faces.Length];
                foreach (var tagg in mlod.Taggs.OfType<BIS.P3D.MLOD.NamedSelectionTagg>())
                {
                    for (var i = 0; i < Math.Min(faceSelections.Length, tagg.Faces?.Length ?? 0); i++)
                    {
                        if (tagg.Faces[i] != 0)
                        {
                            (faceSelections[i] ??= new()).Add(tagg.Name);
                        }
                    }
                }
                var seen = new HashSet<string>();
                for (var i = 0; i < mlod.Faces.Length; i++)
                {
                    var face = mlod.Faces[i];
                    var key = face.Texture + "|" + face.Material;
                    if (seen.Add(key))
                    {
                        yield return (face.Texture ?? "", face.Material ?? "", faceSelections[i] ?? new List<string>());
                    }
                }
                break;
        }
    }

    private static IEnumerable<string> RvmatTextures(Stream stream)
    {
        if (IsRap(stream))
        {
            var file = new ParamFile(stream);
            var result = new List<string>();
            WalkConfig(file.Root, new List<string>(), (value, _, property, _, _) =>
            {
                if (property.Equals("texture", StringComparison.OrdinalIgnoreCase) && !value.StartsWith("#"))
                {
                    result.Add(value);
                }
            });
            return result;
        }
        var text = Encoding.Latin1.GetString(((MemoryStream)stream).ToArray());
        return StageTexture.Matches(text).Select(m => m.Groups[1].Value).Where(v => !v.StartsWith("#")).ToList();
    }

    private static void ScanText(Stream stream, Node owner, Func<string, Node> find, Action<Node, string> assign)
    {
        var text = Encoding.Latin1.GetString(((MemoryStream)stream).ToArray());
        var stem = StemOf(owner.Fixed);
        foreach (Match match in QuotedPath.Matches(text))
        {
            var target = find(match.Groups[1].Value);
            if (target is { Scrambled: true })
            {
                // "name = \"...\"" or "_sound = \"...\"" gives a better hint than the script name.
                var start = Math.Max(0, match.Index - 60);
                var before = text[start..match.Index];
                var variable = Regex.Match(before, "([A-Za-z_][A-Za-z0-9_]{2,})\\s*=\\s*$");
                assign(target, variable.Success ? stem + "_" + variable.Groups[1].Value.TrimStart('_') : stem);
            }
        }
    }
}
