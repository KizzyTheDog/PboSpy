using Gemini.Modules.Output;
using PboSpy.Interfaces;
using PboSpy.Localization;
using PboSpy.Models;
using System.Data;
using System.IO;

namespace PboSpy.Modules.FileManager.Services;

[Export(typeof(IFileManager))]
[PartCreationPolicy(CreationPolicy.Shared)]
class FileManager : IFileManager
{
    private IFileLoader _loaderChain;
    private readonly IOutput _output;
    public ICollection<ITreeItem> FileTree { get; }

    public event EventHandler<FileManagerEventArgs> FileLoaded;
    public event EventHandler<FileManagerEventArgs> FileRemoved;

    [ImportingConstructor]
    public FileManager([ImportMany(typeof(IFileLoader))] IEnumerable<Lazy<IFileLoader>> loaderEports, IOutput output)
    {
        _output = output;
        _loaderChain = BuildLoaderChain(loaderEports);
        FileTree = new BindableCollection<ITreeItem>();
    }

    public async Task LoadSupportedFiles(IEnumerable<string> paths)
    {
        var res = await Task.Run(() =>
        {
            var failures = new List<string>();
            var flat = ExpandPaths(paths)
            .Select(Path.GetFullPath)
            .Select(path =>
            {
                // One unreadable file (half downloaded, corrupt, locked) must not stop the rest.
                try
                {
                    return _loaderChain.Load(path);
                }
                catch (Exception ex)
                {
                    failures.Add(Loc.F("FileManager.LoadFailed", path, ex.Message));
                    return null;
                }
            })
            .Where(x => x != null)
            .ToList();

            if (failures.Count > 0)
            {
                Execute.OnUIThread(() =>
                {
                    foreach (var failure in failures)
                    {
                        _output?.AppendLine(failure);
                    }
                    System.Windows.MessageBox.Show(string.Join(Environment.NewLine, failures.Take(10)),
                        Loc.T("FileManager.LoadFailedTitle"), System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                });
            }

            foreach (var item in flat.OfType<IPersistentItem>())
            {
                OnFileLoaded(item);
            }

            return ToHierarchy(flat);
        }).ConfigureAwait(false);

        Execute.OnUIThread(() => res.Apply(item =>
        {
            FileTree.Add(item);
            Watch(item);
        }));
    }

    /// <summary>Folders taken out with "Hide from the explorer"; a refresh doesn't bring them back.</summary>
    public HashSet<string> Hidden { get; } = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<ITreeItem, FileSystemWatcher> _watchers = new();
    private readonly Dictionary<ITreeItem, CancellationTokenSource> _pendingSync = new();

    // Opened folders follow what happens on disk: new files show up, deleted ones go away.
    private void Watch(ITreeItem root)
    {
        if (root is not PhysicalDirectory || !Directory.Exists(root.Path) || _watchers.ContainsKey(root))
        {
            return;
        }
        try
        {
            var watcher = new FileSystemWatcher(root.Path)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
            };
            FileSystemEventHandler changed = (_, _) => Execute.OnUIThread(() => SyncSoon(root));
            watcher.Created += changed;
            watcher.Deleted += changed;
            watcher.Renamed += (_, _) => Execute.OnUIThread(() => SyncSoon(root));
            watcher.EnableRaisingEvents = true;
            _watchers[root] = watcher;
        }
        catch (Exception)
        {
        }
    }

    private async void SyncSoon(ITreeItem root)
    {
        if (_pendingSync.TryGetValue(root, out var old))
        {
            old.Cancel();
        }
        var delay = _pendingSync[root] = new CancellationTokenSource();
        try
        {
            await Task.Delay(600, delay.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }
        _pendingSync.Remove(root);
        if (!FileTree.Contains(root))
        {
            return;
        }

        var known = new Dictionary<string, ITreeItem>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<ITreeItem>(new[] { root });
        while (stack.Count > 0)
        {
            var item = stack.Pop();
            known[item.Path.TrimEnd('\\')] = item;
            if (item is PhysicalDirectory)
            {
                foreach (var child in item.Children.ToList())
                {
                    stack.Push(child);
                }
            }
        }
        var hidden = Hidden.ToList();
        var (onDisk, loaded) = await Task.Run(() =>
        {
            var paths = ExpandPaths(new[] { root.Path }).Skip(1)
                .Where(p => !hidden.Any(h => p.Equals(h, StringComparison.OrdinalIgnoreCase) || p.StartsWith(h + "\\", StringComparison.OrdinalIgnoreCase)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var added = new List<ITreeItem>();
            foreach (var path in paths.Where(p => !known.ContainsKey(p)))
            {
                try
                {
                    var item = _loaderChain.Load(path);
                    if (item != null)
                    {
                        added.Add(item);
                    }
                }
                catch (Exception)
                {
                }
            }
            return (paths, added);
        });

        foreach (var item in known.Values.Where(i => i != root && i.Parent != null && !onDisk.Contains(i.Path.TrimEnd('\\'))).ToList())
        {
            item.Parent.Children.Remove(item);
            if (item is IPersistentItem persistent)
            {
                OnFileRemoved(persistent);
            }
        }
        foreach (var item in loaded)
        {
            known[item.Path.TrimEnd('\\')] = item;
        }
        foreach (var item in loaded)
        {
            if (known.TryGetValue(Path.GetDirectoryName(item.Path) ?? "", out var parent) && parent.Children != null)
            {
                item.Parent = parent;
                parent.Children.Add(item);
                if (item is IPersistentItem persistent)
                {
                    OnFileLoaded(persistent);
                }
            }
        }
    }

    // TODO: Remove recursion
    public void Close(IPersistentItem file)
    {
        var childrenToClose = file.Children?.OfType<IPersistentItem>().ToList() ?? new();
        childrenToClose.Apply(child => Close(child));

        if (file.Parent is null)
        {
            FileTree.Remove(file);
            if (_watchers.Remove(file, out var watcher))
            {
                watcher.Dispose();
            }
        }
        else
        {
            file.Parent.Children.Remove(file);
        }

        if (file is IPersistentItem persistentFile)
        {
            OnFileRemoved(persistentFile);
        }
    }

    // TODO: Remove recursion
    public void CloseAll()
    {
        var toClose = FileTree.OfType<IPersistentItem>().ToList() ?? new();
        toClose.Apply(item => Close(item));
    }

    public async Task<ITreeItem> Rename(ITreeItem item, string newName)
    {
        if (item is not IPersistentItem persistent || string.IsNullOrWhiteSpace(newName))
        {
            return null;
        }
        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException(Loc.F("Rename.InvalidName", newName));
        }

        var oldPath = item.Path.TrimEnd('\\', '/');
        var directory = Path.GetDirectoryName(oldPath);
        var newPath = Path.Combine(directory ?? "", newName);
        var isFolder = Directory.Exists(oldPath);
        if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase) && (File.Exists(newPath) || Directory.Exists(newPath)))
        {
            throw new IOException(Loc.F("Rename.Exists", newName));
        }

        var parent = item.Parent;
        var siblings = parent?.Children ?? FileTree;
        var index = siblings.ToList().IndexOf(item);

        // Loaded PBOs keep their file open between reads.
        foreach (var pbo in Flatten(item).OfType<PboSpy.Modules.Pbo.Models.PboFile>())
        {
            pbo.PBO.Dispose();
        }
        Close(persistent);

        try
        {
            if (isFolder)
            {
                if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
                {
                    // Case only: Windows needs a detour.
                    var detour = oldPath + ".renaming";
                    Directory.Move(oldPath, detour);
                    Directory.Move(detour, newPath);
                }
                else
                {
                    Directory.Move(oldPath, newPath);
                }
            }
            else
            {
                File.Move(oldPath, newPath);
            }
        }
        catch (Exception)
        {
            await Reinsert(oldPath, parent, index);
            throw;
        }

        return await Reinsert(newPath, parent, index);
    }

    private async Task<ITreeItem> Reinsert(string path, ITreeItem parent, int index)
    {
        var root = await Task.Run(() =>
        {
            var flat = ExpandPaths(new[] { path }).Select(Path.GetFullPath).Select(p =>
            {
                try
                {
                    return _loaderChain.Load(p);
                }
                catch (Exception)
                {
                    return null;
                }
            }).Where(x => x != null).ToList();
            foreach (var loaded in flat.OfType<IPersistentItem>())
            {
                OnFileLoaded(loaded);
            }
            return ToHierarchy(flat).FirstOrDefault();
        });
        if (root == null)
        {
            return null;
        }
        Execute.OnUIThread(() =>
        {
            root.Parent = parent;
            var siblings = parent?.Children ?? FileTree;
            if (siblings is IList<ITreeItem> list && index >= 0 && index <= list.Count)
            {
                list.Insert(index, root);
            }
            else
            {
                siblings.Add(root);
            }
            if (parent == null)
            {
                Watch(root);
            }
        });
        return root;
    }

    private static IEnumerable<ITreeItem> Flatten(ITreeItem item)
    {
        yield return item;
        foreach (var child in item.Children?.ToList() ?? new List<ITreeItem>())
        {
            foreach (var nested in Flatten(child))
            {
                yield return nested;
            }
        }
    }

    private static bool IsDirectory(string path)
        => File.GetAttributes(path).HasFlag(FileAttributes.Directory);

    private void OnFileLoaded(IPersistentItem file)
        => FileLoaded?.Invoke(this, new(file));

    private void OnFileRemoved(IPersistentItem file)
        => FileRemoved?.Invoke(this, new(file));

    private static IFileLoader BuildLoaderChain(IEnumerable<Lazy<IFileLoader>> exports)
    {
        var supportedExtensions = new string[] { ".pbo", ".paa", ".rvmat", ".bin",
        ".pac", ".p3d", ".wrp", ".sqm", ".bisign", ".bikey", ".cpp", ".hpp", ".h", ".ext", ".sqf", ".sqs", ".fsm",
        ".wss", ".ogg", ".wav", ".mp3", ".flac", ".lip", ".rtm", ".csv", ".xml", ".txt", ".html", ".bisurf", ".cfg",
        ".png", ".tga", ".jpg", ".jpeg", ".bmp" };

        var loaders = exports.Select(o => o.Value);
        var stack = new Stack<IFileLoader>(loaders);
        stack.Push(new PhysicalFileLoader(supportedExtensions));
        stack.Push(new PhysicalDirectoryLoader());

        var chain = stack.Pop();

        foreach (var loader in stack)
        {
            loader.Next = chain;
            chain = loader;
        }

        return chain;
    }

    private static IEnumerable<string> ExpandPaths(IEnumerable<string> paths)
    {
        var lookup = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToLookup(IsDirectory);
        var result = new List<string>();
        result.AddRange(lookup[false]);
        foreach (var path in lookup[true])
        {
            result.Add(path);
            result.AddRange(Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories));
        }
        return result;
    }

    private static IEnumerable<ITreeItem> ToHierarchy(IList<ITreeItem> flatNodes)
    {
        var nodesByPath = flatNodes.ToDictionary(node => node.Path);
        var groupsByParent = flatNodes.ToLookup(node => Path.GetDirectoryName(node.Path));

        foreach (var group in groupsByParent)
        {
            nodesByPath.TryGetValue(group.Key, out var parent);

            if (parent != null)
            {
                foreach (var item in group)
                {
                    item.Parent = parent;
                    parent.Children.Add(item);
                }
            }
        }

        return nodesByPath.Values.Where(x => x.Parent == null).ToList();
    }
}
