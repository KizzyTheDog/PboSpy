using BIS.Core.Config;
using PboSpy.Interfaces;
using PboSpy.Models;
using PboSpy.Modules.ConfigExplorer.Models;
using PboSpy.Modules.Pbo.Models;

namespace PboSpy.Modules.P3d.Scene;

/// <summary>
/// What the game puts on a vehicle's hidden selections by default: the CfgVehicles class using this model gives
/// hiddenSelections and their textures, and its first TextureSources entry (what the game applies when it spawns)
/// overrides them. A blank entry means the slot is hidden, like an unused decal.
/// </summary>
internal static class ConfigTextures
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BIS.P3D.ILevelOfDetail, Dictionary<int, string>> Done = new();

    public static Dictionary<int, string> Cached(FileBase model, IEnumerable<ITreeItem> tree, BIS.P3D.ILevelOfDetail lod)
    {
        try
        {
            return Done.GetValue(lod, l => For(model, tree, l));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Section index → texture ("" = hide). Empty when no config in the opened PBOs uses the model.</summary>
    public static Dictionary<int, string> For(FileBase model, IEnumerable<ITreeItem> tree, BIS.P3D.ILevelOfDetail lod)
    {
        var result = new Dictionary<int, string>();
        if (model is not PboEntry || lod is not BIS.P3D.ODOL.LOD odol || odol.NamedSelections == null)
        {
            return result;
        }
        var root = new ConfigClassItem();
        foreach (var pbo in Pbos(tree))
        {
            root.MergePbo(pbo, null);
        }
        var vehicles = root.ResolveClassDirect("CfgVehicles");
        if (vehicles == null)
        {
            if (PboSpy.Services.TestMode.On)
                PboSpy.Services.TestMode.Log("config: no CfgVehicles in " + string.Join(", ", Pbos(tree).Select(p => p.Name)));
            return result;
        }
        var wanted = Path(model.FullPath);
        var users = vehicles.Children.Where(c => c != null && Properties(c).TryGetValue("model", out var m) && m is string s && Path(s) == wanted).ToList();
        if (PboSpy.Services.TestMode.On)
            PboSpy.Services.TestMode.Log($"config: wanted {wanted}; {vehicles.Children.Count} vehicles; users {users.Count}; models: " +
            string.Join(" | ", vehicles.Children.Where(c => c != null).Select(c => Properties(c).TryGetValue("model", out var m) ? m as string : null).Where(m => m != null).Distinct().Take(6)));
        var vehicle = users.FirstOrDefault(c => Properties(c).TryGetValue("scope", out var scope) && Convert.ToInt32(scope) == 2) ?? users.FirstOrDefault();
        if (vehicle == null)
        {
            return result;
        }
        var props = Properties(vehicle);
        var names = Strings(props, "hiddenSelections");
        var textures = Strings(props, "hiddenSelectionsTextures");
        // The default look: the first scheme in textureList[] (name, weight, ...), else the first TextureSources class as written.
        var sources = vehicle.ResolveClassDirect("TextureSources");
        var listed = props.TryGetValue("textureList", out var list) && list is RawArray listArray ? listArray.Entries.FirstOrDefault()?.Value as string : null;
        var firstWritten = sources?.Definitions.Values.SelectMany(d => d.Entries.OfType<ParamClass>()).Select(c => c.Name).FirstOrDefault();
        var source = sources == null ? null : sources.ResolveClassDirect(listed ?? firstWritten ?? "") ?? sources.Children.FirstOrDefault();
        if (source != null)
        {
            var sourceTextures = Strings(Properties(source), "textures");
            for (var i = 0; i < sourceTextures.Count; i++)
            {
                if (i < textures.Count)
                {
                    textures[i] = sourceTextures[i];
                }
                else
                {
                    textures.Add(sourceTextures[i]);
                }
            }
        }
        if (PboSpy.Services.TestMode.On)
            PboSpy.Services.TestMode.Log($"config: {vehicle.Name}, {names.Count} hidden selections, {textures.Count} textures, source {source?.Name}: " +
            string.Join(", ", names.Select((n, k) => $"{n}={(k < textures.Count ? textures[k] : "-")}")));
        for (var i = 0; i < names.Count && i < textures.Count; i++)
        {
            var selection = odol.NamedSelections.FirstOrDefault(s => s.Name.Equals(names[i], StringComparison.OrdinalIgnoreCase));
            if (selection?.Sections == null)
            {
                continue;
            }
            foreach (var index in selection.Sections)
            {
                result[index] = textures[i];
            }
        }
        return result;
    }

    private static Dictionary<string, object> Properties(ConfigClassItem item) =>
        item.GetAllProperties().ToDictionary(p => p.Key, p => p.Value is RawValue raw ? raw.Value : p.Value, StringComparer.OrdinalIgnoreCase);

    private static List<string> Strings(Dictionary<string, object> props, string name) =>
        props.TryGetValue(name, out var value) && value is RawArray array ? array.Entries.Select(e => e.Value as string ?? "").ToList() : new List<string>();

    private static string Path(string path)
    {
        var p = TextureResolver.Normalize(path);
        return p.EndsWith(".p3d") ? p : p + ".p3d";
    }

    private static IEnumerable<PboFile> Pbos(IEnumerable<ITreeItem> items)
    {
        foreach (var item in items ?? Enumerable.Empty<ITreeItem>())
        {
            if (item is PboFile pbo)
            {
                yield return pbo;
            }
            else if (item?.Children != null)
            {
                foreach (var inner in Pbos(item.Children.ToList()))
                {
                    yield return inner;
                }
            }
        }
    }
}
