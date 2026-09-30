using Microsoft.WindowsAPICodePack.Dialogs;
using PboSpy.Localization;
using PboSpy.Models;
using PboSpy.Modules.BulkExport.Services;
using PboSpy.Modules.BulkExport.Views;
using System.IO;
using System.Windows;

namespace PboSpy.Modules.BulkExport.Commands;

[CommandHandler]
public class ConvertFilesCommandHandler : CommandHandlerBase<ConvertFilesCommandDefinition>
{
    public override Task Run(Command command)
    {
        var dialog = new CommonOpenFileDialog
        {
            Title = Loc.T("Command.ConvertFiles"),
            Multiselect = true,
            EnsureFileExists = true
        };
        dialog.Filters.Add(new CommonFileDialogFilter(Loc.T("Convert.FilterSupported"),
            "*.paa;*.pac;*.png;*.tga;*.jpg;*.jpeg;*.bmp;*.tif;*.wss;*.ogg;*.wav;*.mp3;*.flac;*.bin;*.rvmat;*.sqm"));
        dialog.Filters.Add(new CommonFileDialogFilter(Loc.T("Convert.FilterAll"), "*.*"));

        if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
        {
            ConvertRunner.Run(dialog.FileNames.Select(f => (FileBase)new PhysicalFile(f)).ToList(), null, "Convert.Title");
        }
        return Task.CompletedTask;
    }
}

[CommandHandler]
public class ConvertFolderCommandHandler : CommandHandlerBase<ConvertFolderCommandDefinition>
{
    public override Task Run(Command command)
    {
        var dialog = new CommonOpenFileDialog
        {
            Title = Loc.T("Command.ConvertFolder"),
            IsFolderPicker = true
        };

        if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
        {
            var folder = dialog.FileName;
            var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Select(f => (FileBase)new PhysicalFile(f))
                .ToList();
            ConvertRunner.Run(files, Path.GetDirectoryName(folder.TrimEnd('\\')), "Convert.FolderTitle", folder);
        }
        return Task.CompletedTask;
    }
}

internal static class ConvertRunner
{
    public static void Run(IReadOnlyList<FileBase> files, string rootToStrip, string titleKey = "Convert.Title", string label = null,
        System.Action<Models.ExportOptions> setup = null)
    {
        if (files.Count == 0)
        {
            MessageBox.Show(Loc.T("BulkExport.NothingSelected"), Loc.T("BulkExport.Title"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var options = ExportOptionsStore.Load();
        setup?.Invoke(options);
        var window = new BulkExportDialog(files, options, rootToStrip, label)
        {
            Title = Loc.T(titleKey),
            PickOptionsFromFiles = setup == null
        };
        window.Closed += (_, _) =>
        {
            options.RootPathToStrip = null;
            ExportOptionsStore.Save(options);
        };
        window.Show();
    }
}
