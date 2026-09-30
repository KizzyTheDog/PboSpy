using Microsoft.WindowsAPICodePack.Dialogs;
using PboSpy.Localization;
using PboSpy.Models;
using PboSpy.Modules.BulkExport.Models;
using PboSpy.Modules.BulkExport.Services;
using PboSpy.Services;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace PboSpy.Modules.BulkExport.Views;

public partial class BulkExportDialog : Window
{
    private readonly List<SourceRow> _sources = new();
    private readonly ExportOptions _options;
    private CancellationTokenSource _cancellation;

    private sealed class SourceRow
    {
        public string Label;
        public IReadOnlyList<FileBase> Files;
        public string Root;
        public override string ToString() => $"{Label}  ({Files.Count})";
    }

    public BulkExportDialog(IReadOnlyList<FileBase> files, ExportOptions options, string rootToStrip = null, string label = null)
    {
        // Runs next to the main window rather than blocking it, with a themed title bar.
        SourceInitialized += (_, _) => PboSpy.Modules.Windows.TitleBar.Apply(this);
        EventHandler retitle = (_, _) => PboSpy.Modules.Windows.TitleBar.Apply(this);
        PboSpy.Themes.ThemeService.Changed += retitle;
        Closed += (_, _) => PboSpy.Themes.ThemeService.Changed -= retitle;

        InitializeComponent();
        _options = options;
        DataContext = options;

        if (files.Count > 0)
        {
            AddSource(files, rootToStrip, label ?? (files.Count == 1 ? files[0].FullPath : Loc.F("BulkExport.SourceSummary", files.Count)));
        }
        Status.Text = "";
        Loaded += (_, _) => ShowRelevantOptions(AllFiles);
        OpenFolderCheck.IsChecked = AppSettings.Default.OpenFolderAfterExport;
        SelectFilesCheck.IsChecked = AppSettings.Default.SelectFilesAfterExport;
    }

    private IReadOnlyList<FileBase> AllFiles => _sources.SelectMany(s => s.Files).ToList();

    private void AddSource(IReadOnlyList<FileBase> files, string root, string label)
    {
        _sources.Add(new SourceRow { Files = files, Root = root, Label = label });
        RefreshSources();
    }

    private void RefreshSources()
    {
        SourceList.ItemsSource = null;
        SourceList.ItemsSource = _sources;
        var files = AllFiles;
        var byType = files.GroupBy(f => f.Extension).OrderByDescending(g => g.Count()).Take(6)
            .Select(g => $"{g.Count()} {(string.IsNullOrEmpty(g.Key) ? "?" : g.Key)}");
        SourceSummary.Text = Loc.F("BulkExport.SourceSummary", files.Count) + "  (" + string.Join(", ", byType) + ")";
        ShowRelevantOptions(files);
    }

    /// <summary>False when a preset already picked the conversions, so they aren't overridden.</summary>
    public bool PickOptionsFromFiles { get; set; } = true;

    // Only the sections that apply to what's being converted, with their conversion ticked.
    private void ShowRelevantOptions(IReadOnlyList<FileBase> files)
    {
        var extensions = files.Select(f => f.Extension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var any = extensions.Count > 0;
        var paa = extensions.Overlaps(new[] { ".paa", ".pac" });
        var images = extensions.Any(PaaWriter.CanConvert);
        var audio = extensions.Any(PboSpy.Modules.Audio.Services.AudioDecoder.IsAudio);
        var configs = extensions.Overlaps(new[] { ".bin", ".rvmat", ".sqm", ".cfg" });
        TexturesGroup.Visibility = !any || paa ? Visibility.Visible : Visibility.Collapsed;
        ToPaaGroup.Visibility = !any || images ? Visibility.Visible : Visibility.Collapsed;
        AudioGroup.Visibility = !any || audio ? Visibility.Visible : Visibility.Collapsed;
        ConfigsGroup.Visibility = !any || configs ? Visibility.Visible : Visibility.Collapsed;
        if (any && PickOptionsFromFiles && IsLoaded)
        {
            _options.ConvertImages = paa;
            _options.ConvertImagesToPaa = images;
            _options.ConvertAudio = audio;
            _options.ConvertConfigs = configs;
        }
    }

    private void OnAddFiles(object sender, RoutedEventArgs e)
    {
        var dialog = new CommonOpenFileDialog { Title = Loc.T("BulkExport.AddFiles"), Multiselect = true, EnsureFileExists = true };
        if (dialog.ShowDialog(this) == CommonFileDialogResult.Ok)
        {
            foreach (var path in dialog.FileNames)
            {
                AddSource(new[] { (FileBase)new PhysicalFile(path) }, null, path);
            }
        }
        Activate();
    }

    private void OnAddFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new CommonOpenFileDialog { Title = Loc.T("BulkExport.AddFolder"), IsFolderPicker = true, Multiselect = true };
        if (dialog.ShowDialog(this) == CommonFileDialogResult.Ok)
        {
            foreach (var folder in dialog.FileNames)
            {
                var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(f => (FileBase)new PhysicalFile(f)).ToList();
                AddSource(files, Path.GetDirectoryName(folder.TrimEnd('\\')), folder);
            }
        }
        Activate();
    }

    private void OnRemoveSource(object sender, RoutedEventArgs e)
    {
        foreach (var row in SourceList.SelectedItems.OfType<SourceRow>().ToList())
        {
            _sources.Remove(row);
        }
        RefreshSources();
    }

    private void OnSourceKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Delete)
        {
            OnRemoveSource(sender, e);
        }
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new CommonOpenFileDialog
        {
            Title = Loc.T("BulkExport.PickFolder"),
            IsFolderPicker = true,
            InitialDirectory = Directory.Exists(_options.OutputDirectory) ? _options.OutputDirectory : null
        };
        // Without an owner the picker returns focus to the main window, which buried this one.
        if (dialog.ShowDialog(this) == CommonFileDialogResult.Ok)
        {
            _options.OutputDirectory = dialog.FileName;
        }
        Activate();
    }

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        if (_cancellation != null)
        {
            _cancellation.Cancel();
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.OutputDirectory))
        {
            MessageBox.Show(this, Loc.T("BulkExport.NeedFolder"), Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var batches = _sources.Select(s => (s.Root, Files: BulkExportService.Filter(s.Files, _options))).Where(b => b.Files.Count > 0).ToList();
        var total = batches.Sum(b => b.Files.Count);
        if (total == 0)
        {
            MessageBox.Show(this, Loc.T("BulkExport.NothingMatches"), Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (AppSettings.Default.ConfirmBeforeOverwrite && _options.ExistingFileAction == ExistingFileAction.Overwrite &&
            Directory.Exists(_options.OutputDirectory) && Directory.EnumerateFileSystemEntries(_options.OutputDirectory).Any() &&
            MessageBox.Show(this, Loc.T("BulkExport.ConfirmOverwrite"), Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;

        OptionsPanel.IsEnabled = false;
        CloseButton.IsEnabled = false;
        StartButton.Content = Loc.T("Common.Cancel");
        Progress.Maximum = total;
        Progress.Value = 0;

        var watch = Stopwatch.StartNew();
        var started = DateTime.Now.AddSeconds(-2);
        var report = new ExportReport();
        var cancelled = false;
        var failures = new List<string>();

        try
        {
            IProgress<(int, string)> progress = new Progress<(int, string)>(update =>
            {
                Progress.Value = update.Item1;
                if (update.Item2 != null)
                {
                    Status.Text = $"{update.Item1}/{total}  {update.Item2}";
                    if (update.Item2.StartsWith("FAILED"))
                    {
                        failures.Add(update.Item2);
                    }
                }
            });

            var rootBefore = _options.RootPathToStrip;
            report = await Task.Run(() =>
            {
                var sum = new ExportReport();
                var done = 0;
                foreach (var batch in batches)
                {
                    _options.RootPathToStrip = batch.Root;
                    var offset = done;
                    var part = BulkExportService.Run(batch.Files, _options, (i, message) => progress.Report((offset + i, message)), token);
                    sum.Written += part.Written;
                    sum.Skipped += part.Skipped;
                    sum.Failed += part.Failed;
                    done += batch.Files.Count;
                }
                return sum;
            });
            _options.RootPathToStrip = rootBefore;
            cancelled = token.IsCancellationRequested;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            watch.Stop();
            _cancellation.Dispose();
            _cancellation = null;
            OptionsPanel.IsEnabled = true;
            CloseButton.IsEnabled = true;
            StartButton.Content = Loc.T("BulkExport.Export");
        }

        ExportOptionsStore.Save(_options);
        AppSettings.Default.OpenFolderAfterExport = OpenFolderCheck.IsChecked == true;
        AppSettings.Default.SelectFilesAfterExport = SelectFilesCheck.IsChecked == true;
        AppSettings.Default.Save();

        Status.Text = cancelled
            ? Loc.F("BulkExport.Cancelled", report.Written)
            : Loc.F("BulkExport.Done", report.Written, report.Skipped, report.Failed, watch.Elapsed.TotalSeconds);

        if (failures.Count > 0)
        {
            WriteFailureLog(failures);
        }

        if (!cancelled && report.Written > 0 && OpenFolderCheck.IsChecked == true && Directory.Exists(_options.OutputDirectory))
        {
            if (SelectFilesCheck.IsChecked != true || !SelectWritten(_options.OutputDirectory, started))
            {
                Process.Start("explorer.exe", $"\"{_options.OutputDirectory}\"");
            }
        }
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindingContext, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern int SHOpenFolderAndSelectItems(IntPtr folder, uint count, IntPtr[] items, uint flags);

    [System.Runtime.InteropServices.DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr pointer);

    // Selects what this run wrote in the folder that got the most of it (Explorer can only select within one folder).
    private static bool SelectWritten(string output, DateTime started)
    {
        var folder = Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories)
            .Where(f => File.GetLastWriteTime(f) >= started && !f.EndsWith("PboSpy_export_errors.txt"))
            .GroupBy(Path.GetDirectoryName).OrderByDescending(g => g.Count()).FirstOrDefault();
        if (folder == null || SHParseDisplayName(folder.Key, IntPtr.Zero, out var parent, 0, out _) != 0)
        {
            return false;
        }
        var items = new List<IntPtr>();
        try
        {
            foreach (var file in folder.Take(500))
            {
                if (SHParseDisplayName(file, IntPtr.Zero, out var item, 0, out _) == 0)
                {
                    items.Add(item);
                }
            }
            return SHOpenFolderAndSelectItems(parent, (uint)items.Count, items.ToArray(), 0) == 0;
        }
        finally
        {
            items.ForEach(CoTaskMemFree);
            CoTaskMemFree(parent);
        }
    }

    private void WriteFailureLog(List<string> failures)
    {
        try
        {
            var log = Path.Combine(_options.OutputDirectory, "PboSpy_export_errors.txt");
            File.WriteAllLines(log, failures);
            Status.Text += "  " + Loc.F("BulkExport.ErrorLog", log);
        }
        catch (IOException)
        {
        }
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        if (_cancellation != null)
        {
            _cancellation.Cancel();
            return;
        }
        Close();
    }
}
