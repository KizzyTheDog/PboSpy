using PboSpy.Modules.BulkExport.Commands;

namespace PboSpy.Modules.BulkExport;

internal static class ToolBarDefinitions
{
    [Export]
    public static readonly ToolBarItemGroupDefinition BulkExportToolBarGroup =
        new(Extraction.ToolBarDefinitions.ExtractionToolBar, 3);

    [Export]
    public static ToolBarItemDefinition BulkExportToolBarItem =
        new CommandToolBarItemDefinition<BulkExportCommandDefinition>(BulkExportToolBarGroup, 0);
}
