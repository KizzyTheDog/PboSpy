using Microsoft.Win32;
using NAudio.Wave;
using PboSpy.Localization;
using PboSpy.Models;
using PboSpy.Modules.Audio.Services;
using PboSpy.Modules.BulkExport.Models;
using PboSpy.Modules.Preview.ViewModels;
using PboSpy.Services;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PboSpy.Modules.Audio.ViewModels;

public class AudioPreviewViewModel : PreviewViewModel
{
    private readonly AudioData _audio;
    private DispatcherTimer _timer;
    private WaveOutEvent _output;
    private AudioProcessing.ArraySampleProvider _provider;
    private VolumeSampleProviderWrapper _volumeProvider;
    private double _position;
    // Shared by every audio tab; 1 = the file as recorded, up to 2x.
    private static float _volume = 1f;
    private bool _loop;
    private bool _seeking;

    public AudioPreviewViewModel(FileBase model, AudioData audio) : base(model)
    {
        _audio = audio;
        Waveform = RenderWaveform(audio, 1200, 160);
        Loc.Instance.LanguageChanged += (_, _) => NotifyOfPropertyChange(nameof(Info));
    }

    public ImageSource Waveform { get; }

    public string Info => Loc.F("Audio.Info", _audio.SampleRate, _audio.Channels, _audio.Duration.ToString(@"m\:ss\.fff"),
        string.IsNullOrEmpty(_audio.SourceDescription) ? _model.Extension : _audio.SourceDescription);

    public double Duration => _audio.Duration.TotalSeconds;

    public string PositionText => TimeSpan.FromSeconds(Position).ToString(@"m\:ss\.f") + " / " + _audio.Duration.ToString(@"m\:ss\.f");

    public double Position
    {
        get => _position;
        set
        {
            _position = Math.Clamp(value, 0, Duration);
            if (_seeking && _provider != null)
            {
                _provider.Position = (int)(_position * _audio.SampleRate) * _audio.Channels;
            }
            NotifyOfPropertyChange(nameof(Position));
            NotifyOfPropertyChange(nameof(PositionText));
            NotifyOfPropertyChange(nameof(CursorX));
        }
    }

    public double CursorX => Duration > 0 ? Position / Duration : 0;

    public string VolumeText => $"{_volume * 100:0}%";

    public float Volume
    {
        get => _volume;
        set
        {
            // Snaps to 100% near the mark.
            _volume = Math.Abs(value - 1f) < 0.06f ? 1f : Math.Clamp(value, 0f, 2f);
            if (_volumeProvider != null)
            {
                _volumeProvider.Volume = _volume;
            }
            NotifyOfPropertyChange(nameof(Volume));
            NotifyOfPropertyChange(nameof(VolumeText));
        }
    }

    public bool Loop
    {
        get => _loop;
        set
        {
            _loop = value;
            NotifyOfPropertyChange(nameof(Loop));
        }
    }

    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;

    public void PlayPause()
    {
        if (IsPlaying)
        {
            _output.Pause();
            _timer?.Stop();
        }
        else
        {
            try
            {
                EnsureOutput();
            }
            catch (Exception ex)
            {
                DisposeOutput();
                MessageBox.Show(ex.Message, Loc.T("Audio.Play"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (_provider.Position >= _audio.Samples.Length)
            {
                _provider.Position = 0;
            }
            else if (_provider.Position == 0 && _position > 0)
            {
                _provider.Position = (int)(_position * _audio.SampleRate) * _audio.Channels;
            }
            _output.Play();
            _timer.Start();
        }
        NotifyOfPropertyChange(nameof(IsPlaying));
    }

    public void Stop()
    {
        _output?.Stop();
        _timer?.Stop();
        if (_provider != null)
        {
            _provider.Position = 0;
        }
        Position = 0;
        NotifyOfPropertyChange(nameof(IsPlaying));
    }

    public void BeginSeek() => _seeking = true;

    public void EndSeek() => _seeking = false;

    public void SeekTo(double fraction)
    {
        _seeking = true;
        _provider ??= new AudioProcessing.ArraySampleProvider(_audio);
        Position = fraction * Duration;
        _seeking = false;
    }

    public void SaveAs()
    {
        var dlg = new SaveFileDialog
        {
            Title = Loc.T("Audio.SaveAs"),
            FileName = Path.GetFileNameWithoutExtension(_model.Name),
            Filter = "WAV|*.wav|OGG Vorbis|*.ogg|MP3|*.mp3|FLAC|*.flac|AAC (m4a)|*.m4a|Arma WSS|*.wss",
            FilterIndex = 1
        };
        if (dlg.ShowDialog() != true)
        {
            return;
        }

        var format = Path.GetExtension(dlg.FileName).ToLowerInvariant() switch
        {
            ".ogg" => AudioOutputFormat.Ogg,
            ".mp3" => AudioOutputFormat.Mp3,
            ".flac" => AudioOutputFormat.Flac,
            ".m4a" => AudioOutputFormat.M4a,
            ".wss" => AudioOutputFormat.Wss,
            _ => AudioOutputFormat.Wav
        };
        try
        {
            using var stream = File.Create(dlg.FileName);
            AudioEncoder.Encode(_audio, new AudioEncodeSettings
            {
                Format = format,
                Bitrate = AppSettings.Default.AudioBitrate,
                OggQuality = AppSettings.Default.AudioQuality,
                FfmpegPath = AppSettings.Default.FfmpegPath
            }, stream);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Loc.T("Audio.SaveAs"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void CanExecuteCopy(Command command) => command.Enabled = true;

    protected override Task ExecuteCopy(Command command)
    {
        using var stream = new MemoryStream();
        AudioEncoder.WriteWav(stream, _audio);
        stream.Position = 0;
        Clipboard.SetAudio(stream.ToArray());
        return Task.CompletedTask;
    }

    public override Task TryCloseAsync(bool? dialogResult = null)
    {
        DisposeOutput();
        return base.TryCloseAsync(dialogResult);
    }

    protected override Task OnDeactivateAsync(bool close, CancellationToken cancellationToken)
    {
        if (close)
        {
            DisposeOutput();
        }
        return base.OnDeactivateAsync(close, cancellationToken);
    }

    private void EnsureOutput()
    {
        // The view model is built on a worker thread; the timer has to belong to the UI thread or it never ticks.
        _timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Normal, (_, _) => Tick(),
            Application.Current.Dispatcher);
        if (_output != null)
        {
            return;
        }
        _provider ??= new AudioProcessing.ArraySampleProvider(_audio);
        _volumeProvider = new VolumeSampleProviderWrapper(_provider) { Volume = _volume };
        // 16 bit PCM: some sound drivers refuse IEEE float through WaveOut and play nothing.
        _output = new WaveOutEvent { DesiredLatency = 150, NumberOfBuffers = 3 };
        _output.Init(new NAudio.Wave.SampleProviders.SampleToWaveProvider16(_volumeProvider));
        _output.PlaybackStopped += (_, _) =>
        {
            if (_loop && _provider.Position >= _audio.Samples.Length)
            {
                _provider.Position = 0;
                _output.Play();
                return;
            }
            _timer?.Stop();
            Tick();
            NotifyOfPropertyChange(nameof(IsPlaying));
        };
        _timer.Stop();
    }

    private void Tick()
    {
        if (_provider == null || _seeking)
        {
            return;
        }
        _position = (double)_provider.Position / _audio.Channels / Math.Max(1, _audio.SampleRate);
        NotifyOfPropertyChange(nameof(Position));
        NotifyOfPropertyChange(nameof(PositionText));
        NotifyOfPropertyChange(nameof(CursorX));
    }

    private void DisposeOutput()
    {
        _timer?.Stop();
        _output?.Stop();
        _output?.Dispose();
        _output = null;
    }

    private static ImageSource RenderWaveform(AudioData audio, int width, int height)
    {
        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new int[width * height];
        var frames = audio.FrameCount;
        var mid = height / 2;
        const int wave = unchecked((int)0xFF3FA9F5);
        const int rms = unchecked((int)0xFF1F6FB0);
        const int axis = unchecked((int)0x40FFFFFF);

        for (var x = 0; x < width; x++)
        {
            pixels[mid * width + x] = axis;
            var start = (long)x * frames / width;
            var end = Math.Max(start + 1, (long)(x + 1) * frames / width);
            float min = 0, max = 0;
            double sum = 0;
            long count = 0;
            for (var f = start; f < end && f < frames; f++)
            {
                for (var c = 0; c < audio.Channels; c++)
                {
                    var s = audio.Samples[f * audio.Channels + c];
                    min = Math.Min(min, s);
                    max = Math.Max(max, s);
                    sum += s * s;
                    count++;
                }
            }
            var r = count > 0 ? Math.Sqrt(sum / count) : 0;
            var top = Math.Clamp((int)(mid - max * mid), 0, height - 1);
            var bottom = Math.Clamp((int)(mid - min * mid), 0, height - 1);
            var rmsTop = Math.Clamp((int)(mid - r * mid), 0, height - 1);
            var rmsBottom = Math.Clamp((int)(mid + r * mid), 0, height - 1);
            for (var y = top; y <= bottom; y++)
            {
                pixels[y * width + x] = y >= rmsTop && y <= rmsBottom ? rms : wave;
            }
        }
        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        bitmap.Freeze();
        return bitmap;
    }

    private sealed class VolumeSampleProviderWrapper : ISampleProvider
    {
        private readonly ISampleProvider _source;

        public VolumeSampleProviderWrapper(ISampleProvider source)
        {
            _source = source;
        }

        public float Volume { get; set; } = 1f;

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            var read = _source.Read(buffer, offset, count);
            for (var i = offset; i < offset + read; i++)
            {
                buffer[i] *= Volume;
            }
            return read;
        }
    }
}
