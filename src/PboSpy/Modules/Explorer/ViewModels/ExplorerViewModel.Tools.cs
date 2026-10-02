using Microsoft.WindowsAPICodePack.Dialogs;
using PboSpy.Interfaces;
using PboSpy.Localization;
using PboSpy.Models;
using PboSpy.Modules.BulkExport.Models;
using PboSpy.Modules.BulkExport.Services;
using PboSpy.Modules.P3dTools.Views;
using PboSpy.Modules.Pbo.Models;
using PboSpy.Modules.Pbr.Core;
using PboSpy.Modules.Pbr.Views;
using PboSpy.Services;
using System.IO;
using System.Windows;

namespace PboSpy.Modules.Explorer.ViewModels;

/// <summary>Hands the selection over to the P3D Tools and PBR Texture Maker windows.</summary>
public partial class ExplorerViewModel
{
    private static readonly string[] TextureExtensions = PbrImage.Extensions;

    /// <summary>The selection, or the focused item when nothing is selected.</summary>
    public IReadOnlyList<ITreeItem> ContextSelection =>
        _selection.Count > 0 ? _selection.ToList()
        : _selectedItem != null ? new List<ITreeItem> { _selectedItem }
        : new List<ITreeItem>();

    public ITreeItem FocusedItem => _selectedItem;

    public bool ContextHas(params string[] extensions) =>
        Flatten(ContextSelection).Any(i => i.Children == null && extensions.Contains(ExtensionOf(i)));

    public bool ContextHasPbrTextures() =>
        Flatten(ContextSelection).Any(i => i.Children == null && TextureExtensions.Contains(ExtensionOf(i)) &&
                                          PbrMatcher.TryClassify(Path.GetFileNameWithoutExtension(i.Name ?? ""), out _, out _));

    public void SendToP3dTools(P3dAction action)
    {
        var files = BulkExportService.Collect(ContextSelection, IsSelectable).Where(f => f.Extension == ".p3d").ToList();
        if (files.Count == 0)
        {
            return;
        }

        string input;
        string output = null;
        if (files.Count == 1)
        {
            input = Materialize(files[0], null);
        }
        else
        {
            input = TempFolderFor("p3d|" + string.Join("|", files.Select(f => f.FullPath)));
            foreach (var file in files)
            {
                Materialize(file, input);
            }
        }
        if (files.Any(f => f is PboEntry))
        {
            output = DefaultOutput("P3D", files.OfType<PboEntry>().First());
        }
        else if (files.Count > 1)
        {
            // Several files on disk go through a temp folder; without this the results were written there too.
            output = files.Select(f => Path.GetDirectoryName(f.FullPath)).Aggregate(CommonFolder);
        }
        P3dToolsWindow.Open(input, output, action);
    }

    /// <summary>Each selected model as a textured GLB (first visual LOD, no proxies) in a folder the user picks.</summary>
    public Task ExportRoblox() =>
        PboSpy.Modules.Rtm.RobloxExport.Export(BulkExportService.Collect(ContextSelection, IsSelectable).Where(f => f.Extension == ".rtm").ToList());

    public Task ExportModels()
    {
        var files = BulkExportService.Collect(ContextSelection, IsSelectable).Where(f => f.Extension == ".p3d").ToList();
        if (files.Count == 0)
        {
            return Task.CompletedTask;
        }
        var tree = Items.ToList();
        PboSpy.Modules.P3d.Views.ModelExportWindow.Open(true, files[0].Name, async folder =>
        {
            var failures = new List<string>();
            var extension = PboSpy.Modules.P3d.Views.ModelExportWindow.Extension;
            await Task.Run(() =>
            {
                foreach (var file in files)
                {
                    try
                    {
                        var p3d = PboSpy.Modules.P3d.PreviewFactories.Load(file);
                        var lod = p3d.LODs.Where(l => l.FaceCount > 0 && BIS.P3D.Resolution.IsVisual(l.Resolution)).OrderBy(l => l.Resolution).FirstOrDefault();
                        if (lod == null)
                        {
                            continue;
                        }
                        var mesh = PboSpy.Modules.P3d.Scene.ModelMeshBuilder.Build(lod, PboSpy.Modules.P3d.Scene.ConfigTextures.Cached(file, tree, lod));
                        var resolver = new PboSpy.Modules.P3d.Scene.TextureResolver(file, tree);
                        PboSpy.Modules.P3d.Views.ModelExportWindow.Configure(resolver);
                        PboSpy.Modules.P3d.Scene.ModelExport.Write(Path.Combine(folder, Path.GetFileNameWithoutExtension(file.Name) + extension), mesh.Parts, resolver,
                            PboSpy.Modules.P3d.Views.ModelExportWindow.MaxTexture, PboSpy.Modules.P3d.Views.ModelExportWindow.SplitAt,
                            PboSpy.Modules.P3d.Views.ModelExportWindow.Keep, PboSpy.Modules.P3d.Views.ModelExportWindow.ByMaterial,
                            PboSpy.Modules.P3d.Views.ModelExportWindow.Lods);
                    }
                    catch (Exception ex)
                    {
                        failures.Add(file.Name + ": " + ex.Message);
                    }
                }
            });
            if (failures.Count > 0)
            {
                MessageBox.Show(string.Join(Environment.NewLine, failures.Take(10)), Loc.T("Export.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            System.Diagnostics.Process.Start("explorer.exe", $"\"{folder}\"");
        });
        return Task.CompletedTask;
    }

    private static string CommonFolder(string a, string b)
    {
        while (!string.IsNullOrEmpty(a) && !(b + "\\").StartsWith(a.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
        {
            a = Path.GetDirectoryName(a);
        }
        return a ?? b;
    }

    public void SendToPbrMaker()
    {
        var selection = ContextSelection;
        if (selection.Count == 1 && selection[0] is PhysicalDirectory directory)
        {
            PbrMakerWindow.Open(directory.FullPath, Path.Combine(directory.FullPath, "PBR"), true);
            return;
        }

        // Textures plus any rvmats next to them: the rvmats make the matching exact.
        var files = BulkExportService.Collect(selection, IsSelectable)
            .Where(f => f.Extension == ".rvmat" || TextureExtensions.Contains(f.Extension))
            .ToList();
        if (files.Count == 0)
        {
            return;
        }
        if (files.All(f => f is PhysicalFile))
        {
            var folders = files.Select(f => Path.GetDirectoryName(f.FullPath)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (folders.Count == 1)
            {
                PbrMakerWindow.Open(folders[0], Path.Combine(folders[0], "PBR"), true);
                return;
            }
        }

        var input = TempFolderFor("pbr|" + string.Join("|", files.Select(f => f.FullPath)));
        foreach (var file in files)
        {
            Materialize(file, input);
        }
        var first = files.OfType<PboEntry>().FirstOrDefault();
        PbrMakerWindow.Open(input, first != null ? DefaultOutput("PBR", first) : Path.Combine(input, "PBR"), true);
    }

    /// <summary>A path on disk for the file: physical files as they are, PBO entries extracted.</summary>
    private static string Materialize(FileBase file, string intoFolder)
    {
        if (file is PhysicalFile physical)
        {
            if (intoFolder == null)
            {
                return physical.FullPath;
            }
            var copy = Path.Combine(intoFolder, Path.GetFileName(physical.FullPath));
            if (!File.Exists(copy))
            {
                File.Copy(physical.FullPath, copy);
            }
            return copy;
        }
        if (file is PboEntry entry)
        {
            if (intoFolder == null)
            {
                return ExtractToTempRaw(entry);
            }
            var relative = entry.Entry.FileName.Replace('/', '\\')
                .Split('\\', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => BulkExportService.SanitizeSegment(s, NameSanitizeMode.PercentEncode));
            var target = Path.Combine(new[] { intoFolder }.Concat(relative).ToArray());
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!File.Exists(target) || new FileInfo(target).Length != entry.DataSize)
            {
                entry.Extract(target);
            }
            return target;
        }
        return null;
    }

    private static string ExtractToTempRaw(PboEntry entry)
    {
        var directory = TempFolderFor(entry.FullPath);
        var target = Path.Combine(directory, BulkExportService.SanitizeSegment(entry.Name, NameSanitizeMode.PercentEncode));
        if (!File.Exists(target) || new FileInfo(target).Length != entry.DataSize)
        {
            entry.Extract(target);
        }
        return target;
    }

    private static string DefaultOutput(string tool, PboEntry entry)
    {
        var pbo = Path.GetFileNameWithoutExtension(entry.PBO.PBOFilePath ?? entry.PBO.FileName ?? "pbo");
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(documents, "PboSpy", tool, BulkExportService.SanitizeSegment(pbo, NameSanitizeMode.Underscore));
    }
}
