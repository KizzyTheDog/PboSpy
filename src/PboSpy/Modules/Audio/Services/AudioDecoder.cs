using NAudio.Wave;
using NVorbis;
using System.IO;

namespace PboSpy.Modules.Audio.Services;

public static class AudioDecoder
{
    public static readonly string[] SupportedExtensions = { ".wss", ".ogg", ".wav", ".mp3", ".flac", ".m4a", ".aac", ".wma" };

    public static bool IsAudio(string extension) => SupportedExtensions.Contains(extension?.ToLowerInvariant());

    public static AudioData Decode(Stream source, string extension)
    {
        // PBO entry streams are not always seekable, and every decoder here wants to seek.
        using var stream = new MemoryStream();
        source.CopyTo(stream);
        stream.Position = 0;

        var kind = Sniff(stream) ?? extension?.ToLowerInvariant();
        switch (kind)
        {
            case ".wss":
                return WssCodec.Read(stream);
            case ".ogg":
                return DecodeOgg(stream);
            case ".wav":
                using (var wav = new WaveFileReader(stream))
                {
                    return ReadAll(wav.ToSampleProvider(), "WAV " + wav.WaveFormat.Encoding);
                }
            case ".mp3":
                using (var mp3 = new Mp3FileReader(stream))
                {
                    return ReadAll(mp3.ToSampleProvider(), "MP3");
                }
            default:
                return DecodeWithMediaFoundation(stream, extension);
        }
    }

    private static string Sniff(Stream stream)
    {
        var header = new byte[12];
        var read = stream.Read(header, 0, header.Length);
        stream.Position = 0;
        if (read < 4)
        {
            return null;
        }
        if (header[0] == 'W' && header[1] == 'S' && header[2] == 'S' && header[3] == '0')
        {
            return ".wss";
        }
        if (header[0] == 'O' && header[1] == 'g' && header[2] == 'g' && header[3] == 'S')
        {
            return ".ogg";
        }
        if (header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F' && read >= 12 &&
            header[8] == 'W' && header[9] == 'A' && header[10] == 'V' && header[11] == 'E')
        {
            return ".wav";
        }
        if ((header[0] == 'I' && header[1] == 'D' && header[2] == '3') || (header[0] == 0xFF && (header[1] & 0xE0) == 0xE0))
        {
            return ".mp3";
        }
        if (header[0] == 'f' && header[1] == 'L' && header[2] == 'a' && header[3] == 'C')
        {
            return ".flac";
        }
        return null;
    }

    private static AudioData DecodeOgg(Stream stream)
    {
        using var vorbis = new VorbisReader(stream, closeOnDispose: false);
        var channels = vorbis.Channels;
        var samples = new List<float>((int)Math.Min(int.MaxValue / 2, Math.Max(0, vorbis.TotalSamples) * channels));
        var buffer = new float[channels * 4096];
        int read;
        while ((read = vorbis.ReadSamples(buffer, 0, buffer.Length)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                samples.Add(buffer[i]);
            }
        }
        return new AudioData(vorbis.SampleRate, channels, samples.ToArray()) { SourceDescription = "Ogg Vorbis" };
    }

    private static AudioData DecodeWithMediaFoundation(Stream stream, string extension)
    {
        var temp = Path.Combine(Path.GetTempPath(), "PboSpy", "decode_" + Guid.NewGuid().ToString("N") + (extension ?? ".bin"));
        Directory.CreateDirectory(Path.GetDirectoryName(temp));
        try
        {
            using (var file = File.Create(temp))
            {
                stream.CopyTo(file);
            }
            using var reader = new MediaFoundationReader(temp);
            return ReadAll(reader.ToSampleProvider(), extension?.TrimStart('.').ToUpperInvariant());
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
            }
        }
    }

    private static AudioData ReadAll(ISampleProvider provider, string description)
    {
        var format = provider.WaveFormat;
        var samples = new List<float>();
        var buffer = new float[format.Channels * 4096];
        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                samples.Add(buffer[i]);
            }
        }
        return new AudioData(format.SampleRate, format.Channels, samples.ToArray()) { SourceDescription = description };
    }
}
