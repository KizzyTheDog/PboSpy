using System.IO;
using System.Text;

namespace PboSpy.Modules.PreviewBinary.Utils;

/// <summary>texHeaders.bin ("0DHT"): the game's index of every texture in a PBO, written by the binarizer.</summary>
internal static class TexHeaders
{
    private static readonly string[] Formats = { "P8", "AI88", "RGB565", "ARGB1555", "ARGB4444", "ARGB8888", "DXT1", "DXT2", "DXT3", "DXT4", "DXT5" };

    public static bool Is(Stream stream)
    {
        var magic = new byte[4];
        var read = stream.Read(magic, 0, 4);
        stream.Position = 0;
        return read == 4 && Encoding.ASCII.GetString(magic) == "0DHT";
    }

    public static string ToText(Stream stream)
    {
        using var input = new BinaryReader(stream, Encoding.ASCII, true);
        input.ReadBytes(4);
        var version = input.ReadInt32();
        var count = input.ReadInt32();
        var text = new StringBuilder();
        text.AppendLine($"// texHeaders.bin, version {version}, {count} textures");
        text.AppendLine("// size, format, mipmaps, alpha (A = has alpha, T = see-through), average colour (RGBA), .paa size, path");
        text.AppendLine();
        for (var i = 0; i < count; i++)
        {
            input.ReadBytes(8);
            input.ReadBytes(16);
            var average = input.ReadBytes(4);
            input.ReadBytes(4 + 4 + 4);
            input.ReadByte();
            var alpha = input.ReadByte() != 0;
            var transparent = input.ReadByte() != 0;
            input.ReadByte();
            input.ReadInt32();
            var format = input.ReadInt32();
            input.ReadBytes(2);
            var path = ReadAsciiz(input);
            input.ReadInt32();
            var mipmaps = input.ReadInt32();
            int width = 0, height = 0;
            for (var m = 0; m < mipmaps; m++)
            {
                var w = input.ReadUInt16();
                var h = input.ReadUInt16();
                input.ReadBytes(8);
                if (m == 0)
                {
                    width = w;
                    height = h;
                }
            }
            var size = input.ReadInt32();
            var name = format >= 0 && format < Formats.Length ? Formats[format] : "#" + format;
            var flags = (alpha ? "A" : "-") + (transparent ? "T" : "-");
            text.AppendLine($"{width,5} x {height,-5} {name,-9} {mipmaps,2}  {flags}  #{average[2]:X2}{average[1]:X2}{average[0]:X2}{average[3]:X2}  {size,9:N0}  {path}");
        }
        return text.ToString();
    }

    private static string ReadAsciiz(BinaryReader input)
    {
        var bytes = new List<byte>();
        for (var b = input.ReadByte(); b != 0; b = input.ReadByte())
        {
            bytes.Add(b);
        }
        return PboSpy.Modules.Deobfuscate.Core.NameRecovery.FixEncoding(Encoding.Latin1.GetString(bytes.ToArray()));
    }
}
