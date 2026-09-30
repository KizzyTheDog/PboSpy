using PboSpy.Localization;
namespace PboSpy.Modules.Signatures.Commands;

[CommandDefinition]
public class ExtractBiKeyCommandDefinition : CommandDefinition
{
    public const string CommandName = "Edit.ExtractBiKey";

    public override string Name => CommandName;

    public override string Text => Loc.T("Cmd.ExtractBiKey");

    public override string ToolTip => Loc.T("Cmd.ExtractBiKeyTip");

    public override Uri IconSource
        => new("pack://application:,,,/PboSpy;component/Resources/Icons/ExtractKey.png");
}
