using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LauncherWinUI.Services;

/// <summary>
/// CSF parse/write matching web <c>lib/csf-utils.ts</c> (flexible LBL tags, STR/STRW/RTS/WRTS).
/// </summary>
internal static class CsfCodec
{
    internal sealed class CsfStringRec
    {
        public byte[] Tag4 { get; }
        public string Text { get; set; }
        public string? Extra { get; }

        public CsfStringRec(byte[] tag4, string text, string? extra)
        {
            Tag4 = tag4;
            Text = text;
            Extra = extra;
        }

        public bool HasExtra => Tag4.SequenceEqual(StrwTag) || Tag4.SequenceEqual(WrtsTag);
    }

    internal sealed class CsfEntry
    {
        public string Key { get; }
        public List<CsfStringRec> Strings { get; }

        public CsfEntry(string key, List<CsfStringRec> strings)
        {
            Key = key;
            Strings = strings;
        }
    }

    private static readonly byte[] LblTag = { 0x20, (byte)'L', (byte)'B', (byte)'L' };
    private static readonly byte[] RtsTag = { 0x20, (byte)'R', (byte)'T', (byte)'S' };
    private static readonly byte[] StrTag = { 0x20, (byte)'S', (byte)'T', (byte)'R' };
    private static readonly byte[] StrwTag = { (byte)'S', (byte)'T', (byte)'R', (byte)'W' };
    private static readonly byte[] WrtsTag = { (byte)'W', (byte)'R', (byte)'T', (byte)'S' };

    public static List<CsfEntry> ParseAll(byte[] data)
    {
        var entries = new List<CsfEntry>();
        try
        {
            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms, Encoding.ASCII, leaveOpen: true);

            if (new string(br.ReadChars(4)) != " FSC") return entries;

            br.ReadUInt32(); // version
            uint numLabels = br.ReadUInt32();
            br.ReadUInt32(); // numStrings
            br.ReadUInt32(); // reserved
            br.ReadUInt32(); // language

            for (uint i = 0; i < numLabels; i++)
            {
                if (ms.Position + 12 > ms.Length) break;

                byte[] lblBytes = br.ReadBytes(4);
                if (!IsLblTag(lblBytes)) break;

                uint strCount = br.ReadUInt32();
                uint nameLen  = br.ReadUInt32();
                if (ms.Position + nameLen > ms.Length) break;
                string key = new(br.ReadChars((int)nameLen));

                var strings = new List<CsfStringRec>();
                for (uint j = 0; j < strCount; j++)
                {
                    if (ms.Position + 8 > ms.Length) break;
                    byte[] tag = br.ReadBytes(4);
                    if (!IsValidStrTag(tag)) break;

                    uint charLen = br.ReadUInt32();
                    if (ms.Position + charLen * 2 > ms.Length) break;

                    var chars = new char[charLen];
                    for (uint k = 0; k < charLen; k++)
                        chars[k] = (char)(br.ReadUInt16() ^ 0xFFFF);

                    string? extra = null;
                    if (HasExtra(tag))
                    {
                        if (ms.Position + 4 > ms.Length) break;
                        uint extraLen = br.ReadUInt32();
                        if (ms.Position + extraLen > ms.Length) break;
                        extra = Encoding.ASCII.GetString(br.ReadBytes((int)extraLen));
                    }

                    strings.Add(new CsfStringRec(tag, new string(chars), extra));
                }

                entries.Add(new CsfEntry(key, strings));
            }
        }
        catch { /* return partial */ }

        return entries;
    }

    public static byte[] ApplyOverrides(byte[] csfData, IReadOnlyDictionary<string, string> overrides)
    {
        if (overrides.Count == 0) return csfData;

        using var ms = new MemoryStream(csfData);
        using var br = new BinaryReader(ms, Encoding.ASCII, leaveOpen: true);
        if (new string(br.ReadChars(4)) != " FSC") return csfData;

        uint version  = br.ReadUInt32();
        br.ReadUInt32();
        br.ReadUInt32();
        uint reserved = br.ReadUInt32();
        uint language = br.ReadUInt32();

        var entries = ParseAll(csfData);
        var applied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            if (!TryFindOverride(overrides, entry.Key, out var newText)) continue;
            MarkApplied(overrides, entry.Key, applied);
            if (entry.Strings.Count > 0)
                entry.Strings[0].Text = newText!;
        }

        foreach (var kv in overrides)
        {
            if (applied.Contains(kv.Key)) continue;
            if (string.IsNullOrWhiteSpace(kv.Value)) continue;
            if (entries.Exists(e => KeyMatches(kv.Key, e.Key))) continue;

            entries.Add(new CsfEntry(
                CsfVariantKeys.ToNewCsfKey(kv.Key),
                new List<CsfStringRec> { new CsfStringRec(RtsTag, kv.Value, null) }));
        }

        return WriteAll(version, reserved, language, entries);
    }

    public static byte[] WriteAll(uint version, uint reserved, uint language, List<CsfEntry> entries)
    {
        uint totalStrings = 0;
        foreach (var e in entries)
            totalStrings += (uint)e.Strings.Count;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

        bw.Write(Encoding.ASCII.GetBytes(" FSC"));
        bw.Write(version);
        bw.Write((uint)entries.Count);
        bw.Write(totalStrings);
        bw.Write(reserved);
        bw.Write(language);

        foreach (var entry in entries)
        {
            bw.Write(LblTag);
            bw.Write((uint)entry.Strings.Count);
            byte[] nameBytes = Encoding.ASCII.GetBytes(entry.Key);
            bw.Write((uint)nameBytes.Length);
            bw.Write(nameBytes);

            foreach (var s in entry.Strings)
            {
                bw.Write(s.Tag4);
                char[] chars = s.Text.ToCharArray();
                bw.Write((uint)chars.Length);
                foreach (char c in chars)
                    bw.Write((ushort)(c ^ 0xFFFF));

                if (s.HasExtra && s.Extra != null)
                {
                    byte[] extraBytes = Encoding.ASCII.GetBytes(s.Extra);
                    bw.Write((uint)extraBytes.Length);
                    bw.Write(extraBytes);
                }
            }
        }

        return ms.ToArray();
    }

    private static bool IsLblTag(byte[] four)
    {
        int n = 0;
        for (int i = 0; i < 4; i++)
        {
            if (four[i] == 0x20) continue;
            if (n == 0 && four[i] == (byte)'L') { n = 1; continue; }
            if (n == 1 && four[i] == (byte)'B') { n = 2; continue; }
            if (n == 2 && four[i] == (byte)'L') { n = 3; continue; }
            return false;
        }
        return n == 3;
    }

    private static bool IsValidStrTag(byte[] tag)
        => tag.SequenceEqual(StrTag)
        || tag.SequenceEqual(StrwTag)
        || tag.SequenceEqual(RtsTag)
        || tag.SequenceEqual(WrtsTag);

    private static bool HasExtra(byte[] tag)
        => tag.SequenceEqual(StrwTag) || tag.SequenceEqual(WrtsTag);

    private static bool TryFindOverride(IReadOnlyDictionary<string, string> overrides,
                                          string csfKey, out string? value)
    {
        if (overrides.TryGetValue(csfKey, out value)) return true;
        string bare = BareKey(csfKey);
        foreach (var kv in overrides)
        {
            if (string.Equals(BareKey(kv.Key), bare, StringComparison.OrdinalIgnoreCase))
            {
                value = kv.Value;
                return true;
            }
        }
        value = null;
        return false;
    }

    private static void MarkApplied(IReadOnlyDictionary<string, string> overrides, string csfKey,
                                    HashSet<string> applied)
    {
        applied.Add(csfKey);
        string bare = BareKey(csfKey);
        foreach (var kv in overrides)
        {
            if (string.Equals(kv.Key, csfKey, StringComparison.OrdinalIgnoreCase)
             || string.Equals(BareKey(kv.Key), bare, StringComparison.OrdinalIgnoreCase))
                applied.Add(kv.Key);
        }
    }

    private static bool KeyMatches(string overrideKey, string csfKey)
        => string.Equals(overrideKey, csfKey, StringComparison.OrdinalIgnoreCase)
        || string.Equals(BareKey(overrideKey), BareKey(csfKey), StringComparison.OrdinalIgnoreCase);

    private static string BareKey(string k)
    {
        int ci = k.IndexOf(':');
        return ci >= 0 ? k[(ci + 1)..] : k;
    }
}
