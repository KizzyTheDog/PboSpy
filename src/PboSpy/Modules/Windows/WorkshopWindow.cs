using PboSpy.Localization;
using PboSpy.Modules.P3d.Scene;
using PboSpy.Services;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace PboSpy.Modules.Windows;

/// <summary>Arma 3 workshop items already on this PC (Steam subscriptions and steamcmd downloads), plus downloading by link or ID with steamcmd.</summary>
public class WorkshopWindow : ToolWindow
{
    private const string AppId = "107410";
    private static WorkshopWindow _open;
    private readonly TextBox _filter = new() { Margin = new Thickness(0, 0, 0, 6) };
    private readonly ListBox _list = new() { SelectionMode = SelectionMode.Extended };
    private readonly TextBox _item = new();
    private readonly TextBox _user = new() { Width = 140 };
    private readonly TextBox _steamCmd = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };

    private sealed record Item(string Name, string Id, string Folder, long Size)
    {
        public override string ToString() => $"{Name}   ({Id}, {PboSpy.Modules.Explorer.Converters.FileSizeConverter.Format(Size)})";
    }

    public static void Open()
    {
        if (_open == null)
        {
            _open = new WorkshopWindow();
            _open.Closed += (_, _) => _open = null;
            _open.Show();
        }
        if (!PboSpy.Services.TestMode.On)
        {
            _open.Activate();
        }
    }

    private WorkshopWindow()
    {
        Title = Loc.T("Workshop.Title");
        Width = 720;
        Height = 640;
        _steamCmd.Text = File.Exists(AppSettings.Default.SteamCmdPath) ? AppSettings.Default.SteamCmdPath : FindSteamCmd() ?? "";
        _user.Text = AppSettings.Default.SteamUser;
        _list.SetResourceReference(StyleProperty, "PboSpy.ListBox");
        _list.MouseDoubleClick += async (_, _) => await OpenSelected();

        var download = Button("Workshop.Download", async () => await Download());
        var open = Button("Workshop.Open", async () => await OpenSelected());
        var refresh = Button("Workshop.Refresh", () => { Fill(); return Task.CompletedTask; });
        var browse = Button("Common.Browse", () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "steamcmd.exe|steamcmd.exe" };
            if (dialog.ShowDialog(this) == true)
            {
                _steamCmd.Text = dialog.FileName;
            }
            return Task.CompletedTask;
        });

        var top = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        foreach (var w in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
        {
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        }
        var row = 0;
        void Row(string key, FrameworkElement control, FrameworkElement extra = null)
        {
            top.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = Loc.T(key), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 10, 3) };
            Grid.SetRow(label, row);
            Grid.SetRow(control, row);
            Grid.SetColumn(control, 1);
            control.Margin = new Thickness(0, 3, 0, 3);
            top.Children.Add(label);
            top.Children.Add(control);
            if (extra != null)
            {
                Grid.SetRow(extra, row);
                Grid.SetColumn(extra, 2);
                extra.Margin = new Thickness(6, 3, 0, 3);
                top.Children.Add(extra);
            }
            row++;
        }
        Row("Workshop.Item", _item, download);
        Row("Workshop.User", _user);
        Row("Workshop.SteamCmd", _steamCmd, browse);

        var hint = new TextBlock { Text = Loc.T("Workshop.Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        hint.SetResourceReference(ForegroundProperty, "PboSpy.TextDim");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(refresh);
        buttons.Children.Add(open);
        refresh.Margin = new Thickness(0, 0, 6, 0);

        var panel = new DockPanel { Margin = new Thickness(12) };
        foreach (var (element, dock) in new (UIElement, Dock)[] { (top, Dock.Top), (hint, Dock.Top), (_filter, Dock.Top), (_status, Dock.Bottom), (buttons, Dock.Bottom) })
        {
            DockPanel.SetDock(element, dock);
            panel.Children.Add(element);
        }
        panel.Children.Add(_list);
        Content = panel;
        Fill();
    }

    private static Button Button(string key, Func<Task> click)
    {
        var button = new Button { Content = Loc.T(key), Padding = new Thickness(12, 2, 12, 2) };
        button.SetResourceReference(StyleProperty, "PboSpy.Button");
        button.Click += async (_, _) => await click();
        return button;
    }

    // Steam's own workshop folder next to the game, and steamcmd's.
    private IEnumerable<string> Roots()
    {
        var game = GameData.GameFolder;
        if (!string.IsNullOrEmpty(game))
        {
            yield return Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(game)) ?? "", "workshop", "content", AppId);
        }
        if (File.Exists(_steamCmd.Text))
        {
            yield return Path.Combine(Path.GetDirectoryName(_steamCmd.Text), "steamapps", "workshop", "content", AppId);
        }
    }

    private async void Fill()
    {
        _status.Text = Loc.T("Find.Searching");
        var roots = Roots().Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var names = await Task.Run(LinkNames);
        var items = await Task.Run(() => roots.SelectMany(Directory.EnumerateDirectories).Select(f => Read(f, names)).Where(i => i != null)
            .GroupBy(i => i.Id).Select(g => g.First()).OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList());
        var view = CollectionViewSource.GetDefaultView(items);
        view.Filter = o => _filter.Text.Length == 0 || o.ToString().Contains(_filter.Text, StringComparison.OrdinalIgnoreCase);
        _filter.TextChanged -= Refilter;
        _filter.TextChanged += Refilter;
        _list.ItemsSource = view;
        _status.Text = Loc.F("Workshop.Count", items.Count);
    }

    private void Refilter(object sender, TextChangedEventArgs e) => (_list.ItemsSource as System.ComponentModel.ICollectionView)?.Refresh();

    // Arma's launcher links each subscribed item into "Arma 3\!Workshop\@Name", so items without a meta.cpp still get a name.
    private static Dictionary<string, string> LinkNames()
    {
        var names = new Dictionary<string, string>();
        var links = Path.Combine(GameData.GameFolder ?? "", "!Workshop");
        if (!Directory.Exists(links))
        {
            return names;
        }
        foreach (var link in Directory.EnumerateDirectories(links, "@*"))
        {
            try
            {
                var target = Directory.ResolveLinkTarget(link, false)?.FullName;
                if (target != null)
                {
                    names[Path.GetFileName(target.TrimEnd('\\'))] = Path.GetFileName(link).TrimStart('@');
                }
            }
            catch (IOException)
            {
            }
        }
        return names;
    }

    private static Item Read(string folder) => Read(folder, null);

    private static Item Read(string folder, Dictionary<string, string> names)
    {
        try
        {
            var meta = Path.Combine(folder, "meta.cpp");
            var name = File.Exists(meta) ? Regex.Match(File.ReadAllText(meta), @"name\s*=\s*""([^""]*)""").Groups[1].Value : "";
            var size = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
            var id = Path.GetFileName(folder);
            if (name.Length == 0 && names != null)
            {
                names.TryGetValue(id, out name);
            }
            return new Item(string.IsNullOrEmpty(name) ? id : name, id, folder, size);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private Task OpenSelected() => OpenItems(_list.SelectedItems.OfType<Item>().ToList());

    private async Task OpenItems(List<Item> items)
    {
        if (items.Count == 0)
        {
            return;
        }
        // Workshop files are Steam's: changed ones get re-downloaded, and servers that check signatures kick modified mods.
        var answer = MessageBox.Show(this, Loc.T("Workshop.CopyQuestion"), Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel)
        {
            return;
        }
        var folders = items.Select(i => i.Folder).ToList();
        if (answer == MessageBoxResult.Yes)
        {
            _status.Text = Loc.T("Workshop.Copying");
            IsEnabled = false;
            try
            {
                folders = await Task.Run(() => items.Select(Copy).ToList());
            }
            finally
            {
                IsEnabled = true;
            }
            _status.Text = Loc.F("Workshop.Copied", Path.GetDirectoryName(folders[0]));
        }
        await AppOpen.Show(folders, preview: false);
    }

    // Downloads\PboSpy Workshop\<name> (<id>); an existing copy is reused, not overwritten.
    private static string Copy(Item item)
    {
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "PboSpy Workshop");
        var safe = string.Concat(item.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        var target = Path.Combine(downloads, $"{safe} ({item.Id})");
        if (Directory.Exists(target))
        {
            return target;
        }
        foreach (var file in Directory.EnumerateFiles(item.Folder, "*", SearchOption.AllDirectories))
        {
            var to = Path.Combine(target, Path.GetRelativePath(item.Folder, file));
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            File.Copy(file, to);
        }
        return target;
    }

    private static string FindSteamCmd()
    {
        var near = Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd('\\')) ?? "", "steamcmd", "steamcmd.exe");
        return new[] { near, @"C:\steamcmd\steamcmd.exe", @"C:\Program Files\steamcmd\steamcmd.exe" }.FirstOrDefault(File.Exists);
    }

    // Runs steamcmd in its own console so a password or Steam Guard prompt goes straight to the user.
    private async Task Download()
    {
        var id = Regex.Match(_item.Text, @"\d{6,}").Value;
        if (id.Length == 0 || !File.Exists(_steamCmd.Text) || _user.Text.Trim().Length == 0)
        {
            _status.Text = Loc.T("Workshop.Missing");
            return;
        }
        AppSettings.Default.SteamCmdPath = _steamCmd.Text;
        AppSettings.Default.SteamUser = _user.Text.Trim();
        AppSettings.Default.Save();
        _status.Text = Loc.F("Workshop.Downloading", id);
        IsEnabled = false;
        try
        {
            var info = new ProcessStartInfo(_steamCmd.Text, $"+login {AppSettings.Default.SteamUser} +workshop_download_item {AppId} {id} validate +quit")
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(_steamCmd.Text)
            };
            using var process = Process.Start(info);
            await process.WaitForExitAsync();
        }
        finally
        {
            IsEnabled = true;
        }
        var folder = Path.Combine(Path.GetDirectoryName(_steamCmd.Text), "steamapps", "workshop", "content", AppId, id);
        if (Directory.Exists(folder))
        {
            _status.Text = Loc.F("Workshop.Done", id);
            Fill();
            await OpenItems(new List<Item> { Read(folder) }.Where(i => i != null).ToList());
        }
        else
        {
            _status.Text = Loc.T("Workshop.Failed");
        }
    }
}
