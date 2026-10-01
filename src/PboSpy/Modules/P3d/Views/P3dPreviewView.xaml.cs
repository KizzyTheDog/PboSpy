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
        Loaded += (_, _) =>
        {
            (AppSettings.Default.PreviewShading switch { "wire" => ShadeWire, "solid" => ShadeSolid, "material" => ShadeMaterial, _ => ShadeRendered }).IsChecked = true;
            StartStats();
            Last = this;
            if (_shading == "wire")
            {
                ApplyShading();
            }
        };
        Unloaded += (_, _) =>
        {
            StopStats();
            CompositionTarget.Rendering -= OnWireFrame;
            if (_walking)
            {
                StopWalk(keep: true);
            }
        };
        ViewHost.SizeChanged += (_, _) => _wireDirty = true;
    }

    // ---- view modes, like Blender's viewport shading ----

    private string _shading = "rendered";
    private int _wireVersion;

    private void OnShading(object sender, RoutedEventArgs e)
    {
        _shading = sender == ShadeWire ? "wire" : sender == ShadeSolid ? "solid" : sender == ShadeMaterial ? "material" : "rendered";
        AppSettings.Default.PreviewShading = _shading;
        AppSettings.Default.Save();
        if (_vm != null)
        {
            _vm.ShowTextures = _shading is "material" or "rendered";
            _vm.ShowDetailMaps = _shading == "rendered";
        }
        ApplyShading();
    }

    // Like Blender's X-ray wireframe: faces invisible, every edge a thin line, overlaps brighter.
    // WPF 3D can't draw lines, so the edges are projected and drawn into a bitmap on top, once per frame at most.
    private sealed class WirePart
    {
        public float[] Points;
        public int[] Edges;
        public byte[] Heat;
        public float[] ScreenX;
        public float[] ScreenY;
    }

    private readonly Dictionary<ModelPart, WirePart> _wireParts = new();
    private WriteableBitmap _wireBitmap;
    private int[] _wireCount;
    private byte[] _wireHeat;
    private uint[] _wirePixels;
    private bool _wireDirty;

    private async void ApplyShading()
    {
        ApplyMaterials();
        var wire = _shading == "wire";
        Viewport.Visibility = wire ? Visibility.Hidden : Visibility.Visible;
        WireImage.Visibility = wire ? Visibility.Visible : Visibility.Collapsed;
        DenseToggle.Visibility = WireImage.Visibility;
        CompositionTarget.Rendering -= OnWireFrame;
        if (!wire)
        {
            return;
        }
        var version = ++_wireVersion;
        var missing = _models.Select(m => m.Part).Where(p => !_wireParts.ContainsKey(p)).ToList();
        if (missing.Count > 0)
        {
            Overlay.Text = Loc.T("P3dView.Loading");
            var built = await Task.Run(() =>
            {
                var parts = missing.AsParallel().Select(p => (Part: p, Wire: BuildWire(p, out var lengths), Lengths: lengths)).ToList();
                // Short edges compared with the model's typical edge = many small triangles = dense.
                var sample = parts.SelectMany(p => p.Lengths.Where((_, k) => k % 8 == 0)).Where(l => l > 0).OrderBy(l => l).ToList();
                var median = sample.Count > 0 ? sample[sample.Count / 2] : 1f;
                foreach (var (_, data, lengths) in parts)
                {
                    data.Heat = lengths.Select(l => (byte)(Math.Clamp(Math.Log2(median / Math.Max(l, 1e-6f)) / 4 + 0.5, 0, 1) * 255)).ToArray();
                }
                return parts;
            });
            Overlay.Text = "";
            foreach (var (part, data, _) in built)
            {
                _wireParts[part] = data;
            }
        }
        if (version != _wireVersion || _shading != "wire")
        {
            return;
        }
        _wireDirty = true;
        CompositionTarget.Rendering += OnWireFrame;
    }

    private DateTime _lastWireMove;
    private bool _wireSharp = true;

    // While the camera moves the lines are drawn at half resolution (about 4x less work), then sharp again once it stops.
    private void OnWireFrame(object sender, EventArgs e)
    {
        var moving = (DateTime.UtcNow - _lastWireMove).TotalMilliseconds < 150;
        if (_wireDirty)
        {
            _wireDirty = false;
            DrawWire(moving ? 2 : 1);
            _wireSharp = !moving;
        }
        else if (!_wireSharp && !moving)
        {
            DrawWire(1);
            _wireSharp = true;
        }
    }

    private void OnDense(object sender, RoutedEventArgs e) => _wireDirty = true;

    // Unique edges; corners split for UV seams are merged by position so each edge is drawn once.
    private static WirePart BuildWire(ModelPart part, out float[] lengths)
    {
        var positions = part.Mesh.Positions;
        var indices = part.Mesh.TriangleIndices;
        var canonical = new int[positions.Count];
        var seen = new Dictionary<Point3D, int>();
        var points = new List<float>();
        for (var i = 0; i < positions.Count; i++)
        {
            var p = positions[i] + part.Offset;
            if (!seen.TryGetValue(p, out var c))
            {
                c = seen.Count;
                seen[p] = c;
                points.Add((float)p.X);
                points.Add((float)p.Y);
                points.Add((float)p.Z);
            }
            canonical[i] = c;
        }
        var unique = new HashSet<long>();
        var edges = new List<int>();
        for (var t = 0; t + 2 < indices.Count; t += 3)
        {
            for (var k = 0; k < 3; k++)
            {
                int a = canonical[indices[t + k]], b = canonical[indices[t + (k + 1) % 3]];
                if (a != b && unique.Add((long)Math.Min(a, b) << 32 | (uint)Math.Max(a, b)))
                {
                    edges.Add(a);
                    edges.Add(b);
                }
            }
        }
        var pts = points.ToArray();
        lengths = new float[edges.Count / 2];
        for (var e = 0; e < lengths.Length; e++)
        {
            int a = edges[e * 2] * 3, b = edges[e * 2 + 1] * 3;
            float dx = pts[a] - pts[b], dy = pts[a + 1] - pts[b + 1], dz = pts[a + 2] - pts[b + 2];
            lengths[e] = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        }
        var n = pts.Length / 3;
        return new WirePart { Points = pts, Edges = edges.ToArray(), ScreenX = new float[n], ScreenY = new float[n] };
    }

    private int _wireDraws;
    private readonly System.Diagnostics.Stopwatch _wireClock = new();

    private readonly WriteableBitmap[] _wireBitmaps = new WriteableBitmap[3];

    private void DrawWire(int step = 1)
    {
        _wireClock.Restart();
        var dpi = VisualTreeHelper.GetDpi(ViewHost);
        int w = (int)(ViewHost.ActualWidth * dpi.DpiScaleX / step), h = (int)(ViewHost.ActualHeight * dpi.DpiScaleY / step);
        if (w < 1 || h < 1)
        {
            return;
        }
        // One bitmap per resolution; its DPI makes either fill the same area.
        var bitmap = _wireBitmaps[step];
        if (bitmap == null || bitmap.PixelWidth != w || bitmap.PixelHeight != h)
        {
            bitmap = _wireBitmaps[step] = new WriteableBitmap(w, h, 96 * dpi.DpiScaleX / step, 96 * dpi.DpiScaleY / step, PixelFormats.Pbgra32, null);
        }
        _wireBitmap = bitmap;
        if (WireImage.Source != bitmap)
        {
            WireImage.Source = bitmap;
        }
        if (_wireCount == null || _wireCount.Length < w * h)
        {
            _wireCount = new int[w * h];
            _wireHeat = new byte[w * h];
            _wirePixels = new uint[w * h];
        }
        else
        {
            Array.Clear(_wireCount, 0, w * h);
            Array.Clear(_wireHeat, 0, w * h);
        }

        var look = Camera.LookDirection;
        look.Normalize();
        var right = Vector3D.CrossProduct(look, Camera.UpDirection);
        right.Normalize();
        var up = Vector3D.CrossProduct(right, look);
        float ex = (float)Camera.Position.X, ey = (float)Camera.Position.Y, ez = (float)Camera.Position.Z;
        float lx = (float)look.X, ly = (float)look.Y, lz = (float)look.Z;
        float rx = (float)right.X, ry = (float)right.Y, rz = (float)right.Z;
        float ux = (float)up.X, uy = (float)up.Y, uz = (float)up.Z;
        // WPF's field of view is horizontal.
        var scale = (float)(w / 2.0 / Math.Tan(Camera.FieldOfView / 2 * Math.PI / 180));
        var near = (float)Camera.NearPlaneDistance;
        float cx = w / 2f, cy = h / 2f;
        var dense = DenseToggle.IsChecked == true;
        var count = _wireCount;
        var heat = _wireHeat;

        foreach (var (part, _) in _models)
        {
            if (!IsVisible(part.Texture) || !_wireParts.TryGetValue(part, out var wire))
            {
                continue;
            }
            var pts = wire.Points;
            var sx = wire.ScreenX;
            var sy = wire.ScreenY;
            Parallel.For(0, (sx.Length + 8191) / 8192, chunk =>
            {
                for (int i = chunk * 8192, end = Math.Min(sx.Length, i + 8192); i < end; i++)
                {
                    float dx = pts[i * 3] - ex, dy = pts[i * 3 + 1] - ey, dz = pts[i * 3 + 2] - ez;
                    var z = dx * lx + dy * ly + dz * lz;
                    if (z < near)
                    {
                        sx[i] = float.NaN;
                        continue;
                    }
                    sx[i] = cx + (dx * rx + dy * ry + dz * rz) * scale / z;
                    sy[i] = cy - (dx * ux + dy * uy + dz * uz) * scale / z;
                }
            });
            var edges = wire.Edges;
            var edgeHeat = wire.Heat;
            // ponytail: plain (not interlocked) counters; two threads hitting one pixel at once lose a count, which only dims it a touch.
            Parallel.For(0, (edges.Length / 2 + 4095) / 4096, chunk =>
            {
                for (int e = chunk * 4096, end = Math.Min(edges.Length / 2, e + 4096); e < end; e++)
                {
                    int a = edges[e * 2], b = edges[e * 2 + 1];
                    Line(sx[a], sy[a], sx[b], sy[b], w, h, count, heat, dense ? edgeHeat[e] : (byte)0);
                }
            });
        }

        var pixels = _wirePixels;
        Parallel.For(0, h, y =>
        {
            for (int i = y * w, end = i + w; i < end; i++)
            {
                var c = count[i];
                if (c == 0)
                {
                    pixels[i] = 0;
                    continue;
                }
                var a = WireAlpha[Math.Min(c, WireAlpha.Length - 1)];
                uint r = 0xDC, g = 0xDC, b = 0xDC;
                if (dense)
                {
                    var t = heat[i] / 255f;
                    (r, g, b) = t < 0.5f
                        ? ((uint)(70 + 370 * t), (uint)(130 + 180 * t), (uint)(255 - 400 * t))
                        : ((uint)255, (uint)(220 - 340 * (t - 0.5f)), (uint)(55 - 30 * (t - 0.5f)));
                }
                pixels[i] = (uint)(a * 255) << 24 | (uint)(r * a) << 16 | (uint)(g * a) << 8 | (uint)(b * a);
            }
        });
        _wireBitmap.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);
        if (++_wireDraws < 12)
        {
            PboSpy.Services.TestMode.Log($"wire draw {w}x{h} (step {step}): {_wireClock.ElapsedMilliseconds} ms");
        }
    }

    // How opaque a pixel is when this many lines cross it.
    private static readonly float[] WireAlpha = Enumerable.Range(0, 12).Select(c => 1 - MathF.Pow(0.45f, c)).ToArray();

    private static void Line(float x0, float y0, float x1, float y1, int w, int h, int[] count, byte[] heat, byte value)
    {
        if (float.IsNaN(x0) || float.IsNaN(x1))
        {
            return;
        }
        float dx = x1 - x0, dy = y1 - y0, t0 = 0, t1 = 1;
        if (!Clip(-dx, x0, ref t0, ref t1) || !Clip(dx, w - 1 - x0, ref t0, ref t1) ||
            !Clip(-dy, y0, ref t0, ref t1) || !Clip(dy, h - 1 - y0, ref t0, ref t1))
        {
            return;
        }
        float ax = x0 + t0 * dx, ay = y0 + t0 * dy, bx = x0 + t1 * dx, by = y0 + t1 * dy;
        var steps = Math.Max(1, (int)MathF.Ceiling(Math.Max(Math.Abs(bx - ax), Math.Abs(by - ay))));
        float ix = (bx - ax) / steps, iy = (by - ay) / steps;
        for (var i = 0; i < steps; i++)
        {
            var p = (int)(ay + 0.5f) * w + (int)(ax + 0.5f);
            count[p]++;
            if (heat[p] < value)
            {
                heat[p] = value;
            }
            ax += ix;
            ay += iy;
        }
    }

    // Liang-Barsky: trims the line to the bitmap.
    private static bool Clip(float p, float q, ref float t0, ref float t1)
    {
        if (p == 0)
        {
            return q >= 0;
        }
        var r = q / p;
        if (p < 0)
        {
            if (r > t1)
            {
                return false;
            }
            t0 = Math.Max(t0, r);
        }
        else
        {
            if (r < t0)
            {
                return false;
            }
            t1 = Math.Min(t1, r);
        }
        return true;
    }

    // ---- performance stats, bottom right ----

    private int _frames;
    private TimeSpan _lastRender;
    private DateTime _activeUntil;
    private bool _listening;
    private double _fps;
    private System.Windows.Threading.DispatcherTimer _statsTimer;
    private readonly System.Diagnostics.Stopwatch _statsClock = new();

    // Rendering can fire several times per frame (same RenderingTime), and listening to it makes WPF redraw
    // non-stop, so frames are only counted while the camera moves.
    private void OnFrame(object sender, EventArgs e)
    {
        var time = ((RenderingEventArgs)e).RenderingTime;
        if (time != _lastRender)
        {
            _lastRender = time;
            _frames++;
        }
        if (DateTime.UtcNow > _activeUntil)
        {
            CompositionTarget.Rendering -= OnFrame;
            _listening = false;
        }
    }

    private void Moving()
    {
        _activeUntil = DateTime.UtcNow.AddSeconds(0.5);
        if (!_listening && _statsTimer != null)
        {
            _listening = true;
            CompositionTarget.Rendering += OnFrame;
        }
    }

    private void StartStats()
    {
        StopStats();
        if (!AppSettings.Default.PreviewStats)
        {
            PerfText.Text = "";
            return;
        }
        _statsClock.Restart();
        _statsTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(0.5) };
        _statsTimer.Tick += (_, _) =>
        {
            if (_frames > 0)
            {
                _fps = _frames / Math.Max(0.001, _statsClock.Elapsed.TotalSeconds);
            }
            var idle = !_listening;
            _frames = 0;
            _statsClock.Restart();
            var shown = _mesh == null ? 0 : _models.Where(m => m.Model.Material != null).Sum(m => m.Part.Triangles);
            var memory = Environment.WorkingSet / 1024 / 1024;
            PerfText.Text = Loc.F("P3dView.Perf", _fps < 1 ? "-" : _fps.ToString("0") + (idle ? "*" : ""), shown.ToString("N0"), _models.Count, _materials.Count, memory);
        };
        _statsTimer.Start();
    }

    private void StopStats()
    {
        CompositionTarget.Rendering -= OnFrame;
        _listening = false;
        _statsTimer?.Stop();
        _statsTimer = null;
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
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var model = _vm.Model;
            var tree = IoC.Get<PboSpy.Modules.FileManager.IFileManager>().FileTree.ToList();
            mesh = await Task.Run(() =>
            {
                // The vehicle config decides what its hidden selections (camo, decals, numbers) show by default.
                var main = ModelMeshBuilder.Build(lod.Lod, ConfigTextures.Cached(model, tree, lod.Lod));
                PboSpy.Services.TestMode.Log($"p3d mesh build: {clock.ElapsedMilliseconds} ms");
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
        var sceneClock = System.Diagnostics.Stopwatch.StartNew();
        Dispatcher.BeginInvoke(() => PboSpy.Services.TestMode.Log($"p3d scene + first render: {sceneClock.ElapsedMilliseconds} ms"), System.Windows.Threading.DispatcherPriority.ContextIdle);
        SceneRoot.Children.Remove(Parts);
        Parts.Children.Clear();
        _models.Clear();
        _wireParts.Clear();
        // See-through parts go last, otherwise WPF hides whatever is drawn behind them afterwards.
        foreach (var part in mesh.Parts.OrderBy(p => TextureResolver.HasAlpha(TextureResolver.Normalize(p.Texture))))
        {
            var material = IsVisible(part.Texture) ? MaterialFor(part) : null;
            var model = new GeometryModel3D(part.Mesh, material) { BackMaterial = TwoSided(part) ? material : null };
            if (part.Offset.LengthSquared > 0)
            {
                model.Transform = new TranslateTransform3D(part.Offset);
            }
            Parts.Children.Add(model);
            _models.Add((part, model));
        }
        SceneRoot.Children.Add(Parts);
        Overlay.Text = mesh.Parts.Count == 0 ? Loc.T("P3dView.NoGeometry") : "";
        if (_shading == "wire")
        {
            ApplyShading();
        }
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
                var pending = _vm.TextureItems.Where(i => !_results.ContainsKey(i.Key)).Take(Math.Max(4, Environment.ProcessorCount)).ToList();
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
                        var found = resolver.Resolve(item.Path);
                        Thumb(found.Image);
                        return found;
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
        // WPF adds specular on top even where the colour is see-through, which painted decal backgrounds grey.
        if (maps.Specular?.Image != null && !TextureResolver.HasAlpha(TextureResolver.Normalize(colour.Location)))
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
        item.Thumb = Thumb(result.Image);
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

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BitmapSource, BitmapSource> Thumbs = new();

    // The list shows 40 px squares; scaling a 1024 texture down on every layout pass was a visible stutter.
    private static BitmapSource Thumb(BitmapSource image) => image == null ? null : Thumbs.GetValue(image, source =>
    {
        var scale = 64.0 / Math.Max(source.PixelWidth, source.PixelHeight);
        if (scale >= 1)
        {
            return source;
        }
        var small = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        var copy = new WriteableBitmap(small);
        copy.Freeze();
        return copy;
    });

    private Brush Resource(string key) => TryFindResource(key) as Brush;

    // Drawing back faces doubles the work; the game culls them too, except on see-through (_ca) parts.
    private static bool TwoSided(ModelPart part) => TextureResolver.HasAlpha(TextureResolver.Normalize(part.Texture));

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
        return (_isolated == null || key == _isolated) && !_hidden.Contains(key)
            && (!TextureResolver.IsInvisible(key) || _vm?.Textures.Overrides.ContainsKey(key) == true);
    }

    private void ApplyMaterials()
    {
        foreach (var (part, model) in _models)
        {
            var material = IsVisible(part.Texture) ? MaterialFor(part) : null;
            if (model.Material != material)
            {
                model.Material = material;
                model.BackMaterial = TwoSided(part) ? material : null;
            }
        }
        _wireDirty = true;
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
        Add("P3dView.PickUnused", () => PickUnused(item), item.CanPick);
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

    private void PickUnused(ModelTextureItem item)
    {
        if (_vm == null)
        {
            return;
        }
        var used = new HashSet<string>(_results.Values.Select(r => TextureResolver.Normalize(r.Location)), StringComparer.OrdinalIgnoreCase);
        var images = _vm.Textures.Images().Where(i => !used.Contains(TextureResolver.Normalize(i.Path)));
        PickTextureWindow.Open(item.DisplayName, images, path =>
        {
            _vm.Textures.Overrides[item.Key] = path;
            Reload(k => k == item.Key);
        });
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

    // Front, back, sides, top and bottom; the model faces -Z here (Arma's +Z, mirrored).
    private void OnViewMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = ViewButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var (key, yaw, pitch) in new[] { ("P3dView.ViewFront", 180.0, 0.0), ("P3dView.ViewBack", 0.0, 0.0), ("P3dView.ViewLeft", 270.0, 0.0),
                     ("P3dView.ViewRight", 90.0, 0.0), ("P3dView.ViewTop", 180.0, 90.0), ("P3dView.ViewBottom", 180.0, -90.0), ("P3dView.ViewCorner", 325.0, 18.0) })
        {
            var item = new MenuItem { Header = Loc.T(key) };
            item.Click += (_, _) =>
            {
                _yaw = yaw;
                _pitch = pitch;
                UpdateCamera();
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

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
            await Task.Run(() => ModelExport.Write(target, parts, resolver, ModelExportWindow.MaxTexture, ModelExportWindow.SplitAt,
                ModelExportWindow.Keep, ModelExportWindow.ByMaterial, ModelExportWindow.Lods));
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

    /// <summary>The last loaded preview, for test-mode commands.</summary>
    internal static P3dPreviewView Last { get; private set; }

    internal void TestView(double yaw, double pitch, double zoom)
    {
        _yaw = yaw;
        _pitch = pitch;
        _distance = _radius * zoom;
        UpdateCamera();
    }

    internal void TestOrbit(double degrees)
    {
        _yaw += degrees;
        UpdateCamera();
    }

    private void UpdateCamera()
    {
        Moving();
        _wireDirty = true;
        _lastWireMove = DateTime.UtcNow;
        var yaw = _yaw * Math.PI / 180;
        var pitch = _pitch * Math.PI / 180;
        var direction = new Vector3D(Math.Cos(pitch) * Math.Sin(yaw), Math.Sin(pitch), Math.Cos(pitch) * Math.Cos(yaw));
        Camera.Position = _target + direction * _distance;
        Camera.LookDirection = -direction * _distance;
        // Up follows the orbit, so going over the top keeps turning instead of flipping.
        Camera.UpDirection = new Vector3D(-Math.Sin(pitch) * Math.Sin(yaw), Math.Cos(pitch), -Math.Sin(pitch) * Math.Cos(yaw));
        Camera.NearPlaneDistance = Math.Max(0.001, _distance * 0.05);
        Camera.FarPlaneDistance = _distance + _radius * 6;
        var light = -direction;
        light.Y -= 0.35;
        HeadLight.Direction = light;
    }

    // ---- walk navigation, like Blender's (Shift+`) ----

    private bool _walking;
    private (Point3D Target, double Yaw, double Pitch) _walkStart;
    private readonly HashSet<Key> _walkKeys = new();
    private double _walkSpeed = 1;
    private readonly System.Diagnostics.Stopwatch _walkClock = new();

    private Vector3D Direction()
    {
        var yaw = _yaw * Math.PI / 180;
        var pitch = _pitch * Math.PI / 180;
        return new Vector3D(Math.Cos(pitch) * Math.Sin(yaw), Math.Sin(pitch), Math.Cos(pitch) * Math.Cos(yaw));
    }

    private Point WalkCenter => new(ViewHost.ActualWidth / 2, ViewHost.ActualHeight / 2);

    private void OnViewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (!_walking)
        {
            if (key == Key.OemTilde && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && _mesh != null)
            {
                StartWalk();
                e.Handled = true;
            }
            return;
        }
        e.Handled = true;
        if (key == Key.Escape)
        {
            StopWalk(keep: false);
        }
        else if (key is Key.Enter or Key.Space)
        {
            StopWalk(keep: true);
        }
        else
        {
            _walkKeys.Add(key);
        }
    }

    private void OnViewKeyUp(object sender, KeyEventArgs e)
    {
        if (_walking)
        {
            _walkKeys.Remove(e.Key == Key.System ? e.SystemKey : e.Key);
            e.Handled = true;
        }
    }

    private void StartWalk()
    {
        _walking = true;
        _walkStart = (_target, _yaw, _pitch);
        _walkKeys.Clear();
        WalkText.Text = Loc.T("P3dView.WalkHint");
        Mouse.OverrideCursor = Cursors.None;
        ViewHost.CaptureMouse();
        var screen = ViewHost.PointToScreen(WalkCenter);
        SetCursorPos((int)screen.X, (int)screen.Y);
        _walkClock.Restart();
        CompositionTarget.Rendering += OnWalkFrame;
    }

    private void StopWalk(bool keep)
    {
        _walking = false;
        CompositionTarget.Rendering -= OnWalkFrame;
        Mouse.OverrideCursor = null;
        ViewHost.ReleaseMouseCapture();
        WalkText.Text = "";
        if (!keep)
        {
            (_target, _yaw, _pitch) = _walkStart;
        }
        UpdateCamera();
    }

    private void OnWalkFrame(object sender, EventArgs e)
    {
        var seconds = Math.Min(0.1, _walkClock.Elapsed.TotalSeconds);
        _walkClock.Restart();
        var look = -Direction();
        var right = Vector3D.CrossProduct(look, Camera.UpDirection);
        right.Normalize();
        var move = new Vector3D();
        bool Held(params Key[] keys) => keys.Any(_walkKeys.Contains);
        if (Held(Key.W, Key.Up)) move += look;
        if (Held(Key.S, Key.Down)) move -= look;
        if (Held(Key.D, Key.Right)) move += right;
        if (Held(Key.A, Key.Left)) move -= right;
        if (Held(Key.E)) move += new Vector3D(0, 1, 0);
        if (Held(Key.Q)) move -= new Vector3D(0, 1, 0);
        if (move.LengthSquared < 1e-9)
        {
            return;
        }
        move.Normalize();
        var speed = _radius * 0.5 * _walkSpeed * (Held(Key.LeftShift, Key.RightShift) ? 4 : 1) * (Held(Key.LeftAlt, Key.RightAlt) ? 0.25 : 1);
        _target += move * speed * seconds;
        UpdateCamera();
    }

    // Turns the view around the eye, not around the target like orbiting does.
    private void WalkLook(Vector delta)
    {
        var eye = _target + Direction() * _distance;
        _yaw -= delta.X * 0.15;
        _pitch += delta.Y * 0.15;
        _target = eye - Direction() * _distance;
        UpdateCamera();
    }

    private void OnViewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_walking)
        {
            StopWalk(keep: e.ChangedButton != MouseButton.Right);
            e.Handled = true;
            return;
        }
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
        if (_walking)
        {
            var look = e.GetPosition(ViewHost) - WalkCenter;
            if (Math.Abs(look.X) + Math.Abs(look.Y) > 0.5)
            {
                WalkLook(look);
                var screen = ViewHost.PointToScreen(WalkCenter);
                SetCursorPos((int)screen.X, (int)screen.Y);
            }
            return;
        }
        if (_drag == null)
        {
            return;
        }
        var position = e.GetPosition(ViewHost);
        var delta = position - _last;
        _last = position;
        WrapCursor(position);

        // Left: orbit the model. Right: look around from where the camera is. Middle or Shift: pan.
        if (_drag == MouseButton.Right && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            WalkLook(delta * 2.5);
            return;
        }
        var pan = _drag != MouseButton.Left || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (!pan)
        {
            _yaw -= delta.X * 0.4;
            _pitch += delta.Y * 0.4;
        }
        else
        {
            var look = Camera.LookDirection;
            look.Normalize();
            var right = Vector3D.CrossProduct(look, Camera.UpDirection);
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

    private void OnViewLostCapture(object sender, MouseEventArgs e)
    {
        _drag = null;
        if (_walking)
        {
            StopWalk(keep: true);
        }
    }

    private void OnViewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_walking)
        {
            _walkSpeed = Math.Clamp(_walkSpeed * Math.Pow(1.25, e.Delta / 120.0), 0.05, 50);
            return;
        }
        _distance = Math.Clamp(_distance * Math.Pow(0.85, e.Delta / 120.0), _radius * 0.02, _radius * 60);
        UpdateCamera();
        e.Handled = true;
    }
}
