using PboSpy.Localization;
namespace PboSpy.Modules.Extraction.Commands;

[CommandDefinition]
public class ExtractAsPngCommandDefinition : CommandDefinition
{
    public const string CommandName = "Edit.ExtractAsPng";

    public override string Name => CommandName;

    public override string Text => Loc.T("Cmd.ExtractAsPng");

    public override string ToolTip => Loc.T("Cmd.ExtractAsPngTip");

    public override Uri IconSource
        => new("pack://application:,,,/PboSpy;component/Resources/Icons/ExtractImage.png");
}