using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LauncherWinUI.Services;

/// <summary>Reads C&amp;C Generals / Zero Hour .BIG file tables.</summary>
internal static class BigArchiveIndex
{
    public static List<(string Name, int Offset, int Size)> ReadEntries(string bigPath)
    {
        var list = new List<(string, int, int)>();
        if (!File.Exists(bigPath)) return list;

        using var fs = File.OpenRead(bigPath);
        using var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);

        var magic = new string(br.ReadChars(4));
        if (magic != "BIGF" && magic != "BIG4") return list;

        br.ReadUInt32();
        int count = (int)ReadBeU32(br);
        ReadBeU32(br);

        for (int i = 0; i < count; i++)
        {
            int offset = (int)ReadBeU32(br);
            int size   = (int)ReadBeU32(br);
            string name = ReadNullStr(br);
            list.Add((name, offset, size));
        }

        return list;
    }

    public static byte[]? ReadEntryBytes(string bigPath, int offset, int size)
    {
        try
        {
            using var fs = File.OpenRead(bigPath);
            fs.Seek(offset, SeekOrigin.Begin);
            var buf = new byte[size];
            int read = 0;
            while (read < buf.Length)
            {
                int n = fs.Read(buf, read, buf.Length - read);
                if (n == 0) break;
                read += n;
            }
            return buf;
        }
        catch
        {
            return null;
        }
    }

    private static uint ReadBeU32(BinaryReader br)
    {
        byte[] b = br.ReadBytes(4);
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        return BitConverter.ToUInt32(b, 0);
    }

    private static string ReadNullStr(BinaryReader br)
    {
        var sb = new StringBuilder();
        byte b;
        while ((b = br.ReadByte()) != 0) sb.Append((char)b);
        return sb.ToString();
    }
}
