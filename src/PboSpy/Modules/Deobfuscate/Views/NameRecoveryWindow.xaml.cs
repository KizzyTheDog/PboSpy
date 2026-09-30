using PboSpy.Localization;
using PboSpy.Modules.Deobfuscate.Core;
using PboSpy.Modules.Pbo.Models;
using PboSpy.Modules.Windows;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Data;

namespace PboSpy.Modules.Deobfuscate.Views;

public sealed class NameRow : INotifyPropertyChanged
{
    private bool _apply = true;
    private string _suggested;

    public PboSpy.Models.FileBase File { get; init; }
    public PboEntry Entry => File as PboEntry;
    public string Stored { get; init; }
    public string Current { get; init; }
    public NameSource Source { get; init; }
    public string Reason { get; init; }

    public string SourceText => Loc.T("Names.Source." + Source);

    public bool Apply { get => _apply; set => Set(ref _apply, value); }

    public string Suggested
    {
        get => _suggested;
        set => Set(ref _suggested, (value ?? "").Replace('/', '\\').Trim('\\'));
    }

    public event PropertyChangedEventHandler PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
    {
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public partial class NameRecoveryWindow : ToolWindow
{
    private readonly PboFile _pbo;
    private readonly string _root;
    private readonly HashSet<string> _only;
    private readonly List<NameRow> _rows = new();
    private ICollectionView _view;
    private CancellationTokenSource _cancel;
    private string _prefix = "";

    private NameRecoveryWindow(PboFile pbo, string root, IEnumerable<string> only)
    {
        _pbo = pbo;
        _root = root;
        _only = only?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        InitializeComponent();
        Title = Loc.T("Names.Title") + " - " + (pbo?.Name ?? root);
        TitleText.Text = Loc.T("Names.Title") + " - " + (pbo?.Name ?? Path.GetFileName(root.TrimEnd('\\')));
        if (pbo == null)
        {
            ApplyButton.Content = Loc.T("Names.RenameHere");
            ApplyButton.ToolTip = Loc.T("Names.RenameHereTip");
            ExtractButton.Content = Loc.T("Names.CopyRenamed");
        }
        Loaded += async (_, _) => await Analyse();
        Closed += (_, _) => _cancel?.Cancel();
    }

    protected override string PlacementKey => nameof(NameRecoveryWindow);

    public static void Open(PboFile pbo, IEnumerable<string> onlyEntries = null) => Show(new NameRecoveryWindow(pbo, null, onlyEntries));

    /// <summary>A folder on disk (an unpacked addon); <paramref name="onlyFiles"/> limits the rows shown.</summary>
    public static void OpenFolder(string root, IEnumerable<string> onlyFiles = null) => Show(new NameRecoveryWindow(null, root, onlyFiles));

    private static void Show(NameRecoveryWindow window)
    {
        window.Show();
        window.Activate();
    }

    /// <summary>Asks for an unpacked addon folder.</summary>
    public static void PickFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("Names.PickFolder") };
        if (dialog.ShowDialog(Application.Current?.MainWindow) == true)
        {
            OpenFolder(dialog.FolderName);
        }
    }

    /// <summary>True when the PBO has scrambled names worth offering the window for.</summary>
    public static bool IsScrambled(PboFile pbo) =>
        pbo.PBO.PropertiesPairs.Any(p => p.Key.Equals("obfuscated", StringComparison.OrdinalIgnoreCase)) ||
        pbo.AllEntries.Any(e => NameRecovery.IsScrambled(e.Entry.FileName));

    // The folder that stands for the PBO prefix: $PBOPREFIX$ if there is one, else the folder name.
    private static string PrefixOf(string root)
    {
        foreach (var name in new[] { "$PBOPREFIX$", "$PBOPREFIX$.txt", "$PREFIX$" })
        {
            var file = Path.Combine(root, name);
            if (File.Exists(file))
            {
                var line = File.ReadLines(file).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
                var eq = line.IndexOf('=');
                return (eq >= 0 ? line[(eq + 1)..] : line).Trim().Trim('\\', '/');
            }
        }
        return Path.GetFileName(root.TrimEnd('\\', '/'));
    }

    private async Task Analyse()
    {
        _cancel = new CancellationTokenSource();
        var token = _cancel.Token;
        Progress.Visibility = Visibility.Visible;
        StatusText.Text = Loc.T("Names.Working");

        Dictionary<string, PboSpy.Models.FileBase> byPath;
        List<NameInput> inputs;
        List<KeyValuePair<string, string>> properties;
        if (_pbo != null)
        {
            byPath = _pbo.AllEntries.GroupBy(e => e.Entry.FileName).ToDictionary(g => g.Key, g => (PboSpy.Models.FileBase)g.First());
            inputs = _pbo.PBO.Files.Select(f => new NameInput(f.FileName, f.Size, f.OpenRead)).ToList();
            _prefix = _pbo.PBO.Prefix ?? "";
            properties = _pbo.PBO.PropertiesPairs.ToList();
        }
        else
        {
            _prefix = PrefixOf(_root);
            var files = await Task.Run(() => Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).ToList());
            byPath = files.ToDictionary(f => Path.GetRelativePath(_root, f), f => (PboSpy.Models.FileBase)new PboSpy.Models.PhysicalFile(f), StringComparer.OrdinalIgnoreCase);
            inputs = byPath.Select(p => new NameInput(p.Key, new FileInfo(((PboSpy.Models.PhysicalFile)p.Value).FullPath).Length, p.Value.GetStream)).ToList();
            properties = new();
        }

        NameReport report;
        try
        {
            var prefix = _prefix;
            report = await Task.Run(() => NameRecovery.Analyse(prefix, inputs, properties,
                stage => Dispatcher.BeginInvoke(() => StatusText.Text = Loc.T("Names.Stage." + stage)), token), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Progress.Visibility = Visibility.Collapsed;
            StatusText.Text = ex.Message;
            return;
        }

        Progress.Visibility = Visibility.Collapsed;
        foreach (var item in report.Items)
        {
            if (!byPath.TryGetValue(item.Input.Path, out var file))
            {
                continue;
            }
            if (_only != null && !_only.Contains(file.FullPath) && !(file is PboEntry pe && _only.Contains(pe.Entry.FileName)))
            {
                continue;
            }
            var entry = file as PboEntry;
            _rows.Add(new NameRow
            {
                File = file,
                Stored = item.Current,
                Current = item.Current,
                Source = item.Source,
                Reason = item.Reason,
                Suggested = entry?.RenamedPath ?? item.Suggested,
                Apply = item.Source != NameSource.Fallback || entry?.IsRenamed == true
            });
        }

        FindingsList.ItemsSource = Findings(report);
        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = Matches;
        NamesGrid.ItemsSource = _view;
        ApplyButton.IsEnabled = ExtractButton.IsEnabled = SaveListButton.IsEnabled = _rows.Count > 0;
        StatusText.Text = Loc.F("Names.Summary", _rows.Count, _rows.Count(r => r.Source != NameSource.Fallback));
    }

    private List<string> Findings(NameReport report)
    {
        var list = new List<string>();
        var scrambled = report.Items.Count;
        if (scrambled == 0)
        {
            list.Add(Loc.T("Names.NoneFound"));
            return list;
        }
        list.Add(Loc.F("Names.FoundScrambled", scrambled, report.Total, report.MinLength, report.MaxLength));
        if (report.WithWildcard > 0)
        {
            list.Add(Loc.F("Names.FoundWildcards", report.WithWildcard));
        }
        list.Add(Loc.T("Names.FoundKept"));
        if (report.Decoys > 0)
        {
            var names = string.Join("  ", report.DecoyNames.Take(5).Select(n => "\"" + n.Replace("\t", "\\t") + "\""));
            list.Add(Loc.F("Names.FoundDecoys", report.Decoys, names));
        }
        if (report.ObfuscatorTag.Length > 0 || report.PackedWith.Length > 0)
        {
            list.Add(Loc.F("Names.FoundHeader", report.ObfuscatorTag, report.PackedWith));
        }
        var from = string.Join(", ", report.BySource.Where(p => p.Key != NameSource.Fallback).OrderByDescending(p => p.Value)
            .Select(p => $"{Loc.T("Names.Source." + p.Key)} {p.Value}"));
        list.Add(Loc.F("Names.Recovered", scrambled - System.Collections.Generic.CollectionExtensions.GetValueOrDefault(report.BySource, NameSource.Fallback), scrambled, from));
        list.Add(Loc.T("Names.NotReversible"));
        return list;
    }

    private bool Matches(object item)
    {
        if (item is not NameRow row)
        {
            return false;
        }
        if (OnlyGuessed.IsChecked == true && row.Source is not (NameSource.Fallback or NameSource.Model or NameSource.Selection))
        {
            return false;
        }
        var filter = FilterBox.Text.Trim();
        return filter.Length == 0 ||
               row.Current.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               row.Suggested.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               (row.Reason ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e) => _view?.Refresh();

    private int ApplyNames()
    {
        NamesGrid.CommitEdit();
        var applied = 0;
        foreach (var row in _rows.Where(r => r.Entry != null))
        {
            if (row.Apply && row.Suggested.Length > 0)
            {
                row.Entry.RenamedPath = row.Suggested;
                applied++;
            }
            else
            {
                row.Entry.RenamedPath = null;
            }
        }
        return applied;
    }

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        if (_pbo != null)
        {
            var applied = ApplyNames();
            StatusText.Text = Loc.F("Names.Applied", applied);
            return;
        }
        // In place: write the renamed copy next to the folder, then swap it in and keep the old one as a backup.
        var parent = Path.GetDirectoryName(_root.TrimEnd('\\')) ?? _root;
        var name = Path.GetFileName(_root.TrimEnd('\\'));
        var staging = UniqueFolder(Path.Combine(parent, name + "_renaming"));
        var backup = UniqueFolder(Path.Combine(parent, name + "_original"));
        if (!MessageDialog.Ask(this, Loc.T("Names.Title"), Loc.F("Names.RenameHereConfirm", _root, backup)))
        {
            return;
        }
        if (await RunDisk(staging))
        {
            try
            {
                Directory.Move(_root, backup);
                Directory.Move(staging, _root);
                StatusText.Text += "  " + Loc.F("Names.BackupAt", backup);
            }
            catch (Exception ex)
            {
                StatusText.Text = ex.Message + "  (" + staging + ")";
            }
        }
    }

    private static string UniqueFolder(string path)
    {
        var candidate = path;
        for (var i = 2; Directory.Exists(candidate) || File.Exists(candidate); i++)
        {
            candidate = path + "_" + i;
        }
        return candidate;
    }

    private async void OnApplyExtract(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = ExtractButton.Content?.ToString() };
        if (_root != null)
        {
            dialog.InitialDirectory = Path.GetDirectoryName(_root.TrimEnd('\\'));
        }
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }
        var folder = dialog.FolderName;
        if (_pbo != null)
        {
            ApplyNames();
            await Run(() => RenamedExtract.Run(_pbo, folder, ReportProgress, _cancel.Token), folder);
        }
        else
        {
            var target = UniqueFolder(Path.Combine(folder, Path.GetFileName(_root.TrimEnd('\\')) + "_renamed"));
            await RunDisk(target);
        }
    }

    private void ReportProgress(int i, int n, string name) => Dispatcher.BeginInvoke(() => StatusText.Text = $"{i}/{n}  {name}");

    private Task<bool> RunDisk(string target)
    {
        NamesGrid.CommitEdit();
        var renamed = _rows.Where(r => r.Apply && r.Suggested.Length > 0)
            .ToDictionary(r => r.File.FullPath, r => r.Suggested, StringComparer.OrdinalIgnoreCase);
        var items = Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).Select(f =>
        {
            var relative = Path.GetRelativePath(_root, f);
            return new RenameItem(new PboSpy.Models.PhysicalFile(f), relative, renamed.TryGetValue(f, out var to) ? to : null);
        }).ToList();
        var prefix = _prefix;
        return Run(() => RenamedExtract.Run(items, prefix, target, target, ReportProgress, _cancel.Token), target);
    }

    private async Task<bool> Run(Func<RenamedExtractReport> work, string folder)
    {
        _cancel = new CancellationTokenSource();
        ApplyButton.IsEnabled = ExtractButton.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        try
        {
            var report = await Task.Run(work, _cancel.Token);
            StatusText.Text = Loc.F("Names.Extracted", report.Written, report.References, report.Rewritten);
            var message = StatusText.Text + (report.Problems.Count > 0
                ? "\n\n" + string.Join("\n", report.Problems.Take(12)) + (report.Problems.Count > 12 ? "\n…" : "")
                : "");
            if (MessageDialog.Ask(this, Loc.T("Names.Title"), message + "\n\n" + Loc.T("P3D.Tp.AskOpenFolder")))
            {
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = Loc.T("Common.Cancel");
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            Progress.Visibility = Visibility.Collapsed;
            ApplyButton.IsEnabled = ExtractButton.IsEnabled = true;
        }
        return false;
    }

    private void OnSaveList(object sender, RoutedEventArgs e)
    {
        NamesGrid.CommitEdit();
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = Path.GetFileNameWithoutExtension(_pbo?.Name ?? Path.GetFileName(_root.TrimEnd('\\'))) + "_names.csv",
            Filter = "CSV|*.csv"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }
        var csv = new StringBuilder("current;new;found from;where\n");
        foreach (var row in _rows)
        {
            csv.Append(Quote(row.Current)).Append(';').Append(Quote(row.Suggested)).Append(';')
               .Append(Quote(row.SourceText)).Append(';').Append(Quote(row.Reason)).Append('\n');
        }
        File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(true));
        StatusText.Text = dialog.FileName;
    }

    private static string Quote(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
}
