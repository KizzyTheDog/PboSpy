using PboSpy.Interfaces;
using PboSpy.Localization;
using PboSpy.Models;
using PboSpy.Modules.Pbo.Models;
using PboSpy.Modules.Preview.ViewModels;
using PboSpy.Modules.Windows;
using System.IO;
using System.Windows;

namespace PboSpy.Modules.Explorer.ViewModels;

partial class ExplorerViewModel
{
    public event Action<ITreeItem> RevealRequested;

    public void Reveal(ITreeItem item)
    {
        if (item == null)
        {
            return;
        }
        SelectedItem = item;
        // Whoever reveals opens the file itself; a single-click preview on top of that gave two tabs.
        _skipPreviewFor = item;
        RevealRequested?.Invoke(item);
    }

    public static bool CanRename(ITreeItem item) => item is PboEntry or PboDirectory or PhysicalFile or PhysicalDirectory or PboFile;

    public async Task RenameSelected()
    {
        var item = _selectedItem;
        if (item == null || !CanRename(item))
        {
            return;
        }
        var owner = Application.Current?.MainWindow;
        var title = Loc.T("Rename.Title");

        if (item is PboEntry or PboDirectory)
        {
            var name = MessageDialog.Prompt(owner, title, Loc.T("Rename.InsidePbo"), item.Name);
            if (name == null)
            {
                return;
            }
            if (name.IndexOfAny(new[] { '\\', '/' }) >= 0 || name.Trim('.').Length == 0)
            {
                MessageDialog.Info(owner, title, Loc.F("Rename.InvalidName", name));
                return;
            }
            RenameInsidePbo(item, name);
            return;
        }

        var current = Path.GetFileName(item.Path.TrimEnd('\\', '/'));
        var newName = MessageDialog.Prompt(owner, title, Loc.T("Rename.OnDisk"), current);
        if (newName == null)
        {
            return;
        }

        // Previews still point at the old path.
        var shell = IoC.Get<IShell>();
        var root = item.Path.TrimEnd('\\', '/');
        foreach (var document in shell.Documents.OfType<PreviewViewModel>().ToList())
        {
            var path = document.Model switch
            {
                PboEntry entry => entry.PBO.PBOFilePath,
                FileBase file => file.FullPath,
                _ => null
            };
            if (path != null && (string.Equals(path, root, StringComparison.OrdinalIgnoreCase) ||
                                 path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)))
            {
                await shell.CloseDocumentAsync(document);
            }
        }

        try
        {
            var renamed = await _pboManager.Rename(item, newName);
            if (renamed != null)
            {
                SelectedItem = renamed;
            }
        }
        catch (Exception ex)
        {
            MessageDialog.Info(owner, title, ex.Message);
        }
    }

    /// <summary>Entries inside a PBO keep their stored name; the new one is used when exporting.</summary>
    public static void RenameInsidePbo(ITreeItem item, string name)
    {
        switch (item)
        {
            case PboEntry entry:
                var folder = RelativeInsidePbo(entry.Parent);
                entry.RenamedPath = string.IsNullOrEmpty(folder) ? name : folder + "\\" + name;
                break;
            case PboDirectory directory:
                directory.Name = name;
                foreach (var nested in directory.AllFiles)
                {
                    var path = RelativeInsidePbo(nested.Parent);
                    nested.RenamedPath = string.IsNullOrEmpty(path) ? nested.Name : path + "\\" + nested.Name;
                }
                break;
        }
    }
}
