using Gemini.Modules.CodeEditor;
using Gemini.Modules.CodeEditor.Views;
using PboSpy.Modules.Pbo.Models;

namespace PboSpy.Modules.ConfigExplorer.ViewModels;

class ConfigClassDefinitionViewModel : Screen
{
    private readonly PboEntry _pboEntry;

    private readonly LanguageDefinitionManager _languageDefinitionManager;
    private ICodeEditorView _view;

    public string EntryPath => _pboEntry.FullPath;
    public string PboPath => _pboEntry.PBO.PBOFilePath;

    public string Code { get; }

    public ConfigClassDefinitionViewModel(PboEntry pboEntry, string code)
    {
        _pboEntry = pboEntry;
        Code = code;

        // TODO: Find better way to load LanguageDefinitionManager
        _languageDefinitionManager = IoC.Get<LanguageDefinitionManager>();
    }

    protected override void OnViewLoaded(object view)
    {
        _view = (ICodeEditorView)view;
        LoadText();
    }

    // TODO: Remove duplication with TextPreviewViewModel
    private void LoadText()
    {
        if (_view == null)
        {
            return;
        }

        _view.TextEditor.Text = Code;
        _view.TextEditor.IsReadOnly = true;

        PboSpy.Modules.Preview.ArmaHighlighting.Attach(_view.TextEditor, ".cpp",
            () => _languageDefinitionManager.GetDefinitionByExtension(".cpp")?.SyntaxHighlighting);
    }
}
