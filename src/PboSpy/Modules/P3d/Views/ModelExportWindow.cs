using Microsoft.WindowsAPICodePack.Dialogs;
using PboSpy.Localization;
using PboSpy.Modules.P3d.Scene;
using PboSpy.Modules.Windows;
using PboSpy.Services;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace PboSpy.Modules.P3d.Views;

/// <summary>Format, texture size, 20k split and folders for model export; then asks where and runs the export.</summary>
public class ModelExportWindow : ToolWindow
{
    private readonly AppSettings _settings = AppSettings.Default;
    private readonly bool _batch;
    private readonly string _name;
    private readonly Func<string, Task> _run;
    private readonly ComboBox _format = new() { ItemsSource = new[] { "GLB", "glTF", "FBX", "OBJ" } };
    private readonly ComboBox _size = new() { ItemsSource = new[] { 512, 1024, 2048, 4096, 8192 } };
    private readonly CheckBox _split = new();
    private readonly TextBox _splitAt = new() { Width = 80, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _rvmats = new();
    private readonly TextBox _textures = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

    /// <summary>batch: run gets a folder; otherwise a file path with the chosen extension.</summary>
    public static void Open(bool batch, string name, Func<string, Task> run) => new ModelExportWindow(batch, name, run).Show();

    private ModelExportWindow(bool batch, string name, Func<string, Task> run)
    {
        _batch = batch;
        _name = name;
        _run = run;
        Title = Loc.T("Export.Title");
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;

        _format.SelectedIndex = Math.Max(0, Array.IndexOf(ModelExport.Formats, _settings.ModelExportFormat));
        _size.SelectedItem = _settings.ModelExportMaxTexture;
        _split.IsChecked = _settings.ModelExportSplit;
        _splitAt.Text = _settings.ModelExportSplitAt.ToString();
        _splitAt.SetBinding(IsEnabledProperty, new System.Windows.Data.Binding(nameof(CheckBox.IsChecked)) { Source = _split });
        _rvmats.Text = _settings.ModelRvmatFolder;
        _textures.Text = _settings.ModelTextureFolder;
        _split.Content = Loc.T("Export.Split");

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var row = 0;
        void Row(string key, FrameworkElement control, Button browse = null, string tip = null)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = Loc.T(key), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4), ToolTip = tip };
            Grid.SetRow(label, row);
            Grid.SetRow(control, row);
            Grid.SetColumn(control, 1);
            control.Margin = new Thickness(0, 4, 0, 4);
            control.ToolTip = tip;
            grid.Children.Add(label);
            grid.Children.Add(control);
            if (browse != null)
            {
                Grid.SetRow(browse, row);
                Grid.SetColumn(browse, 2);
                browse.Margin = new Thickness(6, 4, 0, 4);
                grid.Children.Add(browse);
            }
            row++;
        }
        Button Browse(TextBox box)
        {
            var button = new Button { Content = Loc.T("Common.Browse"), Padding = new Thickness(10, 2, 10, 2) };
            button.SetResourceReference(StyleProperty, "PboSpy.Button");
            button.Click += (_, _) =>
            {
                var dialog = new CommonOpenFileDialog { IsFolderPicker = true, InitialDirectory = Directory.Exists(box.Text) ? box.Text : null };
                if (dialog.ShowDialog(this) == CommonFileDialogResult.Ok)
                {
                    box.Text = dialog.FileName;
                }
                Activate();
            };
            return button;
        }
        Row("Export.Format", _format, tip: Loc.T("Export.FormatTip"));
        Row("Export.MaxTexture", _size, tip: Loc.T("Export.MaxTextureTip"));
        var splitPanel = new StackPanel { Orientation = Orientation.Horizontal };
        splitPanel.Children.Add(_split);
        splitPanel.Children.Add(_splitAt);
        _splitAt.Margin = new Thickness(8, 0, 0, 0);
        Row("Export.SplitLabel", splitPanel, tip: Loc.T("Export.SplitTip"));
        Row("Export.RvmatFolder", _rvmats, Browse(_rvmats), Loc.T("Export.RvmatFolderTip"));
        Row("P3dView.TextureFolder", _textures, Browse(_textures), Loc.T("P3dView.TextureFolderTip"));

        var go = new Button { Content = Loc.T("Export.Go"), Padding = new Thickness(18, 4, 18, 4), HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true };
        go.SetResourceReference(StyleProperty, "PboSpy.Button");
        go.Click += OnExport;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(go, Dock.Right);
        bottom.Children.Add(go);
        bottom.Children.Add(_status);
        Grid.SetRow(bottom, row);
        Grid.SetColumnSpan(bottom, 3);
        grid.Children.Add(bottom);
        Content = grid;
    }

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        var extension = ModelExport.Formats[Math.Max(0, _format.SelectedIndex)];
        _settings.ModelExportFormat = extension;
        _settings.ModelExportMaxTexture = _size.SelectedItem is int size ? size : 2048;
        _settings.ModelExportSplit = _split.IsChecked == true;
        _settings.ModelExportSplitAt = int.TryParse(_splitAt.Text, out var at) ? at : 20000;
        _settings.ModelRvmatFolder = _rvmats.Text.Trim();
        _settings.ModelTextureFolder = _textures.Text.Trim();
        _settings.Save();

        string target;
        if (_batch)
        {
            var dialog = new CommonOpenFileDialog { IsFolderPicker = true, Title = Loc.T("Export.Title") };
            if (dialog.ShowDialog(this) != CommonFileDialogResult.Ok)
            {
                return;
            }
            target = dialog.FileName;
        }
        else
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { FileName = Path.GetFileNameWithoutExtension(_name) + extension, Filter = $"{extension}|*{extension}" };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }
            target = dialog.FileName;
        }
        IsEnabled = false;
        _status.Text = Loc.T("P3dView.Exporting");
        try
        {
            await _run(target);
            Close();
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            IsEnabled = true;
        }
    }

    /// <summary>Applies the export settings to a resolver.</summary>
    internal static void Configure(TextureResolver resolver)
    {
        resolver.Folder = AppSettings.Default.ModelTextureFolder;
        resolver.RvmatFolder = AppSettings.Default.ModelRvmatFolder;
    }

    public static int MaxTexture => AppSettings.Default.ModelExportMaxTexture;

    public static int SplitAt => AppSettings.Default.ModelExportSplit ? AppSettings.Default.ModelExportSplitAt : 0;

    public static string Extension => AppSettings.Default.ModelExportFormat;
}
