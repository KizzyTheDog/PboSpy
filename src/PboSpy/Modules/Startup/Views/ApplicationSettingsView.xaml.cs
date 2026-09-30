using PboSpy.Modules.Startup.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace PboSpy.Modules.Startup.Views;

public partial class ApplicationSettingsView : UserControl
{
    private Window _window;

    public ApplicationSettingsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    // The page can unload while the dialog stays open (switching pages), so watch the window itself.
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window == null || window == _window)
        {
            return;
        }
        _window = window;
        window.Closed += (_, _) => (DataContext as ApplicationSettingsViewModel)?.OnDialogClosed();
    }
}
