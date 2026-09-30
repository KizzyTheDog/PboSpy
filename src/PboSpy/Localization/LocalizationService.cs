using Gemini.Modules.MainMenu;
using Gemini.Modules.MainMenu.Models;
using Gemini.Modules.ToolBars;
using PboSpy.Services;
using System.Reflection;

namespace PboSpy.Localization;

/// <summary>
/// Pushes a language change into the parts of Gemini that only read their text once:
/// top level menus, command texts, tool window titles and toolbar tooltips.
/// </summary>
public static class LocalizationService
{
    private static readonly (MenuDefinition Menu, string Resource)[] GeminiMenus =
    {
        (Gemini.Modules.MainMenu.MenuDefinitions.FileMenu, "FileMenuText"),
        (Gemini.Modules.MainMenu.MenuDefinitions.EditMenu, "EditMenuText"),
        (Gemini.Modules.MainMenu.MenuDefinitions.ViewMenu, "ViewMenuText"),
        (Gemini.Modules.MainMenu.MenuDefinitions.ToolsMenu, "ToolsMenuText"),
        (Gemini.Modules.MainMenu.MenuDefinitions.WindowMenu, "WindowMenuText"),
        (Gemini.Modules.MainMenu.MenuDefinitions.HelpMenu, "HelpMenuText")
    };

    private static readonly FieldInfo MenuTextField =
        typeof(MenuDefinition).GetField("_text", BindingFlags.Instance | BindingFlags.NonPublic);

    private static IMenu _menu;
    private static IToolBars _toolBars;
    private static IShell _shell;
    private static ICommandService _commands;

    public static void Initialize(IMenu menu, IToolBars toolBars, IShell shell, ICommandService commands)
    {
        _menu = menu;
        _toolBars = toolBars;
        _shell = shell;
        _commands = commands;
        Loc.Instance.LanguageChanged += (_, _) => Refresh();
        Refresh();
    }

    public static void SetLanguage(string code)
    {
        AppSettings.Default.Language = code ?? "";
        AppSettings.Default.Save();

        GeminiSettings.LanguageCode = code ?? "";
        GeminiSettings.Save();

        Loc.Instance.SetLanguage(code);
    }

    public static void Refresh()
    {
        foreach (var (menu, resource) in GeminiMenus)
        {
            var text = Loc.Instance.TryGet("Gemini." + resource, out var mine)
                ? mine
                : Gemini.Properties.Resources.ResourceManager.GetString(resource, System.Globalization.CultureInfo.CurrentUICulture);
            if (text != null)
            {
                MenuTextField?.SetValue(menu, text);
            }
        }

        if (_commands != null)
        {
            foreach (var definition in IoC.GetAll<CommandDefinitionBase>())
            {
                if (definition.IsList)
                {
                    continue;
                }
                var command = _commands.GetCommand(definition);
                if (command == null)
                {
                    continue;
                }
                command.Text = Loc.Instance.TryGet("Cmd." + definition.Name, out var text) ? text : definition.Text;
                command.ToolTip = Loc.Instance.TryGet("Cmd." + definition.Name + "Tip", out var tip) ? tip : definition.ToolTip;
            }
        }

        if (_menu != null)
        {
            foreach (var item in _menu)
            {
                RefreshItem(item);
            }
        }

        if (_toolBars != null)
        {
            foreach (var toolBar in _toolBars.Items)
            {
                foreach (var item in toolBar)
                {
                    item.Refresh();
                }
            }
        }

        if (_shell != null)
        {
            foreach (var tool in _shell.Tools)
            {
                if (Loc.Instance.TryGet("Tool." + tool.GetType().Name, out var title))
                {
                    tool.DisplayName = title;
                }
            }
        }
    }

    private static void RefreshItem(MenuItemBase item)
    {
        item.Refresh();
        foreach (var child in item.Children)
        {
            RefreshItem(child);
        }
    }
}
