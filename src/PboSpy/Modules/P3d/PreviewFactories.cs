using System.IO;
using BIS.Core.Streams;
using BIS.P3D;
using PboSpy.Models;
using PboSpy.Modules.P3d.ViewModels;

namespace PboSpy.Modules.P3d;

internal static class PreviewFactories
{
    [Export("FilePreviewFactory")]
    [ExportMetadata("Extensions", new[] { ".p3d" })]
    public static Document PreviewP3D(FileBase entry)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var model = Load(entry);
        PboSpy.Services.TestMode.Log($"p3d parse {entry.Name}: {clock.ElapsedMilliseconds} ms");
        return new P3dPreviewViewModel(entry, model);
    }

    // The last few parsed models, so going back to one (or reopening its tab) skips parsing and mesh building.
    private static readonly List<(string Key, P3D Model)> Recent = new();

    internal static P3D Load(FileBase entry)
    {
        var key = entry.FullPath + "|" + entry.DataSize;
        lock (Recent)
        {
            var hit = Recent.FindIndex(r => r.Key == key);
            if (hit >= 0)
            {
                var found = Recent[hit];
                Recent.RemoveAt(hit);
                Recent.Insert(0, found);
                return found.Model;
            }
        }
        P3D p3d;
        // The parser reads a few bytes at a time; a big buffer in front of the file makes huge models load ~2x faster.
        using (var stream = new BufferedStream(entry.GetStream(), 4 << 20))
        {
            p3d = StreamHelper.Read<P3D>(stream);
        }
        lock (Recent)
        {
            Recent.Insert(0, (key, p3d));
            if (Recent.Count > 4)
            {
                Recent.RemoveAt(Recent.Count - 1);
            }
        }
        return p3d;
    }
}
