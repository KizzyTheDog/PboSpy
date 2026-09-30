using BIS.Core.Streams;
using BIS.P3D;
using PboSpy.Interfaces;
using PboSpy.Localization;
using PboSpy.Models;
using PboSpy.Modules.P3d.Scene;
using PboSpy.Modules.P3d.ViewModels;
using PboSpy.Modules.Pbo.Models;
using PboSpy.Modules.Windows;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace PboSpy.Modules.Explorer.ViewModels;

partial class ExplorerViewModel
{
    public bool ContextIsTexture() =>
        ContextSelection.Count == 1 && ContextSelection[0] is FileBase file && file.Extension is ".paa" or ".pac" or ".png" or ".tga" or ".jpg";

    /// <summary>Opens one 3D view with every loaded model that uses the selected texture.</summary>
    public async Task ShowModelsUsingTexture()
    {
        if (ContextSelection.FirstOrDefault() is not FileBase texture)
        {
            return;
        }
        var shell = IoC.Get<IShell>();
        Mouse.OverrideCursor = Cursors.AppStarting;
        List<(FileBase File, P3D Model)> found;
        try
        {
            var models = BulkExport.Services.BulkExportService.Collect(Items.ToList())
                .Where(f => f.Extension == ".p3d").ToList();
            found = await Task.Run(() => FindUsers(texture, models));
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

        if (found.Count == 0)
        {
            MessageDialog.Info(Application.Current.MainWindow, Loc.T("Usage.Title"), Loc.F("Usage.None", texture.Name));
            return;
        }

        var reference = TextureReference(texture);
        var view = new P3dPreviewViewModel(texture, found[0].Model)
        {
            MainName = found[0].File.Name,
            FocusTexture = reference
        };
        foreach (var (file, model) in found.Skip(1))
        {
            var lod = VisualLod(model);
            if (lod != null)
            {
                view.Companions.Add((file.Name, lod));
            }
        }
        view.DisplayName = Loc.F("Usage.TabTitle", texture.Name, found.Count);
        await shell.OpenDocumentAsync(view);
    }

    private static string TextureReference(FileBase texture) => texture switch
    {
        PboEntry entry => TextureResolver.Normalize(entry.FullPath),
        _ => TextureResolver.Normalize(texture.Name)
    };

    private static ILevelOfDetail VisualLod(P3D model) =>
        model.LODs.Where(l => l.FaceCount > 0 && BIS.P3D.Resolution.IsVisual(l.Resolution)).OrderBy(l => l.Resolution).FirstOrDefault()
        ?? model.LODs.FirstOrDefault(l => l.FaceCount > 0);

    private static List<(FileBase, P3D)> FindUsers(FileBase texture, IReadOnlyList<FileBase> models)
    {
        var full = texture is PboEntry ? TextureReference(texture) : null;
        var rawName = texture is PboEntry entry ? Path.GetFileName(entry.Entry.FileName.Replace('\\', '/')) : texture.Name;
        var stem = Path.GetFileNameWithoutExtension(rawName).ToLowerInvariant();
        var needle = Encoding.Latin1.GetBytes(stem);
        var result = new List<(FileBase, P3D)>();
        foreach (var file in models)
        {
            try
            {
                byte[] data;
                using (var stream = file.GetStream())
                using (var memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    data = memory.ToArray();
                }
                if (IndexOfIgnoreCase(data, needle) < 0)
                {
                    continue;
                }
                var model = StreamHelper.Read<P3D>(new MemoryStream(data));
                var uses = model.LODs.SelectMany(l => l.GetTextures()).Select(TextureResolver.Normalize).Any(t =>
                    full != null ? t == full || Path.ChangeExtension(t, null) == Path.ChangeExtension(full, null)
                                 : Path.GetFileNameWithoutExtension(t) == stem);
                if (uses)
                {
                    result.Add((file, model));
                }
            }
            catch (Exception)
            {
            }
        }
        return result;
    }

    private static int IndexOfIgnoreCase(byte[] data, byte[] needle)
    {
        if (needle.Length == 0)
        {
            return -1;
        }
        var first = needle[0];
        for (var i = 0; i <= data.Length - needle.Length; i++)
        {
            if (ToLower(data[i]) != first)
            {
                continue;
            }
            var match = true;
            for (var j = 1; j < needle.Length; j++)
            {
                if (ToLower(data[i + j]) != needle[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
            {
                return i;
            }
        }
        return -1;
    }

    private static byte ToLower(byte b) => b is >= (byte)'A' and <= (byte)'Z' ? (byte)(b + 32) : b;
}

partial class ExplorerViewModel
{
    public void ConvertSelected()
    {
        var files = BulkExport.Services.BulkExportService.Collect(ContextSelection, IsSelectable);
        BulkExport.Commands.ConvertRunner.Run(files, null);
    }

    /// <summary>Opens the name recovery tool for the selection: PBOs, entries of a PBO, or files and folders on disk.</summary>
    public void RecoverNames()
    {
        var selection = ContextSelection;
        if (selection.Count == 0)
        {
            Deobfuscate.Views.NameRecoveryWindow.PickFolder();
            return;
        }
        foreach (var pbo in selection.OfType<PboFile>())
        {
            Deobfuscate.Views.NameRecoveryWindow.Open(pbo);
        }
        foreach (var group in selection.Where(i => i is not PboFile && PboOf(i) != null).GroupBy(PboOf))
        {
            var entries = BulkExport.Services.BulkExportService.Collect(group.ToList()).OfType<PboEntry>().Select(e => e.Entry.FileName).ToList();
            Deobfuscate.Views.NameRecoveryWindow.Open(group.Key, entries);
        }
        foreach (var directory in selection.OfType<PhysicalDirectory>())
        {
            Deobfuscate.Views.NameRecoveryWindow.OpenFolder(directory.FullPath);
        }
        var loose = selection.OfType<PhysicalFile>().ToList();
        if (loose.Count > 0)
        {
            var root = RootOf(loose[0]) is PhysicalDirectory top ? top.FullPath : Path.GetDirectoryName(loose[0].FullPath);
            Deobfuscate.Views.NameRecoveryWindow.OpenFolder(root, loose.Select(f => f.FullPath));
        }
    }
}
