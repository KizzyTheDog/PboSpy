using Microsoft.Win32;
using PboSpy.Modules.P3dTools.Core;
using PboSpy.Modules.Windows;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace PboSpy.Modules.P3dTools.Views;

/// <summary>Shows rebuilt RVMATs / model.cfg and lets them be saved.</summary>
public partial class TextPreviewWindow : ToolWindow
{
    private readonly List<OdolExtract.TextItem> _items;
    private readonly string _outputFolder;

    public TextPreviewWindow(string title, List<OdolExtract.TextItem> items, string outputFolder)
    {
        InitializeComponent();
        Title = title;
        _items = items;
        _outputFolder = outputFolder;
        InfoText.Text = Strings.T("Tp.Info", items.Count);

        if (items.Count > 1)
        {
            ItemsList.ItemsSource = items;
            ItemsList.SelectedIndex = 0;
        }
        else
        {
            ItemsList.Visibility = Visibility.Collapsed;
            ListColumn.Width = new GridLength(0);
            if (items.Count == 1)
            {
                Show(items[0]);
            }
        }
    }

    protected override bool RememberPlacement => false;

    private OdolExtract.TextItem Current => _items.Count == 1 ? _items[0] : ItemsList.SelectedItem as OdolExtract.TextItem;

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Current is { } item)
        {
            Show(item);
        }
    }

    // The rebuilt text may already use \r\n; normalise so lines don't double up.
    private void Show(OdolExtract.TextItem item) =>
        ContentBox.Text = item.Content.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(ContentBox.Text);
        }
        catch (Exception)
        {
        }
    }

    private void OnSaveAs(object sender, RoutedEventArgs e)
    {
        if (Current is not { } item)
        {
            return;
        }
        var dialog = new SaveFileDialog
        {
            FileName = Path.GetFileName(item.Name),
            Filter = $"{Path.GetExtension(item.Name)}|*{Path.GetExtension(item.Name)}|*.*|*.*"
        };
        if (Directory.Exists(_outputFolder))
        {
            dialog.InitialDirectory = _outputFolder;
        }
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }
        try
        {
            File.WriteAllText(dialog.FileName, item.Content);
        }
        catch (Exception ex)
        {
            MessageDialog.Info(this, Title, Strings.T("Tp.MsgSaveFailed", ex.Message));
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_outputFolder);
        }
        catch (Exception)
        {
            MessageDialog.Info(this, Title, Strings.T("Tp.MsgInvalidOutput"));
            return;
        }
        try
        {
            foreach (var item in _items)
            {
                var target = Path.Combine(_outputFolder, item.Name);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, item.Content);
            }
            if (MessageDialog.Ask(this, Title, Strings.T("Tp.MsgSaved", _items.Count, _outputFolder) + "\n\n" + Strings.T("Tp.AskOpenFolder")))
            {
                Process.Start("explorer.exe", $"\"{_outputFolder}\"");
            }
        }
        catch (Exception ex)
        {
            MessageDialog.Info(this, Title, Strings.T("Tp.MsgSaveFailed", ex.Message));
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
