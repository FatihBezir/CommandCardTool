using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LauncherWinUI.Services;

/// <summary>
/// Reads a C&C Generals / Zero Hour .BIG archive and parses the first .CSF
/// (Command String File) found inside it, returning a case-insensitive
/// label-key → display-text dictionary.
/// </summary>
internal static class BigCsfReader
{
    /// <summary>
    /// Opens <paramref name="bigPath"/>, locates the first .csf entry,
    /// parses it and returns all labels. Returns an empty dictionary on
    /// any error (file missing, wrong format, I/O error, etc.).
    /// </summary>
    public static Dictionary<string, string> ReadFromBig(string bigPath)
    {
        var empty = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(bigPath)) return empty;

            using var fs  = File.OpenRead(bigPath);
            using var br  = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);

            // ── BIG archive header ────────────────────────────────────────
            // Magic: "BIGF" or "BIG4" (4 ASCII bytes)
            string magic = new(br.ReadChars(4));
            if (magic != "BIGF" && magic != "BIG4") return empty;

            br.ReadUInt32();            // archive size  – little-endian, unused
            uint fileCount = ReadBeU32(br);  // file count    – big-endian
            ReadBeU32(br);              // header size   – big-endian, unused

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // ── File entry table ──────────────────────────────────────────
            // Each entry: offset(BE u32) + size(BE u32) + null-terminated name
            for (uint i = 0; i < fileCount; i++)
            {
                uint   offset = ReadBeU32(br);
                uint   size   = ReadBeU32(br);
                string name   = ReadNullStr(br);

                if (!name.EndsWith(".csf", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Found a CSF – read its bytes and merge labels. First entry wins.
                long savedPos = fs.Position;
                fs.Seek(offset, SeekOrigin.Begin);
                byte[] csfData = br.ReadBytes((int)size);
                fs.Seek(savedPos, SeekOrigin.Begin);

                foreach (var kv in ParseCsf(csfData))
                    result.TryAdd(kv.Key, kv.Value);
            }

            return result;
        }
        catch { /* fall back to built-in labels */ }

        return empty;
    }

    // ── BIG helpers ───────────────────────────────────────────────────────

    private static uint ReadBeU32(BinaryReader br)
    {
        byte[] b = br.ReadBytes(4);
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        return BitConverter.ToUInt32(b, 0);
    }

    private static string ReadNullStr(BinaryReader br)
    {
        var sb = new StringBuilder(64);
        byte b;
        while ((b = br.ReadByte()) != 0)
            sb.Append((char)b);
        return sb.ToString();
    }

    // ── CSF parser ────────────────────────────────────────────────────────
    //
    // Format (all integers little-endian unless noted):
    //   Header  : magic=" FSC"(4) | version(u32) | numLabels(u32)
    //             | numStrings(u32) | reserved(u32) | language(u32)
    //   Label   : magic=" LBL"(4) | strCount(u32) | nameLen(u32)
    //             | name(nameLen ASCII bytes)
    //   String  : magic=" RTS" or "STRW"(4) | charLen(u32)
    //             | chars(charLen × u16, each XOR 0xFFFF → UTF-16LE)
    //             [if "STRW": extraLen(u32) | extra(extraLen bytes, ASCII)]

    private static Dictionary<string, string> ParseCsf(byte[] data)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms, Encoding.ASCII, leaveOpen: true);

            // File header
            if (new string(br.ReadChars(4)) != " FSC") return result;
            br.ReadUInt32();                   // version
            uint numLabels = br.ReadUInt32();
            br.ReadUInt32();                   // numStrings (== numLabels usually)
            br.ReadUInt32();                   // reserved
            br.ReadUInt32();                   // language

            for (uint i = 0; i < numLabels; i++)
            {
                if (ms.Position + 12 > ms.Length) break;
                if (new string(br.ReadChars(4)) != " LBL") break;

                uint   strCount = br.ReadUInt32();
                uint   nameLen  = br.ReadUInt32();
                string key      = new(br.ReadChars((int)nameLen));

                string? firstValue = null;

                for (uint j = 0; j < strCount; j++)
                {
                    if (ms.Position + 8 > ms.Length) break;
                    string sMagic = new string(br.ReadChars(4));
                    bool   hasExtra = sMagic == "STRW";

                    uint charLen = br.ReadUInt32();
                    var  chars   = new char[charLen];
                    for (uint k = 0; k < charLen; k++)
                        chars[k] = (char)(br.ReadUInt16() ^ 0xFFFF);

                    if (j == 0) firstValue = new string(chars);

                    if (hasExtra)
                    {
                        uint extraLen = br.ReadUInt32();
                        ms.Seek(extraLen, SeekOrigin.Current);
                    }
                }

                if (firstValue != null && !result.ContainsKey(key))
                    result[key] = firstValue;
            }
        }
        catch { /* return whatever was parsed so far */ }

        return result;
    }
}
