using Gemini.Framework.Results;
using PboSpy.Localization;

namespace PboSpy.Modules.Start;

[CommandDefinition]
public class ViewStartPageCommandDefinition : CommandDefinition
{
    public const string CommandName = "View.StartPage";

    public override string Name => CommandName;

    public override string Text => Loc.T("Cmd.StartPage");

    public override string ToolTip => Loc.T("Cmd.StartPageTip");

    public override Uri IconSource => new("pack://application:,,,/PboSpy;component/Resources/Icons/WindowIcon.png");
}

[CommandHandler]
public class ViewStartPageCommandHandler : CommandHandlerBase<ViewStartPageCommandDefinition>
{
    public override async Task Run(Command command)
    {
        await Show.Document<IStartPage>().ExecuteAsync();
    }
}

internal static class MenuDefinitions
{
    [Export]
    public static readonly MenuItemDefinition ViewStartPageMenuItem = new CommandMenuItemDefinition<ViewStartPageCommandDefinition>(
        About.MenuDefinitions.ViewMenuGroup, 1);
}
