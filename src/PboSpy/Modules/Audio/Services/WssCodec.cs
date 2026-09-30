using System.IO;

namespace PboSpy.Modules.Audio.Services;

public enum WssCompression
{
    None = 0,
    Nibble = 4,
    Byte = 8
}

/// <summary>
/// Bohemia's WSS container: a "WSS0" magic, a compression id, a WAVEFORMATEX style header
/// and then either plain 16 bit PCM or one of two delta encodings, interleaved per channel.
/// </summary>
public static class WssCodec
{
    private const int HeaderSize = 26;

    // A delta byte b stands for sign(b) * 2^(|b| * k), rounded; 0 repeats the previous sample.
    private static readonly double ByteStep = Math.Log2(10) / 28.12574042515172;

    // Nibble deltas; the 16th code is not part of the table and is treated as "no change".
    private static readonly short[] NibbleSteps =
    {
        -8192, -4096, -2048, -1024, -512, -256, -64, 0, 64, 256, 512, 1024, 2048, 4096, 8192
    };

    public static bool IsWss(Stream stream)
    {
        var buffer = new byte[4];
        var read = stream.Read(buffer, 0, 4);
        stream.Seek(-read, SeekOrigin.Current);
        return read == 4 && buffer[0] == 'W' && buffer[1] == 'S' && buffer[2] == 'S' && buffer[3] == '0';
    }

    public static AudioData Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        var magic = reader.ReadBytes(4);
        if (magic.Length != 4 || magic[0] != 'W' || magic[1] != 'S' || magic[2] != 'S' || magic[3] != '0')
        {
            throw new InvalidDataException("Not a WSS file (missing WSS0 header).");
        }

        var compression = (WssCompression)(reader.ReadUInt32() & 0xFF);
        reader.ReadUInt16(); // format tag
        int channels = reader.ReadUInt16();
        var sampleRate = (int)reader.ReadUInt32();
        reader.ReadUInt32(); // bytes per second
        reader.ReadUInt16(); // block align
        int bits = reader.ReadUInt16();
        reader.ReadUInt16(); // output size

        using var rest = new MemoryStream();
        stream.CopyTo(rest);
        var data = rest.ToArray();
        channels = Math.Max(1, channels);

        short[] pcm = compression switch
        {
            WssCompression.None => bits == 8 ? Unsigned8(data) : Pcm16(data),
            WssCompression.Byte => DecodeByte(data, channels),
            WssCompression.Nibble => DecodeNibble(data, channels),
            _ => throw new InvalidDataException($"Unknown WSS compression {(int)compression}.")
        };

        var description = compression switch
        {
            WssCompression.Byte => "WSS, byte delta",
            WssCompression.Nibble => "WSS, nibble delta",
            _ => "WSS, uncompressed"
        };
        return AudioData.FromPcm16(sampleRate, channels, pcm, description);
    }

    public static void Write(Stream stream, AudioData audio, WssCompression compression = WssCompression.None)
    {
        var pcm = audio.ToPcm16();
        var channels = audio.Channels;
        byte[] body = compression switch
        {
            WssCompression.Byte => EncodeByte(pcm, channels),
            WssCompression.Nibble => EncodeNibble(pcm, channels),
            _ => ToBytes(pcm)
        };

        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.Write(new[] { (byte)'W', (byte)'S', (byte)'S', (byte)'0' });
        writer.Write((uint)compression);
        writer.Write((ushort)1);
        writer.Write((ushort)channels);
        writer.Write((uint)audio.SampleRate);
        writer.Write((uint)(audio.SampleRate * channels * 2));
        writer.Write((ushort)(channels * 2));
        writer.Write((ushort)16);
        writer.Write((ushort)0);
        writer.Write(body);
    }

    private static short[] Pcm16(byte[] data)
    {
        var pcm = new short[data.Length / 2];
        Buffer.BlockCopy(data, 0, pcm, 0, pcm.Length * 2);
        return pcm;
    }

    private static short[] Unsigned8(byte[] data)
    {
        var pcm = new short[data.Length];
        for (var i = 0; i < data.Length; i++)
        {
            pcm[i] = (short)((data[i] - 128) << 8);
        }
        return pcm;
    }

    private static byte[] ToBytes(short[] pcm)
    {
        var bytes = new byte[pcm.Length * 2];
        Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static int ByteDelta(sbyte code)
    {
        if (code == 0)
        {
            return 0;
        }
        var magnitude = Math.Round(Math.Pow(2, Math.Abs((int)code) * ByteStep));
        return (int)(code < 0 ? -magnitude : magnitude);
    }

    private static short[] DecodeByte(byte[] data, int channels)
    {
        var pcm = new short[data.Length];
        var last = new int[channels];
        for (var i = 0; i < data.Length; i++)
        {
            var channel = i % channels;
            var code = unchecked((sbyte)data[i]);
            if (code != 0)
            {
                last[channel] = Math.Clamp(last[channel] + ByteDelta(code), short.MinValue, short.MaxValue);
            }
            pcm[i] = (short)last[channel];
        }
        return pcm;
    }

    private static byte[] EncodeByte(short[] pcm, int channels)
    {
        var output = new byte[pcm.Length];
        var last = new int[channels];
        for (var i = 0; i < pcm.Length; i++)
        {
            var channel = i % channels;
            var delta = pcm[i] - last[channel];
            sbyte best = 0;
            var bestError = Math.Abs(delta);
            if (delta != 0)
            {
                // Search around the log estimate for the code whose reconstruction lands closest.
                var estimate = (int)Math.Round(Math.Log2(Math.Abs(delta)) / ByteStep);
                for (var c = estimate - 2; c <= estimate + 2; c++)
                {
                    var candidate = (sbyte)Math.Clamp(delta < 0 ? -c : c, -127, 127);
                    if (candidate == 0)
                    {
                        continue;
                    }
                    var reconstructed = Math.Clamp(last[channel] + ByteDelta(candidate), short.MinValue, short.MaxValue);
                    var error = Math.Abs(pcm[i] - reconstructed);
                    if (error < bestError)
                    {
                        bestError = error;
                        best = candidate;
                    }
                }
            }
            output[i] = unchecked((byte)best);
            if (best != 0)
            {
                last[channel] = Math.Clamp(last[channel] + ByteDelta(best), short.MinValue, short.MaxValue);
            }
        }
        return output;
    }

    private static short[] DecodeNibble(byte[] data, int channels)
    {
        // Bytes are interleaved per channel and each byte holds two samples, high nibble first.
        var perChannel = new List<short>[channels];
        for (var c = 0; c < channels; c++)
        {
            perChannel[c] = new List<short>(data.Length * 2 / channels + 2);
        }
        var value = new int[channels];
        for (var i = 0; i < data.Length; i++)
        {
            var channel = i % channels;
            foreach (var nibble in new[] { data[i] >> 4, data[i] & 0x0F })
            {
                if (nibble < NibbleSteps.Length)
                {
                    value[channel] += NibbleSteps[nibble];
                }
                perChannel[channel].Add((short)Math.Clamp(value[channel], short.MinValue, short.MaxValue));
            }
        }
        return Interleave(perChannel);
    }

    private static byte[] EncodeNibble(short[] pcm, int channels)
    {
        var frames = pcm.Length / channels;
        var bytesPerChannel = (frames + 1) / 2;
        var output = new byte[bytesPerChannel * channels];
        for (var c = 0; c < channels; c++)
        {
            var value = 0;
            for (var b = 0; b < bytesPerChannel; b++)
            {
                var packed = 0;
                for (var half = 0; half < 2; half++)
                {
                    var frame = b * 2 + half;
                    var target = frame < frames ? pcm[frame * channels + c] : value;
                    var bestIndex = 7;
                    var bestError = int.MaxValue;
                    for (var k = 0; k < NibbleSteps.Length; k++)
                    {
                        var error = Math.Abs(target - (value + NibbleSteps[k]));
                        if (error < bestError)
                        {
                            bestError = error;
                            bestIndex = k;
                        }
                    }
                    value += NibbleSteps[bestIndex];
                    packed = half == 0 ? bestIndex << 4 : packed | bestIndex;
                }
                output[b * channels + c] = (byte)packed;
            }
        }
        return output;
    }

    private static short[] Interleave(List<short>[] channels)
    {
        var frames = channels.Min(c => c.Count);
        var pcm = new short[frames * channels.Length];
        for (var f = 0; f < frames; f++)
        {
            for (var c = 0; c < channels.Length; c++)
            {
                pcm[f * channels.Length + c] = channels[c][f];
            }
        }
        return pcm;
    }
}
