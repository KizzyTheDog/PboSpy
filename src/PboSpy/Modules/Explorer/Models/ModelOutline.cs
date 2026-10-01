using PboSpy.Interfaces;
using PboSpy.Models;
using PboSpy.Modules.P3d.Scene;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Data;

namespace PboSpy.Modules.Explorer.Models;

/// <summary>A row under a .p3d in the explorer, like Blender's outliner: a LOD, or one mesh (texture + material) in it.</summary>
public sealed class OutlineNode
{
    public string Name { get; init; }
    public string Icon { get; init; }
    public LazyNodes Children { get; init; }
    internal FileBase File { get; init; }
    internal int LodIndex { get; init; } = -1;
    internal string Texture { get; init; }
    public override string ToString() => Name;
}

/// <summary>Shows a "…" row until the node is expanded, then loads the real rows in the background.</summary>
public sealed class LazyNodes : ObservableCollection<OutlineNode>
{
    private readonly Func<List<OutlineNode>> _load;
    private bool _loaded;

    public LazyNodes(Func<List<OutlineNode>> load)
    {
        _load = load;
        Add(new OutlineNode { Name = "…" });
    }

    public async void Load()
    {
        if (_loaded)
        {
            return;
        }
        _loaded = true;
        List<OutlineNode> nodes;
        try
        {
            nodes = await Task.Run(_load);
        }
        catch (Exception e)
        {
            nodes = new List<OutlineNode> { new() { Name = e.Message } };
        }
        Clear();
        foreach (var node in nodes)
        {
            Add(node);
        }
    }
}

internal static class ModelOutline
{
    private const string LodIcon = "pack://application:,,,/PboSpy;component/Resources/Icons/p3d.png";
    private const string MeshIcon = "pack://application:,,,/PboSpy;component/Resources/Icons/paa.png";
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FileBase, LazyNodes> Cache = new();

    public static LazyNodes For(FileBase file) => Cache.GetValue(file, f => new LazyNodes(() => Lods(f)));

    private static List<OutlineNode> Lods(FileBase file)
    {
        var model = P3d.PreviewFactories.Load(file);
        return model.LODs.Select((lod, i) => new ModelLodOption(lod, i)).Where(l => l.Faces > 0).Select(option => new OutlineNode
        {
            Name = option.ToString(),
            Icon = LodIcon,
            File = file,
            LodIndex = option.Index,
            Children = new LazyNodes(() => Meshes(file, option))
        }).ToList();
    }

    private static List<OutlineNode> Meshes(FileBase file, ModelLodOption option) =>
        ModelMeshBuilder.Build(option.Lod).Parts
            .GroupBy(p => (p.Texture, p.Material))
            .Select(g => new OutlineNode
            {
                Name = $"{Short(g.Key.Texture) ?? "(no texture)"}   {Short(g.Key.Material)}   {g.Sum(p => p.Triangles):N0} tris",
                Icon = MeshIcon,
                File = file,
                LodIndex = option.Index,
                Texture = g.Key.Texture
            })
            .OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList();

    private static string Short(string path) =>
        string.IsNullOrWhiteSpace(path) ? null : TextureResolver.IsProcedural(path) ? path : System.IO.Path.GetFileName(TextureResolver.Normalize(path));
}

/// <summary>A tree row's children: folders' own, a lazy outline for .p3d files, or an outline node's.</summary>
internal class TreeChildrenConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        OutlineNode node => node.Children,
        ITreeItem { Children: { } children } => children,
        FileBase file when file.Extension == ".p3d" => ModelOutline.For(file),
        _ => null
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
