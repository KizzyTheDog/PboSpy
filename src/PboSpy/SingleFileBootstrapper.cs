using Microsoft.Extensions.Logging;
using Serilog;
using System.ComponentModel.Composition.Hosting;
using System.ComponentModel.Composition.ReflectionModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;

namespace PboSpy;

public class SingleFileBootstrapper : Gemini.AppBootstrapper
{
    private ILoggerFactory _loggerfactory;
    private Microsoft.Extensions.Logging.ILogger _logger;

    public override bool IsPublishSingleFileHandled => true;

    protected override IEnumerable<Assembly> PublishSingleFileBypassAssemblies
    {
        get
        {
            yield return Assembly.GetAssembly(typeof(Gemini.AppBootstrapper));
            yield return Assembly.GetAssembly(typeof(Gemini.Modules.CodeEditor.ILanguageDefinition));
            //yield return Assembly.GetAssembly(typeof(Gemini.Modules.Inspector.IInspectorTool));
            yield return Assembly.GetAssembly(typeof(Gemini.Modules.Output.IOutput));
            yield return Assembly.GetAssembly(typeof(Gemini.Modules.PropertyGrid.IPropertyGrid));
        }
    }

    protected override void PreInitialize()
    {
        base.PreInitialize();

        var language = PboSpy.Services.AppSettings.Default.Language;
        if (string.IsNullOrEmpty(language))
        {
            language = PboSpy.Services.GeminiSettings.LanguageCode;
        }
        PboSpy.Localization.Loc.Instance.SetLanguage(language);

        var logFolder = Path.Combine(PboSpy.Services.AppSettings.Folder, "logs");
        Directory.CreateDirectory(logFolder);

        _loggerfactory = LoggerFactory.Create(builder =>
        {
            var configuration = new LoggerConfiguration()
                .WriteTo.File(Path.Combine(logFolder, "PboSpy_Log_.txt"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14);

            builder.AddSerilog(configuration.CreateLogger());
        });

        _logger = _loggerfactory.CreateLogger("Application");
        if (Environment.GetEnvironmentVariable("PBOSPY_TRACE") == "1")
        {
            AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
                _logger.LogWarning("First chance {Type}: {Message}\n{Stack}", e.Exception.GetType().Name, e.Exception.Message, e.Exception.StackTrace);
        }
        TaskScheduler.UnobservedTaskException += (_, e) => _logger.LogError(e.Exception, "Unobserved task exception");
    }

    protected override IEnumerable<object> GetAllInstances(Type serviceType)
    {
        var instances = base.GetAllInstances(serviceType);
        if (serviceType == typeof(Gemini.Modules.Settings.ISettingsEditor))
        {
            // Gemini's "General" page writes its own theme and language on OK, undoing ours.
            instances = instances.Where(i => i.GetType().FullName != "Gemini.Modules.MainMenu.ViewModels.MainMenuSettingsViewModel");
        }
        return instances;
    }

    protected override void BindServices(CompositionBatch batch)
    {
        batch.AddExportedValue<ILoggerFactory>(_loggerfactory);

        base.BindServices(batch);
    }

    protected override void OnExit(object sender, EventArgs e)
    {
        _logger.LogInformation("Exiting");

        base.OnExit(sender, e);
    }

    protected override void OnStartup(object sender, StartupEventArgs e)
    {
        _logger.LogInformation("Application started");
        base.OnStartup(sender, e);
    }

    protected override void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger.LogCritical(e.Exception, "Unhandled exception");

        base.OnUnhandledException(sender, e);
    }

    protected override void PopulateAssemblySource()
    {
        string executableDirectoryPath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

        var geminiCatalog = new DirectoryCatalog(executableDirectoryPath, "Gemini*.dll");
        AssemblySource.Instance.AddRange(
            geminiCatalog.Parts
                .Select(part => ReflectionModelServices.GetPartType(part).Value.Assembly)
                .Where(assembly => !AssemblySource.Instance.Contains(assembly)));
    }
}
