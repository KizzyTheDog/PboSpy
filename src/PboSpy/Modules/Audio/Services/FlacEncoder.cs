using System.IO;
using System.Security.Cryptography;

namespace PboSpy.Modules.Audio.Services;

/// <summary>
/// Small lossless FLAC writer: 16 bit, fixed block size, fixed polynomial predictors and
/// partitioned Rice residuals. Not the tightest encoder around, but the output is standard
/// FLAC that every decoder accepts.
/// </summary>
public static class FlacEncoder
{
    private const int BlockSize = 4096;
    private const int BitsPerSample = 16;

    public static void Write(Stream output, AudioData audio)
    {
        var pcm = audio.ToPcm16();
        var channels = Math.Clamp(audio.Channels, 1, 8);
        var frames = pcm.Length / channels;

        output.Write(new[] { (byte)'f', (byte)'L', (byte)'a', (byte)'C' });
        WriteStreamInfo(output, audio.SampleRate, channels, frames, pcm);

        var block = new int[channels][];
        for (var c = 0; c < channels; c++)
        {
            block[c] = new int[BlockSize];
        }

        var frameNumber = 0;
        for (var start = 0; start < frames; start += BlockSize, frameNumber++)
        {
            var count = Math.Min(BlockSize, frames - start);
            for (var c = 0; c < channels; c++)
            {
                for (var i = 0; i < count; i++)
                {
                    block[c][i] = pcm[(start + i) * channels + c];
                }
            }
            WriteFrame(output, block, channels, count, frameNumber);
        }
    }

    private static void WriteStreamInfo(Stream output, int sampleRate, int channels, int frames, short[] pcm)
    {
        var bits = new BitWriter();
        bits.Write(1, 1);          // last metadata block
        bits.Write(0, 7);          // STREAMINFO
        bits.Write(34, 24);
        bits.Write(BlockSize, 16);
        bits.Write(BlockSize, 16);
        bits.Write(0, 24);
        bits.Write(0, 24);
        bits.Write((ulong)sampleRate, 20);
        bits.Write((ulong)(channels - 1), 3);
        bits.Write(BitsPerSample - 1, 5);
        bits.Write((ulong)frames, 36);

        var bytes = new byte[pcm.Length * 2];
        Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
        foreach (var b in MD5.HashData(bytes))
        {
            bits.Write((int)b, 8);
        }
        var data = bits.ToArray();
        output.Write(data, 0, data.Length);
    }

    private static void WriteFrame(Stream output, int[][] block, int channels, int count, int frameNumber)
    {
        var bits = new BitWriter();
        bits.Write(0x3FFE, 14);
        bits.Write(0, 1);
        bits.Write(0, 1);                              // fixed block size stream
        var sizeCode = count == BlockSize ? 0b1100 : 0b0111;
        bits.Write((ulong)sizeCode, 4);
        bits.Write(0, 4);                              // sample rate from STREAMINFO
        bits.Write((ulong)(channels - 1), 4);          // independent channels
        bits.Write(0b100, 3);                          // 16 bit
        bits.Write(0, 1);
        WriteUtf8Number(bits, frameNumber);
        if (sizeCode == 0b0111)
        {
            bits.Write((ulong)(count - 1), 16);
        }
        bits.Write((int)Crc8(bits.ToArray()), 8);

        for (var c = 0; c < channels; c++)
        {
            WriteSubframe(bits, block[c], count);
        }
        bits.AlignToByte();

        var frame = bits.ToArray();
        var crc = Crc16(frame);
        output.Write(frame, 0, frame.Length);
        output.WriteByte((byte)(crc >> 8));
        output.WriteByte((byte)crc);
    }

    private static void WriteSubframe(BitWriter bits, int[] samples, int count)
    {
        var constant = true;
        for (var i = 1; i < count && constant; i++)
        {
            constant = samples[i] == samples[0];
        }
        if (constant)
        {
            bits.Write(0, 1);
            bits.Write(0b000000, 6);
            bits.Write(0, 1);
            bits.WriteSigned(samples[0], BitsPerSample);
            return;
        }

        var bestOrder = 0;
        long bestCost = long.MaxValue;
        int[] bestResidual = null;
        RicePlan bestPlan = null;
        for (var order = 0; order <= 4 && order < count; order++)
        {
            var residual = Residual(samples, count, order);
            var plan = PlanRice(residual, count, order);
            var cost = plan.Bits + order * BitsPerSample;
            if (cost < bestCost)
            {
                bestCost = cost;
                bestOrder = order;
                bestResidual = residual;
                bestPlan = plan;
            }
        }

        if (bestPlan == null || bestCost >= (long)count * BitsPerSample)
        {
            bits.Write(0, 1);
            bits.Write(0b000001, 6);
            bits.Write(0, 1);
            for (var i = 0; i < count; i++)
            {
                bits.WriteSigned(samples[i], BitsPerSample);
            }
            return;
        }

        bits.Write(0, 1);
        bits.Write((ulong)(0b001000 | bestOrder), 6);
        bits.Write(0, 1);
        for (var i = 0; i < bestOrder; i++)
        {
            bits.WriteSigned(samples[i], BitsPerSample);
        }
        WriteResidual(bits, bestResidual, bestPlan);
    }

    private static int[] Residual(int[] x, int count, int order)
    {
        var r = new int[count - order];
        for (var n = order; n < count; n++)
        {
            r[n - order] = order switch
            {
                0 => x[n],
                1 => x[n] - x[n - 1],
                2 => x[n] - 2 * x[n - 1] + x[n - 2],
                3 => x[n] - 3 * x[n - 1] + 3 * x[n - 2] - x[n - 3],
                _ => x[n] - 4 * x[n - 1] + 6 * x[n - 2] - 4 * x[n - 3] + x[n - 4]
            };
        }
        return r;
    }

    private sealed class RicePlan
    {
        public int PartitionOrder;
        public int[] Parameters;
        public int[] EscapeBits;
        public long Bits;
        public int Order;
        public int BlockCount;
    }

    private static RicePlan PlanRice(int[] residual, int count, int order)
    {
        RicePlan best = null;
        for (var k = 0; k <= 8; k++)
        {
            var partitions = 1 << k;
            if (count % partitions != 0 || (count >> k) <= order)
            {
                break;
            }

            var plan = new RicePlan
            {
                PartitionOrder = k,
                Parameters = new int[partitions],
                EscapeBits = new int[partitions],
                Bits = 2 + 4,
                Order = order,
                BlockCount = count
            };
            var index = 0;
            for (var p = 0; p < partitions; p++)
            {
                var n = (count >> k) - (p == 0 ? order : 0);
                var (param, cost, escape) = BestParameter(residual, index, n);
                plan.Parameters[p] = param;
                plan.EscapeBits[p] = escape;
                plan.Bits += 4 + cost;
                index += n;
            }
            if (best == null || plan.Bits < best.Bits)
            {
                best = plan;
            }
        }
        return best;
    }

    private static (int Param, long Cost, int EscapeBits) BestParameter(int[] residual, int start, int n)
    {
        long sum = 0;
        var maxBits = 0;
        for (var i = start; i < start + n; i++)
        {
            var u = ZigZag(residual[i]);
            sum += (long)u;
            maxBits = Math.Max(maxBits, SignedBits(residual[i]));
        }

        var mean = n > 0 ? sum / n : 0;
        var guess = mean > 0 ? (int)Math.Floor(Math.Log2(mean)) : 0;

        var bestParam = -1;
        var bestCost = long.MaxValue;
        for (var param = Math.Max(0, guess - 1); param <= Math.Min(14, guess + 2); param++)
        {
            long cost = 0;
            for (var i = start; i < start + n; i++)
            {
                cost += (long)(ZigZag(residual[i]) >> param) + 1 + param;
            }
            if (cost < bestCost)
            {
                bestCost = cost;
                bestParam = param;
            }
        }

        var escapeCost = 5L + (long)n * maxBits;
        if (bestParam < 0 || escapeCost < bestCost)
        {
            return (15, escapeCost, maxBits);
        }
        return (bestParam, bestCost, 0);
    }

    private static void WriteResidual(BitWriter bits, int[] residual, RicePlan plan)
    {
        bits.Write(0b00, 2);
        bits.Write(plan.PartitionOrder, 4);
        var partitions = 1 << plan.PartitionOrder;
        var index = 0;
        for (var p = 0; p < partitions; p++)
        {
            var n = (plan.BlockCount >> plan.PartitionOrder) - (p == 0 ? plan.Order : 0);
            var param = plan.Parameters[p];
            bits.Write(param, 4);
            if (param == 15)
            {
                var width = plan.EscapeBits[p];
                bits.Write(width, 5);
                if (width > 0)
                {
                    for (var i = index; i < index + n; i++)
                    {
                        bits.WriteSigned(residual[i], width);
                    }
                }
            }
            else
            {
                for (var i = index; i < index + n; i++)
                {
                    var u = ZigZag(residual[i]);
                    bits.WriteUnary(u >> param);
                    if (param > 0)
                    {
                        bits.Write(u & ((1UL << param) - 1), param);
                    }
                }
            }
            index += n;
        }
    }

    private static ulong ZigZag(int value) => value >= 0 ? (ulong)value << 1 : ((ulong)(-(long)value) << 1) - 1;

    private static int SignedBits(int value)
    {
        var v = value < 0 ? ~value : value;
        var bits = 1;
        while (v > 0)
        {
            bits++;
            v >>= 1;
        }
        return bits;
    }

    private static void WriteUtf8Number(BitWriter bits, int value)
    {
        var v = (uint)value;
        if (v < 0x80)
        {
            bits.Write((ulong)v, 8);
            return;
        }
        int extra = v < 0x800 ? 1 : v < 0x10000 ? 2 : v < 0x200000 ? 3 : v < 0x4000000 ? 4 : 5;
        var lead = (0xFF00 >> (extra + 1)) & 0xFF;
        bits.Write((ulong)(uint)(lead | (int)(v >> (6 * extra))), 8);
        for (var i = extra - 1; i >= 0; i--)
        {
            bits.Write((ulong)(0x80 | ((v >> (6 * i)) & 0x3F)), 8);
        }
    }

    private static byte Crc8(byte[] data)
    {
        byte crc = 0;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (byte)((crc & 0x80) != 0 ? (crc << 1) ^ 0x07 : crc << 1);
            }
        }
        return crc;
    }

    private static ushort Crc16(byte[] data)
    {
        ushort crc = 0;
        foreach (var b in data)
        {
            crc ^= (ushort)(b << 8);
            for (var i = 0; i < 8; i++)
            {
                crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x8005 : crc << 1);
            }
        }
        return crc;
    }

    private sealed class BitWriter
    {
        private readonly List<byte> _bytes = new();
        private ulong _accumulator;
        private int _count;

        public void Write(ulong value, int bits)
        {
            for (var i = bits - 1; i >= 0; i--)
            {
                _accumulator = (_accumulator << 1) | ((value >> i) & 1);
                if (++_count == 8)
                {
                    _bytes.Add((byte)_accumulator);
                    _accumulator = 0;
                    _count = 0;
                }
            }
        }

        public void Write(int value, int bits) => Write((ulong)(uint)value, bits);

        public void WriteSigned(int value, int bits) => Write((ulong)value & (bits >= 64 ? ulong.MaxValue : (1UL << bits) - 1), bits);

        public void WriteUnary(ulong zeros)
        {
            for (ulong i = 0; i < zeros; i++)
            {
                Write(0UL, 1);
            }
            Write(1UL, 1);
        }

        public void AlignToByte()
        {
            if (_count > 0)
            {
                Write(0UL, 8 - _count);
            }
        }

        public byte[] ToArray()
        {
            var result = new List<byte>(_bytes);
            if (_count > 0)
            {
                result.Add((byte)(_accumulator << (8 - _count)));
            }
            return result.ToArray();
        }
    }
}
