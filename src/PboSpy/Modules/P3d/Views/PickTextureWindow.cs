using PboSpy.Localization;
using PboSpy.Models;
using PboSpy.Modules.P3d.Scene;
using PboSpy.Modules.Windows;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PboSpy.Modules.P3d.Views;

/// <summary>Like Blender's image picker: the textures near the model that it doesn't use yet, with thumbnails and a filter.</summary>
public class PickTextureWindow : ToolWindow
{
    public sealed class Choice : INotifyPropertyChanged
    {
        private BitmapSource _thumb;
        public string Name { get; init; }
        public string Path { get; init; }
        internal FileBase File { get; init; }
        public BitmapSource Thumb { get => _thumb; set { _thumb = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumb))); } }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    private readonly TextBox _filter = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly ListBox _list = new();
    private readonly Action<string> _pick;

    internal static void Open(string forTexture, IEnumerable<(string Path, FileBase File)> images, Action<string> pick) =>
        new PickTextureWindow(forTexture, images, pick).Show();

    private PickTextureWindow(string forTexture, IEnumerable<(string Path, FileBase File)> images, Action<string> pick)
    {
        _pick = pick;
        Title = Loc.T("P3dView.PickUnused") + " - " + forTexture;
        Width = 560;
        Height = 620;
        var choices = images.Select(i => new Choice { Name = System.IO.Path.GetFileName(i.Path), Path = i.Path, File = i.File })
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var view = CollectionViewSource.GetDefaultView(choices);
        view.Filter = o => _filter.Text.Length == 0 || ((Choice)o).Path.Contains(_filter.Text, StringComparison.OrdinalIgnoreCase);
        _filter.TextChanged += (_, _) => view.Refresh();
        _list.ItemsSource = view;
        _list.SetResourceReference(StyleProperty, "PboSpy.ListBox");
        VirtualizingPanel.SetIsVirtualizing(_list, true);
        _list.ItemTemplate = Template();
        _list.MouseDoubleClick += (_, _) => Use();
        _list.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                Use();
            }
        };

        var hint = new TextBlock { Text = choices.Count == 0 ? Loc.T("P3dView.NoUnused") : Loc.T("P3dView.PickUnusedTip"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        hint.SetResourceReference(ForegroundProperty, "PboSpy.TextDim");
        var panel = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(hint, Dock.Top);
        DockPanel.SetDock(_filter, Dock.Top);
        panel.Children.Add(hint);
        panel.Children.Add(_filter);
        panel.Children.Add(_list);
        Content = panel;
        Loaded += (_, _) => _filter.Focus();
        _ = LoadThumbs(choices);
    }

    private static DataTemplate Template()
    {
        var row = new FrameworkElementFactory(typeof(DockPanel));
        row.SetValue(MarginProperty, new Thickness(2));
        var image = new FrameworkElementFactory(typeof(Image));
        image.SetValue(WidthProperty, 48.0);
        image.SetValue(HeightProperty, 48.0);
        image.SetValue(MarginProperty, new Thickness(0, 0, 8, 0));
        image.SetBinding(Image.SourceProperty, new Binding(nameof(Choice.Thumb)));
        row.AppendChild(image);
        var text = new FrameworkElementFactory(typeof(StackPanel));
        text.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new Binding(nameof(Choice.Name)));
        var path = new FrameworkElementFactory(typeof(TextBlock));
        path.SetBinding(TextBlock.TextProperty, new Binding(nameof(Choice.Path)));
        path.SetValue(TextBlock.FontSizeProperty, 10.0);
        path.SetResourceReference(TextBlock.ForegroundProperty, "PboSpy.TextDim");
        text.AppendChild(name);
        text.AppendChild(path);
        row.AppendChild(text);
        return new DataTemplate { VisualTree = row };
    }

    private static async Task LoadThumbs(List<Choice> choices)
    {
        foreach (var batch in choices.Chunk(16))
        {
            var thumbs = await Task.Run(() => batch.AsParallel().AsOrdered().Select(c =>
            {
                try
                {
                    using var stream = c.File?.GetStream() ?? File.OpenRead(c.Path);
                    return TextureResolver.Decode(stream, System.IO.Path.GetExtension(c.Path), 64, true);
                }
                catch (Exception)
                {
                    return null;
                }
            }).ToList());
            for (var i = 0; i < batch.Length; i++)
            {
                batch[i].Thumb = thumbs[i];
            }
        }
    }

    private void Use()
    {
        if (_list.SelectedItem is Choice choice)
        {
            _pick(choice.Path);
            Close();
        }
    }
}
