using PboSpy.Services;
using PboSpy.Themes;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PboSpy.Modules.Windows;

/// <summary>
/// A top level window that lives next to the main window instead of inside it: it has its own
/// taskbar button, keeps working while PboSpy is in the background, follows the theme (including
/// the title bar) and remembers where it was.
/// </summary>
public class ToolWindow : Window
{
    private static readonly Dictionary<Type, ToolWindow> Open = new();

    public ToolWindow()
    {
        SetResourceReference(BackgroundProperty, "PboSpy.Surface");
        SetResourceReference(ForegroundProperty, "PboSpy.Text");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 12.5;
        ShowInTaskbar = true;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        try
        {
            Icon = new BitmapImage(new Uri("pack://application:,,,/PboSpy;component/Resources/Icons/WindowIcon.png"));
        }
        catch (Exception)
        {
        }

        SourceInitialized += (_, _) =>
        {
            RestorePlacement();
            ApplyTitleBar();
        };
        ThemeService.Changed += OnThemeChanged;
        Closed += (_, _) => ThemeService.Changed -= OnThemeChanged;
        Closing += (_, _) => SavePlacement();
    }

    /// <summary>Key used to remember the window position; defaults to the type name.</summary>
    protected virtual string PlacementKey => GetType().Name;

    protected virtual bool RememberPlacement => true;

    /// <summary>Shows the single window of this type, creating it if needed.</summary>
    public static T ShowSingle<T>(Func<T> create) where T : ToolWindow
    {
        if (Open.TryGetValue(typeof(T), out var existing) && existing.IsLoaded)
        {
            if (existing.WindowState == WindowState.Minimized)
            {
                existing.WindowState = WindowState.Normal;
            }
            existing.Activate();
            return (T)existing;
        }
        var window = create();
        Open[typeof(T)] = window;
        window.Closed += (_, _) => Open.Remove(typeof(T));
        window.Show();
        window.Activate();
        return window;
    }

    public static bool IsOpen<T>() where T : ToolWindow => Open.ContainsKey(typeof(T));

    public static void CloseAll()
    {
        foreach (var window in Open.Values.ToList())
        {
            window.Close();
        }
    }

    private void OnThemeChanged(object sender, EventArgs e) => ApplyTitleBar();

    private void RestorePlacement()
    {
        if (!RememberPlacement || !AppSettings.Default.Windows.TryGetValue(PlacementKey, out var placement) || placement.Width < 200 || placement.Height < 150)
        {
            return;
        }
        var bounds = new Rect(placement.Left, placement.Top, placement.Width, placement.Height);
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (!screen.IntersectsWith(bounds))
        {
            return;
        }
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
        if (placement.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void SavePlacement()
    {
        if (!RememberPlacement)
        {
            return;
        }
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (bounds.IsEmpty)
        {
            return;
        }
        AppSettings.Default.Windows[PlacementKey] = new WindowPlacement
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            Maximized = WindowState == WindowState.Maximized
        };
        AppSettings.Default.Save();
    }

    private void ApplyTitleBar() => TitleBar.Apply(this);
}

/// <summary>Colours a window's native title bar to match the theme (Windows 10 20H1+ / 11).</summary>
public static class TitleBar
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }
        try
        {
            var dark = ThemeService.IsDark ? 1 : 0;
            if (DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4) != 0)
            {
                DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref dark, 4);
            }
            if (ThemeService.Current is PaletteTheme palette)
            {
                var caption = ToColorRef(palette.SurfaceAlt);
                var text = ToColorRef(palette.TextColor);
                var border = ToColorRef(palette.BorderColor);
                DwmSetWindowAttribute(handle, DWMWA_CAPTION_COLOR, ref caption, 4);
                DwmSetWindowAttribute(handle, DWMWA_TEXT_COLOR, ref text, 4);
                DwmSetWindowAttribute(handle, DWMWA_BORDER_COLOR, ref border, 4);
            }
        }
        catch (Exception)
        {
        }
    }

    private static int ToColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);
}
