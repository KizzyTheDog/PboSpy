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
    private MeshGeometry3D _mesh;
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
            Body.Geometry = null;
            return;
        }
        var low = _rig.Points.Aggregate(System.Numerics.Vector3.Min);
        var high = _rig.Points.Aggregate(System.Numerics.Vector3.Max);
        _home = new Point3D((low.X + high.X) / 2, (low.Y + high.Y) / 2, -(low.Z + high.Z) / 2);
        _target = _home;
        UpdateCamera();
        _mesh = new MeshGeometry3D { TriangleIndices = new Int32Collection(_rig.Triangles) };
        Body.Geometry = _mesh;
        Show();
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
        _clock.Restart();
        Show();
    }

    private void OnFrame(object sender, EventArgs e)
    {
        if (_animation == null || PlayButton.IsChecked != true)
        {
            return;
        }
        // ponytail: the real speed lives in the config (CfgMovesBasic); about 30 frames per second looks right for most.
        var length = Math.Max(0.6, _animation.Frames.Length / 30.0);
        _phase = _clock.Elapsed.TotalSeconds / length % 1;
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
    }

    private void OnPlay(object sender, RoutedEventArgs e)
    {
        _clock.Restart();
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
        _mesh.Positions = positions;
        FrameText.Text = _animation == null ? "" : $"{_phase * 100:0}%  {_animation.Frames.Length} frames";
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
