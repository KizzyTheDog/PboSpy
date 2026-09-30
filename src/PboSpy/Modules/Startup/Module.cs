using Gemini.Framework.Themes;
using Gemini.Modules.MainMenu;
using Gemini.Modules.ToolBars;
using PboSpy.Localization;
using PboSpy.Modules.About;
using PboSpy.Modules.ConfigExplorer;
using PboSpy.Modules.Explorer;
using PboSpy.Modules.FileManager;
using PboSpy.Properties;
using PboSpy.Modules.Start;
using PboSpy.Modules.Windows;
using PboSpy.Services;
using PboSpy.Themes;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace PboSpy.Modules.Startup;

[Export(typeof(IModule))]
public class Module : ModuleBase
{
    private readonly IMainWindow _mainWindow;
    private readonly IFileManager _pboManager;
    private readonly IThemeManager _themeManager;
    private readonly IMenu _menu;
    private readonly IToolBars _toolBars;
    private readonly ICommandService _commandService;

    public override IEnumerable<IDocument> DefaultDocuments => Enumerable.Empty<IDocument>();

    public override IEnumerable<Type> DefaultTools
    {
        get
        {
            yield return typeof(IPboExplorer);
            yield return typeof(IConfigExplorer);
        }
    }

    [ImportingConstructor]
    public Module(IMainWindow mainWindow, IFileManager pboManager, IThemeManager themeManager, IMenu menu,
        IToolBars toolBars, ICommandService commandService)
    {
        _mainWindow = mainWindow;
        _pboManager = pboManager;
        _themeManager = themeManager;
        _menu = menu;
        _toolBars = toolBars;
        _commandService = commandService;
    }

    public override void Initialize()
    {
        _mainWindow.WindowState = AppSettings.Default.OpenMaximized ? WindowState.Maximized : WindowState.Normal;
        _mainWindow.Title = "PboSpy";
        _mainWindow.Icon = new BitmapImage(new("pack://application:,,,/PboSpy;component/Resources/Icons/WindowIcon.png"));

        _mainWindow.Shell.ToolBars.Visible = true;

        // Runs before Gemini applies the saved theme, so the replacement themes are in place.
        ThemeService.Initialize(_themeManager);
    }

    public override async Task PostInitializeAsync()
    {
        await LoadFromArguments();
        PboSpy.Services.Updater.CheckOnStartup();

        if (AppSettings.Default.ShowStartPage && !Shell.Documents.OfType<IStartPage>().Any())
        {
            await Shell.OpenDocumentAsync(IoC.Get<IStartPage>());
        }

        var explorer = IoC.Get<IPboExplorer>();
        if (!Shell.Tools.Contains(explorer) || !explorer.IsVisible)
        {
            Shell.ShowTool(explorer);
        }
        Shell.ActiveLayoutItem = explorer;

        LocalizationService.Initialize(_menu, _toolBars, Shell, _commandService);
        ViewMenuTicks.Initialize(Shell);

        if (Application.Current.MainWindow is Window main)
        {
            main.Closed += (_, _) => ToolWindow.CloseAll();
        }

        // Start menu shortcuts can open a tool window straight away.
        var args = Environment.GetCommandLineArgs();
        if (args.Contains("--p3d", StringComparer.OrdinalIgnoreCase))
        {
            P3dTools.Views.P3dToolsWindow.Open();
        }
        if (args.Contains("--pbr", StringComparer.OrdinalIgnoreCase))
        {
            Pbr.Views.PbrMakerWindow.Open();
        }
        SingleInstance.Listen(paths => _ = AppOpen.Show(paths));
        if (args.Contains("--names", StringComparer.OrdinalIgnoreCase))
        {
            foreach (var pbo in _pboManager.FileTree.OfType<Pbo.Models.PboFile>())
            {
                Deobfuscate.Views.NameRecoveryWindow.Open(pbo);
            }
        }
    }

    private async Task LoadFromArguments()
    {
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1)
        {
            var paths = args
                .Skip(1)
                .Where(str => File.Exists(str) || Directory.Exists(str))
                .ToList();

            if (paths.Any())
            {
                await AppOpen.Show(paths);
            }
        }
    }
}
