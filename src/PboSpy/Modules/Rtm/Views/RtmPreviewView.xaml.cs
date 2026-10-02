using PboSpy.Localization;
using PboSpy.Modules.Rtm.ViewModels;
using PboSpy.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace PboSpy.Modules.Rtm.Views;

public partial class RtmPreviewView : UserControl
{
    private RtmRig _rig;
    private RtmAnimation _animation;
    private readonly List<(string Texture, MeshGeometry3D Mesh, GeometryModel3D Model)> _parts = new();
    private readonly Dictionary<string, Material> _textures = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Material Plain = Frozen(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA))));

    private static Material Frozen(Material material)
    {
        material.Freeze();
        return material;
    }
    private readonly System.Diagnostics.Stopwatch _clock = new();
    private double _phase;
    private double _yaw = 200, _pitch = 10, _distance = 3.2;
    private Point3D _home = new(0, 0.9, 0);
    private Point3D _target = new(0, 0.9, 0);
    private Point _last;
    private MouseButton? _drag;

    public RtmPreviewView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnFrame;
    }

    private RtmPreviewViewModel ViewModel => DataContext as RtmPreviewViewModel;

    private sealed record RigChoice(string Name, string[] Paths)
    {
        public override string ToString() => Name;
    }

    private bool _fillingRigs;

    private void FillRigs()
    {
        _fillingRigs = true;
        var choices = RtmRig.BuiltIn.Select(r => new RigChoice(Loc.T(r.Key), r.Paths))
            .Concat(AppSettings.Default.RtmRigs.Where(System.IO.File.Exists).Select(p => new RigChoice(System.IO.Path.GetFileNameWithoutExtension(p), new[] { p })))
            .Append(new RigChoice(Loc.T("Rtm.AddRig"), null)).ToList();
        RigPicker.ItemsSource = choices;
        RigPicker.SelectedItem = choices.FirstOrDefault(c => c.Paths != null && string.Join("|", c.Paths) == AppSettings.Default.RtmRig) ?? choices[0];
        _fillingRigs = false;
    }

    private async void OnRig(object sender, SelectionChangedEventArgs e)
    {
        if (_fillingRigs || RigPicker.SelectedItem is not RigChoice choice)
        {
            return;
        }
        if (choice.Paths == null)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "P3D|*.p3d", Title = Loc.T("Rtm.AddRig") };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true && !AppSettings.Default.RtmRigs.Contains(dialog.FileName, StringComparer.OrdinalIgnoreCase))
            {
                AppSettings.Default.RtmRigs.Add(dialog.FileName);
            }
            if (dialog.FileName.Length > 0)
            {
                AppSettings.Default.RtmRig = dialog.FileName;
            }
            AppSettings.Default.Save();
            FillRigs();
        }
        else
        {
            AppSettings.Default.RtmRig = string.Join("|", choice.Paths);
            AppSettings.Default.Save();
        }
        await LoadRig();
    }

    private async Task LoadRig()
    {
        var paths = (RigPicker.SelectedItem as RigChoice)?.Paths ?? RtmRig.BuiltIn[0].Paths;
        Overlay.Text = Loc.T("P3dView.Loading");
        try
        {
            _rig = await Task.Run(() => RtmRig.Load(paths));
            Overlay.Text = _rig == null ? Loc.T("Rtm.NoGame") : "";
        }
        catch (Exception ex)
        {
            _rig = null;
            Overlay.Text = ex.Message;
        }
        if (_rig == null)
        {
            BodyGroup.Children.Clear();
            _parts.Clear();
            return;
        }
        var low = _rig.Points.Aggregate(System.Numerics.Vector3.Min);
        var high = _rig.Points.Aggregate(System.Numerics.Vector3.Max);
        _home = new Point3D((low.X + high.X) / 2, (low.Y + high.Y) / 2, -(low.Z + high.Z) / 2);
        _target = _home;
        UpdateCamera();
        BodyGroup.Children.Clear();
        _parts.Clear();
        var uvs = new PointCollection(_rig.Uvs.Select(u => new Point(u.X, u.Y)));
        uvs.Freeze();
        foreach (var (texture, triangles) in _rig.Groups)
        {
            var mesh = new MeshGeometry3D { TriangleIndices = new Int32Collection(triangles), TextureCoordinates = uvs };
            var model = new GeometryModel3D(mesh, Plain) { BackMaterial = Plain };
            BodyGroup.Children.Add(model);
            _parts.Add((texture, mesh, model));
        }
        Show();
        await LoadTextures();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateCamera();
        if (_rig == null)
        {
            FillRigs();
            await LoadRig();
            await Pick();
        }
        CompositionTarget.Rendering -= OnFrame;
        CompositionTarget.Rendering += OnFrame;
    }

    private async void OnPick(object sender, SelectionChangedEventArgs e)
    {
        if (_rig != null)
        {
            await Pick();
        }
    }

    private async Task Pick()
    {
        var file = (Picker.SelectedItem as RtmChoice)?.File;
        try
        {
            _animation = file == null ? null : await Task.Run(() => RtmAnimation.Read(file));
            Overlay.Text = "";
        }
        catch (Exception ex)
        {
            _animation = null;
            Overlay.Text = Loc.F("Rtm.ReadError", ex.Message);
        }
        _phase = 0;
        _startPhase = 0;
        _clock.Restart();
        UpdatePlayLabel();
        Show();
    }

    private double _startPhase;

    private void UpdatePlayLabel()
    {
        PlayButton.Content = Loc.T(PlayButton.IsChecked == true ? "Rtm.Pause" : "Rtm.Play");
        LoopButton.IsChecked = AppSettings.Default.RtmLoop;
    }

    private void OnFrame(object sender, EventArgs e)
    {
        if (_animation == null || PlayButton.IsChecked != true)
        {
            return;
        }
        // ponytail: the real speed lives in the config (CfgMovesBasic); about 30 frames per second looks right for most.
        var length = Math.Max(0.6, _animation.Frames.Length / 30.0);
        var phase = _startPhase + _clock.Elapsed.TotalSeconds / length;
        if (phase >= 1 && LoopButton.IsChecked != true)
        {
            // Without loop it stops on the last frame; Play starts it again from the beginning.
            phase = 1;
            PlayButton.IsChecked = false;
            UpdatePlayLabel();
        }
        _phase = phase % 1 == 0 && phase >= 1 ? 1 : phase % 1;
        PhaseSlider.Value = _phase;
    }

    private void OnPhase(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _phase = e.NewValue;
        Show();
    }

    private void OnScrub(object sender, MouseButtonEventArgs e)
    {
        PlayButton.IsChecked = false;
        UpdatePlayLabel();
    }

    // Carries on from where it is (or from the start once it reached the end).
    private void OnPlay(object sender, RoutedEventArgs e)
    {
        _startPhase = _phase >= 0.999 ? 0 : _phase;
        _clock.Restart();
        UpdatePlayLabel();
    }

    private void OnLoop(object sender, RoutedEventArgs e)
    {
        AppSettings.Default.RtmLoop = LoopButton.IsChecked == true;
        AppSettings.Default.Save();
    }

    private void Show()
    {
        if (_rig == null)
        {
            return;
        }
        var points = _rig.Pose(_animation, _animation?.Sample(_phase) ?? Array.Empty<System.Numerics.Matrix4x4>());
        var positions = new Point3DCollection(points.Length);
        // Arma is left-handed; mirrored on Z like the model preview.
        foreach (var p in points)
        {
            positions.Add(new Point3D(p.X, p.Y, -p.Z));
        }
        positions.Freeze();
        foreach (var part in _parts)
        {
            part.Mesh.Positions = positions;
        }
        FrameText.Text = _animation == null ? "" : $"{_phase * 100:0}%  {_animation.Frames.Length} frames";
    }

    // The game's own textures for the rig, read from the installed Arma 3 like the model preview does.
    private async Task LoadTextures()
    {
        TexturesButton.IsChecked = AppSettings.Default.RtmTextures;
        var rig = _rig;
        var wanted = _parts.Select(p => p.Texture).Where(t => !string.IsNullOrWhiteSpace(t) && !_textures.ContainsKey(t)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (wanted.Count > 0 && rig.Source != null)
        {
            var found = await Task.Run(() =>
            {
                var resolver = new PboSpy.Modules.P3d.Scene.TextureResolver(rig.Source, null);
                return wanted.AsParallel().Select(t => (t, resolver.Resolve(t, 1024))).ToList();
            });
            foreach (var (texture, result) in found)
            {
                Material material = result.Image != null ? Frozen(new DiffuseMaterial(Tile(result.Image)))
                    : result.Color is Color color ? Frozen(new DiffuseMaterial(new SolidColorBrush(color))) : Plain;
                _textures[texture] = material;
            }
        }
        if (rig == _rig)
        {
            ApplyTextures();
        }
    }

    private static ImageBrush Tile(System.Windows.Media.Imaging.BitmapSource image)
    {
        var brush = new ImageBrush(image) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1), Stretch = Stretch.Fill };
        brush.Freeze();
        return brush;
    }

    private void ApplyTextures()
    {
        foreach (var (texture, _, model) in _parts)
        {
            var material = TexturesButton.IsChecked == true && texture != null && _textures.TryGetValue(texture, out var m) ? m : Plain;
            // Invisible placeholders (empty slots) stay hidden when textured, like in game.
            var hidden = TexturesButton.IsChecked == true && PboSpy.Modules.P3d.Scene.TextureResolver.IsInvisible(texture);
            model.Material = hidden ? null : material;
            model.BackMaterial = hidden ? null : material;
        }
    }

    private void OnTextures(object sender, RoutedEventArgs e)
    {
        AppSettings.Default.RtmTextures = TexturesButton.IsChecked == true;
        AppSettings.Default.Save();
        ApplyTextures();
    }

    private async void OnRoblox(object sender, RoutedEventArgs e)
    {
        if ((DataContext as RtmPreviewViewModel)?.Selected?.File is { } file)
        {
            await RobloxExport.Export(new[] { file });
        }
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        _yaw = 200;
        _pitch = 10;
        _distance = 3.2;
        _target = _home;
        UpdateCamera();
    }

    private void UpdateCamera()
    {
        var yaw = _yaw * Math.PI / 180;
        var pitch = _pitch * Math.PI / 180;
        var direction = new Vector3D(Math.Cos(pitch) * Math.Sin(yaw), Math.Sin(pitch), Math.Cos(pitch) * Math.Cos(yaw));
        Camera.Position = _target + direction * _distance;
        Camera.LookDirection = -direction * _distance;
        Camera.UpDirection = new Vector3D(-Math.Sin(pitch) * Math.Sin(yaw), Math.Cos(pitch), -Math.Sin(pitch) * Math.Cos(yaw));
        Camera.NearPlaneDistance = 0.05;
        HeadLight.Direction = -direction + new Vector3D(0, -0.35, 0);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _drag = e.ChangedButton;
        _last = e.GetPosition(ViewHost);
        ViewHost.CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_drag == null)
        {
            return;
        }
        var position = e.GetPosition(ViewHost);
        var delta = position - _last;
        _last = position;
        if (_drag == MouseButton.Left && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
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

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _drag = null;
        ViewHost.ReleaseMouseCapture();
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        _distance = Math.Clamp(_distance * Math.Pow(0.85, e.Delta / 120.0), 0.3, 30);
        UpdateCamera();
    }
}
