using PboSpy.Modules.BulkExport.Commands;

namespace PboSpy.Modules.BulkExport;

internal static class MenuDefinitions
{
    [Export]
    public static readonly MenuItemGroupDefinition BulkExportMenuGroup = new(
        Gemini.Modules.MainMenu.MenuDefinitions.EditMenu, 1);

    [Export]
    public static readonly MenuItemDefinition BulkExportMenuItem =
        new CommandMenuItemDefinition<BulkExportCommandDefinition>(BulkExportMenuGroup, 0);

    [Export]
    public static readonly MenuItemGroupDefinition ConvertMenuGroup = new(
        Gemini.Modules.MainMenu.MenuDefinitions.FileMenu, 1);

    [Export]
    public static readonly MenuItemDefinition ConvertFilesMenuItem =
        new CommandMenuItemDefinition<ConvertFilesCommandDefinition>(ConvertMenuGroup, 0);

    [Export]
    public static readonly MenuItemDefinition ConvertFolderMenuItem =
        new CommandMenuItemDefinition<ConvertFolderCommandDefinition>(ConvertMenuGroup, 1);
}
