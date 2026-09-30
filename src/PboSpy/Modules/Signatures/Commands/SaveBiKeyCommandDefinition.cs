using PboSpy.Localization;
namespace PboSpy.Modules.Signatures.Commands;

[CommandDefinition]
public class SaveBiKeyCommandDefinition : CommandDefinition
{
    public const string CommandName = "Edit.SaveBiKey";

    public override string Name => CommandName;

    public override string Text => Loc.T("Cmd.SaveBiKey");

    public override string ToolTip => Loc.T("Cmd.SaveBiKeyTip");

    public override Uri IconSource
        => new("pack://application:,,,/PboSpy;component/Resources/Icons/SaveKey.png");
}