using PboSpy.Interfaces;
using PboSpy.Models;
using PboSpy.Modules.Audio.Services;
using PboSpy.Modules.BinaryConfig.Utils;
using PboSpy.Services;
using PboSpy.Modules.BulkExport.Models;
using PboSpy.Modules.Pbo.Models;
using System.IO;
using System.Text;

namespace PboSpy.Modules.BulkExport.Services;

public class ExportReport
{
    public int Written { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
}

public static class BulkExportService
{
    private static readonly string[] TextConfigExtensions = { ".bin", ".rvmat", ".sqm", ".cfg" };

    public static IReadOnlyList<FileBase> Collect(IEnumerable<ITreeItem> roots, Func<ITreeItem, bool> include = null)
    {
        var result = new List<FileBase>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            Walk(root, result, seen, include);
        }
        return result;
    }

    private static void Walk(ITreeItem item, List<FileBase> sink, HashSet<string> seen, Func<ITreeItem, bool> include)
    {
        if (item is FileBase file && (include == null || include(item)) && seen.Add(file.FullPath))
        {
            sink.Add(file);
        }

        var children = item?.Children;
        if (children == null)
        {
            return;
        }
        foreach (var child in children.ToList())
        {
            Walk(child, sink, seen, include);
        }
    }

    public static IReadOnlyList<FileBase> Filter(IReadOnlyList<FileBase> files, ExportOptions options)
    {
        var extensions = options.GetExtensionFilter();
        if (extensions == null)
        {
            return files;
        }
        return files.Where(f => extensions.Contains(f.Extension)).ToList();
    }

    public static ExportReport Run(IReadOnlyList<FileBase> files, ExportOptions options,
        Action<int, string> progress, CancellationToken token)
    {
        var report = new ExportReport();
        var index = 0;

        foreach (var file in files)
        {
            if (token.IsCancellationRequested)
            {
                break;
            }
            index++;

            try
            {
                var wrote = ExportOne(file, options);
                if (wrote)
                {
                    report.Written++;
                    progress(index, file.FullPath);
                }
                else
                {
                    report.Skipped++;
                    progress(index, null);
                }
            }
            catch (Exception ex)
            {
                report.Failed++;
                progress(index, $"FAILED {file.FullPath}: {ex.Message}");
            }
        }

        return report;
    }

    private static bool ExportOne(FileBase file, ExportOptions options)
    {
        var relative = BuildRelativePath(file, options);
        var wroteSomething = false;

        var isImage = file.Extension is ".paa" or ".pac";
        var isConfig = TextConfigExtensions.Contains(file.Extension);
        var isAudio = AudioDecoder.IsAudio(file.Extension);

        if (options.ConvertImagesToPaa && PaaWriter.CanConvert(file.Extension))
        {
            var target = ResolveTarget(Path.ChangeExtension(relative, ".paa"), options);
            if (target != null)
            {
                using var source = file.GetStream();
                using var buffer = new MemoryStream();
                source.CopyTo(buffer);
                buffer.Position = 0;
                using var stream = File.Create(target);
                PaaWriter.Write(buffer, file.Name, stream, options);
                wroteSomething = true;
            }
            return wroteSomething;
        }

        if (isAudio && options.ConvertAudio && file.Extension != options.AudioExtension)
        {
            var target = ResolveTarget(Path.ChangeExtension(relative, options.AudioExtension), options);
            if (target != null)
            {
                AudioData audio;
                using (var source = file.GetStream())
                {
                    audio = AudioDecoder.Decode(source, file.Extension);
                }
                using var stream = File.Create(target);
                AudioEncoder.Encode(audio, SettingsFor(options), stream);
                wroteSomething = true;
            }
            if (!options.KeepOriginalAudio)
            {
                return wroteSomething;
            }
        }

        if (isImage && options.ConvertImages)
        {
            var target = ResolveTarget(Path.ChangeExtension(relative, options.ImageExtension), options);
            if (target != null)
            {
                var bitmap = PaaImageConverter.Decode(file, options);
                using var stream = File.Create(target);
                PaaImageConverter.Encode(bitmap, stream, options);
                wroteSomething = true;
            }
            if (!options.KeepOriginalImages)
            {
                return wroteSomething;
            }
        }

        if (isConfig && options.ConvertConfigs)
        {
            var text = file.GetDetectConfigAsText(out var wasBinary);
            var textExtension = file.Extension == ".bin" ? ".cpp" : file.Extension;
            var name = wasBinary && file.Extension != ".bin"
                ? Path.ChangeExtension(relative, null) + ".txt"
                : Path.ChangeExtension(relative, textExtension);

            var target = ResolveTarget(name, options);
            if (target != null)
            {
                File.WriteAllText(target, text, new UTF8Encoding(false));
                wroteSomething = true;
            }
            if (!options.KeepOriginalConfigs)
            {
                return wroteSomething;
            }
        }

        var rawTarget = ResolveTarget(relative, options);
        if (rawTarget != null)
        {
            using var target = File.Create(rawTarget);
            using var source = file.GetStream();
            source.CopyTo(target);
            wroteSomething = true;
        }

        return wroteSomething;
    }

    public static AudioEncodeSettings SettingsFor(ExportOptions options) => new()
    {
        Format = options.AudioFormat,
        Bitrate = options.AudioBitrate,
        OggQuality = options.OggQuality,
        SampleRate = options.AudioSampleRate,
        Mono = options.AudioMono,
        Normalize = options.NormalizeAudio,
        FfmpegPath = AppSettings.Default.FfmpegPath
    };

    private static string BuildRelativePath(FileBase file, ExportOptions options)
    {
        if (!options.PreserveStructure)
        {
            return SanitizeSegment(file.Name, options.SanitizeMode);
        }

        var full = (file is PboEntry renamed ? renamed.ExportFullPath : file.FullPath).Replace('/', '\\').TrimStart('\\');
        var stripped = false;
        if (!string.IsNullOrEmpty(options.RootPathToStrip))
        {
            var root = options.RootPathToStrip.Replace('/', '\\').Trim('\\') + "\\";
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                full = full[root.Length..];
                stripped = true;
            }
        }
        if (!stripped && file is PhysicalFile physical)
        {
            // Files on disk: keep the path below the folder that was opened, not the whole drive path.
            full = RelativeToLoadedRoot(physical);
        }
        var segments = full.Split('\\', StringSplitOptions.RemoveEmptyEntries);

        if (options.StripPboPrefix && file is PboEntry entry)
        {
            var prefixDepth = entry.PBO.Prefix
                .Replace('/', '\\')
                .Split('\\', StringSplitOptions.RemoveEmptyEntries)
                .Length;
            segments = segments.Skip(Math.Min(prefixDepth, segments.Length - 1)).ToArray();
        }

        return Path.Combine(segments.Select(s => SanitizeSegment(s, options.SanitizeMode)).ToArray());
    }

    private static string RelativeToLoadedRoot(PhysicalFile file)
    {
        ITreeItem top = file;
        while (top.Parent != null)
        {
            top = top.Parent;
        }
        if (top is PhysicalDirectory directory)
        {
            var baseDir = Path.GetDirectoryName(directory.FullPath.TrimEnd('\\', '/'));
            if (!string.IsNullOrEmpty(baseDir))
            {
                return Path.GetRelativePath(baseDir, file.FullPath);
            }
        }
        return file.Name;
    }

    /// <summary>
    /// Obfuscated PBOs use characters that Windows will not accept in a file name.
    /// </summary>
    public static string SanitizeSegment(string segment, NameSanitizeMode mode)
    {
        if (string.IsNullOrEmpty(segment))
        {
            return "_";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(segment.Length);
        foreach (var c in segment)
        {
            if (Array.IndexOf(invalid, c) < 0)
            {
                builder.Append(c);
                continue;
            }
            switch (mode)
            {
                case NameSanitizeMode.PercentEncode:
                    builder.Append('%').Append(((int)c).ToString("x2"));
                    break;
                case NameSanitizeMode.Strip:
                    break;
                default:
                    builder.Append('_');
                    break;
            }
        }

        var result = builder.ToString().TrimEnd(' ', '.');
        return string.IsNullOrEmpty(result) ? "_" : result;
    }

    /// <summary>Returns the full path to write to, or null when the file should be skipped.</summary>
    private static string ResolveTarget(string relative, ExportOptions options)
    {
        var path = Path.Combine(options.OutputDirectory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path));

        if (!File.Exists(path))
        {
            return path;
        }

        switch (options.ExistingFileAction)
        {
            case ExistingFileAction.Skip:
                return null;
            case ExistingFileAction.Rename:
                var directory = Path.GetDirectoryName(path);
                var stem = Path.GetFileNameWithoutExtension(path);
                var extension = Path.GetExtension(path);
                for (var i = 2; i < 10000; i++)
                {
                    var candidate = Path.Combine(directory, $"{stem}_{i}{extension}");
                    if (!File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                return null;
            default:
                return path;
        }
    }
}
