using System.Windows.Input;

namespace PboSpy.Localization;

/// <summary>A submenu header whose text follows the current language.</summary>
public class LocalizedMenuItemDefinition : MenuItemDefinition
{
    private readonly string _key;
    private readonly Uri _iconSource;

    public LocalizedMenuItemDefinition(MenuItemGroupDefinition group, int sortOrder, string key, Uri iconSource = null)
        : base(group, sortOrder)
    {
        _key = key;
        _iconSource = iconSource;
    }

    public override string Text => Loc.T(_key);

    public override Uri IconSource => _iconSource;

    public override KeyGesture KeyGesture => null;

    public override CommandDefinitionBase CommandDefinition => null;
}
