using PboSpy.Interfaces;
using PboSpy.Models;
using PboSpy.Modules.Explorer;
using PboSpy.Modules.FileManager;
using PboSpy.Modules.Preview;
using System.IO;
using System.Windows;

namespace PboSpy.Services;

/// <summary>Loads files into the explorer (if they aren't already), selects the first and previews it.</summary>
public static class AppOpen
{
    public static async Task Show(IEnumerable<string> paths, bool preview = true)
    {
        var list = paths.Where(p => File.Exists(p) || Directory.Exists(p)).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count == 0)
        {
            return;
        }
        var manager = IoC.Get<IFileManager>();
        var missing = list.Where(p => Find(manager.FileTree, p) == null).ToList();
        if (missing.Count > 0)
        {
            await manager.LoadSupportedFiles(missing);
        }

        await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            var main = Application.Current.MainWindow;
            if (main != null && !TestMode.On)
            {
                if (main.WindowState == WindowState.Minimized)
                {
                    main.WindowState = WindowState.Normal;
                }
                main.Activate();
            }
            var item = Find(manager.FileTree, list[0]);
            if (item == null)
            {
                return;
            }
            var shell = IoC.Get<IShell>();
            var explorer = IoC.Get<IPboExplorer>();
            shell.ShowTool(explorer);
            explorer.Reveal(item);
            if (preview && item is FileBase file)
            {
                await IoC.Get<IPreviewManager>().ShowPreview(file, pin: true, activate: true);
            }
        }).Task.Unwrap();
    }

    public static ITreeItem Find(IEnumerable<ITreeItem> items, string path)
    {
        foreach (var item in items?.ToList() ?? new List<ITreeItem>())
        {
            var itemPath = item switch
            {
                PhysicalFile file => file.FullPath,
                PhysicalDirectory directory => directory.FullPath,
                Modules.Pbo.Models.PboFile pbo => pbo.Path,
                _ => null
            };
            if (itemPath == null)
            {
                continue;
            }
            if (string.Equals(itemPath.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
            if (item is PhysicalDirectory && path.StartsWith(itemPath.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
            {
                var nested = Find(item.Children, path);
                if (nested != null)
                {
                    return nested;
                }
            }
        }
        return null;
    }
}
