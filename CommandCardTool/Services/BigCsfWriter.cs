using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LauncherWinUI.Services;

/// <summary>
/// Patches CSF labels and writes the complete modified CSF (all original labels +
/// any overrides) into a "!"-prefixed BIG file — identical to what the web site
/// generates when you download a modified EnglishZH.big.
/// </summary>
internal static class BigCsfWriter
{
    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the "!" prefixed output path next to sourceBigPath.
    /// If the source name already starts with "!" it is returned unchanged.
    /// </summary>
    public static string GetOutputPath(string sourceBigPath)
    {
        string dir  = Path.GetDirectoryName(sourceBigPath) ?? "";
        string name = Path.GetFileName(sourceBigPath);
        if (!name.StartsWith("!", StringComparison.Ordinal))
            name = "!" + name;
        return Path.Combine(dir, name);
    }

    /// <summary>
    /// Applies ALL label overrides to the ORIGINAL sourceBigPath CSF and writes the
    /// complete result to the "!" override file.  This guarantees the override always
    /// contains every label (identical to the web-site download), not just the changed ones.
    ///
    /// Pass the entire in-memory label dictionary so every accumulated edit is included.
    /// Returns the output path on success, or null on failure.
    /// </summary>
    /// <summary>
    /// Applies ALL label overrides + optional TGA patches to the ORIGINAL EnglishZH.big
    /// and writes the complete result to the "!" override file.
    /// tgaPatches: entryName → new raw bytes (already painted with hotkey letters).
    /// </summary>
    public static string? RebuildAll(string sourceBigPath,
                                     IReadOnlyDictionary<string, string> allOverrides,
                                     IReadOnlyDictionary<string, byte[]>? tgaPatches = null)
    {
        if (!File.Exists(sourceBigPath)) return null;
        try
        {
            byte[] originalBigData = File.ReadAllBytes(sourceBigPath);

            // 1. Extract the CSF from the ORIGINAL big.
            var entries = ExtractAllEntries(originalBigData);
            if (entries == null) return null;

            int csfIdx = entries.FindIndex(e =>
                e.Name.EndsWith(".csf", StringComparison.OrdinalIgnoreCase));
            if (csfIdx < 0) return null;

            // 2. Apply ALL label overrides to the full CSF.
            byte[]? patchedCsf = ApplyAllOverrides(entries[csfIdx].Data, allOverrides);
            if (patchedCsf == null) return null;

            // 3. Build entry list: CSF first, then preserve any existing override
            // assets in !EnglishZH.big. Label-only saves must not drop painted
            // TGA/DDS entries that are already higher in the game's BIG order.
            string outputPath = GetOutputPath(sourceBigPath);
            var names = new List<string>();
            var bodies = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

            void UpsertEntry(string name, byte[] data)
            {
                if (!bodies.ContainsKey(name))
                    names.Add(name);
                bodies[name] = data;
            }

            UpsertEntry(entries[csfIdx].Name, patchedCsf);

            if (File.Exists(outputPath))
            {
                var existingEntries = ExtractAllEntries(File.ReadAllBytes(outputPath));
                if (existingEntries != null)
                {
                    foreach (var existing in existingEntries)
                    {
                        if (existing.Name.EndsWith(".csf", StringComparison.OrdinalIgnoreCase))
                            continue;
                        UpsertEntry(existing.Name, existing.Data);
                    }
                }
            }

            if (tgaPatches != null)
            {
                foreach (var kv in tgaPatches)
                    UpsertEntry(kv.Key, kv.Value);
            }

            var blobs = names.ConvertAll(name => bodies[name]);
            byte[] outBig = BuildMultiEntryBig(names, blobs);

            File.WriteAllBytes(outputPath, outBig);
            return outputPath;
        }
        catch { return null; }
    }

    // ── BIG helpers ────────────────────────────────────────────────────────────

    private static List<(string Name, byte[] Data)>? ExtractAllEntries(byte[] bigData)
    {
        try
        {
            using var ms = new MemoryStream(bigData);
            using var br = new BinaryReader(ms, Encoding.ASCII, leaveOpen: true);

            string magic = new(br.ReadChars(4));
            if (magic != "BIGF" && magic != "BIG4") return null;

            br.ReadUInt32();                  // archive size (LE)
            uint fileCount = ReadBeU32(br);
            ReadBeU32(br);                    // header size (BE)

            var meta = new List<(uint Offset, uint Size, string Name)>();
            for (uint i = 0; i < fileCount; i++)
            {
                uint   offset = ReadBeU32(br);
                uint   size   = ReadBeU32(br);
                string name   = ReadNullStr(br);
                meta.Add((offset, size, name));
            }

            var results = new List<(string Name, byte[] Data)>();
            foreach (var m in meta)
            {
                ms.Seek(m.Offset, SeekOrigin.Begin);
                results.Add((m.Name, br.ReadBytes((int)m.Size)));
            }
            return results;
        }
        catch { return null; }
    }

    private static byte[] BuildMultiEntryBig(List<string> names, List<byte[]> blobs)
    {
        // Header: 16 fixed + sum of (8 + nameLen + 1) per entry
        int headerSize = 16;
        var nameBytes = new List<byte[]>();
        foreach (var n in names)
        {
            var nb = Encoding.ASCII.GetBytes(n);
            nameBytes.Add(nb);
            headerSize += 8 + nb.Length + 1;
        }

        var offsets = new uint[blobs.Count];
        uint cur = (uint)headerSize;
        for (int i = 0; i < blobs.Count; i++)
        {
            offsets[i] = cur;
            cur += (uint)blobs[i].Length;
        }

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

        bw.Write(Encoding.ASCII.GetBytes("BIGF"));
        bw.Write(cur);                               // LE archive size
        WriteBeU32(bw, (uint)names.Count);           // BE file count
        WriteBeU32(bw, (uint)headerSize);            // BE header size

        for (int i = 0; i < names.Count; i++)
        {
            WriteBeU32(bw, offsets[i]);
            WriteBeU32(bw, (uint)blobs[i].Length);
            bw.Write(nameBytes[i]);
            bw.Write((byte)0);
        }

        foreach (var blob in blobs) bw.Write(blob);

        return ms.ToArray();
    }

    // ── CSF helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the full CSF, applies every key→value pair from <paramref name="overrides"/>,
    /// and returns the re-serialised bytes.  Labels not listed in overrides are
    /// written back verbatim.
    /// </summary>
    private static byte[]? ApplyAllOverrides(byte[] csfData,
                                              IReadOnlyDictionary<string, string> overrides)
    {
        try
        {
            using var ms = new MemoryStream(csfData);
            using var br = new BinaryReader(ms, Encoding.ASCII, leaveOpen: true);

            if (new string(br.ReadChars(4)) != " FSC") return null;

            uint version    = br.ReadUInt32();
            uint numLabels  = br.ReadUInt32();
            uint numStrings = br.ReadUInt32();
            uint reserved   = br.ReadUInt32();
            uint language   = br.ReadUInt32();

            var labels = new List<CsfLabel>();
            for (uint i = 0; i < numLabels; i++)
            {
                if (ms.Position + 12 > ms.Length) break;
                if (new string(br.ReadChars(4)) != " LBL") break;

                uint   strCount = br.ReadUInt32();
                uint   nameLen  = br.ReadUInt32();
                string key      = new(br.ReadChars((int)nameLen));

                var strings = new List<CsfString>();
                for (uint j = 0; j < strCount; j++)
                {
                    string sMagic   = new(br.ReadChars(4));
                    bool   hasExtra = sMagic == "STRW";
                    uint   charLen  = br.ReadUInt32();

                    var chars = new char[charLen];
                    for (uint k = 0; k < charLen; k++)
                        chars[k] = (char)(br.ReadUInt16() ^ 0xFFFF);
                    string text = new(chars);

                    string? extra = null;
                    if (hasExtra)
                    {
                        uint   extraLen   = br.ReadUInt32();
                        byte[] extraBytes = br.ReadBytes((int)extraLen);
                        extra = Encoding.ASCII.GetString(extraBytes);
                    }
                    strings.Add(new CsfString(sMagic, text, extra));
                }
                labels.Add(new CsfLabel(key, strings));
            }

            // Apply every override whose key matches a label in the CSF.
            foreach (var lbl in labels)
            {
                if (!TryFindOverride(overrides, lbl.Key, out var newText)) continue;
                if (lbl.Strings.Count > 0)
                {
                    var old = lbl.Strings[0];
                    lbl.Strings[0] = new CsfString(old.Magic, newText!, old.Extra);
                }
            }

            return WriteCsf(version, numStrings, reserved, language, labels);
        }
        catch { return null; }
    }

    /// <summary>
    /// Looks up a CSF key in the overrides dict, stripping namespace prefix
    /// (e.g. "CONTROLBAR:foo" matches key "foo" and vice-versa).
    /// </summary>
    private static bool TryFindOverride(IReadOnlyDictionary<string, string> overrides,
                                        string csfKey, out string? value)
    {
        if (overrides.TryGetValue(csfKey, out value)) return true;

        // Try bare name match (strip "NAMESPACE:" prefix)
        string bare = BareKey(csfKey);
        foreach (var kv in overrides)
        {
            if (string.Equals(BareKey(kv.Key), bare, StringComparison.OrdinalIgnoreCase))
            {
                value = kv.Value;
                return true;
            }
        }
        return false;
    }

    private static string BareKey(string k)
    {
        int ci = k.IndexOf(':');
        return ci >= 0 ? k[(ci + 1)..] : k;
    }

    private static byte[] WriteCsf(uint version, uint numStrings, uint reserved,
                                    uint language, List<CsfLabel> labels)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

        bw.Write(Encoding.ASCII.GetBytes(" FSC"));
        bw.Write(version);
        bw.Write((uint)labels.Count);
        bw.Write(numStrings);
        bw.Write(reserved);
        bw.Write(language);

        foreach (var lbl in labels)
        {
            bw.Write(Encoding.ASCII.GetBytes(" LBL"));
            bw.Write((uint)lbl.Strings.Count);
            byte[] nameBytes = Encoding.ASCII.GetBytes(lbl.Key);
            bw.Write((uint)nameBytes.Length);
            bw.Write(nameBytes);

            foreach (var s in lbl.Strings)
            {
                bw.Write(Encoding.ASCII.GetBytes(s.Magic));
                char[] chars = s.Text.ToCharArray();
                bw.Write((uint)chars.Length);
                foreach (char c in chars)
                    bw.Write((ushort)(c ^ 0xFFFF));

                if (s.Extra != null)
                {
                    byte[] extraBytes = Encoding.ASCII.GetBytes(s.Extra);
                    bw.Write((uint)extraBytes.Length);
                    bw.Write(extraBytes);
                }
            }
        }

        return ms.ToArray();
    }

    // ── Binary helpers ─────────────────────────────────────────────────────────

    private static uint ReadBeU32(BinaryReader br)
    {
        byte[] b = br.ReadBytes(4);
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        return BitConverter.ToUInt32(b, 0);
    }

    private static void WriteBeU32(BinaryWriter bw, uint value)
    {
        byte[] b = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        bw.Write(b);
    }

    private static string ReadNullStr(BinaryReader br)
    {
        var sb = new StringBuilder(64);
        byte b;
        while ((b = br.ReadByte()) != 0) sb.Append((char)b);
        return sb.ToString();
    }

    // ── Internal models ────────────────────────────────────────────────────────

    private record CsfLabel(string Key, List<CsfString> Strings);
    private record CsfString(string Magic, string Text, string? Extra);
}
