using BIS.P3D;
using PboSpy.Models;
using PboSpy.Modules.FileManager;
using PboSpy.Modules.P3d.Scene;
using PboSpy.Modules.Preview.ViewModels;
using PboSpy.Services;
using System.Collections.ObjectModel;

namespace PboSpy.Modules.P3d.ViewModels;

internal class P3dPreviewViewModel : PreviewViewModel
{
    private bool _showModel = true;
    private bool _showTextures = true;
    private bool _showDetailMaps = true;
    private ModelLodOption _selectedLod;

    public P3dPreviewViewModel(FileBase model, P3D p3d) : base(model)
    {
        Summary = new(p3d);
        MainName = model.Name;

        LODs = p3d.LODs.Select(lod => new LodViewModel(lod)).ToList();

        ModelLods = p3d.LODs.Select((lod, i) => new ModelLodOption(lod, i)).Where(l => l.Faces > 0).ToList();
        _selectedLod = ModelLods.Where(l => l.IsVisual).OrderBy(l => l.Resolution).FirstOrDefault() ?? ModelLods.FirstOrDefault();
        _showModel = _selectedLod != null;

        IEnumerable<PboSpy.Interfaces.ITreeItem> tree = null;
        try
        {
            tree = IoC.Get<IFileManager>().FileTree.ToList();
        }
        catch (Exception)
        {
        }
        Textures = new TextureResolver(model, tree) { Folder = AppSettings.Default.ModelTextureFolder };
    }

    /// <summary>Other models shown next to this one (texture usage view).</summary>
    public List<(string Name, ILevelOfDetail Lod)> Companions { get; } = new();

    public bool HasLodChoice => Companions.Count == 0;

    public string MainName { get; set; }

    /// <summary>Texture to put first in the list, when the view was opened from a texture.</summary>
    public string FocusTexture { get; set; }

    public P3dSummary Summary { get; set; }
    public IEnumerable<LodViewModel> LODs { get; set; }

    public IReadOnlyList<ModelLodOption> ModelLods { get; }
    public bool HasModel => ModelLods.Count > 0;
    public TextureResolver Textures { get; }
    public ObservableCollection<ModelTextureItem> TextureItems { get; } = new();

    public ModelLodOption SelectedLod
    {
        get => _selectedLod;
        set
        {
            _selectedLod = value;
            NotifyOfPropertyChange();
        }
    }

    public bool ShowModel
    {
        get => _showModel;
        set
        {
            _showModel = value;
            NotifyOfPropertyChange();
            NotifyOfPropertyChange(nameof(ShowDetails));
        }
    }

    public bool ShowDetails
    {
        get => !_showModel;
        set => ShowModel = !value;
    }

    public bool ShowTextures
    {
        get => _showTextures;
        set
        {
            _showTextures = value;
            NotifyOfPropertyChange();
        }
    }

    public bool ShowDetailMaps
    {
        get => _showDetailMaps;
        set
        {
            _showDetailMaps = value;
            NotifyOfPropertyChange();
        }
    }

    protected override void CanExecuteCopy(Command command)
        => command.Enabled = false;

    protected override Task ExecuteCopy(Command command)
        => throw new NotImplementedException();
}
