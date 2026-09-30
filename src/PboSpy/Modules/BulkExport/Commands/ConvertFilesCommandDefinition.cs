using PboSpy.Localization;

namespace PboSpy.Modules.BulkExport.Commands;

[CommandDefinition]
public class ConvertFilesCommandDefinition : CommandDefinition
{
    public const string CommandName = "File.ConvertFiles";

    public override string Name => CommandName;

    public override string Text => Loc.T("Command.ConvertFiles");

    public override string ToolTip => Loc.T("Command.ConvertFilesTip");

    public override Uri IconSource
        => new("pack://application:,,,/PboSpy;component/Resources/Icons/ExtractImage.png");
}

[CommandDefinition]
public class ConvertFolderCommandDefinition : CommandDefinition
{
    public const string CommandName = "File.ConvertFolder";

    public override string Name => CommandName;

    public override string Text => Loc.T("Command.ConvertFolder");

    public override string ToolTip => Loc.T("Command.ConvertFolderTip");

    public override Uri IconSource
        => new("pack://application:,,,/PboSpy;component/Resources/Icons/OpenFolder.png");
}
