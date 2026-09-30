using NAudio.MediaFoundation;
using NAudio.Wave;
using OggVorbisEncoder;
using PboSpy.Modules.BulkExport.Models;
using System.Diagnostics;
using System.IO;

namespace PboSpy.Modules.Audio.Services;

public class AudioEncodeSettings
{
    public AudioOutputFormat Format { get; set; } = AudioOutputFormat.Wav;
    public int Bitrate { get; set; } = 192;
    public int OggQuality { get; set; } = 6;
    public int SampleRate { get; set; }
    public bool Mono { get; set; }
    public bool Normalize { get; set; }
    public string FfmpegPath { get; set; }
}

public static class AudioEncoder
{
    private static bool _mediaFoundationStarted;
    private static readonly object MfLock = new();

    public static void Encode(AudioData source, AudioEncodeSettings settings, Stream output)
    {
        var audio = AudioProcessing.Process(source, settings.SampleRate, settings.Mono, settings.Normalize);
        switch (settings.Format)
        {
            case AudioOutputFormat.Wav:
                WriteWav(output, audio);
                break;
            case AudioOutputFormat.Ogg:
                WriteOgg(output, audio, settings.OggQuality);
                break;
            case AudioOutputFormat.Flac:
                FlacEncoder.Write(output, audio);
                break;
            case AudioOutputFormat.Mp3:
                WriteMediaFoundation(output, EnsureRate(audio, new[] { 32000, 44100, 48000 }), settings, mp3: true);
                break;
            case AudioOutputFormat.M4a:
                WriteMediaFoundation(output, EnsureRate(audio, new[] { 44100, 48000 }), settings, mp3: false);
                break;
            case AudioOutputFormat.Opus:
                WriteWithFfmpeg(output, audio, settings, "libopus", ".opus");
                break;
            case AudioOutputFormat.Wss:
                WssCodec.Write(output, audio, WssCompression.None);
                break;
            default:
                throw new NotSupportedException(settings.Format.ToString());
        }
    }

    public static void WriteWav(Stream output, AudioData audio)
    {
        var pcm = audio.ToPcm16();
        var bytes = new byte[pcm.Length * 2];
        Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
        using var writer = new BinaryWriter(output, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF".ToCharArray());
        writer.Write(36 + bytes.Length);
        writer.Write("WAVE".ToCharArray());
        writer.Write("fmt ".ToCharArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)audio.Channels);
        writer.Write(audio.SampleRate);
        writer.Write(audio.SampleRate * audio.Channels * 2);
        writer.Write((short)(audio.Channels * 2));
        writer.Write((short)16);
        writer.Write("data".ToCharArray());
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static AudioData EnsureRate(AudioData audio, int[] allowed)
    {
        if (allowed.Contains(audio.SampleRate))
        {
            return audio;
        }
        var target = allowed.FirstOrDefault(r => r >= audio.SampleRate);
        return AudioProcessing.Resample(audio, target == 0 ? allowed.Last() : target);
    }

    private static void WriteOgg(Stream output, AudioData audio, int quality)
    {
        var channels = audio.Channels;
        var info = VorbisInfo.InitVariableBitRate(channels, audio.SampleRate, Math.Clamp(quality, 0, 10) / 10f);
        var ogg = new OggStream(new Random().Next());

        var comments = new Comments();
        comments.AddTag("ENCODER", "PboSpy");

        ogg.PacketIn(HeaderPacketBuilder.BuildInfoPacket(info));
        ogg.PacketIn(HeaderPacketBuilder.BuildCommentsPacket(comments));
        ogg.PacketIn(HeaderPacketBuilder.BuildBooksPacket(info));
        FlushPages(ogg, output, true);

        var state = ProcessingState.Create(info);
        const int chunk = 1024;
        var frames = audio.FrameCount;
        var buffer = new float[channels][];
        for (var c = 0; c < channels; c++)
        {
            buffer[c] = new float[chunk];
        }

        for (var start = 0; start < frames; start += chunk)
        {
            var count = Math.Min(chunk, frames - start);
            for (var i = 0; i < count; i++)
            {
                for (var c = 0; c < channels; c++)
                {
                    buffer[c][i] = audio.Samples[(start + i) * channels + c];
                }
            }
            state.WriteData(buffer, count);
            Drain(state, ogg, output);
        }
        // The encoder holds back its last block; a block of silence pushes the real tail out.
        for (var c = 0; c < channels; c++)
        {
            Array.Clear(buffer[c], 0, chunk);
        }
        state.WriteData(buffer, chunk);
        Drain(state, ogg, output);
        state.WriteEndOfStream();
        Drain(state, ogg, output);
        FlushPages(ogg, output, true);
    }

    private static void Drain(ProcessingState state, OggStream ogg, Stream output)
    {
        while (!ogg.Finished && state.PacketOut(out var packet))
        {
            ogg.PacketIn(packet);
            FlushPages(ogg, output, false);
        }
    }

    private static void FlushPages(OggStream ogg, Stream output, bool force)
    {
        while (ogg.PageOut(out var page, force))
        {
            output.Write(page.Header, 0, page.Header.Length);
            output.Write(page.Body, 0, page.Body.Length);
        }
    }

    private static void WriteMediaFoundation(Stream output, AudioData audio, AudioEncodeSettings settings, bool mp3)
    {
        lock (MfLock)
        {
            if (!_mediaFoundationStarted)
            {
                MediaFoundationApi.Startup();
                _mediaFoundationStarted = true;
            }
        }

        var temp = TempFile(mp3 ? ".mp3" : ".m4a");
        try
        {
            var pcm = audio.ToPcm16();
            var bytes = new byte[pcm.Length * 2];
            Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
            using (var raw = new RawSourceWaveStream(new MemoryStream(bytes), new WaveFormat(audio.SampleRate, 16, audio.Channels)))
            {
                if (mp3)
                {
                    MediaFoundationEncoder.EncodeToMp3(raw, temp, Nearest(settings.Bitrate, new[] { 96, 128, 160, 192, 224, 256, 320 }) * 1000);
                }
                else
                {
                    MediaFoundationEncoder.EncodeToAac(raw, temp, Nearest(settings.Bitrate, new[] { 96, 128, 160, 192 }) * 1000);
                }
            }
            using var file = File.OpenRead(temp);
            file.CopyTo(output);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static void WriteWithFfmpeg(Stream output, AudioData audio, AudioEncodeSettings settings, string codec, string extension)
    {
        var ffmpeg = ResolveFfmpeg(settings.FfmpegPath)
            ?? throw new InvalidOperationException("This format needs ffmpeg. Set its location under Tools > Options > PboSpy.");

        var input = TempFile(".wav");
        var result = TempFile(extension);
        try
        {
            using (var wav = File.Create(input))
            {
                WriteWav(wav, audio);
            }
            var info = new ProcessStartInfo(ffmpeg, $"-y -loglevel error -i \"{input}\" -c:a {codec} -b:a {settings.Bitrate}k \"{result}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true
            };
            using var process = Process.Start(info);
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0 || !File.Exists(result))
            {
                throw new InvalidOperationException("ffmpeg failed: " + error.Trim());
            }
            using var file = File.OpenRead(result);
            file.CopyTo(output);
        }
        finally
        {
            TryDelete(input);
            TryDelete(result);
        }
    }

    public static string ResolveFfmpeg(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
            }
        }
        return null;
    }

    private static int Nearest(int value, int[] options) => options.OrderBy(o => Math.Abs(o - value)).First();

    private static string TempFile(string extension)
    {
        var dir = Path.Combine(Path.GetTempPath(), "PboSpy");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "audio_" + Guid.NewGuid().ToString("N") + extension);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }
}
