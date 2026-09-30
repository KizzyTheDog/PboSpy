using PboSpy.Localization;
namespace PboSpy.Modules.Explorer.Commands;

[CommandDefinition]
public class ViewExplorerCommandDefinition : CommandDefinition
{
    public const string CommandName = "View.PboExplorer";

    public override string Name => CommandName;

    public override string Text => Loc.T("Cmd.PboExplorer");

    public override string ToolTip => Loc.T("Cmd.PboExplorerTip");

}
