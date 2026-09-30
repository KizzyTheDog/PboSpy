using PboSpy.Modules.Audio.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PboSpy.Modules.Audio.Views;

public partial class AudioPreviewView : UserControl
{
    public AudioPreviewView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is INotifyPropertyChanged old)
            {
                old.PropertyChanged -= OnViewModelChanged;
            }
            if (e.NewValue is INotifyPropertyChanged next)
            {
                next.PropertyChanged += OnViewModelChanged;
            }
        };
        SizeChanged += (_, _) => MoveCursor();
    }

    private AudioPreviewViewModel ViewModel => DataContext as AudioPreviewViewModel;

    private void OnViewModelChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioPreviewViewModel.CursorX))
        {
            MoveCursor();
        }
    }

    private void MoveCursor()
    {
        if (ViewModel != null)
        {
            Canvas.SetLeft(PlayHead, ViewModel.CursorX * WaveHost.ActualWidth);
        }
    }

    private void OnWaveformClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel != null && WaveHost.ActualWidth > 0)
        {
            ViewModel.SeekTo(e.GetPosition(WaveHost).X / WaveHost.ActualWidth);
        }
    }

    private void OnSliderDown(object sender, MouseButtonEventArgs e) => ViewModel?.BeginSeek();

    private void OnSliderUp(object sender, MouseButtonEventArgs e) => ViewModel?.EndSeek();
}
