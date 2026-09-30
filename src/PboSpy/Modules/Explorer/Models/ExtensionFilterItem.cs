namespace PboSpy.Modules.Explorer.Models;

public class ExtensionFilterItem : PropertyChangedBase
{
    private readonly Action<ExtensionFilterItem> _changed;
    private bool _isChecked;
    private int _count;

    public ExtensionFilterItem(string extension, int count, bool isChecked, Action<ExtensionFilterItem> changed)
    {
        Extension = extension;
        _count = count;
        _isChecked = isChecked;
        _changed = changed;
    }

    public string Extension { get; }

    public string Label => string.IsNullOrEmpty(Extension) ? "(no extension)" : "*" + Extension;

    public int Count
    {
        get => _count;
        set
        {
            _count = value;
            NotifyOfPropertyChange(nameof(Count));
        }
    }

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value)
            {
                return;
            }
            _isChecked = value;
            NotifyOfPropertyChange(nameof(IsChecked));
            _changed?.Invoke(this);
        }
    }

    internal void SetSilently(bool value)
    {
        _isChecked = value;
        NotifyOfPropertyChange(nameof(IsChecked));
    }
}
