using Gemini.Modules.StatusBar;
using Gemini.Modules.StatusBar.ViewModels;
using System.Windows;

namespace PboSpy.Modules.StatusBar.Services;

[Export(typeof(IStatusBarManager))]
[PartCreationPolicy(CreationPolicy.Shared)]
internal class StatusBarManager : IStatusBarManager
{
    private static string IdleStatus => PboSpy.Localization.Loc.T("Status.Ready");

    private readonly IStatusBar _statusBar;
    private readonly StatusBarItemViewModel _statusItem;
    private readonly StatusBarItemViewModel _detailsItem;

    private CancellationTokenSource _cts;

    [ImportingConstructor]
    public StatusBarManager(IStatusBar statusBar)
    {
        _statusBar = statusBar;
        _statusItem = new StatusBarItemViewModel(IdleStatus, new GridLength(1, GridUnitType.Star));
        _detailsItem = new StatusBarItemViewModel("", new GridLength(1, GridUnitType.Auto));

        _statusBar.Items.Clear();
        _statusBar.Items.Add(_statusItem);
        _statusBar.Items.Add(_detailsItem);

        PboSpy.Localization.Loc.Instance.LanguageChanged += (_, _) =>
        {
            if (_cts is null && _detailsItem.Message == "")
            {
                _statusItem.Message = IdleStatus;
            }
        };
    }

    public void Reset()
    {
        if (_cts is not null)
        {
            _cts.Cancel();
            _cts = null;
        }

        _statusItem.Message = IdleStatus;
        _detailsItem.Message = "";
    }

    public void SetStatus(string message, string details = "")
    {
        if (_cts is not null)
        {
            _cts.Cancel();
            _cts = null;
        }

        _statusItem.Message = message;
        _detailsItem.Message = details;
    }

    public void SetTemporaryStatus(string message, string details = "", int duration = 1500)
    {
        SetStatus(message, details);

        _cts = new CancellationTokenSource();
        Task.Delay(duration, _cts.Token)
            .ContinueWith(t =>
            {
                if (!t.IsCanceled)
                {
                    Reset();
                    _cts = null;
                }
            });
    }
}
