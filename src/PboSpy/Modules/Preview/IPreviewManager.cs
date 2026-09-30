using PboSpy.Models;

namespace PboSpy.Modules.Preview;

public interface IPreviewManager
{
    /// <summary>
    /// Shows a file. A preview (<paramref name="pin"/> false) reuses one tab that the next preview replaces;
    /// a pinned one gets its own tab.
    /// </summary>
    Task ShowPreview(FileBase model, bool pin = true, bool activate = true);
}
