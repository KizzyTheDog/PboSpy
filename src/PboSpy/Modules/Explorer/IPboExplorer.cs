namespace PboSpy.Modules.Explorer;

public interface IPboExplorer : ITool
{
    /// <summary>Expands the tree down to the item, selects it and scrolls it into view.</summary>
    void Reveal(PboSpy.Interfaces.ITreeItem item);
}
