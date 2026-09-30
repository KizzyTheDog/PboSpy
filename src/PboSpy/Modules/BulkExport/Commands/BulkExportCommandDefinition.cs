using PboSpy.Localization;
using System.Windows.Input;

namespace PboSpy.Modules.BulkExport.Commands;

[CommandDefinition]
public class BulkExportCommandDefinition : CommandDefinition
{
    public const string CommandName = "Edit.BulkExport";

    public override string Name => CommandName;

    public override string Text => Loc.T("Command.BulkExport");

    public override string ToolTip => Loc.T("Command.BulkExportTip");

    public override Uri IconSource
        => new("pack://application:,,,/PboSpy;component/Resources/Icons/ExtractPBO.png");

    [Export]
    public static CommandKeyboardShortcut KeyGesture =
        new CommandKeyboardShortcut<BulkExportCommandDefinition>(new KeyGesture(Key.E, ModifierKeys.Control | ModifierKeys.Shift));
}
