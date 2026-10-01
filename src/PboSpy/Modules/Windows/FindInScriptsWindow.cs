using PboSpy.Interfaces;
using PboSpy.Localization;
using PboSpy.Models;
using PboSpy.Modules.BinaryConfig.Utils;
using PboSpy.Modules.FileManager;
using PboSpy.Modules.Pbo.Models;
using PboSpy.Modules.Preview;
using PboSpy.Modules.Preview.ViewModels;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PboSpy.Modules.Windows;

/// <summary>Searches the text of every script and config in the opened PBOs and folders, like Roblox Studio's Find All.</summary>
public class FindInScriptsWindow : ToolWindow
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".sqf", ".sqs", ".fsm", ".hpp", ".h", ".inc", ".cpp", ".ext", ".cfg", ".sqm", ".bin", ".rvmat", ".txt", ".xml", ".csv" };

    private static FindInScriptsWindow _open;
    private readonly TextBox _query = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly CheckBox _case = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly ListBox _results = new();
    private readonly TextBlock _status = new() { Margin = new Thickness(0, 6, 0, 0) };
    private CancellationTokenSource _search;

    private sealed record Hit(FileBase File, string Path, int Line, string Text, string Term)
    {
        public override string ToString() => $"{Path}:{Line}   {Text}";
    }

    public static void Open()
    {
        if (_open == null)
        {
            _open = new FindInScriptsWindow();
            _open.Closed += (_, _) => _open = null;
            _open.Show();
        }
        _open.Activate();
        _open._query.Focus();
        _open._query.SelectAll();
    }

    private FindInScriptsWindow()
    {
        Title = Loc.T("Find.Title");
        Width = 820;
        Height = 560;
        _case.Content = Loc.T("Find.MatchCase");
        _results.SetResourceReference(StyleProperty, "PboSpy.ListBox");
        _results.FontFamily = new System.Windows.Media.FontFamily("Consolas");
        _results.MouseDoubleClick += async (_, _) => await OpenHit(true);
        _results.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                await OpenHit(true);
            }
        };
        // One click shows it in the reusable preview tab; double click or Enter keeps it open.
        _results.SelectionChanged += async (_, _) => await OpenHit(false);
        _query.TextChanged += (_, _) => Search();
        _case.Click += (_, _) => Search();

        var panel = new DockPanel { Margin = new Thickness(12) };
        var hint = new TextBlock { Text = Loc.T("Find.Hint"), Margin = new Thickness(0, 0, 0, 6) };
        hint.SetResourceReference(ForegroundProperty, "PboSpy.TextDim");
        DockPanel.SetDock(hint, Dock.Top);
        DockPanel.SetDock(_query, Dock.Top);
        DockPanel.SetDock(_case, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        panel.Children.Add(hint);
        panel.Children.Add(_query);
        panel.Children.Add(_case);
        panel.Children.Add(_status);
        panel.Children.Add(_results);
        Content = panel;
    }

    private async void Search()
    {
        _search?.Cancel();
        var term = _query.Text;
        _results.ItemsSource = null;
        if (term.Length < 2)
        {
            _status.Text = "";
            return;
        }
        var cancel = (_search = new CancellationTokenSource()).Token;
        var comparison = _case.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        try
        {
            await Task.Delay(250, cancel);
            _status.Text = Loc.T("Find.Searching");
            var files = Files(IoC.Get<IFileManager>().FileTree).ToList();
            var hits = await Task.Run(() => files.AsParallel().WithCancellation(cancel).AsOrdered()
                .SelectMany(file => Scan(file, term, comparison)).Take(5000).ToList(), cancel);
            _results.ItemsSource = hits;
            _status.Text = Loc.F("Find.Count", hits.Count, hits.Select(h => h.File).Distinct().Count(), files.Count);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static IEnumerable<FileBase> Files(IEnumerable<ITreeItem> items)
    {
        foreach (var item in items ?? Enumerable.Empty<ITreeItem>())
        {
            switch (item)
            {
                case PboFile pbo:
                    foreach (var entry in pbo.AllEntries.Where(e => Extensions.Contains(e.Extension)))
                    {
                        yield return entry;
                    }
                    break;
                case FileBase file when Extensions.Contains(file.Extension):
                    yield return file;
                    break;
                default:
                    foreach (var child in Files(item.Children?.ToList()))
                    {
                        yield return child;
                    }
                    break;
            }
        }
    }

    private static IEnumerable<Hit> Scan(FileBase file, string term, StringComparison comparison)
    {
        string text;
        try
        {
            if (file.Extension is ".bin" or ".rvmat" or ".sqm" or ".cfg")
            {
                text = file.GetDetectConfigAsText(out _);
            }
            else
            {
                using var reader = new StreamReader(file.GetStream());
                text = reader.ReadToEnd();
            }
        }
        catch (Exception)
        {
            yield break;
        }
        if (text == null || text.IndexOf(term, comparison) < 0)
        {
            yield break;
        }
        var number = 0;
        foreach (var line in text.Split('\n'))
        {
            number++;
            if (line.IndexOf(term, comparison) >= 0)
            {
                var shown = line.Trim();
                yield return new Hit(file, file.FullPath, number, shown.Length > 200 ? shown[..200] + "…" : shown, term);
            }
        }
    }

    private async Task OpenHit(bool pin)
    {
        if (_results.SelectedItem is not Hit hit)
        {
            return;
        }
        await IoC.Get<IPreviewManager>().ShowPreview(hit.File, pin, activate: true);
        IoC.Get<IShell>().Documents.OfType<TextPreviewViewModel>().FirstOrDefault(d => d.Model == hit.File)?.GoTo(hit.Line, hit.Term);
    }
}
