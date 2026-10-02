using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PboSpy.Services;

/// <summary>
/// --test: runs next to the user's own PboSpy without touching their screen. Windows open off-screen without
/// taking focus or a taskbar button, settings live in their own folder, and writing a window title (or "main")
/// into %TEMP%\PboSpyTest\snap makes the app save that window as snap.png.
/// </summary>
internal static class TestMode
{
    public static readonly bool On = Environment.GetCommandLineArgs().Any(a => a.Equals("--test", StringComparison.OrdinalIgnoreCase));

    public static string Folder => Path.Combine(Path.GetTempPath(), "PboSpyTest");

    private static FileSystemWatcher _watcher;

    /// <summary>Call before the window is shown.</summary>
    public static void Hide(Window window)
    {
        if (!On || window == null)
        {
            return;
        }
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.WindowState = WindowState.Normal;
        window.Left = -32000;
        window.Top = 0;
        // Something (saved layout, a maximize) can pull it back on screen later; push it straight back off.
        void Keep()
        {
            if (window.WindowState != WindowState.Normal)
            {
                window.WindowState = WindowState.Normal;
            }
            if (window.Left > -30000)
            {
                window.Left = -32000;
            }
        }
        window.StateChanged += (_, _) => Keep();
        window.LocationChanged += (_, _) => Keep();
        if (window == Application.Current?.MainWindow || double.IsNaN(window.Width) || window.Width < 400)
        {
            // Main window at a typical full-screen size so timings and layout match real use.
            window.Width = window == Application.Current?.MainWindow ? 2560 : 1600;
            window.Height = window == Application.Current?.MainWindow ? 1392 : 900;
        }
    }

    public static void Start()
    {
        if (!On || _watcher != null)
        {
            return;
        }
        // Gemini shows the main window before modules can reach it; move it off as soon as they can.
        Hide(Application.Current.MainWindow);
        Directory.CreateDirectory(Folder);
        _watcher = new FileSystemWatcher(Folder) { EnableRaisingEvents = true };
        void Handle(object sender, FileSystemEventArgs e)
        {
            System.Action run = e.Name switch { "snap" => Snap, "cmd" => Command, _ => null };
            if (run != null)
            {
                Application.Current.Dispatcher.InvokeAsync(run, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        }
        _watcher.Created += Handle;
        _watcher.Changed += Handle;
    }

    // "open <path>" or "window workshop|find|presets"; the result goes to cmd.txt.
    private static async void Command()
    {
        var request = Path.Combine(Folder, "cmd");
        string text;
        try
        {
            text = File.ReadAllText(request).Trim();
            File.Delete(request);
        }
        catch (IOException)
        {
            return;
        }
        var verb = text.Split(' ', 2);
        try
        {
            switch (verb[0])
            {
                case "open":
                    await AppOpen.Show(new[] { verb[1] });
                    break;
                case "window" when verb[1] == "workshop":
                    Modules.Windows.WorkshopWindow.Open();
                    break;
                case "window" when verb[1] == "find":
                    Modules.Windows.FindInScriptsWindow.Open();
                    break;
                case "openlargest":
                    // "openlargest <pbo>": opens the PBO and previews its biggest model.
                    await AppOpen.Show(new[] { verb[1] }, preview: false);
                    var pbo = IoC.Get<Modules.FileManager.IFileManager>().FileTree.OfType<Modules.Pbo.Models.PboFile>()
                        .FirstOrDefault(f => f.PBO.PBOFilePath.Equals(verb[1], StringComparison.OrdinalIgnoreCase));
                    var largest = pbo?.AllEntries.Where(e => e.Extension == ".p3d").OrderByDescending(e => e.DataSize).FirstOrDefault();
                    if (largest != null)
                    {
                        await IoC.Get<Modules.Preview.IPreviewManager>().ShowPreview(largest, pin: true, activate: true);
                    }
                    break;
                case "view":
                    var v = verb[1].Split(' ').Select(double.Parse).ToArray();
                    Modules.P3d.Views.P3dPreviewView.Last?.TestView(v[0], v[1], v[2]);
                    break;
                case "orbit":
                    // Turns the 3D view for a while, like dragging, to measure redraws while moving.
                    for (var i = 0; i < int.Parse(verb[1]); i++)
                    {
                        Modules.P3d.Views.P3dPreviewView.Last?.TestOrbit(3);
                        await Task.Delay(16);
                    }
                    break;
                case "roblox":
                    // "roblox <rtm file>": writes the current rig and the animation into the test folder's roblox folder.
                    var output = Path.Combine(Folder, "roblox");
                    Directory.CreateDirectory(output);
                    var rig = Modules.Rtm.RtmRig.Load(Modules.Rtm.RobloxExport.CurrentRig());
                    Log("roblox: textures " + string.Join(" | ", rig.Groups.Select(g => $"{g.Texture} ({g.Triangles.Length / 3})")));
                    Modules.Rtm.RobloxExport.WriteRig(Path.Combine(output, "rig.fbx"), rig);
                    Modules.Rtm.RobloxExport.WriteAnimation(Path.Combine(output, Path.GetFileNameWithoutExtension(verb[1]) + ".rbxmx"),
                        Modules.Rtm.RtmAnimation.Read(new Models.PhysicalFile(verb[1])), rig);
                    break;
                case "window" when verb[1] == "presets":
                    Modules.Presets.PresetsWindow.Open();
                    break;
            }
            File.WriteAllText(Path.Combine(Folder, "cmd.txt"), "ok " + text);
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(Folder, "cmd.txt"), "error " + e.Message);
        }
    }

    /// <summary>Timings and notes for tests, in %TEMP%\PboSpyTest\log.txt (test mode only).</summary>
    public static void Log(string line)
    {
        if (On)
        {
            Directory.CreateDirectory(Folder);
            File.AppendAllText(Path.Combine(Folder, "log.txt"), $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
        }
    }

    private static void Snap()
    {
        var request = Path.Combine(Folder, "snap");
        string wanted;
        try
        {
            wanted = File.ReadAllText(request).Trim();
            File.Delete(request);
        }
        catch (IOException)
        {
            return;
        }
        var window = wanted is "" or "main"
            ? Application.Current.MainWindow
            : Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.Title.Contains(wanted, StringComparison.OrdinalIgnoreCase));
        if (window?.Content is not FrameworkElement content || content.ActualWidth < 1)
        {
            File.WriteAllText(Path.Combine(Folder, "snap.txt"), "no window: " + wanted + "\nopen: " +
                string.Join(", ", Application.Current.Windows.OfType<Window>().Select(w => w.Title)));
            return;
        }
        var dpi = VisualTreeHelper.GetDpi(content);
        var bitmap = new RenderTargetBitmap((int)(content.ActualWidth * dpi.DpiScaleX), (int)(content.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        // RenderTargetBitmap draws the element without its window background.
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(window.Background ?? Brushes.Black, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
            dc.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        }
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(Folder, "snap.png"));
        encoder.Save(file);
        File.WriteAllText(Path.Combine(Folder, "snap.txt"), "ok " + window.Title);
    }
}
