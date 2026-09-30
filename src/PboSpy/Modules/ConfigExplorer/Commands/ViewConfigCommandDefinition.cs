using PboSpy.Localization;
namespace PboSpy.Modules.ConfigExplorer.Commands;

[CommandDefinition]
public class ViewConfigCommandDefinition : CommandDefinition
{
    public const string CommandName = "View.ConfigExplorer";

    public override string Name => CommandName;

    public override string Text => Loc.T("Cmd.ConfigExplorer");

    public override string ToolTip => Loc.T("Cmd.ConfigExplorerTip");

}
