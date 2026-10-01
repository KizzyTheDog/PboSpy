using BIS.PAA;
using PboSpy.Interfaces;
using PboSpy.Models;
using PboSpy.Modules.BulkExport.Models;
using PboSpy.Modules.BulkExport.Services;
using PboSpy.Modules.Pbo.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;

namespace PboSpy.Modules.P3d.Scene;

internal enum TextureSource { None, Loaded, Disk, Folder, Override, Procedural }

internal sealed class TextureResult
{
    public BitmapSource Image { get; init; }
    public Color? Color { get; init; }
    public TextureSource Source { get; init; }
    public string Location { get; init; } = "";
    public FileBase File { get; init; }
    public bool Found => Image != null || Color != null;
}

/// <summary>The other maps a material uses next to its colour texture: from the rvmat stages, or files named like it.</summary>
internal sealed class LinkedMaps
{
    public TextureResult Normal { get; set; }
    public TextureResult Specular { get; set; }
    public TextureResult Ambient { get; set; }
    public string RvmatLocation { get; set; } = "";
    public bool Any => Normal?.Found == true || Specular?.Found == true || Ambient?.Found == true;

    public string Summary => string.Join(" ", new[]
    {
        Normal?.Found == true ? "NOHQ" : null,
        Specular?.Found == true ? "SMDI" : null,
        Ambient?.Found == true ? "AS" : null,
        RvmatLocation.Length > 0 ? "RVMAT" : null
    }.Where(s => s != null));
}

/// <summary>Decoded textures shared by every model preview, so reopening a model or its neighbours is instant.</summary>
internal static class TextureCache
{
    private const long Budget = 384L * 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, LinkedListNode<(string Key, BitmapSource Image)>> Map = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<(string Key, BitmapSource Image)> Order = new();
    private static long _bytes;

    public static BitmapSource Get(string key, Func<BitmapSource> load)
    {
        lock (Gate)
        {
            if (Map.TryGetValue(key, out var node))
            {
                Order.Remove(node);
                Order.AddFirst(node);
                return node.Value.Image;
            }
        }
        var image = load();
        if (image == null)
        {
            return null;
        }
        lock (Gate)
        {
            if (!Map.ContainsKey(key))
            {
                Map[key] = Order.AddFirst((key, image));
                _bytes += Size(image);
                while (_bytes > Budget && Order.Last != null && Order.Count > 1)
                {
                    var last = Order.Last;
                    Order.RemoveLast();
                    Map.Remove(last.Value.Key);
                    _bytes -= Size(last.Value.Image);
                }
            }
        }
        return image;
    }

    public static void Forget(string location)
    {
        lock (Gate)
        {
            foreach (var key in Map.Keys.Where(k => k.StartsWith(location + "|", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                _bytes -= Size(Map[key].Value.Image);
                Order.Remove(Map[key]);
                Map.Remove(key);
            }
        }
    }

    private static long Size(BitmapSource image) => (long)image.PixelWidth * image.PixelHeight * 4;
}

/// <summary>
/// Finds the files a model points at: opened PBOs first (by prefix + path), then folders around
/// the model on disk, then the folder the user picked. Procedural textures become flat colours.
/// </summary>
internal sealed class TextureResolver
{
    private static readonly string[] Variants = { ".paa", ".pac", ".png", ".tga", ".jpg", ".jpeg", ".bmp" };
    private static readonly Regex ProceduralColor = new(@"color\(([^)]*)\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex StageTexture = new(@"texture\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly string[] ColorSuffixes = { "_co", "_ca", "_mc", "_mco", "_dt" };
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PboFile, List<PboEntry>> PboEntries = new();

    private readonly Dictionary<string, FileBase> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FileBase> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _roots = new();
    private Dictionary<string, string> _folderIndex;
    private string _folder = "";

    public Dictionary<string, string> Overrides { get; } = new(StringComparer.OrdinalIgnoreCase);

    public TextureResolver(FileBase model, IEnumerable<ITreeItem> tree)
    {
        foreach (var item in tree ?? Enumerable.Empty<ITreeItem>())
        {
            Index(item);
        }

        var start = model switch
        {
            PboEntry entry => Path.GetDirectoryName(entry.PBO.PBOFilePath),
            PhysicalFile file => Path.GetDirectoryName(file.FullPath),
            _ => null
        };
        for (var dir = start; !string.IsNullOrEmpty(dir) && _roots.Count < 8; dir = Path.GetDirectoryName(dir))
        {
            _roots.Add(dir);
        }
    }

    public string Folder
    {
        get => _folder;
        set
        {
            value ??= "";
            if (!string.Equals(_folder, value, StringComparison.OrdinalIgnoreCase))
            {
                _folder = value;
                _folderIndex = null;
                _nearIndex = null;
            }
        }
    }

    // Scrambled names are UTF-8 bytes read as Latin-1: decode before lowercasing, which would corrupt the bytes.
    public static string Normalize(string path) =>
        PboSpy.Modules.Deobfuscate.Core.NameRecovery.FixEncoding((path ?? "").Trim()).Replace('/', '\\').TrimStart('\\').ToLowerInvariant();

    // Usually #(argb,8,8,3)color(...), but some tools drop the # or change the case.
    public static bool IsProcedural(string path)
    {
        var text = (path ?? "").TrimStart().TrimStart('\\', '/');
        return text.StartsWith("#") || Regex.IsMatch(text, @"^\(\s*(argb|rgb|ai)\s*,", RegexOptions.IgnoreCase);
    }

    private void Index(ITreeItem item)
    {
        switch (item)
        {
            case PboFile pbo:
                // Listing a big PBO's entries is the slow part of opening a model; do it once per PBO.
                foreach (var entry in PboEntries.GetValue(pbo, p => p.AllEntries.ToList()))
                {
                    _byPath.TryAdd(Normalize(entry.FullPath), entry);
                    _byName.TryAdd(entry.Name, entry);
                }
                break;
            case PhysicalFile file:
                _byName.TryAdd(file.Name, file);
                break;
            default:
                if (item?.Children != null)
                {
                    foreach (var child in item.Children.ToList())
                    {
                        Index(child);
                    }
                }
                break;
        }
    }

    public TextureResult Resolve(string texture, int maxSize = 1024)
    {
        if (string.IsNullOrWhiteSpace(texture))
        {
            return new TextureResult();
        }
        var key = Normalize(texture);
        // A picked texture wins, even on a placeholder or procedural slot (e.g. a decal chosen for a blank one).
        if (Overrides.TryGetValue(key, out var chosen))
        {
            var alpha = HasAlpha(key) || HasAlpha(Normalize(chosen));
            if (File.Exists(chosen))
            {
                return FromDisk(chosen, TextureSource.Override, maxSize, alpha);
            }
            if (_byPath.TryGetValue(Normalize(chosen), out var picked))
            {
                return FromFile(picked, TextureSource.Override, maxSize, alpha);
            }
        }
        if (IsInvisible(texture))
        {
            return new TextureResult { Color = Colors.Transparent, Source = TextureSource.Procedural, Location = texture };
        }
        if (IsProcedural(texture))
        {
            var color = ParseProcedural(texture);
            return color == null ? new TextureResult() : new TextureResult { Color = color, Source = TextureSource.Procedural, Location = texture };
        }
        return Locate(key, Variants, maxSize, HasAlpha(key));
    }

    /// <summary>Normal, specular and ambient maps for a face: rvmat stages first, then files named like the colour texture.</summary>
    public LinkedMaps ResolveLinked(string texture, string rvmat, int maxSize = 1024)
    {
        var result = new LinkedMaps();
        var refs = new List<string>();
        if (!string.IsNullOrWhiteSpace(rvmat) && !IsProcedural(rvmat))
        {
            var found = FromRvmatFolder(rvmat) ?? Locate(Normalize(rvmat), new[] { ".rvmat" }, 0, false, decode: false);
            if (found.File != null || File.Exists(found.Location))
            {
                result.RvmatLocation = found.Location;
                try
                {
                    var file = found.File ?? new PhysicalFile(found.Location);
                    var text = PboSpy.Modules.BinaryConfig.Utils.FileBaseExtensions.GetDetectConfigAsText(file, out _);
                    refs.AddRange(StageTexture.Matches(text).Select(m => m.Groups[1].Value).Where(r => !IsProcedural(r)));
                }
                catch (Exception)
                {
                }
            }
        }
        if (!string.IsNullOrWhiteSpace(texture) && !IsProcedural(texture))
        {
            var key = Normalize(texture);
            var stem = Path.ChangeExtension(key, null);
            var suffix = ColorSuffixes.FirstOrDefault(s => stem.EndsWith(s, StringComparison.OrdinalIgnoreCase));
            var bare = suffix != null ? stem[..^suffix.Length] : stem;
            refs.AddRange(new[] { "_nohq", "_smdi", "_as" }.Select(s => bare + s + ".paa"));
        }

        foreach (var reference in refs)
        {
            var kind = KindOf(reference);
            if (kind == null)
            {
                continue;
            }
            var current = kind switch { "n" => result.Normal, "s" => result.Specular, _ => result.Ambient };
            if (current?.Found == true)
            {
                continue;
            }
            var found = Locate(Normalize(reference), Variants, maxSize, keepAlpha: kind == "n");
            if (!found.Found)
            {
                continue;
            }
            switch (kind)
            {
                case "n": result.Normal = found; break;
                case "s": result.Specular = found; break;
                default: result.Ambient = found; break;
            }
        }
        return result;
    }

    private Dictionary<string, string> _rvmatIndex;
    private string _rvmatFolder = "";

    /// <summary>Extra folder searched first for materials (e.g. rvmats extracted by P3D Tools).</summary>
    public string RvmatFolder
    {
        get => _rvmatFolder;
        set
        {
            _rvmatFolder = value ?? "";
            _rvmatIndex = null;
        }
    }

    private TextureResult FromRvmatFolder(string rvmat)
    {
        if (!Directory.Exists(_rvmatFolder))
        {
            return null;
        }
        if (_rvmatIndex == null)
        {
            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            foreach (var file in Directory.EnumerateFiles(_rvmatFolder, "*.rvmat", options).Take(100_000))
            {
                index.TryAdd(Path.GetFileName(file), file);
            }
            _rvmatIndex = index;
        }
        var name = Normalize(rvmat).Split('\\').Last();
        return _rvmatIndex.TryGetValue(name, out var path) ? new TextureResult { Location = path, Source = TextureSource.Folder } : null;
    }

    private static string KindOf(string reference)
    {
        var stem = Path.GetFileNameWithoutExtension(reference ?? "").ToLowerInvariant();
        if (stem.EndsWith("_nohq") || stem.EndsWith("_no") || stem.EndsWith("_nofhq") || stem.EndsWith("_novhq") || stem.EndsWith("_normal"))
        {
            return "n";
        }
        if (stem.EndsWith("_smdi") || stem.EndsWith("_sm"))
        {
            return "s";
        }
        if (stem.EndsWith("_as") || stem.EndsWith("_ao"))
        {
            return "a";
        }
        return null;
    }

    private readonly HashSet<string> _gameIndexed = new(StringComparer.OrdinalIgnoreCase);

    private void IndexGame(string key)
    {
        var segments = key.Split('\\');
        if (segments.Length < 3 || segments[0] != "a3" || !_gameIndexed.Add(segments[1]))
        {
            return;
        }
        var pbo = GameData.Pbo(segments[1]);
        if (pbo != null)
        {
            foreach (var entry in PboEntries.GetValue(pbo, p => p.AllEntries.ToList()))
            {
                _byPath.TryAdd(Normalize(entry.FullPath), entry);
            }
        }
    }

    private TextureResult Locate(string key, string[] variants, int maxSize, bool keepAlpha, bool decode = true)
    {
        IndexGame(key);
        foreach (var candidate in WithVariants(key, variants))
        {
            if (_byPath.TryGetValue(candidate, out var entry))
            {
                return decode ? FromFile(entry, TextureSource.Loaded, maxSize, keepAlpha)
                    : new TextureResult { File = entry, Location = entry.FullPath, Source = TextureSource.Loaded };
            }
        }

        var segments = key.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var roots = _roots.ToList();
        if (Directory.Exists(_folder))
        {
            roots.Insert(0, _folder);
        }
        foreach (var root in roots)
        {
            for (var skip = 0; skip < segments.Length; skip++)
            {
                var tail = string.Join("\\", segments.Skip(skip));
                foreach (var relative in new[] { tail, Encoded(segments.Skip(skip)) }.Distinct())
                {
                    foreach (var candidate in WithVariants(relative, variants))
                    {
                        var full = Path.Combine(root, candidate);
                        if (File.Exists(full))
                        {
                            var source = root == _folder ? TextureSource.Folder : TextureSource.Disk;
                            return decode ? FromDisk(full, source, maxSize, keepAlpha) : new TextureResult { Location = full, Source = source };
                        }
                    }
                }
            }
        }

        // By file name alone, also as it looks after extraction (percent-encoded or with _ for * and ?).
        var name = segments.LastOrDefault() ?? "";
        var names = new[] { name, BulkExportService.SanitizeSegment(name, NameSanitizeMode.PercentEncode),
            BulkExportService.SanitizeSegment(name, NameSanitizeMode.Underscore) }.Distinct();
        foreach (var candidate in names.SelectMany(n => WithVariants(n, variants)))
        {
            if (_byName.TryGetValue(candidate, out var byName))
            {
                return decode ? FromFile(byName, TextureSource.Loaded, maxSize, keepAlpha)
                    : new TextureResult { File = byName, Location = byName.FullPath, Source = TextureSource.Loaded };
            }
            if (FolderIndex().TryGetValue(candidate, out var inFolder))
            {
                return decode ? FromDisk(inFolder, TextureSource.Folder, maxSize, keepAlpha)
                    : new TextureResult { Location = inFolder, Source = TextureSource.Folder };
            }
        }

        // Like the Blender addon: every file in the folders around the model, matched by name while
        // ignoring case, extension, separators and percent-encoding.
        var loose = Loose(name) + (variants.Contains(".rvmat") ? "|m" : "");
        if (loose.Length > 2 && NearIndex().TryGetValue(loose, out var near) &&
            variants.Contains(Path.GetExtension(near).ToLowerInvariant()))
        {
            return decode ? FromDisk(near, TextureSource.Disk, maxSize, keepAlpha) : new TextureResult { Location = near, Source = TextureSource.Disk };
        }
        return new TextureResult();
    }

    private Dictionary<string, string> _nearIndex;

    /// <summary>Every image the model could use: in the opened PBOs and in the folders around it. Path is a PBO path or a file on disk.</summary>
    public List<(string Path, FileBase File)> Images()
    {
        // Base-game files (indexed for a3\ references) would bury the model's own textures.
        var images = _byPath.Values.Where(f => Variants.Contains(f.Extension.ToLowerInvariant()) && !Normalize(f.FullPath).StartsWith(@"a3\"))
            .Select(f => (f.FullPath, f)).ToList();
        images.AddRange(NearIndex().Values.Where(p => !p.EndsWith(".rvmat", StringComparison.OrdinalIgnoreCase)).Select(p => (p, (FileBase)null)));
        return images.GroupBy(i => i.Item1, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
    }

    private static string Loose(string name)
    {
        var stem = Path.GetFileNameWithoutExtension(Uri.UnescapeDataString(name ?? ""));
        return new string(stem.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    private Dictionary<string, string> NearIndex()
    {
        if (_nearIndex != null)
        {
            return _nearIndex;
        }
        var index = new Dictionary<string, string>();
        var top = _roots.Count > 2 ? _roots[2] : _roots.LastOrDefault();
        foreach (var root in new[] { _folder, top }.Where(Directory.Exists))
        {
            try
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
                foreach (var file in Directory.EnumerateFiles(root, "*", options).Take(150_000))
                {
                    var extension = Path.GetExtension(file).ToLowerInvariant();
                    if (Variants.Contains(extension) || extension == ".rvmat")
                    {
                        var key = Loose(Path.GetFileName(file)) + (extension == ".rvmat" ? "|m" : "");
                        // .paa beats a converted .png of the same name.
                        if (!index.TryGetValue(key, out var existing) || extension == ".paa" && Path.GetExtension(existing) != ".paa")
                        {
                            index[key] = file;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
        }
        return _nearIndex = index;
    }

    private Dictionary<string, string> FolderIndex()
    {
        if (_folderIndex != null)
        {
            return _folderIndex;
        }
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(_folder))
        {
            try
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
                foreach (var file in Directory.EnumerateFiles(_folder, "*", options).Take(300_000))
                {
                    var name = Path.GetFileName(file);
                    if (Variants.Contains(Path.GetExtension(name).ToLowerInvariant()) || name.EndsWith(".rvmat", StringComparison.OrdinalIgnoreCase))
                    {
                        index.TryAdd(name, file);
                    }
                }
            }
            catch (Exception)
            {
            }
        }
        return _folderIndex = index;
    }

    private static string Encoded(IEnumerable<string> segments) =>
        string.Join("\\", segments.Select(s => BulkExportService.SanitizeSegment(s, NameSanitizeMode.PercentEncode)));

    private static IEnumerable<string> WithVariants(string path, string[] variants)
    {
        yield return path;
        var extension = Path.GetExtension(path);
        var stem = extension.Length > 0 ? path[..^extension.Length] : path;
        foreach (var variant in variants)
        {
            if (!variant.Equals(extension, StringComparison.OrdinalIgnoreCase))
            {
                yield return stem + variant;
            }
        }
    }

    // Base-game placeholders the game draws as nothing (unused hidden selections, empty clan logo slot).
    // Plain white procedural is the usual placeholder on decal / number slots that scripts fill in (blank by default).
    public static bool IsInvisible(string texture) =>
        Path.GetFileNameWithoutExtension(Normalize(texture)) is "empty" or "empty_ca" or "clear_empty" or "bis_klan"
        || IsProcedural(texture) && ParseProcedural(texture) == Colors.White;

    // Only _ca style textures are meant to be see-through; other suffixes store data in alpha.
    public static bool HasAlpha(string key)
    {
        var stem = Path.GetFileNameWithoutExtension(key);
        return stem.EndsWith("_ca") || stem.Contains("_ca_") || stem.EndsWith("_ti_ca");
    }

    private static TextureResult FromFile(FileBase file, TextureSource source, int maxSize, bool keepAlpha)
    {
        try
        {
            var image = TextureCache.Get($"{file.FullPath}|{maxSize}|{keepAlpha}", () =>
            {
                using var stream = file.GetStream();
                return Decode(stream, file.Extension, maxSize, keepAlpha);
            });
            return new TextureResult { Image = image, Source = source, Location = file.FullPath, File = file };
        }
        catch (Exception)
        {
            return new TextureResult();
        }
    }

    private static TextureResult FromDisk(string path, TextureSource source, int maxSize, bool keepAlpha)
    {
        try
        {
            var stamp = File.GetLastWriteTimeUtc(path).Ticks;
            var image = TextureCache.Get($"{path}|{maxSize}|{keepAlpha}|{stamp}", () =>
            {
                using var stream = File.OpenRead(path);
                return Decode(stream, Path.GetExtension(path), maxSize, keepAlpha);
            });
            return new TextureResult { Image = image, Source = source, Location = path };
        }
        catch (Exception)
        {
            return new TextureResult();
        }
    }

    public static BitmapSource Decode(Stream stream, string extension, int maxSize, bool keepAlpha)
    {
        extension = (extension ?? "").ToLowerInvariant();
        int width, height;
        byte[] bgra;
        if (extension is ".paa" or ".pac")
        {
            var seekable = stream.CanSeek ? stream : Copy(stream);
            var paa = new PAA(seekable, extension == ".pac");
            var mipmap = paa[0];
            foreach (var candidate in paa.Mipmaps)
            {
                if (mipmap.Width > maxSize || mipmap.Height > maxSize)
                {
                    if (candidate.Width < mipmap.Width && candidate.Width > 0)
                    {
                        mipmap = candidate;
                    }
                }
            }
            bgra = PAA.GetARGB32PixelData(paa, seekable, mipmap);
            width = mipmap.Width;
            height = mipmap.Height;
        }
        else
        {
            using var image = SixLabors.ImageSharp.Image.Load<Bgra32>(stream);
            if (image.Width > maxSize || image.Height > maxSize)
            {
                var scale = Math.Min((double)maxSize / image.Width, (double)maxSize / image.Height);
                image.Mutate(x => x.Resize(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale))));
            }
            width = image.Width;
            height = image.Height;
            bgra = new byte[width * height * 4];
            image.CopyPixelDataTo(bgra);
        }

        if (!keepAlpha)
        {
            for (var i = 3; i < bgra.Length; i += 4)
            {
                bgra[i] = 255;
            }
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, bgra, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static MemoryStream Copy(Stream stream)
    {
        var memory = new MemoryStream();
        stream.CopyTo(memory);
        memory.Position = 0;
        return memory;
    }

    // #(argb,8,8,3)color(r,g,b,a,tag) - channels are 0..1.
    private static Color? ParseProcedural(string texture)
    {
        var match = ProceduralColor.Match(texture);
        if (!match.Success)
        {
            // Render targets (in-game cameras, screens) and other generators: a neutral stand-in.
            return texture.Contains("r2t", StringComparison.OrdinalIgnoreCase)
                ? Color.FromRgb(0x18, 0x1C, 0x22) : Color.FromRgb(0x80, 0x80, 0x80);
        }
        var parts = match.Groups[1].Value.Split(',');
        float Channel(int i, float fallback) =>
            i < parts.Length && float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                ? Math.Clamp(v, 0f, 1f) : fallback;
        var r = Channel(0, 0.5f);
        var g = Channel(1, r);
        var b = Channel(2, r);
        var a = Channel(3, 1f);
        return Color.FromArgb((byte)(Math.Max(a, 0.15f) * 255), (byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }
}

/// <summary>Files from the installed Arma 3 (found through the registry), read from the PBO headers only.</summary>
internal static class GameData
{
    private static readonly Dictionary<string, PboFile> Pbos = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<string> Folder = new(() =>
        Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Bohemia Interactive\ArmA 3", "main", null) as string);

    // a3\data_f\... lives in data_f.pbo; DLC addons sit in their own folders.
    public static PboFile Pbo(string addon)
    {
        lock (Pbos)
        {
            if (!Pbos.TryGetValue(addon, out var pbo))
            {
                try
                {
                    var path = Directory.Exists(Folder.Value)
                        ? Directory.EnumerateFiles(Folder.Value, addon + ".pbo", SearchOption.AllDirectories).FirstOrDefault() : null;
                    pbo = path == null ? null : new PboFile(new BIS.PBO.PBO(path, false));
                }
                catch (Exception)
                {
                    pbo = null;
                }
                Pbos[addon] = pbo;
            }
            return pbo;
        }
    }

    public static PboEntry Find(string path)
    {
        var key = TextureResolver.Normalize(path);
        var segments = key.Split('\\');
        return segments.Length < 3 ? null : Pbo(segments[1])?.AllEntries.FirstOrDefault(e => TextureResolver.Normalize(e.FullPath) == key);
    }
}
