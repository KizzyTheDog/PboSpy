using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace PboSpy.Modules.Audio.Services;

public static class AudioProcessing
{
    public static AudioData Process(AudioData audio, int sampleRate, bool mono, bool normalize)
    {
        var result = audio;
        if (mono && result.Channels > 1)
        {
            result = Downmix(result);
        }
        if (sampleRate > 0 && sampleRate != result.SampleRate)
        {
            result = Resample(result, sampleRate);
        }
        if (normalize)
        {
            result = Normalize(result);
        }
        return result;
    }

    public static AudioData Downmix(AudioData audio)
    {
        var frames = audio.FrameCount;
        var mono = new float[frames];
        for (var f = 0; f < frames; f++)
        {
            var sum = 0f;
            for (var c = 0; c < audio.Channels; c++)
            {
                sum += audio.Samples[f * audio.Channels + c];
            }
            mono[f] = sum / audio.Channels;
        }
        return new AudioData(audio.SampleRate, 1, mono) { SourceDescription = audio.SourceDescription };
    }

    public static AudioData Resample(AudioData audio, int sampleRate)
    {
        var resampler = new WdlResamplingSampleProvider(new ArraySampleProvider(audio), sampleRate);
        var output = new List<float>((int)((long)audio.Samples.Length * sampleRate / Math.Max(1, audio.SampleRate)) + 1024);
        var buffer = new float[audio.Channels * 4096];
        int read;
        while ((read = resampler.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                output.Add(buffer[i]);
            }
        }
        return new AudioData(sampleRate, audio.Channels, output.ToArray()) { SourceDescription = audio.SourceDescription };
    }

    /// <summary>Peak normalise to -1 dBFS.</summary>
    public static AudioData Normalize(AudioData audio)
    {
        var peak = 0f;
        foreach (var s in audio.Samples)
        {
            peak = Math.Max(peak, Math.Abs(s));
        }
        if (peak <= 0.0001f)
        {
            return audio;
        }
        var gain = 0.891f / peak;
        var samples = new float[audio.Samples.Length];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = audio.Samples[i] * gain;
        }
        return new AudioData(audio.SampleRate, audio.Channels, samples) { SourceDescription = audio.SourceDescription };
    }

    public sealed class ArraySampleProvider : ISampleProvider
    {
        private readonly AudioData _audio;
        private int _position;

        public ArraySampleProvider(AudioData audio)
        {
            _audio = audio;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(audio.SampleRate, audio.Channels);
        }

        public WaveFormat WaveFormat { get; }

        public int Position
        {
            get => _position;
            set => _position = Math.Clamp(value - value % _audio.Channels, 0, _audio.Samples.Length);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var available = Math.Min(count, _audio.Samples.Length - _position);
            if (available <= 0)
            {
                return 0;
            }
            Array.Copy(_audio.Samples, _position, buffer, offset, available);
            _position += available;
            return available;
        }
    }
}
