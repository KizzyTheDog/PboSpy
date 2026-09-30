using PboSpy.Localization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PboSpy.Modules.Windows;

/// <summary>Small themed message box that opens centred on its owner.</summary>
public sealed class MessageDialog : ToolWindow
{
    private readonly CheckBox _check;
    private readonly TextBox _input;

    private MessageDialog(Window owner, string title, string message, string primary, string secondary, string checkText, string input = null)
    {
        Title = title;
        Owner = owner;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        MinWidth = 360;
        MaxWidth = 560;

        var root = new StackPanel { Margin = new Thickness(22, 20, 22, 18) };
        root.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)FindResource("PboSpy.Section"),
            FontSize = 15
        });
        root.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 16),
            Foreground = (System.Windows.Media.Brush)FindResource("PboSpy.TextDim")
        });

        if (input != null)
        {
            _input = new TextBox { Text = input, Margin = new Thickness(0, 0, 0, 16), MinWidth = 380 };
            _input.Style = (Style)FindResource("PboSpy.TextBox");
            root.Children.Add(_input);
            Loaded += (_, _) =>
            {
                _input.Focus();
                var stem = Path.GetFileNameWithoutExtension(input);
                _input.Select(0, Directory.Exists(input) || stem.Length == 0 ? input.Length : input.LastIndexOf(stem, StringComparison.Ordinal) + stem.Length);
            };
        }

        if (checkText != null)
        {
            _check = new CheckBox { Content = checkText, Margin = new Thickness(0, 0, 0, 16) };
            _check.SetResourceReference(ForegroundProperty, "PboSpy.Text");
            root.Children.Add(_check);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (secondary != null)
        {
            var no = new Button { Content = secondary, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
            no.Style = (Style)FindResource("PboSpy.Button");
            no.Click += (_, _) => { DialogResult = false; };
            buttons.Children.Add(no);
        }
        var yes = new Button { Content = primary, MinWidth = 90, IsDefault = true };
        yes.Style = (Style)FindResource("PboSpy.AccentButton");
        yes.Click += (_, _) => { DialogResult = true; };
        buttons.Children.Add(yes);
        root.Children.Add(buttons);

        Content = root;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
            }
        };
    }

    protected override bool RememberPlacement => false;

    public bool IsChecked => _check?.IsChecked == true;

    /// <summary>Yes/No question. Returns true for yes.</summary>
    public static bool Ask(Window owner, string title, string message, string checkText, out bool isChecked)
    {
        var dialog = new MessageDialog(owner ?? Application.Current?.MainWindow, title, message, Loc.T("Common.Yes"), Loc.T("Common.No"), checkText);
        var result = dialog.ShowDialog() == true;
        isChecked = dialog.IsChecked;
        return result;
    }

    public static bool Ask(Window owner, string title, string message) => Ask(owner, title, message, null, out _);

    /// <summary>Asks for a line of text; null when cancelled or unchanged.</summary>
    public static string Prompt(Window owner, string title, string message, string initial)
    {
        var dialog = new MessageDialog(owner ?? Application.Current?.MainWindow, title, message, Loc.T("Common.OK"), Loc.T("Common.Cancel"), null, initial ?? "");
        if (dialog.ShowDialog() != true)
        {
            return null;
        }
        var text = dialog._input.Text.Trim();
        return text.Length == 0 || text == initial ? null : text;
    }

    public static void Info(Window owner, string title, string message)
    {
        new MessageDialog(owner ?? Application.Current?.MainWindow, title, message, Loc.T("Common.OK"), null, null).ShowDialog();
    }
}
