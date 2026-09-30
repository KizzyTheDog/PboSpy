using Microsoft.Win32;
using PboSpy.Localization;
using PboSpy.Modules.P3dTools.Core;
using PboSpy.Modules.Windows;
using PboSpy.Services;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace PboSpy.Modules.P3dTools.Views;

public enum P3dAction { None, Debinarize, ExtractRvmats, ExtractModelCfg, ChangePaths, StripProxies }

public partial class P3dToolsWindow : ToolWindow
{
    private readonly AppSettings _settings = AppSettings.Default;
    private P3DFormat _format = P3DFormat.Unknown;
    private bool _batchMode;
    private bool _busy;
    private CancellationTokenSource _cancel;
    private string _statusKey = "Main.StatusReady";
    private object[] _statusArgs = Array.Empty<object>();

    public P3dToolsWindow()
    {
        InitializeComponent();

        LanguageBox.ItemsSource = Loc.Instance.Languages;
        LanguageBox.SelectedItem = Loc.Instance.Languages.FirstOrDefault(l => l.Code == Loc.Instance.Language);
        Loc.Instance.LanguageChanged += OnAppLanguageChanged;
        Closed += (_, _) => Loc.Instance.LanguageChanged -= OnAppLanguageChanged;

        BisDllBanner.Visibility = Converter.IsAvailable ? Visibility.Collapsed : Visibility.Visible;
        MirrorCheck.IsChecked = _settings.P3dMirrorFolders;

        if (!string.IsNullOrWhiteSpace(_settings.P3dOutput))
        {
            OutputBox.Text = _settings.P3dOutput;
        }
        if (!string.IsNullOrWhiteSpace(_settings.P3dInput) && (File.Exists(_settings.P3dInput) || Directory.Exists(_settings.P3dInput)))
        {
            SetInput(_settings.P3dInput);
        }

        DragEnter += OnDragOver;
        DragOver += OnDragOver;
        Drop += OnDrop;
        Closing += (_, _) => SavePrefs();
        RefreshActions();
        SetStatus("Main.StatusReady");
    }

    /// <summary>Opens the window (or brings it up) with an input, optionally running an action straight away.</summary>
    public static P3dToolsWindow Open(string input = null, string output = null, P3dAction action = P3dAction.None)
    {
        var window = ShowSingle(() => new P3dToolsWindow());
        if (window._busy)
        {
            return window;
        }
        if (!string.IsNullOrEmpty(output))
        {
            window.OutputBox.Text = output;
        }
        if (!string.IsNullOrEmpty(input))
        {
            window.SetInput(input, string.IsNullOrEmpty(output));
        }
        switch (action)
        {
            case P3dAction.Debinarize:
                window.OnDebinarize(null, null);
                break;
            case P3dAction.ExtractRvmats:
                window.OnRvmats(null, null);
                break;
            case P3dAction.ExtractModelCfg:
                window.OnModelCfg(null, null);
                break;
            case P3dAction.ChangePaths:
                window.OnChangePaths(null, null);
                break;
            case P3dAction.StripProxies:
                window.OnStrip(null, null);
                break;
        }
        return window;
    }

    private void SavePrefs()
    {
        _settings.P3dInput = InputBox.Text.Trim();
        _settings.P3dOutput = OutputBox.Text.Trim();
        _settings.P3dMirrorFolders = MirrorCheck.IsChecked == true;
        _settings.Save();
    }

    // ---- language -------------------------------------------------------

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageBox.SelectedItem is LanguageInfo language && language.Code != Loc.Instance.Language)
        {
            LocalizationService.SetLanguage(language.Code);
        }
    }

    private void OnAppLanguageChanged(object sender, EventArgs e)
    {
        LanguageBox.SelectedItem = Loc.Instance.Languages.FirstOrDefault(l => l.Code == Loc.Instance.Language);
        RefreshActions();
        SetStatus(_statusKey, _statusArgs);
    }

    // ---- input / output -------------------------------------------------

    private void OnPickFile(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Strings.T("Main.DlgFileTitle"),
            Filter = Strings.T("Main.DlgFileFilter"),
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            SetInput(dialog.FileName);
        }
    }

    private void OnPickFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = Strings.T("Main.DlgFolderInTitle") };
        if (dialog.ShowDialog(this) == true)
        {
            SetInput(dialog.FolderName);
        }
    }

    private void OnPickOutput(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = Strings.T("Main.DlgFolderOutTitle") };
        if (Directory.Exists(OutputBox.Text.Trim()))
        {
            dialog.InitialDirectory = OutputBox.Text.Trim();
        }
        if (dialog.ShowDialog(this) == true)
        {
            OutputBox.Text = dialog.FolderName;
        }
    }

    private void OnOpenOutput(object sender, RoutedEventArgs e)
    {
        var folder = OutputBox.Text.Trim();
        if (Directory.Exists(folder))
        {
            Process.Start("explorer.exe", $"\"{folder}\"");
        }
    }

    private bool _settingInput;

    private void OnInputChanged(object sender, TextChangedEventArgs e)
    {
        if (!_settingInput)
        {
            SetInput(InputBox.Text.Trim(), false, false);
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && !_busy ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!_busy && e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            var target = paths.FirstOrDefault(p => Directory.Exists(p) || p.EndsWith(".p3d", StringComparison.OrdinalIgnoreCase)) ?? paths[0];
            SetInput(target);
        }
    }

    private void SetInput(string path, bool resetOutput = false, bool fillOutput = true)
    {
        _settingInput = true;
        if (InputBox.Text.Trim() != (path ?? ""))
        {
            InputBox.Text = path ?? "";
        }
        _settingInput = false;
        if (Directory.Exists(path))
        {
            _batchMode = true;
            if (resetOutput || (fillOutput && string.IsNullOrWhiteSpace(OutputBox.Text)))
            {
                OutputBox.Text = path;
            }
        }
        else if (File.Exists(path))
        {
            _batchMode = false;
            if (resetOutput || (fillOutput && string.IsNullOrWhiteSpace(OutputBox.Text)))
            {
                OutputBox.Text = Path.GetDirectoryName(path) ?? "";
            }
        }
        RefreshActions();
    }

    private string[] BatchFiles()
    {
        try
        {
            return Directory.GetFiles(InputBox.Text.Trim(), "*.p3d", SearchOption.AllDirectories);
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    private void RefreshActions()
    {
        if (_busy)
        {
            return;
        }
        var input = InputBox.Text.Trim();
        var bis = Converter.IsAvailable;
        MirrorCheck.IsEnabled = _batchMode;

        if (_batchMode && Directory.Exists(input))
        {
            var count = BatchFiles().Length;
            FormatText.Text = Strings.T("Main.FmtBatch", count);
            FormatText.SetResourceReference(TextBlock.ForegroundProperty, count > 0 ? "PboSpy.Accent" : "PboSpy.TextDim");
            ChangePathsButton.IsEnabled = count > 0;
            DebinButton.IsEnabled = RvmatButton.IsEnabled = CfgButton.IsEnabled = count > 0 && bis;
            return;
        }

        _format = File.Exists(input) ? Converter.DetectFormat(input) : P3DFormat.Unknown;
        switch (_format)
        {
            case P3DFormat.Odol:
                FormatText.Text = Strings.T("Main.FmtOdol");
                FormatText.SetResourceReference(TextBlock.ForegroundProperty, "PboSpy.Accent");
                break;
            case P3DFormat.Mlod:
                FormatText.Text = Strings.T("Main.FmtMlod");
                FormatText.SetResourceReference(TextBlock.ForegroundProperty, "PboSpy.Success");
                break;
            default:
                FormatText.Text = File.Exists(input) ? Strings.T("Main.FmtUnknown") : Strings.T("Main.FmtNone");
                FormatText.SetResourceReference(TextBlock.ForegroundProperty, "PboSpy.TextDim");
                break;
        }

        var isOdol = _format == P3DFormat.Odol;
        ChangePathsButton.IsEnabled = _format == P3DFormat.Mlod || (isOdol && bis);
        DebinButton.IsEnabled = RvmatButton.IsEnabled = CfgButton.IsEnabled = isOdol && bis;
        StripButton.IsEnabled = _format == P3DFormat.Mlod || (isOdol && bis);
    }

    // ---- actions --------------------------------------------------------

    private async void OnDebinarize(object sender, RoutedEventArgs e)
    {
        if (!Validate(out var input, out var output))
        {
            return;
        }
        var strip = StripCheck.IsChecked == true;
        ClearRemembered();
        SetBusy(true);
        try
        {
            if (_batchMode)
            {
                await BatchOdol(Strings.T("Main.OpDebinarize"), input, output, (file, target) =>
                {
                    var result = Converter.Convert(file, target, m => Log(m));
                    if (!result.Success)
                    {
                        throw new InvalidOperationException(result.Message);
                    }
                    if (strip)
                    {
                        Log(Strings.T("Main.LogStripped", ProxyStrip.Strip(result.OutputPath, result.OutputPath)));
                    }
                    Remember(result.OutputPath);
                    Log($"  ✓ {Path.GetFileName(result.OutputPath)}");
                });
            }
            else
            {
                Log(Strings.T("Main.LogDebinarizing", Path.GetFileName(input)));
                var result = await Task.Run(() =>
                {
                    var converted = Converter.Convert(input, output, m => Log(m));
                    if (converted.Success && strip)
                    {
                        Log(Strings.T("Main.LogStripped", ProxyStrip.Strip(converted.OutputPath, converted.OutputPath)));
                    }
                    return converted;
                });
                if (result.Success)
                {
                    Remember(result.OutputPath);
                    Log($"✓  {Path.GetFileName(result.OutputPath)}", LogKind.Success);
                    Log(Strings.T("Main.LogSavedTo", result.OutputPath));
                    SetStatus("Main.LogConcluded", Path.GetFileName(result.OutputPath));
                }
                else
                {
                    Log($"✗  {result.Message}", LogKind.Error);
                    SetStatus("Main.LogFailureDebin");
                }
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnStrip(object sender, RoutedEventArgs e)
    {
        if (!Validate(out var input, out var output))
        {
            return;
        }
        ClearRemembered();
        SetBusy(true);
        try
        {
            if (_batchMode)
            {
                await BatchOdol(Strings.T("Main.OpStrip"), input, output, (file, target) => StripOne(file, target), true);
            }
            else
            {
                await Task.Run(() => StripOne(input, output));
                SetStatus("Main.LogConcluded", Path.GetFileName(input));
            }
        }
        catch (Exception ex)
        {
            Log($"✗  {ex.Message}", LogKind.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ODOL goes through the debinarizer first; the copy keeps the source untouched.
    private void StripOne(string file, string folder)
    {
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, Path.GetFileNameWithoutExtension(file) + "_noproxy.p3d");
        var source = file;
        if (Converter.DetectFormat(file) == P3DFormat.Odol)
        {
            var result = Converter.Convert(file, folder, m => Log(m));
            if (!result.Success)
            {
                throw new InvalidOperationException(result.Message);
            }
            source = result.OutputPath;
        }
        var removed = ProxyStrip.Strip(source, target);
        if (!string.Equals(source, file, StringComparison.OrdinalIgnoreCase) && !string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(source);
        }
        Remember(target);
        Log(Strings.T("Main.LogStripped", removed), LogKind.Success);
        Log(Strings.T("Main.LogSavedTo", target));
    }

    private readonly List<string> _outputs = new();

    private void Remember(string path)
    {
        lock (_outputs)
        {
            _outputs.Add(path);
        }
        Dispatcher.BeginInvoke(() => OpenInAppButton.IsEnabled = true);
    }

    private void ClearRemembered()
    {
        lock (_outputs)
        {
            _outputs.Clear();
        }
        OpenInAppButton.IsEnabled = false;
    }

    private async void OnOpenInApp(object sender, RoutedEventArgs e)
    {
        List<string> paths;
        lock (_outputs)
        {
            paths = _outputs.ToList();
        }
        await PboSpy.Services.AppOpen.Show(paths);
    }

    private async void OnRvmats(object sender, RoutedEventArgs e)
    {
        if (!Validate(out var input, out var output))
        {
            return;
        }
        SetBusy(true);
        try
        {
            if (_batchMode)
            {
                await BatchOdol(Strings.T("Main.OpExtractRvmats"), input, output, (file, target) =>
                {
                    var count = OdolExtract.ExtractRvmats(file, target, m => Log(m));
                    Log($"  ✓ {Path.GetFileName(file)}: {count} RVMAT(s)");
                });
            }
            else
            {
                Log(Strings.T("Main.LogExtractingRvmats", Path.GetFileName(input)));
                try
                {
                    var items = await Task.Run(() => OdolExtract.GetRvmats(input, m => Log(m)));
                    if (items.Count == 0)
                    {
                        Log(Strings.T("Main.LogNoRvmats"));
                        SetStatus("Main.LogStatusNoRvmats");
                    }
                    else
                    {
                        Log(Strings.T("Main.LogRvmatsReconstructed", items.Count), LogKind.Success);
                        SetStatus("Main.LogStatusRvmatsPreview", items.Count);
                        new TextPreviewWindow(Strings.T("Tp.TitleRvmats", Path.GetFileName(input)), items, output) { Owner = this }.Show();
                    }
                }
                catch (Exception ex)
                {
                    Log($"✗  {ex.Message}", LogKind.Error);
                    SetStatus("Main.LogFailureRvmats");
                }
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnModelCfg(object sender, RoutedEventArgs e)
    {
        if (!Validate(out var input, out var output))
        {
            return;
        }
        SetBusy(true);
        try
        {
            if (_batchMode)
            {
                await BatchOdol(Strings.T("Main.OpExtractCfg"), input, output, (file, target) =>
                {
                    var written = OdolExtract.ExtractModelCfg(file, target, m => Log(m));
                    Log($"  ✓ {Path.GetFileName(written)}");
                });
            }
            else
            {
                Log(Strings.T("Main.LogExtractingCfg", Path.GetFileName(input)));
                try
                {
                    var item = await Task.Run(() => OdolExtract.GetModelCfg(input, m => Log(m)));
                    Log(Strings.T("Main.LogCfgReconstructed", item.Name), LogKind.Success);
                    SetStatus("Main.LogStatusCfgPreview");
                    new TextPreviewWindow(Strings.T("Tp.TitleCfg", Path.GetFileName(input)), new List<OdolExtract.TextItem> { item }, output) { Owner = this }.Show();
                }
                catch (Exception ex)
                {
                    Log($"✗  {ex.Message}", LogKind.Error);
                    SetStatus("Main.LogFailureCfg");
                }
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnChangePaths(object sender, RoutedEventArgs e)
    {
        if (!Validate(out var input, out var output))
        {
            return;
        }
        var files = _batchMode ? BatchFiles() : new[] { input };
        if (files.Length == 0)
        {
            Log(Strings.T("Main.LogNoP3dFound"), LogKind.Error);
            return;
        }

        SetBusy(true);
        try
        {
            Log(Strings.T("Main.LogChangePathsReading", files.Length));
            var loaded = await Task.Run(() =>
            {
                var list = new List<(string, IReadOnlyList<string>)>();
                foreach (var file in files)
                {
                    try
                    {
                        var paths = P3DRepath.ListPaths(file);
                        list.Add((file, paths));
                        Log(Strings.T("Main.LogChangePathsItem", Path.GetFileName(file), paths.Count));
                    }
                    catch (Exception ex)
                    {
                        Log($"  ✗ {Path.GetFileName(file)}: {ex.Message}", LogKind.Error);
                    }
                }
                return list;
            });

            if (loaded.Count == 0)
            {
                Log(Strings.T("Main.LogChangePathsNoneRead"), LogKind.Error);
                return;
            }

            var dialog = new ChangePathsWindow(loaded) { Owner = this };
            dialog.PreloadRules(_settings.P3dRecentRules.Select(r => new RepathRules.Rule(r.From, r.To, r.Regex)));
            if (dialog.ShowDialog() != true)
            {
                Log(Strings.T("Main.LogChangePathsCancelled"));
                SetStatus("Main.StatusCancelled");
                return;
            }

            if (dialog.AppliedPrefixRules.Count > 0)
            {
                _settings.P3dRecentRules = dialog.AppliedPrefixRules
                    .Select(r => new RepathRuleSetting { From = r.From, To = r.To, Regex = r.IsRegex })
                    .ToList();
                _settings.Save();
            }

            var replacements = dialog.Replacements;
            var targets = dialog.SelectedFiles;
            var batch = _batchMode;
            Log(Strings.T("Main.LogChangePathsApplying", replacements.Count, targets.Count));

            await Task.Run(() =>
            {
                foreach (var file in targets)
                {
                    try
                    {
                        var name = Path.GetFileNameWithoutExtension(file) + "_repath.p3d";
                        // Batch mode writes next to each source; a single file goes to the output folder.
                        var destination = batch ? Path.Combine(Path.GetDirectoryName(file) ?? "", name) : Path.Combine(output, name);
                        var result = P3DRepath.Apply(file, destination, replacements, m => Log(m));
                        Log(Strings.T(batch ? "Main.LogChangePathsBatchOk" : "Main.LogChangePathsSingleOk", name, result.Sites));
                        if (!batch)
                        {
                            Log(Strings.T("Main.LogSavedTo", result.OutputPath));
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"  ✗ {Path.GetFileName(file)}: {ex.Message}", LogKind.Error);
                    }
                }
            });

            Log(Strings.T("Main.LogChangePathsDone", targets.Count), LogKind.Success);
            SetStatus("Main.StatusChangePathsDone", targets.Count);
        }
        catch (Exception ex)
        {
            Log($"✗  {ex.Message}", LogKind.Error);
            SetStatus("Main.LogFailureChangePaths");
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>Runs an ODOL-only operation over every .p3d below the input folder.</summary>
    private async Task BatchOdol(string operation, string folder, string output, Action<string, string> perFile, bool allowMlod = false)
    {
        var files = BatchFiles();
        var mirror = MirrorCheck.IsChecked == true;
        var token = _cancel.Token;
        Log(Strings.T("Main.LogBatchHeader", operation, files.Length, folder));

        int ok = 0, skipped = 0, failed = 0;
        await Task.Run(() =>
        {
            foreach (var file in files)
            {
                if (token.IsCancellationRequested)
                {
                    Log(Strings.T("Main.LogCancelled"), LogKind.Error);
                    break;
                }
                var format = Converter.DetectFormat(file);
                if (format != P3DFormat.Odol && !(allowMlod && format == P3DFormat.Mlod))
                {
                    skipped++;
                    Log(Strings.T("Main.LogBatchSkipNonOdol", Path.GetFileName(file)));
                    continue;
                }
                try
                {
                    // Two models with the same name in different folders would overwrite each other otherwise.
                    var target = output;
                    if (mirror)
                    {
                        var relative = Path.GetRelativePath(folder, Path.GetDirectoryName(file) ?? folder);
                        if (relative != ".")
                        {
                            target = Path.Combine(output, relative);
                        }
                    }
                    Directory.CreateDirectory(target);
                    perFile(file, target);
                    ok++;
                }
                catch (Exception ex)
                {
                    failed++;
                    Log($"  ✗ {Path.GetFileName(file)}: {ex.Message}", LogKind.Error);
                }
            }
        });

        Log(Strings.T("Main.LogBatchSummary", operation, ok, skipped, failed), failed > 0 ? LogKind.Error : LogKind.Success);
        SetStatus("Main.StatusBatchSummary", operation, ok, skipped, failed);
    }

    private bool Validate(out string input, out string output)
    {
        input = InputBox.Text.Trim();
        output = OutputBox.Text.Trim();

        var inputOk = _batchMode ? Directory.Exists(input) : File.Exists(input);
        if (!inputOk)
        {
            Log(Strings.T("Main.LogInputNotFound", input), LogKind.Error);
            return false;
        }
        if (string.IsNullOrEmpty(output))
        {
            output = _batchMode ? input : Path.GetDirectoryName(input) ?? "";
            OutputBox.Text = output;
        }
        if (!Directory.Exists(output))
        {
            try
            {
                Directory.CreateDirectory(output);
                Log(Strings.T("Main.LogOutputCreated", output));
            }
            catch (Exception)
            {
                Log(Strings.T("Main.LogOutputNotFound", output), LogKind.Error);
                return false;
            }
        }
        SavePrefs();
        return true;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        FileButton.IsEnabled = FolderButton.IsEnabled = OutputButton.IsEnabled = InputBox.IsEnabled = !busy;
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Hidden;
        Progress.IsIndeterminate = busy;
        CancelButton.Visibility = busy && _batchMode ? Visibility.Visible : Visibility.Collapsed;

        if (busy)
        {
            _cancel = new CancellationTokenSource();
            ChangePathsButton.IsEnabled = DebinButton.IsEnabled = RvmatButton.IsEnabled = CfgButton.IsEnabled = false;
            SetStatus("Main.StatusBusy");
        }
        else
        {
            RefreshActions();
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => _cancel?.Cancel();

    private void SetStatus(string key, params object[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        StatusText.Text = Strings.T(key, args);
    }

    // ---- log ------------------------------------------------------------

    private enum LogKind { Normal, Success, Error }

    private void Log(string message, LogKind kind = LogKind.Normal)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Log(message, kind));
            return;
        }
        var run = new Run($"[{DateTime.Now:HH:mm:ss}] {message}");
        run.SetResourceReference(TextElement.ForegroundProperty, kind switch
        {
            LogKind.Error => "PboSpy.Error",
            LogKind.Success => "PboSpy.Success",
            _ => "PboSpy.TextDim"
        });
        LogBox.Document.Blocks.Add(new Paragraph(run) { Margin = new Thickness(0) });
        LogBox.ScrollToEnd();
    }

    private void OnClearLog(object sender, RoutedEventArgs e) => LogBox.Document.Blocks.Clear();

    private void OnCopyLog(object sender, RoutedEventArgs e)
    {
        var text = new TextRange(LogBox.Document.ContentStart, LogBox.Document.ContentEnd).Text;
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception)
        {
        }
    }
}
