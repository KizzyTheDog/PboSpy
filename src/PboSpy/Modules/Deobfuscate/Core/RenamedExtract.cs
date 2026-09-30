using PboSpy.Localization;
using PboSpy.Modules.BinaryConfig.Utils;
using PboSpy.Modules.BulkExport.Models;
using PboSpy.Modules.BulkExport.Services;
using PboSpy.Modules.P3dTools.Core;
using PboSpy.Modules.Pbo.Models;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PboSpy.Modules.Deobfuscate.Core;

public sealed record RenameItem(PboSpy.Models.FileBase File, string Stored, string Renamed);

public sealed class RenamedExtractReport
{
    public int Written { get; set; }
    public int Rewritten { get; set; }
    public int References { get; set; }
    public List<string> Problems { get; } = new();
}

/// <summary>
/// Extracts a PBO using the renamed paths and fixes the references to them inside configs,
/// materials, scripts and models, so the unpacked addon still points at its own files.
/// </summary>
public static class RenamedExtract
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cpp", ".hpp", ".h", ".inc", ".sqf", ".sqs", ".fsm", ".ext", ".xml", ".cfg", ".bikb", ".txt", ".sqm", ".rvmat", ".bisurf", ".html", ".csv"
    };

    private static readonly string[] OptionalExtensions = { ".p3d", ".wss", ".ogg", ".wav", ".rtm", ".sqf", ".paa" };

    public static RenamedExtractReport Run(PboFile pbo, string outputDirectory, Action<int, int, string> progress, CancellationToken cancel)
    {
        var prefix = (pbo.PBO.Prefix ?? "").Replace('/', '\\').Trim('\\');
        var items = pbo.AllEntries.Select(e => new RenameItem(e, e.StoredPath, e.RenamedPath)).ToList();
        var root = Path.Combine(new[] { outputDirectory }.Concat(prefix.Split('\\', StringSplitOptions.RemoveEmptyEntries)).ToArray());
        return Run(items, prefix, root, outputDirectory, progress, cancel);
    }

    /// <summary>Writes every item under <paramref name="root"/> with its new name, fixing references on the way.</summary>
    public static RenamedExtractReport Run(IReadOnlyList<RenameItem> items, string prefix, string root, string logFolder,
        Action<int, int, string> progress, CancellationToken cancel)
    {
        var report = new RenamedExtractReport();
        prefix = (prefix ?? "").Replace('/', '\\').Trim('\\');

        // Old full path -> new full path, both without the leading backslash. Keys are "loose" so a
        // file saved as a_b.paa on disk still matches the a*b.paa its config points at.
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items.Where(i => i.Renamed != null))
        {
            map[Loose(Join(prefix, item.Stored))] = Join(prefix, item.Renamed);
        }
        var byStem = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items.Where(i => i.Renamed != null))
        {
            var from = Join(prefix, item.Stored);
            var to = Join(prefix, item.Renamed);
            var extension = Path.GetExtension(from);
            if (OptionalExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) &&
                string.Equals(extension, Path.GetExtension(to), StringComparison.OrdinalIgnoreCase))
            {
                byStem.TryAdd(Loose(from[..^extension.Length]), to[..^extension.Length]);
            }
        }

        var index = 0;
        foreach (var item in items)
        {
            cancel.ThrowIfCancellationRequested();
            index++;
            var relative = item.Renamed ?? item.Stored;
            var target = Path.Combine(new[] { root }
                .Concat(relative.Split('\\', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => BulkExportService.SanitizeSegment(s, NameSanitizeMode.Underscore)))
                .ToArray());
            progress?.Invoke(index, items.Count, relative);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                var file = item.File;
                var extension = file.Extension;
                if (extension is ".bin" && IsRap(file))
                {
                    var text = file.GetDetectConfigAsText(out _);
                    target = Path.ChangeExtension(target, ".cpp");
                    File.WriteAllText(target, Rewrite(text, prefix, map, byStem, report), new UTF8Encoding(false));
                }
                else if (TextExtensions.Contains(extension))
                {
                    string text;
                    if (IsRap(file))
                    {
                        text = file.GetDetectConfigAsText(out _);
                    }
                    else
                    {
                        using var stream = file.GetStream();
                        using var reader = new StreamReader(stream, Encoding.UTF8, true);
                        text = reader.ReadToEnd();
                    }
                    File.WriteAllText(target, Rewrite(text, prefix, map, byStem, report), new UTF8Encoding(false));
                }
                else if (extension == ".p3d" && map.Count > 0)
                {
                    WriteModel(file, relative, target, prefix, map, byStem, report);
                }
                else
                {
                    using var source = file.GetStream();
                    using var output = File.Create(target);
                    source.CopyTo(output);
                }
                report.Written++;
            }
            catch (Exception ex)
            {
                report.Problems.Add(relative + ": " + ex.Message);
            }
        }

        var log = new StringBuilder("old;new\n");
        foreach (var item in items.Where(i => i.Renamed != null).OrderBy(i => i.Stored, StringComparer.OrdinalIgnoreCase))
        {
            log.Append(Join(prefix, item.Stored)).Append(';').Append(Join(prefix, item.Renamed)).Append('\n');
        }
        Directory.CreateDirectory(logFolder);
        File.WriteAllText(Path.Combine(logFolder, "renamed_files.csv"), log.ToString(), new UTF8Encoding(true));
        return report;
    }

    /// <summary>Wildcards the obfuscator used can't exist on disk; unpackers turn them into '_' or %2a.</summary>
    public static string Loose(string path) => NameRecovery.Loose(path);

    private static string Join(string prefix, string path) =>
        string.IsNullOrEmpty(prefix) ? path.Trim('\\') : prefix + "\\" + path.Trim('\\');

    private static bool IsRap(PboSpy.Models.FileBase file)
    {
        using var stream = file.GetStream();
        var head = new byte[4];
        return stream.Read(head, 0, 4) == 4 && head[0] == 0 && head[1] == 'r' && head[2] == 'a' && head[3] == 'P';
    }

    private static bool IsPathChar(char c) => !(c is '"' or '\'' or ',' or ';' or ')' or '(' or '}' or '{' or ']' or '[' or '<' or '>' or '=' || char.IsWhiteSpace(c));

    /// <summary>Replaces every "prefix\old" reference, with or without leading slash or extension.</summary>
    private static string Rewrite(string text, string prefix, Dictionary<string, string> map, Dictionary<string, string> byStem, RenamedExtractReport report)
    {
        if (map.Count == 0 || string.IsNullOrEmpty(prefix))
        {
            return text;
        }
        var builder = new StringBuilder(text.Length + 256);
        var changed = 0;
        var position = 0;
        while (true)
        {
            var hit = text.IndexOf(prefix, position, StringComparison.OrdinalIgnoreCase);
            if (hit < 0)
            {
                break;
            }
            var end = hit + prefix.Length;
            if (end >= text.Length || text[end] is not ('\\' or '/') || hit > 0 && char.IsLetterOrDigit(text[hit - 1]))
            {
                builder.Append(text, position, end - position);
                position = end;
                continue;
            }
            while (end < text.Length && IsPathChar(text[end]))
            {
                end++;
            }
            var found = text[hit..end];
            var key = Loose(found);
            string replacement = null;
            if (map.TryGetValue(key, out var full))
            {
                replacement = full;
            }
            else if (byStem.TryGetValue(key, out var stem))
            {
                replacement = stem;
            }
            builder.Append(text, position, hit - position);
            if (replacement != null)
            {
                builder.Append(found.Contains('/') ? replacement.Replace('\\', '/') : replacement);
                changed++;
            }
            else
            {
                builder.Append(found);
            }
            position = end;
        }
        builder.Append(text, position, text.Length - position);
        if (changed > 0)
        {
            report.Rewritten++;
            report.References += changed;
        }
        return builder.ToString();
    }

    /// <summary>
    /// Every stored string that points into this PBO: textures, materials, and proxies, which are
    /// written without extension and with a ".001" style index (plus "proxy:" in selection names).
    /// </summary>
    private static List<(string From, string To)> ModelPairs(byte[] data, string prefix, Dictionary<string, string> map, Dictionary<string, string> byStem)
    {
        var pairs = new List<(string From, string To)>();
        if (string.IsNullOrEmpty(prefix))
        {
            return pairs;
        }
        var text = Encoding.Latin1.GetString(data);
        var pattern = new Regex("(?<![\\x20-\\x7E])(proxy:)?\\\\?" + Regex.Escape(prefix) + "\\\\[^\\0]{1,240}(?=\\0)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in pattern.Matches(text))
        {
            var stored = NameRecovery.FixEncoding(match.Value);
            if (!seen.Add(stored))
            {
                continue;
            }
            var head = match.Groups[1].Success ? match.Groups[1].Value : "";
            var body = stored[head.Length..];
            var slash = body.StartsWith("\\") ? "\\" : "";
            var key = body.TrimStart('\\');
            var index = "";
            var proxyIndex = Regex.Match(key, "\\.\\d{3}$");
            if (proxyIndex.Success && !map.ContainsKey(Loose(key)))
            {
                index = proxyIndex.Value;
                key = key[..^index.Length];
            }
            if (map.TryGetValue(Loose(key), out var to) || byStem.TryGetValue(Loose(key), out to))
            {
                pairs.Add((stored, head + slash + to + index));
            }
        }
        return pairs;
    }

    private static void WriteModel(PboSpy.Models.FileBase entry, string relative, string target, string prefix, Dictionary<string, string> map, Dictionary<string, string> byStem, RenamedExtractReport report)
    {
        var temp = Path.Combine(Path.GetTempPath(), "PboSpy", "names", Guid.NewGuid().ToString("N") + ".p3d");
        Directory.CreateDirectory(Path.GetDirectoryName(temp));
        try
        {
            using (var source = entry.GetStream())
            using (var output = File.Create(temp))
            {
                source.CopyTo(output);
            }

            var pairs = ModelPairs(File.ReadAllBytes(temp), prefix, map, byStem);
            if (pairs.Count == 0)
            {
                File.Copy(temp, target, true);
                return;
            }
            if (!Converter.IsAvailable && Converter.DetectFormat(temp) == P3DFormat.Odol)
            {
                File.Copy(temp, target, true);
                report.Problems.Add(relative + ": " + Loc.T("Names.NeedBisDll"));
                return;
            }
            P3DRepath.Apply(temp, target, pairs);
            report.Rewritten++;
            report.References += pairs.Count;
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception)
            {
            }
        }
    }
}
