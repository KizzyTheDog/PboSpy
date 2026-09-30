using PboSpy.Localization;
﻿using Gemini.Modules.Output;
using Microsoft.Extensions.Logging;
using PboSpy.Models;
using PboSpy.Modules.Preview.Factories;
using PboSpy.Modules.Preview.ViewModels;
using PboSpy.Modules.StatusBar;
using System.Windows.Input;

namespace PboSpy.Modules.Preview.Services;

[Export(typeof(IPreviewManager))]
public class PreviewManager : IPreviewManager
{
    private readonly IShell _shell;
    private readonly IStatusBarManager _statusBar;
    private readonly IOutput _output;
    private readonly PreviewFactory _previewFactory;
    private readonly ILogger<PreviewManager> _logger;

    [ImportingConstructor]
    public PreviewManager(IShell shell, IStatusBarManager statusBar, IOutput output, PreviewFactory previewFactory,
        ILoggerFactory loggerFactory)
    {
        _shell = shell;
        _statusBar = statusBar;
        _output = output;
        _previewFactory = previewFactory;
        _logger = loggerFactory.CreateLogger<PreviewManager>();
    }

    public async Task ShowPreview(FileBase model, bool pin = true, bool activate = true)
    {
        var existing = _shell.Documents
            .OfType<PreviewViewModel>()
            .FirstOrDefault(doc => doc.Model == model);

        if (existing != null)
        {
            if (pin && existing.IsPreviewTab)
            {
                existing.IsPreviewTab = false;
            }
            await Show(existing, activate);
            return;
        }

        var document = await CreatePreview(model) as PreviewViewModel;
        if (document == null)
        {
            return;
        }

        if (!pin)
        {
            // Only one preview tab at a time: the next single click replaces it.
            foreach (var old in _shell.Documents.OfType<PreviewViewModel>().Where(d => d.IsPreviewTab).ToList())
            {
                await _shell.CloseDocumentAsync(old);
            }
            document.IsPreviewTab = true;
        }

        await Show(document, activate);
    }

    private async Task Show(PreviewViewModel document, bool activate)
    {
        var previous = _shell.ActiveLayoutItem;
        await _shell.OpenDocumentAsync(document);
        if (!activate && previous is ITool && previous != document)
        {
            // Keep the keyboard in the explorer while previewing.
            _shell.ActiveLayoutItem = previous;
        }
    }

    private async Task<Document> CreatePreview(FileBase model)
    {
        try
        {
            var work = Task.Run(() => _previewFactory.CreatePreview(model));
            // Most previews are ready in a few milliseconds; only big ones get the busy status.
            if (await Task.WhenAny(work, Task.Delay(250)) != work)
            {
                _statusBar.SetStatus(Loc.T("Status.OpeningPreview"), model.Name);
                Mouse.OverrideCursor = Cursors.AppStarting;
            }
            var document = await work;
            _statusBar.Reset();
            return document;
        }
        catch (Exception ex)
        {
            ReportError(model.Name, ex);
            return null;
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void ReportError(string source, Exception ex)
    {
        _output.AppendLine($"ERROR: \"{ex.Message}\" while opening {source}");
        _statusBar.SetTemporaryStatus($"ERROR: {ex.Message}. See output for details.", duration: 3000);
        _logger.LogError(ex, "Error opening File Preview for {source}", source);
    }
}
