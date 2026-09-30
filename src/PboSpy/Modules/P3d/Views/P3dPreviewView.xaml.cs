using PboSpy.Localization;
using PboSpy.Modules.P3d.Scene;
using PboSpy.Modules.P3d.ViewModels;
using PboSpy.Services;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace PboSpy.Modules.P3d.Views;

public partial class P3dPreviewView : UserControl
{
    private static readonly Material Plain = Frozen(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA))));
    private static readonly Material Missing = Frozen(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0xB4, 0x8C, 0x96))));

    private P3dPreviewViewModel _vm;
    private readonly Dictionary<string, TextureResult> _results = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Material> _materials = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Material> _detailed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LinkedMaps> _linked = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(ModelPart Part, GeometryModel3D Model)> _models = new();
    private ModelMesh _mesh;
    private int _buildVersion;
    private bool _fitted;
    private bool _loadingTextures;

    private double _yaw = 325;
    private double _pitch = 18;
    private double _distance = 10;
    private double _radius = 1;
    private Point3D _target;
    private Point _last;
    private MouseButton? _drag;

    public P3dPreviewView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private static Material Frozen(Material material)
    {
        material.Freeze();
        return material;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm != null)
        {
            _vm.PropertyChanged -= OnViewModelChanged;
        }
        _vm = DataContext as P3dPreviewViewModel;
        if (_vm == null)
        {
            return;
        }
        _vm.PropertyChanged += OnViewModelChanged;
        if (_vm.ShowModel)
        {
            Rebuild();
        }
    }

    private void OnViewModelChanged(object sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(P3dPreviewViewModel.SelectedLod):
                Rebuild();
                break;
            case nameof(P3dPreviewViewModel.ShowModel):
                if (_vm.ShowModel && _mesh == null)
                {
                    Rebuild();
                }
                break;
            case nameof(P3dPreviewViewModel.ShowTextures):
            case nameof(P3dPreviewViewModel.ShowDetailMaps):
                ApplyMaterials();
                break;
        }
    }

    private async void Rebuild()
    {
        var lod = _vm?.SelectedLod;
        var version = ++_buildVersion;
        if (lod == null)
        {
            Overlay.Text = Loc.T("P3dView.NoGeometry");
            return;
        }

        Overlay.Text = Loc.T("P3dView.Loading");
        ModelMesh mesh;
        try
        {
            var companions = _vm.Companions.ToList();
            var title = _vm.MainName;
            mesh = await Task.Run(() =>
            {
                var main = ModelMeshBuilder.Build(lod.Lod);
                if (companions.Count == 0)
                {
                    return main;
                }
                var all = new List<(string, ModelMesh)> { (title, main) };
                all.AddRange(companions.Select(c => (c.Name, ModelMeshBuilder.Build(c.Lod))));
                return ModelMesh.Combine(all);
            });
        }
        catch (Exception ex)
        {
            if (version == _buildVersion)
            {
                Overlay.Text = Loc.F("P3dView.Error", ex.Message);
            }
            return;
        }
        if (version != _buildVersion)
        {
            return;
        }

        _mesh = mesh;
        Parts.Children.Clear();
        _models.Clear();
        // See-through parts go last, otherwise WPF hides whatever is drawn behind them afterwards.
        foreach (var part in mesh.Parts.OrderBy(p => TextureResolver.HasAlpha(TextureResolver.Normalize(p.Texture))))
        {
            var material = IsVisible(part.Texture) ? MaterialFor(part) : null;
            var model = new GeometryModel3D(part.Mesh, material) { BackMaterial = material };
            if (part.Offset.LengthSquared > 0)
            {
                model.Transform = new TranslateTransform3D(part.Offset);
            }
            Parts.Children.Add(model);
            _models.Add((part, model));
        }
        Overlay.Text = mesh.Parts.Count == 0 ? Loc.T("P3dView.NoGeometry") : "";
        if (!_fitted && !mesh.Bounds.IsEmpty)
        {
            Fit();
            _fitted = true;
        }

        _vm.TextureItems.Clear();
        foreach (var group in mesh.Parts.GroupBy(p => TextureResolver.Normalize(p.Texture))
                     .OrderByDescending(g => _vm.FocusTexture != null && g.Key == TextureResolver.Normalize(_vm.FocusTexture))
                     .ThenByDescending(g => g.Sum(p => p.Triangles)))
        {
            var item = new ModelTextureItem(group.First().Texture, group.Sum(p => p.Triangles));
            if (_results.TryGetValue(item.Key, out var known))
            {
                Describe(item, known);
            }
            _vm.TextureItems.Add(item);
        }
        UpdateStats();
        await LoadTextures();
    }

    private async Task LoadTextures()
    {
        if (_loadingTextures || _vm == null)
        {
            return;
        }
        _loadingTextures = true;
        try
        {
            while (true)
            {
                var pending = _vm.TextureItems.Where(i => !_results.ContainsKey(i.Key)).Take(4).ToList();
                if (pending.Count == 0)
                {
                    break;
                }
                foreach (var item in pending)
                {
                    item.Status = Loc.T("P3dView.Searching");
                    item.StatusBrush = Resource("PboSpy.TextDim");
                }
                var resolver = _vm.Textures;
                var found = await Task.WhenAll(pending.Select(item => Task.Run(() =>
                {
                    try
                    {
                        return resolver.Resolve(item.Path);
                    }
                    catch (Exception)
                    {
                        return new TextureResult();
                    }
                })));
                for (var i = 0; i < pending.Count; i++)
                {
                    Store(pending[i].Key, found[i]);
                }
                foreach (var item in _vm.TextureItems.Where(i => pending.Any(p => p.Key == i.Key)))
                {
                    Describe(item, _results[item.Key]);
                }
                ApplyMaterials();
                UpdateStats();
            }
            await LoadLinked();
        }
        finally
        {
            _loadingTextures = false;
        }
    }

    private static string PartKey(ModelPart part) => TextureResolver.Normalize(part.Texture) + "|" + TextureResolver.Normalize(part.Material);

    /// <summary>Finds each material's normal / specular / ambient maps and folds them into what WPF can draw.</summary>
    private async Task LoadLinked()
    {
        var pending = _models.Select(m => m.Part)
            .Where(p => !_linked.ContainsKey(PartKey(p)) && _results.TryGetValue(TextureResolver.Normalize(p.Texture), out var r) && r.Image != null)
            .GroupBy(PartKey).Select(g => g.First()).ToList();
        if (pending.Count == 0)
        {
            return;
        }
        var resolver = _vm.Textures;
        var work = pending.Select(part => (Key: PartKey(part), Part: part, Base: _results[TextureResolver.Normalize(part.Texture)])).ToList();
        var done = await Task.Run(() => work.Select(w =>
        {
            try
            {
                var maps = resolver.ResolveLinked(w.Part.Texture, w.Part.Material);
                return (w.Key, Maps: maps, Material: Compose(w.Base, maps));
            }
            catch (Exception)
            {
                return (w.Key, Maps: new LinkedMaps(), Material: (Material)null);
            }
        }).ToList());
        foreach (var (key, maps, material) in done)
        {
            _linked[key] = maps;
            if (material != null)
            {
                _detailed[key] = material;
            }
        }
        if (_vm != null)
        {
            foreach (var item in _vm.TextureItems)
            {
                item.Linked = string.Join(" ", _linked.Where(l => l.Key.StartsWith(item.Key + "|")).SelectMany(l => l.Value.Summary.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Distinct());
            }
        }
        ApplyMaterials();
    }

    private static ImageBrush TileBrush(BitmapSource image)
    {
        var brush = new ImageBrush(image)
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 1, 1),
            Stretch = Stretch.Fill
        };
        brush.Freeze();
        return brush;
    }

    // WPF has no normal mapping, so the normal map is baked into the colour as fixed top-left lighting;
    // SMDI green becomes the specular strength.
    private static Material Compose(TextureResult colour, LinkedMaps maps)
    {
        if (!maps.Any)
        {
            return null;
        }
        var diffuse = colour.Image;
        if (maps.Normal?.Image != null)
        {
            diffuse = TextureCache.Get($"shade|{colour.Location}|{maps.Normal.Location}", () => Shade(colour.Image, maps.Normal.Image));
        }
        var group = new MaterialGroup();
        group.Children.Add(new DiffuseMaterial(TileBrush(diffuse)));
        if (maps.Specular?.Image != null)
        {
            var specular = TextureCache.Get($"spec|{maps.Specular.Location}", () => SpecularFromSmdi(maps.Specular.Image));
            group.Children.Add(new SpecularMaterial(TileBrush(specular), 40));
        }
        group.Freeze();
        return group;
    }

    private static byte[] Pixels(BitmapSource image)
    {
        var bytes = new byte[image.PixelWidth * image.PixelHeight * 4];
        image.CopyPixels(bytes, image.PixelWidth * 4, 0);
        return bytes;
    }

    private static BitmapSource Shade(BitmapSource colour, BitmapSource normal)
    {
        int w = colour.PixelWidth, h = colour.PixelHeight, nw = normal.PixelWidth, nh = normal.PixelHeight;
        var c = Pixels(colour);
        var n = Pixels(normal);
        // DXT5nm keeps X in alpha with red flat.
        byte minA = 255, minR = 255, maxR = 0;
        for (var i = 0; i < n.Length; i += 4 * 17)
        {
            minA = Math.Min(minA, n[i + 3]);
            minR = Math.Min(minR, n[i + 2]);
            maxR = Math.Max(maxR, n[i + 2]);
        }
        var swizzled = minA < 250 && maxR - minR < 24;
        const double lx = -0.35, ly = 0.45, lz = 0.82;
        for (var y = 0; y < h; y++)
        {
            var ny = y * nh / h;
            for (var x = 0; x < w; x++)
            {
                var ni = (ny * nw + x * nw / w) * 4;
                var nx = (swizzled ? n[ni + 3] : n[ni + 2]) / 127.5 - 1;
                var nyv = n[ni + 1] / 127.5 - 1;
                var nz = Math.Sqrt(Math.Max(0, 1 - nx * nx - nyv * nyv));
                var shade = Math.Clamp((nx * lx + nyv * ly + nz * lz) / lz, 0.35, 1.3);
                var ci = (y * w + x) * 4;
                c[ci] = (byte)Math.Min(255, c[ci] * shade);
                c[ci + 1] = (byte)Math.Min(255, c[ci + 1] * shade);
                c[ci + 2] = (byte)Math.Min(255, c[ci + 2] * shade);
            }
        }
        var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, c, w * 4);
        result.Freeze();
        return result;
    }

    private static BitmapSource SpecularFromSmdi(BitmapSource smdi)
    {
        var p = Pixels(smdi);
        for (var i = 0; i < p.Length; i += 4)
        {
            var g = (byte)(p[i + 1] * 0.55);
            p[i] = g;
            p[i + 1] = g;
            p[i + 2] = g;
            p[i + 3] = 255;
        }
        var result = BitmapSource.Create(smdi.PixelWidth, smdi.PixelHeight, 96, 96, PixelFormats.Bgra32, null, p, smdi.PixelWidth * 4);
        result.Freeze();
        return result;
    }

    private void Store(string key, TextureResult result)
    {
        _results[key] = result;
        Material material = null;
        if (result.Image != null)
        {
            material = Frozen(new DiffuseMaterial(TileBrush(result.Image)));
        }
        else if (result.Color is Color color)
        {
            material = Frozen(new DiffuseMaterial(new SolidColorBrush(color)));
        }
        if (material != null)
        {
            _materials[key] = material;
        }
        else
        {
            _materials.Remove(key);
        }
    }

    private void Describe(ModelTextureItem item, TextureResult result)
    {
        item.Thumb = result.Image;
        item.Swatch = result.Color is Color color ? new SolidColorBrush(color) : null;
        item.Location = result.Location;
        item.File = result.File;
        item.IsOverride = result.Source == TextureSource.Override;
        if (string.IsNullOrWhiteSpace(item.Path))
        {
            item.Status = Loc.T("P3dView.Untextured");
            item.StatusBrush = Resource("PboSpy.TextDim");
            return;
        }
        item.Status = result.Source switch
        {
            TextureSource.Loaded => Loc.T("P3dView.FromPbo"),
            TextureSource.Disk => Loc.T("P3dView.FromDisk"),
            TextureSource.Folder => Loc.T("P3dView.FromFolder"),
            TextureSource.Override => Loc.T("P3dView.FromOverride"),
            TextureSource.Procedural => Loc.T("P3dView.Procedural"),
            _ => Loc.T("P3dView.Missing")
        };
        item.StatusBrush = Resource(result.Found ? "PboSpy.Success" : "PboSpy.Warning");
    }

    private Brush Resource(string key) => TryFindResource(key) as Brush;

    private Material MaterialFor(ModelPart part)
    {
        if (_vm != null && _vm.ShowTextures && _vm.ShowDetailMaps && _detailed.TryGetValue(PartKey(part), out var detailed))
        {
            return detailed;
        }
        return MaterialFor(part.Texture);
    }

    private Material MaterialFor(string texture)
    {
        if (_vm == null || !_vm.ShowTextures)
        {
            return Plain;
        }
        var key = TextureResolver.Normalize(texture);
        if (_materials.TryGetValue(key, out var material))
        {
            return material;
        }
        return _results.ContainsKey(key) && !string.IsNullOrWhiteSpace(texture) ? Missing : Plain;
    }

    private readonly HashSet<string> _hidden = new(StringComparer.OrdinalIgnoreCase);
    private string _isolated;

    private bool IsVisible(string texture)
    {
        var key = TextureResolver.Normalize(texture);
        return (_isolated == null || key == _isolated) && !_hidden.Contains(key);
    }

    private void ApplyMaterials()
    {
        foreach (var (part, model) in _models)
        {
            var material = IsVisible(part.Texture) ? MaterialFor(part) : null;
            if (model.Material != material)
            {
                model.Material = material;
                model.BackMaterial = material;
            }
        }
    }

    private void UpdateStats()
    {
        if (_mesh == null || _vm == null)
        {
            StatsText.Text = "";
            return;
        }
        var textured = _vm.TextureItems.Where(i => !string.IsNullOrWhiteSpace(i.Path)).ToList();
        var found = textured.Count(i => _results.TryGetValue(i.Key, out var r) && r.Found);
        StatsText.Text = Loc.F("P3dView.Stats", _mesh.Triangles.ToString("N0"), textured.Count, found);
    }

    private void Reload(Func<string, bool> which)
    {
        foreach (var key in _results.Keys.Where(which).ToList())
        {
            _results.Remove(key);
            _materials.Remove(key);
            foreach (var part in _linked.Keys.Where(k => k.StartsWith(key + "|")).ToList())
            {
                _linked.Remove(part);
                _detailed.Remove(part);
            }
        }
        _ = LoadTextures();
    }

    private void OnTextureMenu(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is ModelTextureItem item)
        {
            ShowTextureMenu(element, item);
        }
    }

    private void OnTextureRowRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is ModelTextureItem item)
        {
            ShowTextureMenu(element, item);
            e.Handled = true;
        }
    }

    private async void OnTextureRowClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && (sender as FrameworkElement)?.DataContext is ModelTextureItem item)
        {
            await OpenTexture(item);
        }
    }

    private string DiskPath(ModelTextureItem item) =>
        item.File is PboSpy.Models.PhysicalFile physical ? physical.FullPath
        : item.File == null && File.Exists(item.Location) ? item.Location : null;

    private void ShowTextureMenu(FrameworkElement anchor, ModelTextureItem item)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        MenuItem Add(string key, System.Action action, bool enabled = true)
        {
            var entry = new MenuItem { Header = Loc.T(key), IsEnabled = enabled };
            entry.Click += (_, _) => action();
            menu.Items.Add(entry);
            return entry;
        }
        var key = item.Key;
        var result = _results.TryGetValue(key, out var r) ? r : null;
        var disk = DiskPath(item);
        var canOpen = item.File != null || disk != null;

        Add("P3dView.Menu.Open", () => _ = OpenTexture(item), canOpen).FontWeight = FontWeights.SemiBold;
        Add("P3dView.Menu.ShowFile", () => ShowFile(item), canOpen);
        Add("P3dView.Menu.CopyPath", () => Clipboard.SetText(disk ?? (item.Location.Length > 0 ? item.Location : item.Path)), !string.IsNullOrWhiteSpace(item.Path));
        Add("P3dView.Menu.CopyReference", () => Clipboard.SetText(item.Path), !string.IsNullOrWhiteSpace(item.Path));
        menu.Items.Add(new Separator());
        Add("P3dView.Menu.Change", () => PickTexture(item), item.CanPick);
        Add("P3dView.Clear", () => ClearTexture(item), item.IsOverride);
        Add("P3dView.Menu.Reload", () => Reload(k => k == key), item.CanPick);
        menu.Items.Add(new Separator());
        if (_isolated == key)
        {
            Add("P3dView.Menu.ShowAll", () => { _isolated = null; ApplyMaterials(); });
        }
        else
        {
            Add("P3dView.Menu.Isolate", () => { _isolated = key; _hidden.Remove(key); ApplyMaterials(); });
        }
        if (_hidden.Contains(key))
        {
            Add("P3dView.Menu.Unhide", () => { _hidden.Remove(key); ApplyMaterials(); });
        }
        else
        {
            Add("P3dView.Menu.Hide", () => { _hidden.Add(key); if (_isolated == key) { _isolated = null; } ApplyMaterials(); });
        }
        if (_hidden.Count > 0 || _isolated != null)
        {
            Add("P3dView.Menu.ShowEverything", () => { _hidden.Clear(); _isolated = null; ApplyMaterials(); });
        }
        menu.Items.Add(new Separator());
        Add("P3dView.Menu.SavePng", () => SavePng(item, result), result?.Image != null);
        menu.IsOpen = true;
    }

    private async Task OpenTexture(ModelTextureItem item)
    {
        if (item.File is PboSpy.Modules.Pbo.Models.PboEntry entry)
        {
            await IoC.Get<PboSpy.Modules.Preview.IPreviewManager>().ShowPreview(entry, pin: true, activate: true);
        }
        else if (DiskPath(item) is { } disk)
        {
            await PboSpy.Services.AppOpen.Show(new[] { disk });
        }
    }

    private void ShowFile(ModelTextureItem item)
    {
        if (item.File is PboSpy.Modules.Pbo.Models.PboEntry entry)
        {
            var shell = IoC.Get<IShell>();
            var explorer = IoC.Get<PboSpy.Modules.Explorer.IPboExplorer>();
            shell.ShowTool(explorer);
            explorer.Reveal(entry);
        }
        else if (DiskPath(item) is { } disk)
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{disk}\"");
        }
    }

    private void SavePng(ModelTextureItem item, TextureResult result)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = Path.GetFileNameWithoutExtension(item.DisplayName) + ".png",
            Filter = "PNG|*.png"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(result.Image));
        using var stream = File.Create(dialog.FileName);
        encoder.Save(stream);
    }

    private void OnPickTexture(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ModelTextureItem item)
        {
            PickTexture(item);
        }
    }

    private void PickTexture(ModelTextureItem item)
    {
        if (_vm == null)
        {
            return;
        }
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("P3dView.Browse") + " - " + item.DisplayName,
            Filter = Loc.T("P3dView.ImageFilter") + "|*.paa;*.pac;*.png;*.tga;*.jpg;*.jpeg;*.bmp|*.*|*.*"
        };
        var folder = AppSettings.Default.ModelTextureFolder;
        if (Directory.Exists(folder))
        {
            dialog.InitialDirectory = folder;
        }
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }
        _vm.Textures.Overrides[item.Key] = dialog.FileName;
        Reload(k => k == item.Key);
    }

    private void OnClearTexture(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ModelTextureItem item)
        {
            ClearTexture(item);
        }
    }

    private void ClearTexture(ModelTextureItem item)
    {
        if (_vm == null)
        {
            return;
        }
        _vm.Textures.Overrides.Remove(item.Key);
        Reload(k => k == item.Key);
    }

    private void OnPickFolder(object sender, RoutedEventArgs e)
    {
        if (_vm == null)
        {
            return;
        }
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("P3dView.TextureFolder") };
        if (Directory.Exists(AppSettings.Default.ModelTextureFolder))
        {
            dialog.InitialDirectory = AppSettings.Default.ModelTextureFolder;
        }
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }
        AppSettings.Default.ModelTextureFolder = dialog.FolderName;
        AppSettings.Default.Save();
        _vm.Textures.Folder = dialog.FolderName;
        Reload(k => !_results[k].Found);
    }

    private void OnResetView(object sender, RoutedEventArgs e) => Fit();

    // What's on screen (this LOD, proxies left out) as OBJ + MTL, with full size PNG textures next to it.
    private void OnExportObj(object sender, RoutedEventArgs e)
    {
        if (_mesh == null || _vm == null || _mesh.Parts.Count == 0)
        {
            return;
        }
        var parts = _models.Where(m => IsVisible(m.Part.Texture)).Select(m => m.Part).ToList();
        var resolver = _vm.Textures;
        ModelExportWindow.Open(false, _vm.MainName, async target =>
        {
            ModelExportWindow.Configure(resolver);
            await Task.Run(() => ModelExport.Write(target, parts, resolver, ModelExportWindow.MaxTexture, ModelExportWindow.SplitAt));
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{target}\"");
        });
    }

    private void Fit()
    {
        if (_mesh == null || _mesh.Bounds.IsEmpty)
        {
            return;
        }
        var b = _mesh.Bounds;
        _target = new Point3D(b.X + b.SizeX / 2, b.Y + b.SizeY / 2, b.Z + b.SizeZ / 2);
        _radius = Math.Max(0.01, new Vector3D(b.SizeX, b.SizeY, b.SizeZ).Length / 2);
        _distance = _radius / Math.Sin(Camera.FieldOfView / 2 * Math.PI / 180) * 1.05;
        _yaw = 325;
        _pitch = 18;
        UpdateCamera();
    }

    private void UpdateCamera()
    {
        var yaw = _yaw * Math.PI / 180;
        var pitch = _pitch * Math.PI / 180;
        var direction = new Vector3D(Math.Cos(pitch) * Math.Sin(yaw), Math.Sin(pitch), Math.Cos(pitch) * Math.Cos(yaw));
        Camera.Position = _target + direction * _distance;
        Camera.LookDirection = -direction * _distance;
        Camera.NearPlaneDistance = Math.Max(0.001, _distance * 0.01);
        Camera.FarPlaneDistance = _distance + _radius * 6;
        var light = -direction;
        light.Y -= 0.35;
        HeadLight.Direction = light;
    }

    private void OnViewMouseDown(object sender, MouseButtonEventArgs e)
    {
        ViewHost.Focus();
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
        {
            Fit();
            e.Handled = true;
            return;
        }
        _drag = e.ChangedButton;
        _last = e.GetPosition(ViewHost);
        ViewHost.CaptureMouse();
        e.Handled = true;
    }

    private void OnViewMouseMove(object sender, MouseEventArgs e)
    {
        if (_drag == null)
        {
            return;
        }
        var position = e.GetPosition(ViewHost);
        var delta = position - _last;
        _last = position;
        WrapCursor(position);

        var pan = _drag != MouseButton.Left || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (!pan)
        {
            _yaw -= delta.X * 0.4;
            _pitch = Math.Clamp(_pitch + delta.Y * 0.4, -89, 89);
        }
        else
        {
            var look = Camera.LookDirection;
            look.Normalize();
            var right = Vector3D.CrossProduct(look, new Vector3D(0, 1, 0));
            if (right.LengthSquared < 1e-9)
            {
                right = new Vector3D(1, 0, 0);
            }
            right.Normalize();
            var up = Vector3D.CrossProduct(right, look);
            var perPixel = 2 * _distance * Math.Tan(Camera.FieldOfView / 2 * Math.PI / 180) / Math.Max(1, ViewHost.ActualHeight);
            _target += (-right * delta.X + up * delta.Y) * perPixel;
        }
        UpdateCamera();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    // Like Blender: dragging past an edge of the view carries on from the opposite edge.
    private void WrapCursor(Point position)
    {
        double width = ViewHost.ActualWidth, height = ViewHost.ActualHeight;
        if (width < 20 || height < 20)
        {
            return;
        }
        var wrapped = position;
        if (position.X <= 1) wrapped.X = width - 3;
        else if (position.X >= width - 1) wrapped.X = 3;
        if (position.Y <= 1) wrapped.Y = height - 3;
        else if (position.Y >= height - 1) wrapped.Y = 3;
        if (wrapped == position)
        {
            return;
        }
        var screen = ViewHost.PointToScreen(wrapped);
        SetCursorPos((int)screen.X, (int)screen.Y);
        _last = wrapped;
    }

    private void OnViewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_drag == e.ChangedButton)
        {
            _drag = null;
            ViewHost.ReleaseMouseCapture();
        }
        e.Handled = true;
    }

    private void OnViewLostCapture(object sender, MouseEventArgs e) => _drag = null;

    private void OnViewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _distance = Math.Clamp(_distance * Math.Pow(0.85, e.Delta / 120.0), _radius * 0.02, _radius * 60);
        UpdateCamera();
        e.Handled = true;
    }
}
