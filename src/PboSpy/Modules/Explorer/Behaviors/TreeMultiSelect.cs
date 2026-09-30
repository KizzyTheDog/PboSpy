using PboSpy.Interfaces;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace PboSpy.Modules.Explorer.Behaviors;

/// <summary>
/// Explorer style selection for a TreeView: click, Ctrl+click, Shift+click, rubber band
/// dragging on empty space, Ctrl+A and Escape. The selection lives on the host, the native
/// TreeView selection only tracks the focused item.
/// </summary>
public static class TreeMultiSelect
{
    public static readonly DependencyProperty HostProperty = DependencyProperty.RegisterAttached(
        "Host", typeof(ITreeSelectionHost), typeof(TreeMultiSelect), new PropertyMetadata(null, OnHostChanged));

    private static readonly DependencyProperty ControllerProperty = DependencyProperty.RegisterAttached(
        "Controller", typeof(Controller), typeof(TreeMultiSelect));

    /// <summary>Where modifier keys come from; swapped out by tests.</summary>
    internal static Func<ModifierKeys> ModifierSource = () => Keyboard.Modifiers;

    internal static Controller GetController(TreeView tree) => tree.GetValue(ControllerProperty) as Controller;

    public static ITreeSelectionHost GetHost(DependencyObject d) => (ITreeSelectionHost)d.GetValue(HostProperty);

    public static void SetHost(DependencyObject d, ITreeSelectionHost value) => d.SetValue(HostProperty, value);

    private static void OnHostChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TreeView tree)
        {
            return;
        }
        if (tree.GetValue(ControllerProperty) is not Controller controller)
        {
            controller = new Controller(tree);
            tree.SetValue(ControllerProperty, controller);
        }
        controller.Host = e.NewValue as ITreeSelectionHost;
    }

    internal sealed class Controller
    {
        private readonly TreeView _tree;
        private ITreeItem _anchor;
        private Point? _pressPoint;
        private ITreeItem _pressItem;
        private bool _deferredSingleSelect;
        private bool _syncing;

        private bool _banding;
        private Point _bandStart;
        private HashSet<ITreeItem> _bandBase;
        private RubberBandAdorner _adorner;

        public ITreeSelectionHost Host { get; set; }

        public Controller(TreeView tree)
        {
            _tree = tree;
            tree.PreviewMouseLeftButtonDown += OnMouseDown;
            tree.PreviewMouseMove += OnMouseMove;
            tree.PreviewMouseLeftButtonUp += OnMouseUp;
            tree.PreviewMouseRightButtonDown += OnRightMouseDown;
            tree.LostMouseCapture += (_, _) => EndBand();
            tree.SelectedItemChanged += OnNativeSelectionChanged;
            tree.PreviewKeyDown += OnKeyDown;
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (Host == null || e.OriginalSource is not DependencyObject source)
            {
                return;
            }
            if (FindAncestor<ToggleButton>(source) != null || FindAncestor<ScrollBar>(source) != null)
            {
                return;
            }

            var modifiers = ModifierSource();
            var ctrl = modifiers.HasFlag(ModifierKeys.Control);
            var shift = modifiers.HasFlag(ModifierKeys.Shift);
            var container = FindAncestor<TreeViewItem>(source);

            if (container == null || !IsOnHeader(source, container))
            {
                if (e.ClickCount > 1)
                {
                    return;
                }
                BeginBand(e.GetPosition(_tree), ctrl || shift);
                e.Handled = true;
                return;
            }

            if (container.DataContext is not ITreeItem item || !Host.IsSelectable(item))
            {
                e.Handled = true;
                return;
            }

            Click(container, item, modifiers, e.ClickCount);

            _pressPoint = e.GetPosition(_tree);
            e.Handled = true;
        }

        internal void Click(TreeViewItem container, ITreeItem item, ModifierKeys modifiers, int clickCount = 1)
        {
            var ctrl = modifiers.HasFlag(ModifierKeys.Control);
            var shift = modifiers.HasFlag(ModifierKeys.Shift);

            if (clickCount == 2)
            {
                if (item.Children != null)
                {
                    container.IsExpanded = !container.IsExpanded;
                }
                return;
            }

            _deferredSingleSelect = false;
            _pressItem = item;

            if (shift)
            {
                var range = Range(_anchor ?? item, item);
                Host.SetSelection(ctrl ? Host.SelectedItems.Concat(range) : range);
                Focus(container, true);
            }
            else if (ctrl)
            {
                var selected = Host.IsSelected(item);
                Host.SetSelection(selected
                    ? Host.SelectedItems.Where(i => !Equals(i, item))
                    : Host.SelectedItems.Append(item));
                _anchor = item;
                Focus(container, !selected);
            }
            else if (Host.IsSelected(item) && Host.SelectedItems.Count > 1)
            {
                // Keep the group so it can be dragged; collapse to one item on release.
                _deferredSingleSelect = true;
                _anchor = item;
                Focus(container, true);
            }
            else
            {
                Host.SetSelection(new[] { item });
                _anchor = item;
                Focus(container, true);
            }
        }

        internal void Release()
        {
            if (_deferredSingleSelect && _pressItem != null)
            {
                Host?.SetSelection(new[] { _pressItem });
            }
            _deferredSingleSelect = false;
            _pressPoint = null;
            _pressItem = null;
        }

        private void OnRightMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (Host == null || e.OriginalSource is not DependencyObject source)
            {
                return;
            }
            var container = FindAncestor<TreeViewItem>(source);
            if (container?.DataContext is ITreeItem item && Host.IsSelectable(item) && !Host.IsSelected(item))
            {
                Host.SetSelection(new[] { item });
                _anchor = item;
                Focus(container, true);
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (Host == null)
            {
                return;
            }

            if (_banding)
            {
                UpdateBand(e.GetPosition(_tree));
                return;
            }

            if (_pressPoint is not Point start || e.LeftButton != MouseButtonState.Pressed || _pressItem == null)
            {
                return;
            }

            var delta = e.GetPosition(_tree) - start;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            _pressPoint = null;
            _deferredSingleSelect = false;
            if (Host.IsSelected(_pressItem))
            {
                Host.BeginDrag(_tree);
            }
            _pressItem = null;
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_banding)
            {
                EndBand();
                e.Handled = true;
                return;
            }

            Release();
        }

        private void OnNativeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_syncing || Host == null || e.NewValue is not ITreeItem item)
            {
                return;
            }

            // Keyboard navigation.
            var modifiers = ModifierSource();
            if (modifiers.HasFlag(ModifierKeys.Shift) && _anchor != null)
            {
                Host.SetSelection(Range(_anchor, item));
            }
            else if (!modifiers.HasFlag(ModifierKeys.Control))
            {
                Host.SetSelection(Host.IsSelectable(item) ? new[] { item } : Array.Empty<ITreeItem>());
                _anchor = item;
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (Host == null)
            {
                return;
            }
            if (e.Key == Key.A && ModifierSource() == ModifierKeys.Control)
            {
                Host.SetSelection(VisibleItems().Select(v => v.Item).Where(Host.IsSelectable));
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Host.SetSelection(Array.Empty<ITreeItem>());
                e.Handled = true;
            }
            else if (e.Key == Key.Space && ModifierSource() == ModifierKeys.Control && _tree.SelectedItem is ITreeItem focused)
            {
                Host.SetSelection(Host.IsSelected(focused)
                    ? Host.SelectedItems.Where(i => !Equals(i, focused))
                    : Host.SelectedItems.Append(focused));
                e.Handled = true;
            }
        }

        private void Focus(TreeViewItem container, bool select)
        {
            _syncing = true;
            try
            {
                if (select)
                {
                    container.IsSelected = true;
                    container.Focus();
                }
                else if (container.IsSelected)
                {
                    container.IsSelected = false;
                }
            }
            finally
            {
                _syncing = false;
            }
        }

        private IEnumerable<ITreeItem> Range(ITreeItem from, ITreeItem to)
        {
            var visible = VisibleItems().Select(v => v.Item).ToList();
            var a = visible.IndexOf(from);
            var b = visible.IndexOf(to);
            if (a < 0 || b < 0)
            {
                return new[] { to };
            }
            if (a > b)
            {
                (a, b) = (b, a);
            }
            return visible.Skip(a).Take(b - a + 1).Where(Host.IsSelectable).ToList();
        }

        internal List<(TreeViewItem Container, ITreeItem Item)> VisibleItems()
        {
            var result = new List<(TreeViewItem, ITreeItem)>();
            Collect(_tree, result);
            return result;
        }

        private static void Collect(ItemsControl parent, List<(TreeViewItem, ITreeItem)> result)
        {
            foreach (var data in parent.Items)
            {
                if (parent.ItemContainerGenerator.ContainerFromItem(data) is not TreeViewItem container ||
                    container.Visibility != Visibility.Visible)
                {
                    continue;
                }
                if (data is ITreeItem item)
                {
                    result.Add((container, item));
                }
                if (container.IsExpanded)
                {
                    Collect(container, result);
                }
            }
        }

        internal void BeginBand(Point position, bool additive)
        {
            _bandBase = additive ? new HashSet<ITreeItem>(Host.SelectedItems) : new HashSet<ITreeItem>();
            if (!additive)
            {
                Host.SetSelection(Array.Empty<ITreeItem>());
            }

            _bandStart = ToContent(position);
            _banding = true;
            _tree.Focus();
            _tree.CaptureMouse();

            var layer = AdornerLayer.GetAdornerLayer(_tree);
            if (layer != null)
            {
                _adorner = new RubberBandAdorner(_tree);
                layer.Add(_adorner);
            }
        }

        internal void UpdateBand(Point position)
        {
            var viewer = FindChild<ScrollViewer>(_tree);
            if (viewer != null)
            {
                if (position.Y < 0)
                {
                    viewer.LineUp();
                }
                else if (position.Y > _tree.ActualHeight)
                {
                    viewer.LineDown();
                }
            }

            var end = ToContent(position);
            var band = new Rect(_bandStart, end);
            var offset = ScrollOffset();
            _adorner?.Update(new Rect(band.X - offset.X, band.Y - offset.Y, band.Width, band.Height));

            var hits = new HashSet<ITreeItem>(_bandBase);
            foreach (var (container, item) in VisibleItems())
            {
                var header = HeaderOf(container);
                if (header == null || !header.IsVisible || !Host.IsSelectable(item))
                {
                    continue;
                }
                var bounds = header.TransformToAncestor(_tree).TransformBounds(new Rect(header.RenderSize));
                bounds.Offset(offset.X, offset.Y);
                if (bounds.IntersectsWith(band))
                {
                    hits.Add(item);
                }
            }
            Host.SetSelection(hits);
        }

        internal void EndBand()
        {
            if (!_banding)
            {
                return;
            }
            _banding = false;
            if (_adorner != null)
            {
                AdornerLayer.GetAdornerLayer(_tree)?.Remove(_adorner);
                _adorner = null;
            }
            if (_tree.IsMouseCaptured)
            {
                _tree.ReleaseMouseCapture();
            }
        }

        private Point ToContent(Point viewport)
        {
            var offset = ScrollOffset();
            return new Point(viewport.X + offset.X, viewport.Y + offset.Y);
        }

        private Vector ScrollOffset()
        {
            var viewer = FindChild<ScrollViewer>(_tree);
            return viewer == null ? new Vector() : new Vector(viewer.HorizontalOffset, viewer.VerticalOffset);
        }

        internal static FrameworkElement HeaderOf(TreeViewItem container)
            => container.Template?.FindName("PART_Header", container) as FrameworkElement
               ?? container.Template?.FindName("Bd", container) as FrameworkElement;

        internal static bool IsOnHeader(DependencyObject source, TreeViewItem container)
        {
            var header = HeaderOf(container);
            if (header == null)
            {
                return true;
            }
            var border = container.Template?.FindName("Bd", container) as FrameworkElement;
            for (var current = source; current != null && current != container; current = Parent(current))
            {
                if (current == header || current == border)
                {
                    return true;
                }
            }
            return false;
        }

        private static DependencyObject Parent(DependencyObject d)
            => d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);

        private static T FindAncestor<T>(DependencyObject d) where T : DependencyObject
        {
            for (var current = d; current != null; current = Parent(current))
            {
                if (current is T match)
                {
                    return match;
                }
            }
            return null;
        }

        private static T FindChild<T>(DependencyObject d) where T : DependencyObject
        {
            var count = VisualTreeHelper.GetChildrenCount(d);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(d, i);
                if (child is T match)
                {
                    return match;
                }
                var nested = FindChild<T>(child);
                if (nested != null)
                {
                    return nested;
                }
            }
            return null;
        }
    }

    private sealed class RubberBandAdorner : Adorner
    {
        private Rect _rect;

        public RubberBandAdorner(UIElement adorned) : base(adorned)
        {
            IsHitTestVisible = false;
        }

        public void Update(Rect rect)
        {
            _rect = rect;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            var accent = (TryFindBrush("TreeViewItem.Highlight.Static") as SolidColorBrush)?.Color ?? SystemColors.HighlightColor;
            var fill = new SolidColorBrush(Color.FromArgb(0x40, accent.R, accent.G, accent.B));
            var pen = new Pen(new SolidColorBrush(accent), 1);
            dc.PushClip(new RectangleGeometry(new Rect(AdornedElement.RenderSize)));
            dc.DrawRectangle(fill, pen, _rect);
            dc.Pop();
        }

        private object TryFindBrush(string key) => (AdornedElement as FrameworkElement)?.TryFindResource(key);
    }
}
