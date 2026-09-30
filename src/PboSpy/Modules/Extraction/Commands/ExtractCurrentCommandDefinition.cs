using PboSpy.Localization;
using System.Windows.Input;

namespace PboSpy.Modules.Extraction.Commands;

[CommandDefinition]
public class ExtractCurrentCommandDefinition : CommandDefinition
{
    public const string CommandName = "Edit.Extract";

    public override string Name => CommandName;

    public override string Text => Loc.T("Cmd.Extract");

    public override string ToolTip => Loc.T("Cmd.ExtractTip");

    public override Uri IconSource
        => new("pack://application:,,,/PboSpy;component/Resources/Icons/ExtractFile.png");

    [Export]
    public static CommandKeyboardShortcut KeyGesture =
        new CommandKeyboardShortcut<ExtractCurrentCommandDefinition>(new KeyGesture(Key.E, ModifierKeys.Control));
}
