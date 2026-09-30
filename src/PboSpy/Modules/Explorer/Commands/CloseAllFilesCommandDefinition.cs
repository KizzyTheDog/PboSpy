using PboSpy.Localization;
namespace PboSpy.Modules.Explorer.Commands;

[CommandDefinition]
internal class CloseAllFilesCommandDefinition : CommandDefinition
{
    public override string Name => "File.CloseAllFiles";

    public override string Text => Loc.T("Cmd.CloseAll");

    public override string ToolTip => Loc.T("Cmd.CloseAllTip");

    public override Uri IconSource
       => new("pack://application:,,,/PboSpy;component/Resources/Icons/CloseAll.png");
}
