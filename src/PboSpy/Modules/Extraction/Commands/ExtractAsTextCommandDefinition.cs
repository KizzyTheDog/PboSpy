using PboSpy.Localization;
namespace PboSpy.Modules.Extraction.Commands;

[CommandDefinition]
public class ExtractAsTextCommandDefinition : CommandDefinition
{
    public const string CommandName = "Edit.ExtractAsText";

    public override string Name => CommandName;

    public override string Text => Loc.T("Cmd.ExtractAsText");

    public override string ToolTip => Loc.T("Cmd.ExtractAsTextTip");

    public override Uri IconSource
        => new("pack://application:,,,/PboSpy;component/Resources/Icons/ExtractText.png");
}
