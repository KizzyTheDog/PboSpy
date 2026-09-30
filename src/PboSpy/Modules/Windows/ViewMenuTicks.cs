using Gemini.Modules.MainMenu.Models;
using PboSpy.Modules.About;
using PboSpy.Modules.ConfigExplorer;
using PboSpy.Modules.Explorer;
using PboSpy.Modules.P3dTools.Views;
using PboSpy.Modules.Pbr.Views;
using PboSpy.Modules.Start;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PboSpy.Modules.Windows;

/// <summary>
/// Puts a tick at the right-hand end of menu entries whose panel or window is currently open
/// (PBO Explorer, Config Explorer, Output, P3D Tools...). Works on any menu, so the Tools menu gets it too.
/// </summary>
public static class ViewMenuTicks
{
    private const string Tick = "✓";
    private static readonly Dictionary<Type, Func<bool>> States = new();
    private static IShell _shell;

    public static void Initialize(IShell shell)
    {
        if (_shell != null)
        {
            return;
        }
        _shell = shell;
        Register<Explorer.Commands.ViewExplorerCommandDefinition>(IsToolOpen<IPboExplorer>);
        Register<ConfigExplorer.Commands.ViewConfigCommandDefinition>(IsToolOpen<IConfigExplorer>);
        Register<Gemini.Modules.Output.Commands.ViewOutputCommandDefinition>(IsToolOpen<Gemini.Modules.Output.IOutput>);
        Register<Gemini.Modules.PropertyGrid.Commands.ViewPropertyGridCommandDefinition>(IsToolOpen<Gemini.Modules.PropertyGrid.IPropertyGrid>);
        Register<About.Commands.ViewAboutCommandDefinition>(() => _shell.Documents.OfType<IAboutInformation>().Any());
        Register<ViewStartPageCommandDefinition>(() => _shell.Documents.OfType<IStartPage>().Any());
        Register<OpenP3dToolsCommandDefinition>(ToolWindow.IsOpen<P3dToolsWindow>);
        Register<OpenPbrMakerCommandDefinition>(ToolWindow.IsOpen<PbrMakerWindow>);

        EventManager.RegisterClassHandler(typeof(MenuItem), MenuItem.SubmenuOpenedEvent, new RoutedEventHandler(OnSubmenuOpened));
    }

    public static void Register<T>(Func<bool> isOpen) where T : CommandDefinitionBase => States[typeof(T)] = isOpen;

    private static bool IsToolOpen<T>() where T : ITool => _shell.Tools.OfType<T>().Any(t => t.IsVisible);

    private static void OnSubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem parent || !ReferenceEquals(e.OriginalSource, parent))
        {
            return;
        }
        // Containers only exist once the popup has laid out.
        parent.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => Update(parent));
    }

    private static void Update(MenuItem parent)
    {
        foreach (var item in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(item) is not MenuItem container ||
                container.DataContext is not ICommandUiItem ui ||
                ui.CommandDefinition == null ||
                !States.TryGetValue(ui.CommandDefinition.GetType(), out var isOpen))
            {
                continue;
            }
            var gesture = (container.DataContext as StandardMenuItem)?.InputGestureText ?? "";
            bool open;
            try
            {
                open = isOpen();
            }
            catch (Exception)
            {
                open = false;
            }
            container.InputGestureText = open
                ? (string.IsNullOrEmpty(gesture) ? Tick : gesture + "   " + Tick)
                : gesture;
        }
    }
}
