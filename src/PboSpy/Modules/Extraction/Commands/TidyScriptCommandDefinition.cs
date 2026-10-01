using PboSpy.Localization;
using System.Windows.Input;

namespace PboSpy.Modules.Extraction.Commands;

[CommandDefinition]
public class TidyScriptCommandDefinition : CommandDefinition
{
    public const string CommandName = "Edit.TidyScript";

    public override string Name => CommandName;

    public override string Text => Loc.T("Cmd.TidyScript");

    public override string ToolTip => Loc.T("Cmd.TidyScriptTip");

    [Export]
    public static CommandKeyboardShortcut KeyGesture =
        new CommandKeyboardShortcut<TidyScriptCommandDefinition>(new KeyGesture(Key.T, ModifierKeys.Control | ModifierKeys.Shift));
}
