using PboSpy.Interfaces;
using System.Windows;

namespace PboSpy.Modules.Explorer;

public interface ITreeSelectionHost
{
    bool IsSelectable(ITreeItem item);

    bool IsSelected(ITreeItem item);

    IReadOnlyCollection<ITreeItem> SelectedItems { get; }

    void SetSelection(IEnumerable<ITreeItem> items);

    void BeginDrag(DependencyObject source);
}
