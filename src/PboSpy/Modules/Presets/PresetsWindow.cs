using Microsoft.WindowsAPICodePack.Dialogs;
using PboSpy.Localization;
using PboSpy.Models;
using PboSpy.Modules.BulkExport.Commands;
using PboSpy.Modules.BulkExport.Models;
using PboSpy.Modules.Explorer;
using PboSpy.Modules.Explorer.ViewModels;
using PboSpy.Modules.P3dTools.Views;
using PboSpy.Modules.Pbr.Views;
using PboSpy.Modules.Windows;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Action = System.Action;

namespace PboSpy.Modules.Presets;

/// <summary>
/// Shortcuts grouped by game and job: pick a game, then a button opens the tools that job needs,
/// already set up (e.g. Textures > PAA to PNG opens Convert with only image conversion ticked).
/// </summary>
public class PresetsWindow : ToolWindow
{
    private sealed record Preset(string Key, Action Run);
    private sealed record Category(string Key, Preset[] Items);
    private sealed record Game(string Key, bool Ready, Category[] Categories);

    private readonly ComboBox _games = new() { MinWidth = 220 };
    private readonly StackPanel _body = new();
    private readonly Game[] _list;

    public static void Open() => ShowSingle(() => new PresetsWindow());

    private PresetsWindow()
    {
        Width = 480;
        Height = 620;
        _list = new[]
        {
            new Game("Presets.Game.Arma3", true, Arma3()),
            new Game("Presets.Game.Msfs2024", false, Array.Empty<Category>()),
        };

        var top = new DockPanel { Margin = new Thickness(12, 12, 12, 6) };
        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        label.SetResourceReference(StyleProperty, "PboSpy.Label");
        top.Children.Add(label);
        top.Children.Add(_games);
        _games.SelectionChanged += (_, _) => Fill();

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _body, Padding = new Thickness(12, 0, 12, 12) };
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(scroll);
        Content = root;

        void Texts()
        {
            Title = Loc.T("Presets.Title");
            label.Text = Loc.T("Presets.Game");
            var index = Math.Max(0, _games.SelectedIndex);
            _games.ItemsSource = _list.Select(g => g.Ready ? Loc.T(g.Key) : Loc.T(g.Key) + "  " + Loc.T("Presets.Planned")).ToList();
            _games.SelectedIndex = index;
            Fill();
        }
        Texts();
        Loc.Instance.LanguageChanged += (_, _) => Texts();
    }

    private void Fill()
    {
        _body.Children.Clear();
        var game = _games.SelectedIndex >= 0 ? _list[_games.SelectedIndex] : null;
        if (game == null)
        {
            return;
        }
        if (!game.Ready)
        {
            _body.Children.Add(new TextBlock { Text = Loc.T("Presets.NotYet"), TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 12, 0, 0) });
            return;
        }
        foreach (var category in game.Categories)
        {
            var header = new TextBlock { Text = Loc.T(category.Key), Margin = new Thickness(0, 14, 0, 6) };
            header.SetResourceReference(StyleProperty, "PboSpy.Section");
            _body.Children.Add(header);
            var wrap = new WrapPanel();
            foreach (var preset in category.Items)
            {
                var button = new Button
                {
                    Content = Loc.T(preset.Key),
                    ToolTip = Loc.T(preset.Key + "Tip"),
                    Margin = new Thickness(0, 0, 6, 6),
                    Padding = new Thickness(10, 4, 10, 4)
                };
                button.SetResourceReference(StyleProperty, "PboSpy.Button");
                button.Click += (_, _) =>
                {
                    try
                    {
                        preset.Run();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                };
                wrap.Children.Add(button);
            }
            _body.Children.Add(wrap);
        }
    }

    private Category[] Arma3() => new[]
    {
        new Category("Presets.Cat.Import", new[]
        {
            new Preset("Presets.OpenPbo", OpenPbo),
            new Preset("Presets.RecoverNames", RecoverNames),
            new Preset("Presets.Debinarize", () => P3dToolsWindow.Open()),
            new Preset("Presets.TexturesToPng", () => Convert(Loc.T("Presets.TexturesToPng"), ImagesToPng)),
            new Preset("Presets.Pbr", () => PbrMakerWindow.Open()),
        }),
        new Category("Presets.Cat.Models", new[]
        {
            new Preset("Presets.Debinarize", () => P3dToolsWindow.Open()),
            new Preset("Presets.ModelCfg", () => P3dToolsWindow.Open()),
            new Preset("Presets.StripProxies", () => P3dToolsWindow.Open()),
            new Preset("Presets.ModelPreview", OpenModel),
        }),
        new Category("Presets.Cat.Textures", new[]
        {
            new Preset("Presets.TexturesToPng", () => Convert(Loc.T("Presets.TexturesToPng"), ImagesToPng)),
            new Preset("Presets.PngToPaa", () => Convert(Loc.T("Presets.PngToPaa"), o => Only(o, paa: true))),
            new Preset("Presets.Pbr", () => PbrMakerWindow.Open()),
        }),
        new Category("Presets.Cat.Sounds", new[]
        {
            new Preset("Presets.SoundsToWav", () => Convert(Loc.T("Presets.SoundsToWav"), o => { Only(o, audio: true); o.AudioFormat = AudioOutputFormat.Wav; })),
        }),
        new Category("Presets.Cat.Configs", new[]
        {
            new Preset("Presets.ConfigsToText", () => Convert(Loc.T("Presets.ConfigsToText"), o => Only(o, configs: true))),
        }),
    };

    private static void ImagesToPng(ExportOptions options)
    {
        Only(options, images: true);
        options.ImageFormat = ImageOutputFormat.Png;
    }

    private static void Only(ExportOptions options, bool images = false, bool paa = false, bool audio = false, bool configs = false)
    {
        options.ConvertImages = images;
        options.ConvertImagesToPaa = paa;
        options.ConvertAudio = audio;
        options.ConvertConfigs = configs;
    }

    // Files or a folder; whatever is picked opens the Convert window with the preset's options.
    private void Convert(string title, Action<ExportOptions> setup)
    {
        var dialog = new CommonOpenFileDialog { Title = title, IsFolderPicker = MessageBox.Show(this, Loc.T("Presets.FolderQuestion"), title,
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes, Multiselect = true };
        if (dialog.ShowDialog(this) != CommonFileDialogResult.Ok)
        {
            return;
        }
        if (dialog.IsFolderPicker)
        {
            var folder = dialog.FileNames.First();
            var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(f => (FileBase)new PhysicalFile(f)).ToList();
            ConvertRunner.Run(files, Path.GetDirectoryName(folder.TrimEnd('\\')), "Convert.FolderTitle", folder, setup);
        }
        else
        {
            ConvertRunner.Run(dialog.FileNames.Select(f => (FileBase)new PhysicalFile(f)).ToList(), null, "Convert.Title", null, setup);
        }
    }

    private void OpenPbo()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "PBO|*.pbo|*.*|*.*", Multiselect = true };
        if (dialog.ShowDialog(this) == true)
        {
            _ = PboSpy.Services.AppOpen.Show(dialog.FileNames, preview: false);
        }
    }

    private void OpenModel()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "P3D|*.p3d", Multiselect = false };
        if (dialog.ShowDialog(this) == true)
        {
            _ = PboSpy.Services.AppOpen.Show(dialog.FileNames);
        }
    }

    private static void RecoverNames()
    {
        if (IoC.Get<IPboExplorer>() is ExplorerViewModel explorer)
        {
            explorer.RecoverNames();
        }
    }
}
