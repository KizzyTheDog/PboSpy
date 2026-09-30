using Microsoft.Win32;
using PboSpy.Localization;
using PboSpy.Modules.BulkExport.Models;
using PboSpy.Modules.BulkExport.Services;
using PboSpy.Modules.Pbr.Core;
using PboSpy.Modules.Windows;
using PboSpy.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PboSpy.Modules.Pbr.Views;

public partial class PbrMakerWindow : ToolWindow
{
    private readonly AppSettings _settings = AppSettings.Default;
    private readonly ObservableCollection<GroupVm> _groups = new();
    private readonly Dictionary<TextureKind, List<object>> _candidates = new();
    private CancellationTokenSource _cancel;
    private bool _busy;

    public PbrMakerWindow()
    {
        InitializeComponent();
        GroupsList.ItemsSource = _groups;

        NamingBox.ItemsSource = new[] { Loc.T("Pbr.NamingNumbered"), Loc.T("Pbr.NamingOriginal") };
        NamingBox.SelectedIndex = 0;
        Loc.Instance.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => Loc.Instance.LanguageChanged -= OnLanguageChanged;

        InputBox.Text = _settings.PbrInput;
        OutputBox.Text = _settings.PbrOutput;
        PrefixBox.Text = _settings.PbrPrefix;
        UseNamesCheck.IsChecked = _settings.PbrUseNames;
        FlipGreenCheck.IsChecked = _settings.PbrFlipGreen;
        BaseColorCheck.IsChecked = _settings.PbrWriteBaseColor;
        OrmCheck.IsChecked = _settings.PbrWriteOrm;
        RecursiveCheck.IsChecked = true;
        PrefixBox.TextChanged += (_, _) => Renumber();

        DragEnter += OnDragOver;
        DragOver += OnDragOver;
        Drop += OnDrop;
        Closing += (_, _) =>
        {
            _cancel?.Cancel();
            SavePrefs();
        };
        UpdateSummary();
    }

    /// <summary>Opens the window, optionally pointed at a folder and scanning it straight away.</summary>
    public static PbrMakerWindow Open(string input = null, string output = null, bool scan = false)
    {
        var window = ShowSingle(() => new PbrMakerWindow());
        if (window._busy)
        {
            return window;
        }
        if (!string.IsNullOrEmpty(input))
        {
            window.InputBox.Text = input;
            window.OutputBox.Text = !string.IsNullOrEmpty(output) ? output : Path.Combine(input, "PBR");
        }
        if (scan && Directory.Exists(window.InputBox.Text))
        {
            window.OnScan(null, null);
        }
        return window;
    }

    private void SavePrefs()
    {
        _settings.PbrInput = InputBox.Text.Trim();
        _settings.PbrOutput = OutputBox.Text.Trim();
        _settings.PbrPrefix = PrefixBox.Text.Trim();
        _settings.PbrUseNames = UseNamesCheck.IsChecked == true;
        _settings.PbrFlipGreen = FlipGreenCheck.IsChecked == true;
        _settings.PbrWriteBaseColor = BaseColorCheck.IsChecked == true;
        _settings.PbrWriteOrm = OrmCheck.IsChecked == true;
        _settings.Save();
    }

    private void OnLanguageChanged(object sender, EventArgs e)
    {
        var index = NamingBox.SelectedIndex;
        NamingBox.ItemsSource = new[] { Loc.T("Pbr.NamingNumbered"), Loc.T("Pbr.NamingOriginal") };
        NamingBox.SelectedIndex = index;
        foreach (var group in _groups)
        {
            group.Refresh();
        }
        UpdateSummary();
    }

    // ---- folders --------------------------------------------------------

    private void OnPickInput(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = Loc.T("Pbr.Input") };
        if (Directory.Exists(InputBox.Text))
        {
            dialog.InitialDirectory = InputBox.Text;
        }
        if (dialog.ShowDialog(this) == true)
        {
            InputBox.Text = dialog.FolderName;
            if (string.IsNullOrWhiteSpace(OutputBox.Text))
            {
                OutputBox.Text = Path.Combine(dialog.FolderName, "PBR");
            }
        }
    }

    private void OnPickOutput(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = Loc.T("Pbr.Output") };
        if (Directory.Exists(OutputBox.Text))
        {
            dialog.InitialDirectory = OutputBox.Text;
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

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = !_busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (_busy || e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            return;
        }
        var folder = Directory.Exists(paths[0]) ? paths[0] : Path.GetDirectoryName(paths[0]);
        InputBox.Text = folder;
        OutputBox.Text = Path.Combine(folder ?? "", "PBR");
        OnScan(null, null);
    }

    // ---- scan -----------------------------------------------------------

    private async void OnScan(object sender, RoutedEventArgs e)
    {
        var input = InputBox.Text.Trim();
        if (!Directory.Exists(input))
        {
            Status(Loc.F("Pbr.InputMissing", input));
            return;
        }
        SavePrefs();
        SetBusy(true);
        _groups.Clear();
        UpdateSummary();
        var recursive = RecursiveCheck.IsChecked == true;
        var options = new MatchOptions
        {
            UseNames = UseNamesCheck.IsChecked == true,
            UseRvmats = true,
            Threshold = _settings.PbrThreshold > 0 ? _settings.PbrThreshold : 0.08
        };
        var token = _cancel.Token;
        try
        {
            var files = await Task.Run(() => PbrMatcher.FindTextures(input, recursive), token);
            var rvmats = await Task.Run(() => PbrMatcher.FindRvmats(input, recursive), token);
            if (files.Count == 0)
            {
                Status(Loc.T("Pbr.NoTextures"));
                return;
            }

            var done = 0;
            var textures = new PbrTexture[files.Count];
            await Task.Run(() => Parallel.For(0, files.Count,
                new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) },
                i =>
                {
                    textures[i] = PbrMatcher.Analyse(files[i]);
                    var count = Interlocked.Increment(ref done);
                    Dispatcher.BeginInvoke(() =>
                    {
                        Progress.Value = (double)count / files.Count;
                        Status(Loc.F("Pbr.Reading", count, files.Count, Path.GetFileName(files[i])));
                    });
                }), token);

            Status(Loc.T("Pbr.Matching"));
            Progress.IsIndeterminate = true;
            var groups = await Task.Run(() => PbrMatcher.Group(textures, rvmats, options), token);

            BuildCandidates(textures);
            foreach (var group in groups)
            {
                _groups.Add(new GroupVm(this, group));
            }
            Renumber();
            UpdateSummary();

            var failed = textures.Count(t => t.Error != null);
            Status(Loc.F("Pbr.ScanDone", files.Count, rvmats.Count, _groups.Count(g => g.Members > 1)) +
                   (failed > 0 ? "  " + Loc.F("Pbr.ReadErrors", failed) : ""));
        }
        catch (OperationCanceledException)
        {
            Status(Loc.T("Pbr.Cancelled"));
        }
        catch (Exception ex)
        {
            Status(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void BuildCandidates(IEnumerable<PbrTexture> textures)
    {
        _candidates.Clear();
        foreach (var kind in PbrMatcher.Kinds)
        {
            var list = new List<object> { NoneItem.Instance };
            list.AddRange(textures.Where(t => t.Kind == kind)
                .OrderBy(t => t.FileName, NaturalComparer.Instance)
                .Select(t => (object)TextureVm.For(t)));
            _candidates[kind] = list;
        }
    }

    // ---- convert --------------------------------------------------------

    private async void OnConvert(object sender, RoutedEventArgs e)
    {
        var output = OutputBox.Text.Trim();
        if (string.IsNullOrEmpty(output))
        {
            Status(Loc.T("Pbr.NeedOutput"));
            return;
        }
        var jobs = _groups.Where(g => g.Include && g.Members > 0).ToList();
        if (jobs.Count == 0)
        {
            Status(Loc.T("Pbr.NothingToConvert"));
            return;
        }
        var duplicate = jobs.GroupBy(g => g.SafeName, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
        {
            Status(Loc.F("Pbr.DuplicateName", duplicate.Key));
            return;
        }

        SavePrefs();
        SetBusy(true);
        var options = new OutputOptions
        {
            FlipGreen = FlipGreenCheck.IsChecked == true,
            WriteBaseColor = BaseColorCheck.IsChecked == true,
            WriteOrm = OrmCheck.IsChecked == true
        };
        var token = _cancel.Token;
        var report = new StringBuilder();
        var failures = 0;
        var files = 0;
        try
        {
            Directory.CreateDirectory(output);
            Progress.IsIndeterminate = false;
            var work = jobs.Select(j => (Name: j.SafeName, Badge: j.Badge, Group: j.ToGroup())).ToList();
            var reports = new string[work.Count];
            var finished = 0;
            // Big textures take a while each; a few at a time keeps memory reasonable.
            await Task.Run(() => Parallel.For(0, work.Count,
                new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 4) },
                i =>
                {
                    var (name, badge, group) = work[i];
                    var lines = new StringBuilder();
                    try
                    {
                        var written = PbrOutput.Write(group, name, output, options);
                        Interlocked.Add(ref files, written.Count);
                        lines.AppendLine($"[{name}]  {badge}");
                        foreach (var kind in PbrMatcher.Kinds)
                        {
                            lines.AppendLine($"  {kind,-10} {(group.Members.TryGetValue(kind, out var t) ? t.Path : "-")}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref failures);
                        lines.AppendLine($"[{name}]  FAILED: {ex.Message}");
                    }
                    reports[i] = lines.ToString();
                    var count = Interlocked.Increment(ref finished);
                    Dispatcher.BeginInvoke(() =>
                    {
                        Progress.Value = (double)count / work.Count;
                        Status(Loc.F("Pbr.Writing", count, work.Count, name));
                    });
                }), token);
            foreach (var text in reports)
            {
                report.AppendLine(text);
            }
            File.WriteAllText(Path.Combine(output, "groups.txt"), report.ToString());
            Progress.Value = 1;
            Status(Loc.F("Pbr.ConvertDone", jobs.Count - failures, files, output) + (failures > 0 ? "  " + Loc.F("Pbr.ConvertFailed", failures) : ""));
            if (_settings.OpenFolderAfterExport)
            {
                Process.Start("explorer.exe", $"\"{output}\"");
            }
        }
        catch (OperationCanceledException)
        {
            Status(Loc.T("Pbr.Cancelled"));
        }
        catch (Exception ex)
        {
            Status(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ---- helpers --------------------------------------------------------

    private void OnCancel(object sender, RoutedEventArgs e) => _cancel?.Cancel();

    private void OnNamingChanged(object sender, SelectionChangedEventArgs e) => Renumber();

    private bool UseOriginalNames => NamingBox.SelectedIndex == 1;

    private void Renumber()
    {
        var prefix = string.IsNullOrWhiteSpace(PrefixBox?.Text) ? "UVmap" : PrefixBox.Text.Trim();
        var index = 1;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in _groups)
        {
            if (group.NameEdited)
            {
                used.Add(group.Name);
                continue;
            }
            string name = null;
            if (UseOriginalNames && !string.IsNullOrWhiteSpace(group.Original))
            {
                name = group.Original;
                var suffix = 2;
                while (!used.Add(name))
                {
                    name = $"{group.Original}_{suffix++}";
                }
            }
            if (name == null)
            {
                do
                {
                    name = $"{prefix}{index++}";
                }
                while (!used.Add(name));
            }
            group.SetGeneratedName(name);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        if (busy)
        {
            _cancel = new CancellationTokenSource();
        }
        ScanButton.IsEnabled = !busy;
        ConvertButton.IsEnabled = !busy && _groups.Count > 0;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Hidden;
        Progress.IsIndeterminate = false;
        Progress.Value = 0;
        InputBox.IsEnabled = OutputBox.IsEnabled = !busy;
    }

    private void Status(string text) => StatusText.Text = text;

    internal void UpdateSummary()
    {
        EmptyHint.Visibility = _groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var sets = _groups.Count(g => g.Members > 1);
        var check = _groups.Count(g => g.NeedsCheck);
        SummaryText.Text = _groups.Count == 0 ? "" : Loc.F("Pbr.Summary", sets, _groups.Count - sets, check);
        ConvertButton.IsEnabled = !_busy && _groups.Count > 0;
    }

    /// <summary>Moving a texture into a set takes it out of the set it was in (and hands that set this one's old texture).</summary>
    internal void Reassign(GroupVm target, SlotVm slot, TextureVm previous, TextureVm next)
    {
        if (next != null)
        {
            foreach (var group in _groups)
            {
                if (group == target)
                {
                    continue;
                }
                var other = group.SlotFor(slot.Kind);
                if (other.Current == next)
                {
                    other.SetSilently(previous);
                    group.MarkManual();
                }
            }
        }
        target.MarkManual();
        UpdateSummary();
    }

    internal IList<object> CandidatesFor(TextureKind kind) =>
        _candidates.TryGetValue(kind, out var list) ? list : new List<object> { NoneItem.Instance };

    // ---- view models ----------------------------------------------------

    public sealed class NoneItem
    {
        public static readonly NoneItem Instance = new();
        public string FileName => Loc.T("Pbr.None");
        public override string ToString() => FileName;
    }

    public sealed class TextureVm
    {
        private static readonly ConditionalWeakTable<PbrTexture, TextureVm> Cache = new();
        private ImageSource _thumb;

        private TextureVm(PbrTexture texture) => Texture = texture;

        public static TextureVm For(PbrTexture texture) => texture == null ? null : Cache.GetValue(texture, t => new TextureVm(t));

        public PbrTexture Texture { get; }
        public string FileName => Texture.FileName;
        public string Tooltip => Texture.Error != null
            ? $"{Texture.Path}\n{Texture.Error}"
            : $"{Texture.Path}\n{Texture.Width} × {Texture.Height}";

        public ImageSource Thumb
        {
            get
            {
                if (_thumb == null && Texture.Preview != null)
                {
                    var bitmap = BitmapSource.Create(128, 128, 96, 96, PixelFormats.Bgra32, null, Texture.Preview, 128 * 4);
                    bitmap.Freeze();
                    _thumb = bitmap;
                }
                return _thumb;
            }
        }

        public override string ToString() => FileName;
    }

    public sealed class SlotVm : INotifyPropertyChanged
    {
        private readonly GroupVm _owner;
        private object _selected;

        public SlotVm(GroupVm owner, TextureKind kind, TextureVm texture)
        {
            _owner = owner;
            Kind = kind;
            _selected = (object)texture ?? NoneItem.Instance;
        }

        public TextureKind Kind { get; }
        public string Label => Loc.T("Pbr.Kind." + Kind);
        public IList<object> Candidates => _owner.Window.CandidatesFor(Kind);
        public TextureVm Current => _selected as TextureVm;
        public ImageSource Thumb => Current?.Thumb;
        public string Tooltip => Current?.Tooltip;
        public bool IsEmpty => Current == null;

        public object Selected
        {
            get => _selected;
            set
            {
                value ??= NoneItem.Instance;
                if (ReferenceEquals(value, _selected))
                {
                    return;
                }
                var previous = Current;
                _selected = value;
                Changed();
                _owner.Window.Reassign(_owner, this, previous, value as TextureVm);
            }
        }

        public void SetSilently(TextureVm texture)
        {
            _selected = (object)texture ?? NoneItem.Instance;
            Changed();
        }

        public void RefreshText() => OnChanged(nameof(Label));

        private void Changed()
        {
            OnChanged(nameof(Selected));
            OnChanged(nameof(Thumb));
            OnChanged(nameof(Tooltip));
            OnChanged(nameof(IsEmpty));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class GroupVm : INotifyPropertyChanged
    {
        private string _name;
        private bool _include = true;
        private GroupSource _source;
        private double? _confidence;

        public GroupVm(PbrMakerWindow window, PbrGroup group)
        {
            Window = window;
            _source = group.Source;
            _confidence = group.Confidence;
            Rvmat = group.Rvmat;
            Original = group.SuggestedName != null
                ? BulkExportService.SanitizeSegment(group.SuggestedName, NameSanitizeMode.Underscore)
                : null;
            Slots = PbrMatcher.Kinds
                .Select(k => new SlotVm(this, k, TextureVm.For(group.Members.TryGetValue(k, out var t) ? t : null)))
                .ToArray();
            // A lone _co would just be copied, so it's left out unless ticked.
            _include = Members > 1 || (Members == 1 && SlotFor(TextureKind.BaseColor).Current == null);
        }

        public PbrMakerWindow Window { get; }
        public SlotVm[] Slots { get; }
        public string Rvmat { get; }
        public string Original { get; }
        public bool NameEdited { get; private set; }

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                NameEdited = true;
                OnChanged();
                OnChanged(nameof(SafeName));
            }
        }

        public string SafeName => BulkExportService.SanitizeSegment(string.IsNullOrWhiteSpace(_name) ? "UVmap" : _name.Trim(), NameSanitizeMode.Underscore);

        public void SetGeneratedName(string name)
        {
            _name = name;
            OnChanged(nameof(Name));
            OnChanged(nameof(SafeName));
        }

        public bool Include { get => _include; set { _include = value; OnChanged(); } }

        public int Members => Slots.Count(s => s.Current != null);

        public bool NeedsCheck => _source == GroupSource.Visual && (_confidence ?? 0) < 0.15;

        public string Badge => _source switch
        {
            GroupSource.Rvmat => Loc.T("Pbr.Badge.Rvmat"),
            GroupSource.Name => Loc.T("Pbr.Badge.Name"),
            GroupSource.Manual => Loc.T("Pbr.Badge.Manual"),
            GroupSource.Single => Loc.T("Pbr.Badge.Single"),
            _ => NeedsCheck ? Loc.F("Pbr.Badge.Check", Percent) : Loc.F("Pbr.Badge.Visual", Percent)
        };

        public Brush BadgeBrush => new SolidColorBrush(_source switch
        {
            GroupSource.Rvmat or GroupSource.Name => Color.FromRgb(0x16, 0xA3, 0x4A),
            GroupSource.Manual => Color.FromRgb(0x25, 0x63, 0xEB),
            GroupSource.Single => Color.FromRgb(0x6B, 0x72, 0x80),
            _ => NeedsCheck ? Color.FromRgb(0xD9, 0x77, 0x06) : Color.FromRgb(0x0E, 0x74, 0x90)
        });

        public string Detail => _source switch
        {
            GroupSource.Rvmat => Loc.F("Pbr.Detail.Rvmat", Path.GetFileName(Rvmat)),
            GroupSource.Name => Loc.T("Pbr.Detail.Name"),
            GroupSource.Visual => Loc.T("Pbr.Detail.Visual"),
            GroupSource.Manual => Loc.T("Pbr.Detail.Manual"),
            _ => Loc.T("Pbr.Detail.Single")
        };

        // Similarity scores sit around 0..0.6; this maps them onto a readable percentage.
        private int Percent => (int)Math.Round(Math.Clamp((_confidence ?? 0) / 0.5, 0, 1) * 100);

        public SlotVm SlotFor(TextureKind kind) => Slots[(int)kind];

        public void MarkManual()
        {
            _source = Members <= 1 ? GroupSource.Single : GroupSource.Manual;
            var group = ToGroup();
            PbrMatcher.UpdateConfidence(group);
            _confidence = group.Confidence;
            if (Members > 0 && !_include)
            {
                _include = true;
                OnChanged(nameof(Include));
            }
            Refresh();
        }

        public PbrGroup ToGroup()
        {
            var group = new PbrGroup { Source = _source, Confidence = _confidence, Rvmat = Rvmat };
            foreach (var slot in Slots.Where(s => s.Current != null))
            {
                group.Members[slot.Kind] = slot.Current.Texture;
            }
            return group;
        }

        public void Refresh()
        {
            OnChanged(nameof(Badge));
            OnChanged(nameof(BadgeBrush));
            OnChanged(nameof(Detail));
            OnChanged(nameof(Members));
            foreach (var slot in Slots)
            {
                slot.RefreshText();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
