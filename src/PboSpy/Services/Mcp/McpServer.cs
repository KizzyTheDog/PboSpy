using BIS.PBO;
using PboSpy.Models;
using PboSpy.Modules.BinaryConfig.Utils;
using PboSpy.Modules.BulkExport.Models;
using PboSpy.Modules.BulkExport.Services;
using PboSpy.Modules.Deobfuscate.Core;
using PboSpy.Modules.P3dTools.Core;
using PboSpy.Modules.Pbo.Models;
using PboSpy.Modules.Pbr.Core;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PboSpy.Services.Mcp;

/// <summary>
/// "PboSpy.exe --mcp": a Model Context Protocol server over stdin/stdout, so an assistant such as
/// Claude can list, read, extract and convert Arma files with the same code the app uses.
/// </summary>
public static class McpServer
{
    private const string DefaultProtocol = "2025-06-18";

    private sealed record Tool(string Name, string Description, JsonObject Schema, Func<JsonObject, CancellationToken, string> Run);

    private static readonly List<Tool> Tools = new()
    {
        new("pbo_list", "List the files inside a PBO with their sizes, plus the PBO prefix and header properties.",
            Schema(("pbo", "string", "Full path of the .pbo file", true), ("filter", "string", "Optional: extensions (\".paa,.p3d\"), a wildcard (\"data\\\\*_co.paa\") or text to match", false)),
            PboList),
        new("pbo_read", "Read one file inside a PBO as text. Binarised configs and materials are converted to readable text.",
            Schema(("pbo", "string", "Full path of the .pbo file", true), ("entry", "string", "Path of the file inside the PBO, as pbo_list shows it", true)),
            PboRead),
        new("pbo_extract", "Extract a PBO (or the files matching a filter) to a folder, optionally converting textures to PNG, configs to text and audio to WAV.",
            Schema(("pbo", "string", "Full path of the .pbo file", true), ("output", "string", "Folder to write to", true),
                   ("filter", "string", "Optional filter, same as pbo_list", false), ("convert", "boolean", "Convert textures, configs and audio (default false = raw files)", false)),
            PboExtract),
        new("convert_files", "Convert files or whole folders on disk: PAA textures to PNG/JPG/BMP/TIFF, images to PAA, binarised configs to text, WSS/OGG audio to WAV/OGG/MP3/FLAC.",
            Schema(("inputs", "array", "Files or folders to convert", true), ("output", "string", "Folder to write to", true),
                   ("image_format", "string", "png (default), jpg, bmp, tiff or paa", false), ("audio_format", "string", "wav (default), ogg, mp3, flac, m4a, opus or wss", false)),
            ConvertFiles),
        new("p3d_info", "Describe a P3D model: format, LODs, face counts, textures and materials per LOD. Accepts a file on disk or an entry inside a PBO.",
            Schema(("path", "string", "Full path of a .p3d file, or of a .pbo when entry is given", true), ("entry", "string", "Optional: path of the model inside the PBO", false)),
            P3dInfo),
        new("p3d_debinarize", "Convert a binarised (ODOL) model to an editable MLOD P3D. Needs BisDll from P3D Debinarizer.",
            Schema(("input", "string", "Full path of the .p3d", true), ("output", "string", "Folder to write to", true)),
            P3dDebinarize),
        new("p3d_extract_rvmats", "Write the materials embedded in a binarised model as .rvmat files.",
            Schema(("input", "string", "Full path of the .p3d", true), ("output", "string", "Folder to write to", true)),
            P3dRvmats),
        new("p3d_export", "Export a model (first visual LOD, proxy triangles left out) as a textured GLB with PBR materials, or OBJ + MTL + PNGs. Textures are found in the folders around the model and the texture folder set in PboSpy.",
            Schema(("input", "string", "Full path of the .p3d, or of the PBO holding it", true), ("entry", "string", "Path of the model inside the PBO", false), ("output", "string", "Full path of the .glb, .gltf, .fbx or .obj to write", true),
                ("max_texture", "integer", "Largest texture side in pixels (default 2048)", false), ("split_at", "integer", "Cut parts with more triangles than this, e.g. 20000 for Roblox (default off)", false),
                ("rvmat_folder", "string", "Extra folder to look for .rvmat files in", false),
                ("decimate", "number", "Share of triangles to keep, 0.05 to 1 (default 1 = no decimation)", false),
                ("by_material", "boolean", "One object per material (default true); false joins them", false)),
            P3dExport),
        new("p3d_extract_model_cfg", "Rebuild a model.cfg (skeleton and animations) from a binarised model.",
            Schema(("input", "string", "Full path of the .p3d", true), ("output", "string", "Folder to write to", true)),
            P3dModelCfg),
        new("pbr_make", "Build PBR maps (normal, metallic, roughness, AO, base colour) from Arma textures (_co/_nohq/_smdi/_as) in a folder.",
            Schema(("input", "string", "Folder with PAA/PNG textures and optionally rvmats", true), ("output", "string", "Folder to write to", true),
                   ("recursive", "boolean", "Include sub folders", false), ("flip_green", "boolean", "Flip the normal map green channel (DirectX to OpenGL)", false)),
            PbrMake),
        new("recover_names", "Work out readable names for the scrambled files in an obfuscated PBO. With output set, extracts the PBO with those names and fixes the references in configs, materials, scripts and models.",
            Schema(("pbo", "string", "Full path of the .pbo file", true), ("output", "string", "Optional folder to extract to", false)),
            RecoverNames),
        new("config_to_text", "Convert a binarised config (config.bin, .rvmat, .bisurf) on disk to readable text.",
            Schema(("path", "string", "Full path of the file", true)),
            ConfigToText)
    };

    public static int Run()
    {
        var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        var writeLock = new object();

        void Send(JsonObject message)
        {
            lock (writeLock)
            {
                output.WriteLine(message.ToJsonString());
            }
        }

        string line;
        while ((line = input.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            JsonObject request;
            try
            {
                request = JsonNode.Parse(line) as JsonObject;
            }
            catch (JsonException)
            {
                Send(Error(null, -32700, "Parse error"));
                continue;
            }
            if (request == null)
            {
                continue;
            }
            var id = request["id"]?.DeepClone();
            var method = request["method"]?.GetValue<string>() ?? "";
            var parameters = request["params"] as JsonObject ?? new JsonObject();
            if (id == null)
            {
                continue;
            }
            try
            {
                switch (method)
                {
                    case "initialize":
                        var protocol = parameters["protocolVersion"]?.GetValue<string>() ?? DefaultProtocol;
                        Send(Result(id, new JsonObject
                        {
                            ["protocolVersion"] = protocol,
                            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                            ["serverInfo"] = new JsonObject { ["name"] = "pbospy", ["version"] = typeof(McpServer).Assembly.GetName().Version?.ToString(3) ?? "2.0.0" },
                            ["instructions"] = "Arma 3 file tools from PboSpy. All paths are full Windows paths on this computer."
                        }));
                        break;
                    case "ping":
                        Send(Result(id, new JsonObject()));
                        break;
                    case "tools/list":
                        var list = new JsonArray();
                        foreach (var tool in Tools)
                        {
                            list.Add(new JsonObject { ["name"] = tool.Name, ["description"] = tool.Description, ["inputSchema"] = tool.Schema.DeepClone() });
                        }
                        Send(Result(id, new JsonObject { ["tools"] = list }));
                        break;
                    case "tools/call":
                        var name = parameters["name"]?.GetValue<string>();
                        var arguments = parameters["arguments"] as JsonObject ?? new JsonObject();
                        var match = Tools.FirstOrDefault(t => t.Name == name);
                        if (match == null)
                        {
                            Send(Error(id, -32602, "Unknown tool: " + name));
                            break;
                        }
                        string text;
                        var failed = false;
                        try
                        {
                            text = match.Run(arguments, CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            text = ex.Message;
                            failed = true;
                        }
                        Send(Result(id, new JsonObject
                        {
                            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
                            ["isError"] = failed
                        }));
                        break;
                    default:
                        Send(Error(id, -32601, "Method not found: " + method));
                        break;
                }
            }
            catch (Exception ex)
            {
                Send(Error(id, -32603, ex.Message));
            }
        }
        return 0;
    }

    private static JsonObject Result(JsonNode id, JsonObject result) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };

    private static JsonObject Error(JsonNode id, int code, string message) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };

    private static JsonObject Schema(params (string Name, string Type, string Description, bool Required)[] properties)
    {
        var props = new JsonObject();
        var required = new JsonArray();
        foreach (var (name, type, description, isRequired) in properties)
        {
            var property = new JsonObject { ["type"] = type, ["description"] = description };
            if (type == "array")
            {
                property["items"] = new JsonObject { ["type"] = "string" };
            }
            props[name] = property;
            if (isRequired)
            {
                required.Add(name);
            }
        }
        return new JsonObject { ["type"] = "object", ["properties"] = props, ["required"] = required };
    }

    private static string Text(JsonObject args, string name, bool required = true)
    {
        var value = args[name]?.GetValue<string>();
        if (required && string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"'{name}' is required.");
        }
        return value?.Trim().Trim('"');
    }

    private static bool Flag(JsonObject args, string name) => args[name] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

    private static string ExistingFile(JsonObject args, string name)
    {
        var path = Text(args, name);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("File not found: " + path);
        }
        return path;
    }

    private static PboFile OpenPbo(JsonObject args) => new(new PBO(ExistingFile(args, "pbo"), false));

    private static Func<PboEntry, bool> EntryFilter(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return _ => true;
        }
        var tests = filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select<string, Func<PboEntry, bool>>(token =>
        {
            if (token.StartsWith('.') && !token.Contains('*'))
            {
                return e => e.Extension.Equals(token, StringComparison.OrdinalIgnoreCase);
            }
            if (token.Contains('*') || token.Contains('?'))
            {
                var regex = new Regex("^" + Regex.Escape(token.Replace('/', '\\')).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase);
                return e => regex.IsMatch(e.StoredPath) || regex.IsMatch(e.Name);
            }
            return e => e.StoredPath.Contains(token.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase);
        }).ToList();
        return e => tests.Any(t => t(e));
    }

    private static PboEntry FindEntry(PboFile pbo, string entry)
    {
        var wanted = entry.Replace('/', '\\').Trim('\\');
        if (!string.IsNullOrEmpty(pbo.PBO.Prefix) && wanted.StartsWith(pbo.PBO.Prefix + "\\", StringComparison.OrdinalIgnoreCase))
        {
            wanted = wanted[(pbo.PBO.Prefix.Length + 1)..];
        }
        return pbo.AllEntries.FirstOrDefault(e => e.StoredPath.Equals(wanted, StringComparison.OrdinalIgnoreCase))
               ?? pbo.AllEntries.FirstOrDefault(e => e.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase))
               ?? throw new FileNotFoundException($"'{entry}' is not in {pbo.Name}. Use pbo_list to see the paths.");
    }

    private static string PboList(JsonObject args, CancellationToken cancel)
    {
        var pbo = OpenPbo(args);
        try
        {
            var filter = EntryFilter(Text(args, "filter", false));
            var entries = pbo.AllEntries.Where(filter).OrderBy(e => e.StoredPath, StringComparer.OrdinalIgnoreCase).ToList();
            var text = new StringBuilder();
            text.AppendLine($"{pbo.Name}  prefix: {pbo.PBO.Prefix}");
            foreach (var property in pbo.PBO.PropertiesPairs)
            {
                text.AppendLine($"  {property.Key} = {NameRecovery.FixEncoding(property.Value)}");
            }
            text.AppendLine($"{entries.Count} files{(entries.Count > 3000 ? " (first 3000 shown)" : "")}:");
            foreach (var entry in entries.Take(3000))
            {
                text.AppendLine($"{entry.StoredPath}\t{entry.DataSize}");
            }
            return text.ToString();
        }
        finally
        {
            pbo.PBO.Dispose();
        }
    }

    private static string PboRead(JsonObject args, CancellationToken cancel)
    {
        var pbo = OpenPbo(args);
        try
        {
            var entry = FindEntry(pbo, Text(args, "entry"));
            var text = entry.GetDetectConfigAsText(out var wasBinary);
            if (!wasBinary && text.Take(4096).Count(c => c == '\0' || char.IsControl(c) && c is not ('\r' or '\n' or '\t')) > 8)
            {
                return $"{entry.StoredPath} is a binary file ({entry.DataSize} bytes). Use pbo_extract to write it out.";
            }
            return text.Length > 400_000 ? text[..400_000] + "\n… (cut at 400000 characters)" : text;
        }
        finally
        {
            pbo.PBO.Dispose();
        }
    }

    private static string PboExtract(JsonObject args, CancellationToken cancel)
    {
        var pbo = OpenPbo(args);
        try
        {
            var output = Text(args, "output");
            var convert = Flag(args, "convert");
            var files = pbo.AllEntries.Where(EntryFilter(Text(args, "filter", false))).Cast<FileBase>().ToList();
            var options = new ExportOptions
            {
                OutputDirectory = output,
                Preset = ContentPreset.Everything,
                PreserveStructure = true,
                ConvertImages = convert,
                ConvertConfigs = convert,
                ConvertAudio = convert,
                ExistingFileAction = ExistingFileAction.Overwrite
            };
            var report = BulkExportService.Run(BulkExportService.Filter(files, options), options, (_, _) => { }, cancel);
            return $"{report.Written} files written to {output}, {report.Skipped} skipped, {report.Failed} failed.";
        }
        finally
        {
            pbo.PBO.Dispose();
        }
    }

    private static string ConvertFiles(JsonObject args, CancellationToken cancel)
    {
        var inputs = (args["inputs"] as JsonArray)?.Select(n => n?.GetValue<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
                     ?? throw new ArgumentException("'inputs' is required.");
        var files = new List<FileBase>();
        foreach (var input in inputs)
        {
            if (Directory.Exists(input))
            {
                var root = new PhysicalDirectory(Path.GetFileName(input.TrimEnd('\\')), input);
                foreach (var file in Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories))
                {
                    var item = new PhysicalFile(file, root);
                    root.Children.Add(item);
                    files.Add(item);
                }
            }
            else if (File.Exists(input))
            {
                files.Add(new PhysicalFile(input));
            }
            else
            {
                throw new FileNotFoundException("Not found: " + input);
            }
        }

        var imageFormat = (Text(args, "image_format", false) ?? "png").ToLowerInvariant();
        var audioFormat = (Text(args, "audio_format", false) ?? "wav").ToLowerInvariant();
        var options = new ExportOptions
        {
            OutputDirectory = Text(args, "output"),
            Preset = ContentPreset.Everything,
            PreserveStructure = true,
            ConvertImages = imageFormat != "paa",
            ConvertImagesToPaa = imageFormat == "paa",
            ConvertConfigs = true,
            ConvertAudio = true,
            ExistingFileAction = ExistingFileAction.Overwrite
        };
        if (imageFormat != "paa")
        {
            options.ImageFormat = imageFormat switch
            {
                "jpg" or "jpeg" => ImageOutputFormat.Jpeg,
                "bmp" => ImageOutputFormat.Bmp,
                "tif" or "tiff" => ImageOutputFormat.Tiff,
                _ => ImageOutputFormat.Png
            };
        }
        options.AudioFormat = audioFormat switch
        {
            "ogg" => AudioOutputFormat.Ogg,
            "mp3" => AudioOutputFormat.Mp3,
            "flac" => AudioOutputFormat.Flac,
            "m4a" => AudioOutputFormat.M4a,
            "opus" => AudioOutputFormat.Opus,
            "wss" => AudioOutputFormat.Wss,
            _ => AudioOutputFormat.Wav
        };
        var report = BulkExportService.Run(files, options, (_, _) => { }, cancel);
        return $"{report.Written} files written to {options.OutputDirectory}, {report.Skipped} skipped, {report.Failed} failed.";
    }

    private static string P3dInfo(JsonObject args, CancellationToken cancel)
    {
        var path = ExistingFile(args, "path");
        var entryName = Text(args, "entry", false);
        Stream stream;
        PboFile pbo = null;
        if (!string.IsNullOrEmpty(entryName))
        {
            pbo = new PboFile(new PBO(path, false));
            stream = FindEntry(pbo, entryName).GetStream();
        }
        else
        {
            stream = File.OpenRead(path);
        }
        try
        {
            var p3d = BIS.Core.Streams.StreamHelper.Read<BIS.P3D.P3D>(stream);
            var text = new StringBuilder();
            text.AppendLine($"{(p3d.IsEditable ? "MLOD (editable)" : "ODOL (binarised)")} version {p3d.Version}");
            text.AppendLine($"Bounding box {p3d.ModelInfo.BboxMin} .. {p3d.ModelInfo.BboxMax}");
            foreach (var lod in p3d.LODs)
            {
                text.AppendLine($"LOD {BIS.P3D.Resolution.GetLODName(lod.Resolution)}: {lod.FaceCount} faces, {lod.VertexCount} vertices");
                foreach (var texture in lod.GetTextures().Distinct())
                {
                    text.AppendLine("  texture  " + NameRecovery.FixEncoding(texture));
                }
                foreach (var material in lod.GetMaterials().Distinct())
                {
                    text.AppendLine("  material " + NameRecovery.FixEncoding(material));
                }
            }
            return text.ToString();
        }
        finally
        {
            stream.Dispose();
            pbo?.PBO.Dispose();
        }
    }

    private static void NeedBisDll()
    {
        if (!Converter.IsAvailable)
        {
            throw new InvalidOperationException("BisDll.dll is not available: " + Converter.LoadError);
        }
    }

    private static string P3dDebinarize(JsonObject args, CancellationToken cancel)
    {
        NeedBisDll();
        var log = new StringBuilder();
        var result = Converter.Convert(ExistingFile(args, "input"), Text(args, "output"), m => log.AppendLine(m));
        return (result.Success ? "Written " + result.OutputPath : "Failed: " + result.Message) + "\n" + log;
    }

    private static string P3dRvmats(JsonObject args, CancellationToken cancel)
    {
        NeedBisDll();
        var log = new StringBuilder();
        var count = OdolExtract.ExtractRvmats(ExistingFile(args, "input"), Text(args, "output"), m => log.AppendLine(m));
        return $"{count} rvmat files written.\n{log}";
    }

    private static string P3dExport(JsonObject args, CancellationToken cancel)
    {
        var input = ExistingFile(args, "input");
        var output = Text(args, "output");
        var entryName = Text(args, "entry", false);
        PboFile pbo = string.IsNullOrEmpty(entryName) ? null : new PboFile(new PBO(input, false));
        PboSpy.Models.FileBase file = pbo == null ? new PboSpy.Models.PhysicalFile(input) : FindEntry(pbo, entryName);
        var p3d = PboSpy.Modules.P3d.PreviewFactories.Load(file);
        var lod = p3d.LODs.Where(l => l.FaceCount > 0 && BIS.P3D.Resolution.IsVisual(l.Resolution)).OrderBy(l => l.Resolution).FirstOrDefault()
            ?? throw new InvalidOperationException("The model has no visual LOD.");
        var mesh = PboSpy.Modules.P3d.Scene.ModelMeshBuilder.Build(lod);
        var resolver = new PboSpy.Modules.P3d.Scene.TextureResolver(file, pbo == null ? null : new[] { pbo })
        {
            Folder = AppSettings.Default.ModelTextureFolder,
            RvmatFolder = Text(args, "rvmat_folder", false) ?? AppSettings.Default.ModelRvmatFolder
        };
        var maxTexture = args["max_texture"]?.GetValue<int>() ?? 2048;
        var splitAt = args["split_at"]?.GetValue<int>() ?? 0;
        var decimate = Math.Clamp(args["decimate"]?.GetValue<double>() ?? 1, 0.05, 1);
        PboSpy.Modules.P3d.Scene.ModelExport.Write(output, mesh.Parts, resolver, maxTexture, splitAt, decimate, args["by_material"]?.GetValue<bool>() ?? true);
        var report = mesh.Parts.Where(p => !string.IsNullOrWhiteSpace(p.Texture)).GroupBy(p => p.Texture, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, Found: resolver.Resolve(g.Key, 16).Found, Maps: resolver.ResolveLinked(g.Key, g.First().Material, 16).Summary, g.First().Material))
            .ToList();
        var missing = report.Where(r => !r.Found).Select(r => r.Key).ToList();
        var maps = string.Join("\n", report.Where(r => r.Found).Select(r => $"  {r.Key}: {(r.Maps.Length > 0 ? r.Maps : "- " + r.Material)}"));
        return $"Written {output} ({mesh.Triangles} triangles, {mesh.Parts.Count} parts, {new FileInfo(output).Length / 1024 / 1024} MB)\n" +
               $"Textures found {report.Count - missing.Count}/{report.Count}" +
               (missing.Count > 0 ? "\nMissing:\n  " + string.Join("\n  ", missing) : "") + "\nLinked maps:\n" + maps;
    }

    private static string P3dModelCfg(JsonObject args, CancellationToken cancel)
    {
        NeedBisDll();
        var log = new StringBuilder();
        var file = OdolExtract.ExtractModelCfg(ExistingFile(args, "input"), Text(args, "output"), m => log.AppendLine(m));
        return $"Written {file}\n{log}";
    }

    private static string PbrMake(JsonObject args, CancellationToken cancel)
    {
        var input = Text(args, "input");
        var output = Text(args, "output");
        if (!Directory.Exists(input))
        {
            throw new DirectoryNotFoundException("Folder not found: " + input);
        }
        var recursive = Flag(args, "recursive");
        var files = PbrMatcher.FindTextures(input, recursive);
        var rvmats = PbrMatcher.FindRvmats(input, recursive);
        var textures = new PbrTexture[files.Count];
        Parallel.For(0, files.Count, new ParallelOptions { CancellationToken = cancel, MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) },
            i =>
            {
                try
                {
                    textures[i] = PbrMatcher.Analyse(files[i]);
                }
                catch (Exception)
                {
                }
            });
        var groups = PbrMatcher.Group(textures.Where(t => t != null).ToList(), rvmats,
            new MatchOptions { UseNames = true, UseRvmats = true, Threshold = AppSettings.Default.PbrThreshold > 0 ? AppSettings.Default.PbrThreshold : 0.08 });
        Directory.CreateDirectory(output);
        var options = new OutputOptions { FlipGreen = Flag(args, "flip_green"), WriteBaseColor = true };
        var text = new StringBuilder();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var group in groups.Where(g => g.Members.Count > 1 || g.Members.ContainsKey(TextureKind.Normal) || g.Members.ContainsKey(TextureKind.Smdi)))
        {
            cancel.ThrowIfCancellationRequested();
            index++;
            var name = string.IsNullOrWhiteSpace(group.SuggestedName) ? $"{AppSettings.Default.PbrPrefix}_{index:00}" : group.SuggestedName;
            var unique = name;
            for (var n = 2; !used.Add(unique); n++)
            {
                unique = name + "_" + n;
            }
            var written = PbrOutput.Write(group, unique, output, options);
            text.AppendLine($"{unique} ({group.Source}): {string.Join(", ", group.Members.Select(m => m.Key + "=" + Path.GetFileName(m.Value.Path)))} -> {written.Count} files");
        }
        return $"{files.Count} textures, {rvmats.Count} rvmats, {index} sets written to {output}\n{text}";
    }

    private static string RecoverNames(JsonObject args, CancellationToken cancel)
    {
        var pbo = OpenPbo(args);
        try
        {
            var inputs = pbo.PBO.Files.Select(f => new NameInput(f.FileName, f.Size, f.OpenRead)).ToList();
            var report = NameRecovery.Analyse(pbo.PBO.Prefix ?? "", inputs, pbo.PBO.PropertiesPairs, null, cancel);
            var text = new StringBuilder();
            text.AppendLine($"{report.Items.Count} of {report.Total} names scrambled, {report.Decoys} decoy entries.");
            text.AppendLine("Recovered from: " + string.Join(", ", report.BySource.Select(p => $"{p.Key} {p.Value}")));
            var byPath = pbo.AllEntries.GroupBy(e => e.Entry.FileName).ToDictionary(g => g.Key, g => g.First());
            foreach (var item in report.Items)
            {
                text.AppendLine($"{item.Current}\t->\t{item.Suggested}\t[{item.Source}] {item.Reason}");
                if (byPath.TryGetValue(item.Input.Path, out var entry))
                {
                    entry.RenamedPath = item.Suggested;
                }
            }
            var output = Text(args, "output", false);
            if (!string.IsNullOrEmpty(output))
            {
                var extract = RenamedExtract.Run(pbo, output, null, cancel);
                text.Insert(0, $"Extracted {extract.Written} files to {output}, {extract.References} references updated in {extract.Rewritten} files."
                               + (extract.Problems.Count > 0 ? "\nProblems:\n" + string.Join("\n", extract.Problems) : "") + "\n\n");
            }
            return text.ToString();
        }
        finally
        {
            pbo.PBO.Dispose();
        }
    }

    private static string ConfigToText(JsonObject args, CancellationToken cancel) =>
        new PhysicalFile(ExistingFile(args, "path")).GetDetectConfigAsText(out _);
}
