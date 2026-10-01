using PboSpy.Localization;
using PboSpy.Modules.P3dTools.Views;
using PboSpy.Modules.Pbr.Views;
using System.Windows.Input;

namespace PboSpy.Modules.Windows;

[CommandDefinition]
public class OpenP3dToolsCommandDefinition : CommandDefinition
{
    public const string CommandName = "Tools.P3dTools";

    public override string Name => CommandName;

    public override string Text => Loc.T("Command.P3dTools");

    public override string ToolTip => Loc.T("Command.P3dToolsTip");

    public override Uri IconSource => new("pack://application:,,,/PboSpy;component/Resources/Icons/p3d.png");

    [Export]
    public static CommandKeyboardShortcut KeyGesture =
        new CommandKeyboardShortcut<OpenP3dToolsCommandDefinition>(new KeyGesture(Key.D, ModifierKeys.Control | ModifierKeys.Shift));
}

[CommandHandler]
public class OpenP3dToolsCommandHandler : CommandHandlerBase<OpenP3dToolsCommandDefinition>
{
    public override Task Run(Command command)
    {
        P3dToolsWindow.Open();
        return Task.CompletedTask;
    }
}

[CommandDefinition]
public class OpenPbrMakerCommandDefinition : CommandDefinition
{
    public const string CommandName = "Tools.PbrMaker";

    public override string Name => CommandName;

    public override string Text => Loc.T("Command.PbrMaker");

    public override string ToolTip => Loc.T("Command.PbrMakerTip");

    public override Uri IconSource => new("pack://application:,,,/PboSpy;component/Resources/Icons/paa.png");

    [Export]
    public static CommandKeyboardShortcut KeyGesture =
        new CommandKeyboardShortcut<OpenPbrMakerCommandDefinition>(new KeyGesture(Key.R, ModifierKeys.Control | ModifierKeys.Shift));
}

[CommandHandler]
public class OpenPbrMakerCommandHandler : CommandHandlerBase<OpenPbrMakerCommandDefinition>
{
    public override Task Run(Command command)
    {
        PbrMakerWindow.Open();
        return Task.CompletedTask;
    }
}

[CommandDefinition]
public class RecoverNamesCommandDefinition : CommandDefinition
{
    public const string CommandName = "Tools.RecoverNames";

    public override string Name => CommandName;

    public override string Text => Loc.T("Command.RecoverNames");

    public override string ToolTip => Loc.T("Command.RecoverNamesTip");
}

[CommandHandler]
public class RecoverNamesCommandHandler : CommandHandlerBase<RecoverNamesCommandDefinition>
{
    public override Task Run(Command command)
    {
        if (IoC.Get<Explorer.IPboExplorer>() is Explorer.ViewModels.ExplorerViewModel explorer)
        {
            explorer.RecoverNames();
        }
        return Task.CompletedTask;
    }
}

[CommandDefinition]
public class ConvertSelectedCommandDefinition : CommandDefinition
{
    public const string CommandName = "Tools.ConvertSelected";

    public override string Name => CommandName;

    public override string Text => Loc.T("Command.ConvertSelected");

    public override string ToolTip => Loc.T("Command.ConvertSelectedTip");
}

[CommandHandler]
public class ConvertSelectedCommandHandler : CommandHandlerBase<ConvertSelectedCommandDefinition>
{
    public override void Update(Command command)
    {
        command.Enabled = IoC.Get<Explorer.IPboExplorer>() is Explorer.ViewModels.ExplorerViewModel explorer && explorer.ContextSelection.Count > 0;
    }

    public override Task Run(Command command)
    {
        if (IoC.Get<Explorer.IPboExplorer>() is Explorer.ViewModels.ExplorerViewModel explorer)
        {
            explorer.ConvertSelected();
        }
        return Task.CompletedTask;
    }
}

[CommandDefinition]
public class OpenPresetsCommandDefinition : CommandDefinition
{
    public const string CommandName = "Tools.Presets";

    public override string Name => CommandName;

    public override string Text => Loc.T("Command.Presets");

    public override string ToolTip => Loc.T("Command.PresetsTip");

    public override Uri IconSource => new("pack://application:,,,/PboSpy;component/Resources/Icons/ExtractFile.png");

    [Export]
    public static CommandKeyboardShortcut KeyGesture =
        new CommandKeyboardShortcut<OpenPresetsCommandDefinition>(new KeyGesture(Key.G, ModifierKeys.Control | ModifierKeys.Shift));
}

[CommandHandler]
public class OpenPresetsCommandHandler : CommandHandlerBase<OpenPresetsCommandDefinition>
{
    public override Task Run(Command command)
    {
        Presets.PresetsWindow.Open();
        return Task.CompletedTask;
    }
}

[CommandDefinition]
public class FindInScriptsCommandDefinition : CommandDefinition
{
    public const string CommandName = "Tools.FindInScripts";

    public override string Name => CommandName;

    public override string Text => Loc.T("Find.Title");

    public override string ToolTip => Loc.T("Find.Hint");

    // Inside an open script the editor's own Ctrl+F comes first.
    [Export]
    public static CommandKeyboardShortcut KeyGesture =
        new CommandKeyboardShortcut<FindInScriptsCommandDefinition>(new KeyGesture(Key.F, ModifierKeys.Control));
}

[CommandHandler]
public class FindInScriptsCommandHandler : CommandHandlerBase<FindInScriptsCommandDefinition>
{
    public override Task Run(Command command)
    {
        FindInScriptsWindow.Open();
        return Task.CompletedTask;
    }
}

internal static class MenuDefinitions
{
    [Export]
    public static readonly MenuItemGroupDefinition ToolsWindowsGroup = new(
        Gemini.Modules.MainMenu.MenuDefinitions.ToolsMenu, 0);

    [Export]
    public static readonly MenuItemDefinition PresetsMenuItem =
        new CommandMenuItemDefinition<OpenPresetsCommandDefinition>(ToolsWindowsGroup, -1);

    [Export]
    public static readonly MenuItemDefinition FindInScriptsMenuItem =
        new CommandMenuItemDefinition<FindInScriptsCommandDefinition>(ToolsWindowsGroup, -2);

    [Export]
    public static readonly MenuItemDefinition P3dToolsMenuItem =
        new CommandMenuItemDefinition<OpenP3dToolsCommandDefinition>(ToolsWindowsGroup, 0);

    [Export]
    public static readonly MenuItemDefinition PbrMakerMenuItem =
        new CommandMenuItemDefinition<OpenPbrMakerCommandDefinition>(ToolsWindowsGroup, 1);

    [Export]
    public static readonly MenuItemDefinition ConvertFilesMenuItem =
        new CommandMenuItemDefinition<BulkExport.Commands.ConvertFilesCommandDefinition>(ToolsWindowsGroup, 2);

    [Export]
    public static readonly MenuItemDefinition ConvertFolderMenuItem =
        new CommandMenuItemDefinition<BulkExport.Commands.ConvertFolderCommandDefinition>(ToolsWindowsGroup, 3);

    [Export]
    public static readonly MenuItemDefinition ConvertSelectedMenuItem =
        new CommandMenuItemDefinition<ConvertSelectedCommandDefinition>(ToolsWindowsGroup, 4);

    [Export]
    public static readonly MenuItemDefinition RecoverNamesMenuItem =
        new CommandMenuItemDefinition<RecoverNamesCommandDefinition>(ToolsWindowsGroup, 5);
}

internal static class ToolBarDefinitions
{
    [Export]
    public static readonly ToolBarItemGroupDefinition ToolWindowsToolBarGroup =
        new(FileManager.ToolBarDefinitions.PboManagerToolBar, 5);

    [Export]
    public static ToolBarItemDefinition PresetsToolBarItem =
        new CommandToolBarItemDefinition<OpenPresetsCommandDefinition>(ToolWindowsToolBarGroup, -1);

    [Export]
    public static ToolBarItemDefinition P3dToolsToolBarItem =
        new CommandToolBarItemDefinition<OpenP3dToolsCommandDefinition>(ToolWindowsToolBarGroup, 0);

    [Export]
    public static ToolBarItemDefinition PbrMakerToolBarItem =
        new CommandToolBarItemDefinition<OpenPbrMakerCommandDefinition>(ToolWindowsToolBarGroup, 1);
}
