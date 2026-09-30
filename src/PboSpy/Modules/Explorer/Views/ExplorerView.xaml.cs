using PboSpy.Interfaces;
using PboSpy.Models;
using PboSpy.Modules.Explorer.ViewModels;
using PboSpy.Modules.P3dTools.Views;
using PboSpy.Modules.Pbo.Models;
using System.Windows;
using System.Windows.Controls;

namespace PboSpy.Modules.Explorer.Views;

public partial class ExplorerView : UserControl
{
    public ExplorerView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ExplorerViewModel old)
            {
                old.RevealRequested -= RevealItem;
                old.SearchApplied -= OnSearchApplied;
            }
            if (e.NewValue is ExplorerViewModel model)
            {
                model.RevealRequested += RevealItem;
                model.SearchApplied += OnSearchApplied;
            }
        };
    }

    private void RevealItem(ITreeItem item)
    {
        var chain = new List<ITreeItem>();
        for (var current = item; current != null; current = current.Parent)
        {
            chain.Insert(0, current);
        }
        ItemsControl parent = Items;
        TreeViewItem container = null;
        foreach (var node in chain)
        {
            container = parent.ItemContainerGenerator.ContainerFromItem(node) as TreeViewItem;
            if (container == null)
            {
                parent.UpdateLayout();
                container = parent.ItemContainerGenerator.ContainerFromItem(node) as TreeViewItem;
            }
            if (container == null)
            {
                return;
            }
            if (node != item)
            {
                container.IsExpanded = true;
                container.UpdateLayout();
            }
            parent = container;
        }
        container.IsSelected = true;
        container.BringIntoView();
        container.Focus();
    }

    private ExplorerViewModel ViewModel => DataContext as ExplorerViewModel;

    private bool _searchExpanded;

    private void OnSearchApplied(HashSet<ITreeItem> visible)
    {
        if (visible == null)
        {
            if (_searchExpanded)
            {
                _searchExpanded = false;
                SetExpanded(Items, false);
                if (ViewModel?.SelectedItem is ITreeItem selected)
                {
                    RevealItem(selected);
                }
            }
            return;
        }
        _searchExpanded = true;
        ExpandMatches(Items, visible);
    }

    private static void ExpandMatches(ItemsControl parent, HashSet<ITreeItem> visible)
    {
        foreach (var data in parent.Items)
        {
            if (data is not ITreeItem item || item.Children == null || !visible.Contains(item) ||
                !item.Children.Any(visible.Contains) ||
                parent.ItemContainerGenerator.ContainerFromItem(data) is not TreeViewItem container)
            {
                continue;
            }
            container.IsExpanded = true;
            container.UpdateLayout();
            ExpandMatches(container, visible);
        }
    }

    private void OnToolBarLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ToolBar toolBar && toolBar.Template.FindName("OverflowGrid", toolBar) is FrameworkElement overflow)
        {
            overflow.Visibility = Visibility.Collapsed;
        }
    }

    private void OnExportClick(object sender, RoutedEventArgs e) => ViewModel?.ExportSelection();

    private void OnExpandAll(object sender, RoutedEventArgs e) => SetExpanded(Items, true);

    private void OnCollapseAll(object sender, RoutedEventArgs e) => SetExpanded(Items, false);

    private static void SetExpanded(ItemsControl parent, bool expanded)
    {
        foreach (var data in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(data) is not TreeViewItem container)
            {
                continue;
            }
            container.IsExpanded = expanded;
            if (expanded)
            {
                container.UpdateLayout();
            }
            SetExpanded(container, expanded);
        }
    }

    private async void OnMenuOpen(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            await ViewModel.PreviewSelected();
        }
    }

    private void OnMenuExtractPbo(object sender, RoutedEventArgs e) => ViewModel?.ExtractSelectedPbo();

    private void OnMenuSelectFolder(object sender, RoutedEventArgs e) => ViewModel?.SelectAllInFolder();

    private void OnMenuSelectType(object sender, RoutedEventArgs e) => ViewModel?.SelectSameType();

    private void OnMenuClearSelection(object sender, RoutedEventArgs e) => ViewModel?.ClearSelection();

    private void OnMenuCopyPath(object sender, RoutedEventArgs e) => ViewModel?.CopyPath();

    private void OnMenuShowInExplorer(object sender, RoutedEventArgs e) => ViewModel?.ShowInExplorer();

    private void OnMenuClose(object sender, RoutedEventArgs e) => ViewModel?.CloseSelected();

    private void OnMenuHide(object sender, RoutedEventArgs e) => ViewModel?.HideSelected();

    private void OnMenuNames(object sender, RoutedEventArgs e) => ViewModel?.RecoverNames();

    private void OnMenuConvert(object sender, RoutedEventArgs e) => ViewModel?.ConvertSelected();

    private async void OnMenuModelsUsing(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            await ViewModel.ShowModelsUsingTexture();
        }
    }

    private void OnMenuCopyPathInPbo(object sender, RoutedEventArgs e) => ViewModel?.CopyPathInPbo();

    private async void OnMenuRename(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            await ViewModel.RenameSelected();
        }
    }

    private async void OnTreeKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.F2 && ViewModel != null)
        {
            e.Handled = true;
            await ViewModel.RenameSelected();
        }
    }

    private void OnMenuP3dDebin(object sender, RoutedEventArgs e) => ViewModel?.SendToP3dTools(P3dAction.Debinarize);

    private void OnMenuP3dStrip(object sender, RoutedEventArgs e) => ViewModel?.SendToP3dTools(P3dAction.StripProxies);

    private async void OnMenuExportModel(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            await ViewModel.ExportModels();
        }
    }

    private void OnMenuP3dRvmats(object sender, RoutedEventArgs e) => ViewModel?.SendToP3dTools(P3dAction.ExtractRvmats);

    private void OnMenuP3dCfg(object sender, RoutedEventArgs e) => ViewModel?.SendToP3dTools(P3dAction.ExtractModelCfg);

    private void OnMenuP3dPaths(object sender, RoutedEventArgs e) => ViewModel?.SendToP3dTools(P3dAction.ChangePaths);

    private void OnMenuP3dOpen(object sender, RoutedEventArgs e) => ViewModel?.SendToP3dTools(P3dAction.None);

    private void OnMenuPbr(object sender, RoutedEventArgs e) => ViewModel?.SendToPbrMaker();

    /// <summary>Only offers what applies to what was right-clicked, e.g. no "Extract PBO" on a PNG.</summary>
    private void OnContextMenuOpened(object sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm == null || sender is not ContextMenu menu)
        {
            return;
        }
        var selection = vm.ContextSelection;
        var single = selection.Count == 1 ? selection[0] : null;
        var anything = selection.Count > 0;

        Show(MenuModelPreview, single is FileBase { Extension: ".p3d" });
        Show(MenuOpen, single is FileBase and not FileBase { Extension: ".p3d" });
        Show(MenuExport, anything);
        Show(MenuExtractPbo, selection.Any(i => i is PboFile));
        Show(MenuNames, anything);
        Show(MenuConvert, anything);
        Show(MenuModelsUsing, vm.ContextIsTexture());
        Show(MenuCopyPathInPbo, selection.Any(i => i is not PboFile && i.Parent != null && RootIsPbo(i)));
        Show(MenuP3d, anything && vm.ContextHas(".p3d"));
        Show(MenuPbr, anything && vm.ContextHasPbrTextures());
        Show(MenuSelectFolder, single != null);
        Show(MenuSelectType, single != null && single.Children == null);
        Show(MenuClearSelection, vm.SelectedItems.Count > 0);
        Show(MenuRename, single != null && ExplorerViewModel.CanRename(single));
        Show(MenuCopyPath, anything);
        Show(MenuShowInExplorer, single is PhysicalFile or PhysicalDirectory or PboFile);
        Show(MenuHide, selection.Any(i => i.Children != null));
        Show(MenuClose, selection.Any(i => i is IPersistentItem && i.Parent == null));

        TidySeparators(menu);
    }

    private static bool RootIsPbo(ITreeItem item)
    {
        for (var current = item; current != null; current = current.Parent)
        {
            if (current is PboFile)
            {
                return true;
            }
        }
        return false;
    }

    private static void Show(UIElement item, bool visible) => item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    // No separator at the top, bottom or twice in a row once items are hidden.
    private static void TidySeparators(ItemsControl menu)
    {
        Separator pending = null;
        var seenItem = false;
        foreach (var child in menu.Items.OfType<Control>())
        {
            if (child is Separator separator)
            {
                separator.Visibility = Visibility.Collapsed;
                if (seenItem)
                {
                    pending = separator;
                }
                continue;
            }
            if (child.Visibility != Visibility.Visible)
            {
                continue;
            }
            if (pending != null)
            {
                pending.Visibility = Visibility.Visible;
                pending = null;
            }
            seenItem = true;
        }
    }
}
