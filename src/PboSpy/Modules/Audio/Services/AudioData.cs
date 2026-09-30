namespace PboSpy.Modules.Audio.Services;

/// <summary>Decoded audio as interleaved floats in the -1..1 range.</summary>
public sealed class AudioData
{
    public AudioData(int sampleRate, int channels, float[] samples)
    {
        SampleRate = sampleRate;
        Channels = Math.Max(1, channels);
        Samples = samples;
    }

    public int SampleRate { get; }

    public int Channels { get; }

    public float[] Samples { get; }

    public int FrameCount => Samples.Length / Channels;

    public TimeSpan Duration => SampleRate > 0 ? TimeSpan.FromSeconds((double)FrameCount / SampleRate) : TimeSpan.Zero;

    /// <summary>Anything format specific worth showing, e.g. the WSS compression.</summary>
    public string SourceDescription { get; init; } = "";

    public short[] ToPcm16()
    {
        var pcm = new short[Samples.Length];
        for (var i = 0; i < pcm.Length; i++)
        {
            pcm[i] = (short)Math.Clamp(Math.Round(Samples[i] * 32767f), short.MinValue, short.MaxValue);
        }
        return pcm;
    }

    public static AudioData FromPcm16(int sampleRate, int channels, short[] pcm, string description = "")
    {
        var samples = new float[pcm.Length];
        for (var i = 0; i < pcm.Length; i++)
        {
            samples[i] = pcm[i] / 32768f;
        }
        return new AudioData(sampleRate, channels, samples) { SourceDescription = description };
    }
}
