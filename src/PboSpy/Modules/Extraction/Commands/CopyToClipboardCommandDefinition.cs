using PboSpy.Localization;
using System.Windows.Input;

namespace PboSpy.Modules.Extraction.Commands;

[CommandDefinition]
public class CopyToClipboardCommandDefinition : CommandDefinition
{

    public const string CommandName = "Edit.Copy";

    public override string Name => CommandName;

    public override string Text => Loc.T("Cmd.CopyToClipboard");

    public override string ToolTip => Loc.T("Cmd.CopyToClipboardTip");

    public override Uri IconSource
        => new("pack://application:,,,/PboSpy;component/Resources/Icons/Copy.png");

    [Export]
    public static CommandKeyboardShortcut KeyGesture =
        new CommandKeyboardShortcut<CopyToClipboardCommandDefinition>(new KeyGesture(Key.C, ModifierKeys.Control));
}
