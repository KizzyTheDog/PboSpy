using PboSpy.Localization;
using Gemini.Modules.CodeEditor;
using Gemini.Modules.CodeEditor.Views;
using Microsoft.Win32;
using PboSpy.Models;
using PboSpy.Modules.Extraction.Commands;
using System.IO;
using System.Windows;

namespace PboSpy.Modules.Preview.ViewModels;

public class TextPreviewViewModel : PreviewViewModel, ICommandHandler<ExtractAsTextCommandDefinition>, ICommandHandler<TidyScriptCommandDefinition>
{

    private readonly LanguageDefinitionManager _languageDefinitionManager;
    private ICodeEditorView _view;

    public string Text { get; }

    public TextPreviewViewModel(FileBase model, string text) : base(model)
    {
        Text = text;

        // TODO: Find better way to load LanguageDefinitionManager
        _languageDefinitionManager = IoC.Get<LanguageDefinitionManager>();
    }

    protected override void CanExecuteCopy(Command command)
    {
        command.Enabled = true;
    }

    protected override Task ExecuteCopy(Command command)
    {
        Clipboard.SetText(Text);
        return Task.CompletedTask;
    }

    protected override void OnViewLoaded(object view)
    {
        _view = (ICodeEditorView)view;
        LoadText();
    }

    private void LoadText()
    {
        if (_view == null)
        {
            return;
        }

        _view.TextEditor.Text = Text;
        _view.TextEditor.IsReadOnly = true;

        var extension = _model.Extension.ToLower();
        ArmaHighlighting.Attach(_view.TextEditor, extension,
            () => _languageDefinitionManager.GetDefinitionByExtension(extension == ".bin" ? ".cpp" : extension)?.SyntaxHighlighting);
        ApplyGoTo();
    }

    private (int Line, string Term)? _goTo;

    /// <summary>Selects the search term on that line (or the whole line) and scrolls to it.</summary>
    public void GoTo(int line, string term)
    {
        _goTo = (line, term);
        ApplyGoTo();
    }

    private void ApplyGoTo()
    {
        if (_view == null || _goTo == null)
        {
            return;
        }
        var (number, term) = _goTo.Value;
        _goTo = null;
        var editor = _view.TextEditor;
        // After layout, or the scroll lands short on a freshly opened tab.
        editor.Dispatcher.BeginInvoke(() =>
        {
            var line = editor.Document.GetLineByNumber(Math.Clamp(number, 1, editor.Document.LineCount));
            var at = editor.Document.GetText(line).IndexOf(term ?? "", StringComparison.OrdinalIgnoreCase);
            editor.Select(at >= 0 ? line.Offset + at : line.Offset, at >= 0 ? term.Length : line.Length);
            editor.TextArea.Caret.Offset = line.Offset;
            editor.ScrollToLine(line.LineNumber);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static readonly HashSet<string> Scripts = new(StringComparer.OrdinalIgnoreCase) { ".sqf", ".sqs", ".fsm", ".hpp", ".h", ".cpp", ".inc", ".ext", ".bin" };

    void ICommandHandler<TidyScriptCommandDefinition>.Update(Command command)
    {
        command.Enabled = _view != null && Scripts.Contains(_model.Extension);
    }

    // Only changes what is shown (and what Extract as text saves), never the file in the PBO.
    Task ICommandHandler<TidyScriptCommandDefinition>.Run(Command command)
    {
        _view.TextEditor.Text = Utils.SqfTidy.Tidy(_view.TextEditor.Text);
        return Task.CompletedTask;
    }

    void ICommandHandler<ExtractAsTextCommandDefinition>.Update(Command command)
    {
        command.Enabled = true;
    }

    Task ICommandHandler<ExtractAsTextCommandDefinition>.Run(Command command)
    {
        var dlg = new SaveFileDialog
        {
            Title = Loc.T("Dialog.ExtractText"),
            FileName = _model.Extension == ".bin" ? Path.ChangeExtension(_model.Name, ".cpp") : _model.Name,
            DefaultExt = ".txt",
            Filter = "Text file|*.txt|CPP|*.cpp|HPP|*.hpp|SQM|*.sqm|SQF|*.sqf|RVMAT|*.rvmat"
        };

        if (dlg.ShowDialog() == true)
        {
            return File.WriteAllTextAsync(dlg.FileName, _view?.TextEditor.Text ?? Text);
        }

        return Task.CompletedTask;
    }

}
