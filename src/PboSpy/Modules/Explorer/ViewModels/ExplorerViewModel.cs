using Gemini.Modules.Shell.Commands;
using Microsoft.WindowsAPICodePack.Dialogs;
using PboSpy.Interfaces;
using PboSpy.Localization;
using PboSpy.Models;
using PboSpy.Modules.BulkExport.Commands;
using PboSpy.Modules.BulkExport.Models;
using PboSpy.Modules.BulkExport.Services;
using PboSpy.Modules.BulkExport.Views;
using PboSpy.Modules.Explorer.Commands;
using PboSpy.Modules.Explorer.Models;
using PboSpy.Modules.FileManager;
using PboSpy.Modules.Metadata;
using PboSpy.Modules.Pbo.Models;
using PboSpy.Modules.Preview;
using PboSpy.Services;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace PboSpy.Modules.Explorer.ViewModels;

[Export(typeof(IPboExplorer))]
[PartCreationPolicy(CreationPolicy.Shared)]
public partial class ExplorerViewModel : Tool, IPboExplorer, ITreeSelectionHost,
    ICommandHandler<CloseFileCommandDefinition>,
    ICommandHandler<CloseAllFilesCommandDefinition>,
    ICommandHandler<BulkExportCommandDefinition>
{
    private readonly IFileManager _pboManager;
    private readonly IPreviewManager _previewManager;
    private readonly IMetadataInspector _metadataInspector;

    private readonly HashSet<ITreeItem> _selection = new();
    private readonly HashSet<string> _unchecked;
    private ITreeItem _selectedItem;
    private ITreeItem _filterScope;
    private int _selectionVersion;
    private int _filterVersion;
    private string _searchText = "";
    private string _selectionSummary = "";
    private bool _isFilterOpen;

    public ExportOptions ExportOptions { get; }

    public ICollection<ITreeItem> Items => _pboManager.FileTree;

    public BindableCollection<ExtensionFilterItem> ExtensionFilters { get; } = new();

    public ITreeItem SelectedItem
    {
        get => _selectedItem;
        set
        {
            _selectedItem = value;
            NotifyOfPropertyChange(nameof(SelectedItem));
            NotifyOfPropertyChange(nameof(CanExtractSelectedPbo));
            NotifyOfPropertyChange(nameof(CanCloseSelected));
            UpdateFilterScope();
        }
    }

    public bool CanExtractSelectedPbo => SelectedItem is PboFile;

    public bool CanCloseSelected => SelectedItem is IPersistentItem;

    public int SelectionVersion
    {
        get => _selectionVersion;
        private set
        {
            _selectionVersion = value;
            NotifyOfPropertyChange(nameof(SelectionVersion));
        }
    }

    public int FilterVersion
    {
        get => _filterVersion;
        private set
        {
            _filterVersion = value;
            NotifyOfPropertyChange(nameof(FilterVersion));
        }
    }

    public bool HideFilteredFiles
    {
        get => AppSettings.Default.HideFilteredFiles;
        set
        {
            AppSettings.Default.HideFilteredFiles = value;
            AppSettings.Default.Save();
            NotifyOfPropertyChange(nameof(HideFilteredFiles));
            FilterVersion++;
        }
    }

    public bool IsFilterOpen
    {
        get => _isFilterOpen;
        set
        {
            _isFilterOpen = value;
            NotifyOfPropertyChange(nameof(IsFilterOpen));
        }
    }

    public bool HasActiveFilter => _unchecked.Count > 0;

    public string FilterScopeText => _filterScope == null
        ? Loc.T("Explorer.Filter.ScopeAll")
        : Loc.F("Explorer.Filter.ScopeFolder", _filterScope.Name);

    public string SearchText
    {
        get => _searchText;
        set
        {
            _searchText = value ?? "";
            NotifyOfPropertyChange(nameof(SearchText));
            _ = ApplySearchSoon();
        }
    }

    private HashSet<ITreeItem> _searchVisible;
    private CancellationTokenSource _searchDelay;

    /// <summary>Raised after the search filter changes; the view expands the folders that lead to matches.</summary>
    public event Action<HashSet<ITreeItem>> SearchApplied;

    public bool IsHiddenBySearch(ITreeItem item) => _searchVisible != null && !_searchVisible.Contains(item);

    // Like Roblox Studio's explorer: only matches and the folders leading to them stay visible.
    private async Task ApplySearchSoon()
    {
        _searchDelay?.Cancel();
        var delay = _searchDelay = new CancellationTokenSource();
        try
        {
            await Task.Delay(200, delay.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }
        var regex = SearchRegex();
        HashSet<ITreeItem> visible = null;
        if (regex != null)
        {
            var roots = Items.ToList();
            visible = await Task.Run(() =>
            {
                var set = new HashSet<ITreeItem>();
                foreach (var item in Flatten(roots))
                {
                    if (!regex.IsMatch(item.Name ?? ""))
                    {
                        continue;
                    }
                    for (var current = item; current != null && set.Add(current); current = current.Parent)
                    {
                    }
                }
                return set;
            });
            if (delay.IsCancellationRequested)
            {
                return;
            }
        }
        _searchVisible = visible;
        FilterVersion++;
        SearchApplied?.Invoke(visible);
    }

    private Regex SearchRegex()
    {
        var pattern = SearchText.Trim();
        if (pattern.Length == 0)
        {
            return null;
        }
        if (pattern.IndexOfAny(new[] { '*', '?' }) >= 0)
        {
            return new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        return new Regex(Regex.Escape(pattern), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public string SelectionSummary
    {
        get => _selectionSummary;
        private set
        {
            _selectionSummary = value;
            NotifyOfPropertyChange(nameof(SelectionSummary));
        }
    }

    public IReadOnlyCollection<ITreeItem> SelectedItems => _selection;

    public override PaneLocation PreferredLocation => PaneLocation.Left;

    public override double PreferredWidth => 340;

    [ImportingConstructor]
    public ExplorerViewModel(IFileManager pboManager, IPreviewManager previewManager, IMetadataInspector metadataInspector)
    {
        _pboManager = pboManager;
        _previewManager = previewManager;
        _metadataInspector = metadataInspector;
        _unchecked = new HashSet<string>(AppSettings.Default.UncheckedExtensions, StringComparer.OrdinalIgnoreCase);

        ExportOptions = ExportOptionsStore.Load();

        DisplayName = Loc.T("Explorer.Title");
        Loc.Instance.LanguageChanged += (_, _) =>
        {
            DisplayName = Loc.T("Explorer.Title");
            NotifyOfPropertyChange(nameof(FilterScopeText));
            UpdateSummary();
        };

        if (_pboManager.FileTree is INotifyCollectionChanged tree)
        {
            tree.CollectionChanged += (_, e) =>
            {
                // Only what was opened goes into recents, not every file under an opened folder.
                foreach (var added in e.NewItems?.OfType<IPersistentItem>() ?? Enumerable.Empty<IPersistentItem>())
                {
                    if (!string.IsNullOrEmpty(added.Path))
                    {
                        AppSettings.Default.AddRecentFile(added.Path);
                    }
                }
                _selection.RemoveWhere(i => !IsStillLoaded(i));
                SelectionVersion++;
                UpdateFilterScope();
                UpdateSummary();
            };
        }
    }

    #region Selection host

    public bool IsSelectable(ITreeItem item)
    {
        if (item == null)
        {
            return false;
        }
        if (item.Children != null)
        {
            return true;
        }
        return !_unchecked.Contains(ExtensionOf(item));
    }

    bool ITreeSelectionHost.IsSelected(ITreeItem item) => item != null && _selection.Contains(item);

    public void SetSelection(IEnumerable<ITreeItem> items)
    {
        var next = new HashSet<ITreeItem>(items.Where(IsSelectable));
        if (next.SetEquals(_selection))
        {
            return;
        }
        _selection.Clear();
        _selection.UnionWith(next);
        SelectionVersion++;
        UpdateSummary();
    }

    public void BeginDrag(DependencyObject source)
    {
        var paths = new StringCollection();
        try
        {
            foreach (var item in _selection.ToList())
            {
                var path = MaterializeForDrag(item);
                if (path != null)
                {
                    paths.Add(path);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Loc.T("Explorer.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (paths.Count == 0)
        {
            return;
        }
        var data = new DataObject();
        data.SetFileDropList(paths);
        DragDrop.DoDragDrop(source, data, DragDropEffects.Copy);
    }

    #endregion

    #region Filter

    public void SetAllFilters(bool isChecked)
    {
        foreach (var filter in ExtensionFilters)
        {
            filter.SetSilently(isChecked);
            Apply(filter.Extension, isChecked);
        }
        FilterChanged();
    }

    public void InvertFilters()
    {
        foreach (var filter in ExtensionFilters)
        {
            filter.SetSilently(!filter.IsChecked);
            Apply(filter.Extension, filter.IsChecked);
        }
        FilterChanged();
    }

    public void CheckAllFilters() => SetAllFilters(true);

    public void UncheckAllFilters() => SetAllFilters(false);

    public void SelectAllOfCheckedTypes()
    {
        var roots = _filterScope != null ? new[] { _filterScope } : Items.ToArray();
        SetSelection(Flatten(roots).Where(i => i.Children == null && IsSelectable(i)));
    }

    private void OnFilterToggled(ExtensionFilterItem filter)
    {
        Apply(filter.Extension, filter.IsChecked);
        FilterChanged();
    }

    private void Apply(string extension, bool isChecked)
    {
        if (isChecked)
        {
            _unchecked.Remove(extension);
        }
        else
        {
            _unchecked.Add(extension);
        }
    }

    private void FilterChanged()
    {
        AppSettings.Default.UncheckedExtensions = _unchecked.OrderBy(e => e).ToList();
        AppSettings.Default.Save();

        var before = _selection.Count;
        _selection.RemoveWhere(i => !IsSelectable(i));
        if (before != _selection.Count)
        {
            SelectionVersion++;
        }
        FilterVersion++;
        NotifyOfPropertyChange(nameof(HasActiveFilter));
        UpdateSummary();
    }

    private void UpdateFilterScope()
    {
        _filterScope = _selectedItem == null ? null
            : _selectedItem.Children != null ? _selectedItem
            : _selectedItem.Parent;
        NotifyOfPropertyChange(nameof(FilterScopeText));

        var roots = _filterScope != null ? new[] { _filterScope } : Items.ToArray();
        var counts = Flatten(roots)
            .Where(i => i.Children == null)
            .GroupBy(ExtensionOf)
            .ToDictionary(g => g.Key, g => g.Count());

        // Keep unchecked types visible in the list even when this folder has none,
        // otherwise there would be no way to turn them back on from here.
        foreach (var ext in _unchecked)
        {
            counts.TryAdd(ext, 0);
        }

        ExtensionFilters.Clear();
        foreach (var kv in counts.OrderByDescending(k => k.Value).ThenBy(k => k.Key))
        {
            ExtensionFilters.Add(new ExtensionFilterItem(kv.Key, kv.Value, !_unchecked.Contains(kv.Key), OnFilterToggled));
        }
    }

    #endregion

    #region Search and selection helpers

    public void SelectMatching()
    {
        var regex = SearchRegex();
        if (regex == null)
        {
            return;
        }

        var roots = _filterScope != null ? new[] { _filterScope } : Items.ToArray();
        SetSelection(Flatten(roots).Where(i => i.Children == null && IsSelectable(i) && regex.IsMatch(i.Name)));
    }

    public void OnSearchKey(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            SelectMatching();
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Escape)
        {
            ClearSearch();
            e.Handled = true;
        }
    }

    public async Task PreviewSelected()
    {
        if (_selectedItem is FileBase file)
        {
            await _previewManager.ShowPreview(file, pin: true, activate: true);
        }
    }

    public void ClearSearch()
    {
        SearchText = "";
    }

    public void SelectAllInFolder()
    {
        var folder = _selectedItem?.Children != null ? _selectedItem : _selectedItem?.Parent;
        if (folder == null)
        {
            return;
        }
        SetSelection(Flatten(new[] { folder }).Where(i => i.Children == null && IsSelectable(i)));
    }

    public void SelectSameType()
    {
        if (_selectedItem == null || _selectedItem.Children != null)
        {
            return;
        }
        var ext = ExtensionOf(_selectedItem);
        var root = RootOf(_selectedItem);
        SetSelection(Flatten(new[] { root }).Where(i => i.Children == null && ExtensionOf(i) == ext));
    }

    public void ClearSelection() => SetSelection(Array.Empty<ITreeItem>());

    public void CopyPath()
    {
        var items = _selection.Count > 0 ? _selection.ToList() : new List<ITreeItem> { _selectedItem };
        var text = string.Join(Environment.NewLine, items.Where(i => i != null).Select(PathOf));
        if (!string.IsNullOrEmpty(text))
        {
            Clipboard.SetText(text);
        }
    }

    public void ShowInExplorer()
    {
        var item = _selectedItem;
        while (item != null && item is not IPersistentItem)
        {
            item = item.Parent;
        }
        var path = item?.Path;
        if (string.IsNullOrEmpty(path))
        {
            return;
        }
        if (File.Exists(path))
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        else if (Directory.Exists(path))
        {
            Process.Start("explorer.exe", $"\"{path}\"");
        }
    }

    public void ExportSelection() => RunBulkExport();

    #endregion

    public void ExtractSelectedPbo()
    {
        if (SelectedItem is not PboFile selectedPbo)
        {
            return;
        }

        var dialog = new CommonOpenFileDialog
        {
            Title = Loc.T("Explorer.ExtractTo"),
            IsFolderPicker = true
        };
        if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
        {
            return;
        }

        // Going through the exporter rather than PBO.ExtractAllFiles keeps this working
        // for obfuscated PBOs, whose entry names contain characters Windows rejects.
        var options = new ExportOptions
        {
            OutputDirectory = dialog.FileName,
            Preset = ContentPreset.Everything,
            ConvertImages = false,
            ConvertConfigs = false,
            ConvertAudio = false,
            PreserveStructure = true
        };

        var files = BulkExportService.Collect(new[] { (ITreeItem)selectedPbo });
        var report = BulkExportService.Run(files, options, (_, _) => { }, CancellationToken.None);

        if (report.Failed > 0)
        {
            MessageBox.Show(Loc.F("Explorer.ExtractPartial", report.Written, report.Failed), Loc.T("Explorer.Extract"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void CloseSelected()
    {
        var targets = _selection.OfType<IPersistentItem>().ToList();
        if (targets.Count == 0 && SelectedItem is IPersistentItem single)
        {
            targets.Add(single);
        }
        foreach (var item in targets)
        {
            _pboManager.Close(item);
        }

        SelectedItem = null;
        ClearSelection();
    }

    /// <summary>Takes items out of the explorer without touching them on disk.</summary>
    public void HideSelected()
    {
        foreach (var item in ContextSelection.ToList())
        {
            if (item.Parent == null)
            {
                if (item is IPersistentItem persistent)
                {
                    _pboManager.Close(persistent);
                }
            }
            else
            {
                _pboManager.Hidden.Add(item.Path.TrimEnd('\\'));
                item.Parent.Children.Remove(item);
            }
        }
        SelectedItem = null;
        ClearSelection();
        FilterVersion++;
    }

    public async Task OpenPreview(object item)
    {
        if (item is Models.OutlineNode { File: not null } node)
        {
            await OpenOutline(node);
            return;
        }
        if (item != SelectedItem)
        {
            return; // Handle bubbling
        }

        if (item is FileBase file)
        {
            _previewDelay?.Cancel();
            await _previewManager.ShowPreview(file, pin: true, activate: true);
        }
    }

    // Opens the model on that LOD, with the mesh's texture first in the list.
    private async Task OpenOutline(Models.OutlineNode node)
    {
        await _previewManager.ShowPreview(node.File, pin: true, activate: true);
        var preview = IoC.Get<IShell>().Documents.OfType<P3d.ViewModels.P3dPreviewViewModel>().FirstOrDefault(d => d.Model == node.File);
        if (preview == null)
        {
            return;
        }
        preview.FocusTexture = node.Texture;
        preview.SelectedLod = preview.ModelLods.FirstOrDefault(l => l.Index == node.LodIndex) ?? preview.SelectedLod;
    }

    private CancellationTokenSource _previewDelay;
    private ITreeItem _skipPreviewFor;

    public async void OnSelectedItemChanged(RoutedPropertyChangedEventArgs<object> args)
    {
        if (args.NewValue is ITreeItem item)
        {
            SelectedItem = item;
            await _metadataInspector.DispalyMetadataFor(item);
            await PreviewOnSelect(item);
        }
    }

    /// <summary>Single click (or arrowing through the tree) shows the file in the shared preview tab.</summary>
    private async Task PreviewOnSelect(ITreeItem item)
    {
        _previewDelay?.Cancel();
        if (item == _skipPreviewFor)
        {
            _skipPreviewFor = null;
            return;
        }
        if (!AppSettings.Default.SingleClickPreview || item is not FileBase file || _selection.Count > 1)
        {
            return;
        }
        var delay = _previewDelay = new CancellationTokenSource();
        try
        {
            await Task.Delay(40, delay.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }
        if (SelectedItem == item && _selection.Count <= 1)
        {
            await _previewManager.ShowPreview(file, pin: false, activate: false);
        }
    }

    public void OnDragOver(DragEventArgs e)
    {
        // Prevent drop of extracted files
        // Drop from Explorer has Copy|Move|Link effects
        // Drop from app has only Copy
        if (e.Effects == DragDropEffects.Copy)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }
    }

    public async Task OnDrop(DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] paths = (string[])e.Data.GetData(DataFormats.FileDrop);

            await _pboManager.LoadSupportedFiles(paths);
        }
    }

    private void RunBulkExport()
    {
        var roots = _selection.Count > 0 ? _selection.ToList() : new List<ITreeItem>();
        if (roots.Count == 0 && _selectedItem != null)
        {
            roots.Add(_selectedItem);
        }
        if (roots.Count == 0)
        {
            roots.AddRange(Items);
        }

        var files = BulkExportService.Collect(roots, IsSelectable);
        if (files.Count == 0)
        {
            MessageBox.Show(Loc.T("BulkExport.NothingSelected"), Loc.T("BulkExport.Title"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new BulkExportDialog(files, ExportOptions);
        dialog.Closed += (_, _) => ExportOptionsStore.Save(ExportOptions);
        dialog.Show();
    }

    private string MaterializeForDrag(ITreeItem item)
    {
        switch (item)
        {
            case PhysicalFile physical:
                return physical.FullPath;
            case PhysicalDirectory directory:
                return directory.FullPath;
            case PboFile pbo:
                return pbo.Path;
            case PboEntry entry:
                return ExtractToTemp(entry);
            case PboDirectory folder:
                return ExtractFolderToTemp(folder);
            default:
                return null;
        }
    }

    private static string TempFolderFor(string key)
    {
        var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(key)))[..12];
        var directory = Path.Combine(Path.GetTempPath(), "PboSpy", hash);
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string ExtractToTemp(PboEntry entry)
    {
        // Entry names are not unique across folders and obfuscated PBOs use characters
        // that Windows rejects, so drag and drop needs a per-entry folder and a safe name.
        var directory = TempFolderFor(entry.FullPath);

        if (AppSettings.Default.DragOutConvertsImages && entry.Extension is ".paa" or ".pac")
        {
            var options = new ExportOptions { OutputDirectory = directory, PreserveStructure = false, ConvertImages = true };
            BulkExportService.Run(new[] { entry }, options, (_, _) => { }, CancellationToken.None);
            var png = Path.Combine(directory, Path.ChangeExtension(
                BulkExportService.SanitizeSegment(entry.Name, NameSanitizeMode.Underscore), options.ImageExtension));
            if (File.Exists(png))
            {
                return png;
            }
        }

        var name = BulkExportService.SanitizeSegment(entry.Name, NameSanitizeMode.Underscore);
        var tempFilePath = Path.Combine(directory, name);

        if (!File.Exists(tempFilePath) || new FileInfo(tempFilePath).Length != entry.DataSize)
        {
            entry.Extract(tempFilePath);
        }

        return tempFilePath;
    }

    private string ExtractFolderToTemp(PboDirectory folder)
    {
        var pbo = PboOf(folder);
        if (pbo == null)
        {
            return null;
        }

        var relative = RelativeInsidePbo(folder);
        var target = Path.Combine(TempFolderFor(folder.Path),
            BulkExportService.SanitizeSegment(folder.Name, NameSanitizeMode.Underscore));
        var options = new ExportOptions
        {
            OutputDirectory = target,
            Preset = ContentPreset.Everything,
            PreserveStructure = true,
            ConvertImages = false,
            ConvertConfigs = false,
            ConvertAudio = false,
            ExistingFileAction = ExistingFileAction.Skip,
            RootPathToStrip = string.IsNullOrEmpty(pbo.PBO.Prefix) ? relative : pbo.PBO.Prefix + "\\" + relative
        };
        BulkExportService.Run(BulkExportService.Collect(new[] { folder }, IsSelectable), options, (_, _) => { }, CancellationToken.None);
        return target;
    }

    private static PboFile PboOf(ITreeItem item)
    {
        for (var current = item; current != null; current = current.Parent)
        {
            if (current is PboFile pbo)
            {
                return pbo;
            }
        }
        return null;
    }

    private static string RelativeInsidePbo(ITreeItem item)
    {
        var parts = new List<string>();
        for (var current = item; current != null && current is not PboFile; current = current.Parent)
        {
            if (!string.IsNullOrEmpty(current.Name))
            {
                parts.Insert(0, current.Name);
            }
        }
        return string.Join("\\", parts);
    }

    // Full Windows path; for files inside a PBO, where they land when the PBO is extracted next to itself.
    private static string PathOf(ITreeItem item)
    {
        var pbo = PboOf(item);
        if (pbo != null && item is not PboFile)
        {
            var folder = Path.GetDirectoryName(pbo.Path) ?? "";
            var inside = item is PboEntry entry ? entry.ExportFullPath
                : (string.IsNullOrEmpty(pbo.PBO.Prefix) ? "" : pbo.PBO.Prefix + "\\") + RelativeInsidePbo(item);
            return Path.Combine(folder, inside.Replace('/', '\\').TrimStart('\\'));
        }
        return item switch
        {
            FileBase file => file.FullPath,
            _ => item.Path
        };
    }

    public void CopyPathInPbo()
    {
        var items = _selection.Count > 0 ? _selection.ToList() : new List<ITreeItem> { _selectedItem };
        var text = string.Join(Environment.NewLine, items.Where(i => i != null).Select(i => i is PboEntry e ? e.ExportFullPath : i.Path));
        if (!string.IsNullOrEmpty(text))
        {
            Clipboard.SetText(text);
        }
    }

    private static ITreeItem RootOf(ITreeItem item)
    {
        var current = item;
        while (current.Parent != null)
        {
            current = current.Parent;
        }
        return current;
    }

    private bool IsStillLoaded(ITreeItem item)
    {
        var root = RootOf(item);
        return Items.Contains(root);
    }

    private static string ExtensionOf(ITreeItem item) => Path.GetExtension(item.Name ?? "").ToLowerInvariant();

    private static IEnumerable<ITreeItem> Flatten(IEnumerable<ITreeItem> roots)
    {
        var stack = new Stack<ITreeItem>(roots.Where(r => r != null).Reverse());
        while (stack.Count > 0)
        {
            var item = stack.Pop();
            yield return item;
            var children = item.Children;
            if (children == null)
            {
                continue;
            }
            foreach (var child in children.Reverse())
            {
                stack.Push(child);
            }
        }
    }

    private void UpdateSummary()
    {
        if (_selection.Count == 0)
        {
            SelectionSummary = "";
            return;
        }
        var files = BulkExportService.Collect(_selection, IsSelectable).Count;
        SelectionSummary = Loc.F("Explorer.SelectionSummary", _selection.Count, files);
    }

    void ICommandHandler<BulkExportCommandDefinition>.Update(Command command)
        => command.Enabled = Items.Count > 0;

    Task ICommandHandler<BulkExportCommandDefinition>.Run(Command command)
    {
        RunBulkExport();
        return Task.CompletedTask;
    }

    void ICommandHandler<CloseFileCommandDefinition>.Update(Command command)
        => command.Enabled = CanCloseSelected || _selection.OfType<IPersistentItem>().Any();

    Task ICommandHandler<CloseFileCommandDefinition>.Run(Command command)
    {
        CloseSelected();
        return Task.CompletedTask;
    }

    void ICommandHandler<CloseAllFilesCommandDefinition>.Update(Command command)
        => command.Enabled = true;

    Task ICommandHandler<CloseAllFilesCommandDefinition>.Run(Command command)
    {
        _pboManager.CloseAll();
        SelectedItem = null;
        ClearSelection();
        return Task.CompletedTask;
    }
}
