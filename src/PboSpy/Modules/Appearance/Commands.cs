using PboSpy.Localization;
using PboSpy.Modules.FileManager;
using PboSpy.Services;
using PboSpy.Themes;
using System.IO;

namespace PboSpy.Modules.Appearance;

[CommandDefinition]
public class ThemeListCommandDefinition : CommandListDefinition
{
    public override string Name => "View.ThemeList";
}

[CommandDefinition]
public class LanguageListCommandDefinition : CommandListDefinition
{
    public override string Name => "View.LanguageList";
}

[CommandDefinition]
public class RecentFilesCommandDefinition : CommandListDefinition
{
    public override string Name => "File.RecentFiles";
}

[CommandHandler]
public class ThemeListCommandHandler : ICommandListHandler<ThemeListCommandDefinition>
{
    public void Populate(Command command, List<Command> commands)
    {
        foreach (var theme in ThemeService.Themes)
        {
            commands.Add(new Command(command.CommandDefinition)
            {
                Text = theme.Name,
                Checked = ThemeService.Current == theme,
                Tag = theme
            });
        }
    }

    public Task Run(Command command)
    {
        ThemeService.SetTheme(command.Tag as Gemini.Framework.Themes.ITheme);
        return Task.CompletedTask;
    }
}

[CommandHandler]
public class LanguageListCommandHandler : ICommandListHandler<LanguageListCommandDefinition>
{
    public void Populate(Command command, List<Command> commands)
    {
        foreach (var language in Loc.Instance.Languages)
        {
            commands.Add(new Command(command.CommandDefinition)
            {
                Text = language.NativeName,
                Checked = Loc.Instance.Language == language.Code,
                Tag = language.Code
            });
        }
    }

    public Task Run(Command command)
    {
        LocalizationService.SetLanguage(command.Tag as string);
        return Task.CompletedTask;
    }
}

[CommandHandler]
public class RecentFilesCommandHandler : ICommandListHandler<RecentFilesCommandDefinition>
{
    private readonly IFileManager _fileManager;

    [ImportingConstructor]
    public RecentFilesCommandHandler(IFileManager fileManager)
    {
        _fileManager = fileManager;
    }

    public void Populate(Command command, List<Command> commands)
    {
        var index = 1;
        foreach (var path in AppSettings.Default.RecentFiles.Where(p => File.Exists(p) || Directory.Exists(p)))
        {
            commands.Add(new Command(command.CommandDefinition)
            {
                Text = $"_{index++} {Path.GetFileName(path.TrimEnd('\\'))}",
                ToolTip = path,
                Tag = path
            });
        }
        if (commands.Count == 0)
        {
            commands.Add(new Command(command.CommandDefinition)
            {
                Text = Loc.T("Menu.NoRecent"),
                Enabled = false
            });
        }
    }

    public Task Run(Command command)
    {
        return command.Tag is string path ? _fileManager.LoadSupportedFiles(new[] { path }) : Task.CompletedTask;
    }
}

internal static class MenuDefinitions
{
    [Export]
    public static readonly MenuItemGroupDefinition ViewAppearanceGroup = new(
        Gemini.Modules.MainMenu.MenuDefinitions.ViewMenu, 50);

    [Export]
    public static readonly MenuItemDefinition ThemeMenuItem = new LocalizedMenuItemDefinition(ViewAppearanceGroup, 0, "Menu.Theme");

    [Export]
    public static readonly MenuItemGroupDefinition ThemeListGroup = new(ThemeMenuItem, 0);

    [Export]
    public static readonly MenuItemDefinition ThemeListMenuItem = new CommandMenuItemDefinition<ThemeListCommandDefinition>(ThemeListGroup, 0);

    [Export]
    public static readonly MenuItemDefinition LanguageMenuItem = new LocalizedMenuItemDefinition(ViewAppearanceGroup, 1, "Menu.Language");

    [Export]
    public static readonly MenuItemGroupDefinition LanguageListGroup = new(LanguageMenuItem, 0);

    [Export]
    public static readonly MenuItemDefinition LanguageListMenuItem = new CommandMenuItemDefinition<LanguageListCommandDefinition>(LanguageListGroup, 0);

    [Export]
    public static readonly MenuItemDefinition RecentMenuItem = new LocalizedMenuItemDefinition(
        Gemini.Modules.MainMenu.MenuDefinitions.FileNewOpenMenuGroup, 5, "Menu.Recent");

    [Export]
    public static readonly MenuItemGroupDefinition RecentListGroup = new(RecentMenuItem, 0);

    [Export]
    public static readonly MenuItemDefinition RecentListMenuItem = new CommandMenuItemDefinition<RecentFilesCommandDefinition>(RecentListGroup, 0);
}
