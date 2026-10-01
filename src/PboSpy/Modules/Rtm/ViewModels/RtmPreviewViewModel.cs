using PboSpy.Localization;
using System.Windows;
using PboSpy.Interfaces;
using PboSpy.Models;
using PboSpy.Modules.Pbo.Models;
using PboSpy.Modules.Preview.ViewModels;
using System.IO;

namespace PboSpy.Modules.Rtm.ViewModels;

internal sealed class RtmChoice
{
    public string Name { get; init; }
    public FileBase File { get; init; }
    public override string ToString() => Name;
}

internal class RtmPreviewViewModel : PreviewViewModel
{
    private bool _showAnimation = true;

    public RtmPreviewViewModel(FileBase model) : base(model)
    {
        Choices = new List<RtmChoice> { new() { Name = Loc.T("Rtm.DefaultPose") } };
        Choices.AddRange(Siblings(model));
        Selected = Choices.FirstOrDefault(c => c.File != null && c.File.FullPath == model.FullPath) ?? Choices[0];
        try
        {
            Details = RtmAnimation.Read(model).Details;
        }
        catch (Exception e)
        {
            Details = Loc.F("Rtm.ReadError", e.Message);
        }
    }

    public List<RtmChoice> Choices { get; }

    public RtmChoice Selected { get; set; }

    public string Details { get; }

    public bool ShowAnimation
    {
        get => _showAnimation;
        set
        {
            _showAnimation = value;
            NotifyOfPropertyChange(nameof(ShowAnimation));
            NotifyOfPropertyChange(nameof(ShowDetails));
        }
    }

    public bool ShowDetails
    {
        get => !_showAnimation;
        set => ShowAnimation = !value;
    }

    // Every .rtm in the same folder and the folders under it.
    private static IEnumerable<RtmChoice> Siblings(FileBase model)
    {
        switch (model)
        {
            case PboEntry entry when entry.Parent != null:
                return All(entry.Parent).Select(e => new RtmChoice { Name = Relative(e.FullPath, entry.FullPath), File = e })
                    .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Take(2000).ToList();
            case PhysicalFile file:
                var folder = Path.GetDirectoryName(file.FullPath);
                return Directory.EnumerateFiles(folder, "*.rtm", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                    .Take(2000).Select(p => new RtmChoice { Name = Path.GetRelativePath(folder, p), File = new PhysicalFile(p) })
                    .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
            default:
                return new[] { new RtmChoice { Name = model.Name, File = model } };
        }
    }

    private static IEnumerable<PboEntry> All(ITreeItem item) =>
        (item.Children ?? Enumerable.Empty<ITreeItem>()).SelectMany(c => c is PboEntry e
            ? (e.Extension == ".rtm" ? new[] { e } : Enumerable.Empty<PboEntry>())
            : All(c));

    private static string Relative(string path, string anchor)
    {
        var folder = Path.GetDirectoryName(anchor) ?? "";
        return path.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase) ? path[(folder.Length + 1)..] : path;
    }

    protected override void CanExecuteCopy(Command command) => command.Enabled = true;

    protected override Task ExecuteCopy(Command command)
    {
        Clipboard.SetText(Details);
        return Task.CompletedTask;
    }
}
