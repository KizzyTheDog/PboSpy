using PboSpy.Localization;
using PboSpy.Modules.P3d.Scene;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PboSpy.Modules.P3d.ViewModels;

internal class ModelTextureItem : PropertyChangedBase
{
    private BitmapSource _thumb;
    private Brush _swatch;
    private string _status = "";
    private Brush _statusBrush;
    private bool _isOverride;
    private string _location = "";
    private string _linked = "";

    public ModelTextureItem(string path, int triangles)
    {
        Path = path;
        Triangles = triangles;
        DisplayName = string.IsNullOrWhiteSpace(path) ? Loc.T("P3dView.NoTexture")
            : TextureResolver.IsProcedural(path) ? path : System.IO.Path.GetFileName(PboSpy.Modules.Deobfuscate.Core.NameRecovery.FixEncoding(path));
    }

    public string Path { get; }
    public string Key => TextureResolver.Normalize(Path);
    public string DisplayName { get; }
    public int Triangles { get; set; }
    public bool CanPick => !string.IsNullOrWhiteSpace(Path) && (!TextureResolver.IsProcedural(Path) || TextureResolver.IsInvisible(Path));

    public BitmapSource Thumb { get => _thumb; set => Set(ref _thumb, value); }
    public Brush Swatch { get => _swatch; set => Set(ref _swatch, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    public Brush StatusBrush { get => _statusBrush; set => Set(ref _statusBrush, value); }
    public bool IsOverride { get => _isOverride; set => Set(ref _isOverride, value); }
    public string Location { get => _location; set => Set(ref _location, value ?? ""); }
    public PboSpy.Models.FileBase File { get; set; }
    public string Linked { get => _linked; set => Set(ref _linked, value ?? ""); }
    public bool IsHidden { get; set; }

    public string Tooltip => string.IsNullOrWhiteSpace(Path) ? DisplayName
        : string.Join("\n", new[] { Path, Status, Location }.Where(s => !string.IsNullOrEmpty(s)));

    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string name = null)
    {
        field = value;
        NotifyOfPropertyChange(name);
        if (name is nameof(Status) or nameof(Location))
        {
            NotifyOfPropertyChange(nameof(Tooltip));
        }
    }
}
