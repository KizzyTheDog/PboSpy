using Microsoft.Win32;
using PboSpy.Modules.P3dTools.Core;
using PboSpy.Modules.Windows;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PboSpy.Modules.P3dTools.Views;

/// <summary>
/// Lists the loaded models and the union of the paths they reference; paths can be edited one by
/// one or rewritten with prefix / regex rules, which can be exported and imported again.
/// </summary>
public partial class ChangePathsWindow : ToolWindow
{
    public sealed class FileRow : INotifyPropertyChanged
    {
        private bool _isChecked = true;
        public string FullPath { get; init; }
        public IReadOnlyList<string> Paths { get; init; }
        public string Name => Path.GetFileName(FullPath);
        public bool IsChecked { get => _isChecked; set { _isChecked = value; OnChanged(); } }
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class PathRow : INotifyPropertyChanged
    {
        private string _new;
        public string Current { get; init; }
        public string New { get => _new; set { _new = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(New))); } }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    private readonly List<FileRow> _files;
    private readonly Dictionary<string, string> _edits = new(StringComparer.Ordinal);
    private readonly List<RepathRules.Rule> _prefixRules = new();
    private List<PathRow> _rows = new();
    private bool _suspend;

    public List<(string From, string To)> Replacements { get; } = new();
    public List<string> SelectedFiles { get; } = new();
    public IReadOnlyList<RepathRules.Rule> AppliedPrefixRules => _prefixRules;

    public ChangePathsWindow(IEnumerable<(string FilePath, IReadOnlyList<string> Paths)> files)
    {
        InitializeComponent();
        _files = files.Select(f => new FileRow { FullPath = f.FilePath, Paths = f.Paths }).ToList();
        FilesList.ItemsSource = _files;
        RebuildGrid();
    }

    protected override bool RememberPlacement => false;

    public void PreloadRules(IEnumerable<RepathRules.Rule> rules)
    {
        foreach (var rule in rules)
        {
            ApplyPrefixRule(rule);
            _prefixRules.Add(rule);
        }
    }

    private void OnFileToggled(object sender, RoutedEventArgs e)
    {
        if (!_suspend)
        {
            RebuildGrid();
        }
    }

    private void OnCheckAll(object sender, RoutedEventArgs e) => SetAll(true);

    private void OnCheckNone(object sender, RoutedEventArgs e) => SetAll(false);

    private void SetAll(bool value)
    {
        _suspend = true;
        foreach (var file in _files)
        {
            file.IsChecked = value;
        }
        _suspend = false;
        RebuildGrid();
    }

    private void RebuildGrid()
    {
        PathsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var union = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in _files.Where(f => f.IsChecked))
        {
            foreach (var path in file.Paths)
            {
                union.Add(path);
            }
        }
        _rows = union.Select(p => new PathRow { Current = p, New = _edits.TryGetValue(p, out var edited) ? edited : p }).ToList();
        PathsGrid.ItemsSource = _rows;
        PathsHeader.Text = Strings.T("Cp.LblPathsHeaderCount", union.Count);
    }

    private void OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit || e.Row.Item is not PathRow row || e.EditingElement is not TextBox box)
        {
            return;
        }
        var value = box.Text.Trim();
        if (value.Length == 0 || string.Equals(value, row.Current, StringComparison.Ordinal))
        {
            _edits.Remove(row.Current);
        }
        else
        {
            _edits[row.Current] = value;
        }
    }

    private void OnGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PathsGrid.CurrentCell.Column is DataGridTextColumn column && PathsGrid.CurrentCell.Item is PathRow row && column.IsReadOnly)
        {
            try
            {
                Clipboard.SetText(row.Current);
                Flash(Strings.T("Cp.MsgCopied", row.Current));
            }
            catch (Exception)
            {
            }
        }
    }

    private void OnApplySegment(object sender, RoutedEventArgs e)
    {
        var isRegex = RegexCheck.IsChecked == true;
        var from = FromBox.Text.Trim();
        var to = ToBox.Text.Trim();
        if (!isRegex)
        {
            from = from.TrimStart('\\');
            to = to.TrimStart('\\');
        }
        if (from.Length == 0)
        {
            MessageDialog.Info(this, Title, Strings.T("Cp.MsgNeedFrom"));
            return;
        }
        var rule = new RepathRules.Rule(from, to, isRegex);
        var affected = ApplyPrefixRule(rule);
        _prefixRules.Add(rule);
        Flash(Strings.T("Cp.MsgSegmentApplied", affected));
    }

    private int ApplyPrefixRule(RepathRules.Rule rule)
    {
        PathsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var affected = 0;
        foreach (var row in _rows)
        {
            if (RepathRules.TryApply(rule, row.New ?? row.Current, out var updated) && !string.Equals(updated, row.New, StringComparison.Ordinal))
            {
                row.New = updated;
                _edits[row.Current] = updated;
                affected++;
            }
        }
        return affected;
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = Strings.T("Cp.DlgImportTitle"), Filter = Strings.T("Cp.DlgListFilter") };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }
        try
        {
            var rules = RepathRules.Load(dialog.FileName);
            var total = 0;
            foreach (var rule in rules)
            {
                total += ApplyPrefixRule(rule);
                _prefixRules.Add(rule);
            }
            Flash(Strings.T("Cp.MsgImported", rules.Count, total));
        }
        catch (Exception ex)
        {
            MessageDialog.Info(this, Title, Strings.T("Cp.MsgImportFailed", ex.Message));
        }
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        if (_prefixRules.Count == 0)
        {
            MessageDialog.Info(this, Title, Strings.T("Cp.MsgNoRulesToExport"));
            return;
        }
        var dialog = new SaveFileDialog
        {
            Title = Strings.T("Cp.DlgExportTitle"),
            Filter = Strings.T("Cp.DlgExportFilter"),
            FileName = Strings.T("Cp.DlgExportDefault")
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }
        try
        {
            RepathRules.Save(dialog.FileName, _prefixRules);
            Flash(Strings.T("Cp.MsgExported", _prefixRules.Count));
        }
        catch (Exception ex)
        {
            MessageDialog.Info(this, Title, Strings.T("Cp.MsgExportFailed", ex.Message));
        }
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        PathsGrid.CommitEdit(DataGridEditingUnit.Row, true);

        SelectedFiles.Clear();
        SelectedFiles.AddRange(_files.Where(f => f.IsChecked).Select(f => f.FullPath));
        if (SelectedFiles.Count == 0)
        {
            MessageDialog.Info(this, Title, Strings.T("Cp.MsgNoFileChecked"));
            return;
        }

        Replacements.Clear();
        foreach (var row in _rows)
        {
            var value = (row.New ?? "").Trim();
            if (value.Length > 0 && !string.Equals(value, row.Current, StringComparison.Ordinal))
            {
                Replacements.Add((row.Current, value));
            }
        }
        if (Replacements.Count == 0)
        {
            MessageDialog.Info(this, Title, Strings.T("Cp.MsgNoChange"));
            return;
        }
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Flash(string text) => StatusText.Text = text;
}
