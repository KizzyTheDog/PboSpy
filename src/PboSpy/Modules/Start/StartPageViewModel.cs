using PboSpy.Localization;
using PboSpy.Modules.FileManager;
using PboSpy.Modules.Windows;
using PboSpy.Services;
using System.IO;
using System.Reflection;

namespace PboSpy.Modules.Start;

public interface IStartPage : IDocument
{
}

public sealed class RecentEntry
{
    public string Path { get; init; }
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\'));
    public string Folder => System.IO.Path.GetDirectoryName(Path.TrimEnd('\\'));
}

[Export(typeof(IStartPage))]
[PartCreationPolicy(CreationPolicy.Shared)]
public class StartPageViewModel : Document, IStartPage
{
    private readonly IFileManager _fileManager;
    private readonly AppSettings _settings = AppSettings.Default;

    [ImportingConstructor]
    public StartPageViewModel(IFileManager fileManager)
    {
        _fileManager = fileManager;
        DisplayName = Loc.T("Start.Title");
        Loc.Instance.LanguageChanged += (_, _) => DisplayName = Loc.T("Start.Title");
        _settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.RecentFiles))
            {
                NotifyOfPropertyChange(nameof(Recent));
                NotifyOfPropertyChange(nameof(HasRecent));
            }
            else if (e.PropertyName == nameof(AppSettings.ShowStartPage))
            {
                NotifyOfPropertyChange(nameof(ShowOnStartup));
            }
        };
    }

    public override bool ShouldReopenOnStart => false;

    public string Version => "v" + (Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "2");

    public IReadOnlyList<RecentEntry> Recent => _settings.RecentFiles
        .Where(p => File.Exists(p) || Directory.Exists(p))
        .Select(p => new RecentEntry { Path = p })
        .ToList();

    public bool HasRecent => Recent.Count > 0;

    public bool ShowOnStartup
    {
        get => _settings.ShowStartPage;
        set
        {
            _settings.ShowStartPage = value;
            _settings.Save();
        }
    }

    public void OpenFiles() => Run<FileManager.Commands.OpenFilesCommandDefinition>();

    public void OpenFolder() => Run<FileManager.Commands.OpenFolderCommandDefinition>();

    public void ConvertFiles() => Run<BulkExport.Commands.ConvertFilesCommandDefinition>();

    public void ConvertFolder() => Run<BulkExport.Commands.ConvertFolderCommandDefinition>();

    public void OpenP3dTools() => Run<OpenP3dToolsCommandDefinition>();

    public void OpenPbrMaker() => Run<OpenPbrMakerCommandDefinition>();

    public void OpenSettings() => Run<Gemini.Modules.Settings.Commands.OpenSettingsCommandDefinition>();

    public void OpenAbout() => Run<About.Commands.ViewAboutCommandDefinition>();

    public Task OpenRecent(RecentEntry entry) =>
        entry != null ? _fileManager.LoadSupportedFiles(new[] { entry.Path }) : Task.CompletedTask;

    public void ClearRecent()
    {
        _settings.RecentFiles = new List<string>();
        _settings.Save();
    }

    private static void Run<T>() where T : CommandDefinitionBase
    {
        var service = IoC.Get<ICommandService>();
        var definition = service.GetCommandDefinition(typeof(T));
        var command = service.GetTargetableCommand(service.GetCommand(definition));
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    /// <summary>Closing the start page asks once whether it should keep showing up.</summary>
    public override async Task<bool> CanCloseAsync(CancellationToken cancellationToken)
    {
        if (_settings.AskAboutStartPage && _settings.ShowStartPage)
        {
            var keep = MessageDialog.Ask(null, Loc.T("Start.AskTitle"), Loc.T("Start.AskMessage"), Loc.T("Start.AskDontAsk"), out var dontAsk);
            _settings.ShowStartPage = keep;
            if (dontAsk)
            {
                _settings.AskAboutStartPage = false;
            }
            _settings.Save();
        }
        return await base.CanCloseAsync(cancellationToken);
    }
}
