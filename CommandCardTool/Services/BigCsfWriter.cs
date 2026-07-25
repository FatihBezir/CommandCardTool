using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LauncherWinUI.Services;

/// <summary>
/// Minimal !EnglishZH.big override: full patched CSF (all labels via <see cref="CsfCodec"/>)
/// plus only changed TGA/DDS atlases. Vanilla EnglishZH.big supplies everything else.
/// </summary>
internal static class BigCsfWriter
{
    public static string GetOutputPath(string sourceBigPath)
    {
        string dir  = Path.GetDirectoryName(sourceBigPath) ?? "";
        string name = Path.GetFileName(sourceBigPath);
        if (!name.StartsWith("!", StringComparison.Ordinal))
            name = "!" + name;
        return Path.Combine(dir, name);
    }

    public static string? RebuildAll(string sourceBigPath,
                                     IReadOnlyDictionary<string, string> overrides,
                                     IReadOnlyDictionary<string, byte[]>? tgaPatches = null)
    {
        if (!File.Exists(sourceBigPath)) return null;
        if (overrides.Count == 0 && (tgaPatches == null || tgaPatches.Count == 0)) return null;

        try
        {
            string outputPath = GetOutputPath(sourceBigPath);

            // Rebuilding an override on top of itself compounds any earlier damage.
            if (string.Equals(Path.GetFullPath(outputPath), Path.GetFullPath(sourceBigPath),
                              StringComparison.OrdinalIgnoreCase))
                return null;

            var sourceEntries = ExtractAllEntries(File.ReadAllBytes(sourceBigPath));
            if (sourceEntries == null || sourceEntries.Count == 0) return null;

            int csfIdx = sourceEntries.FindIndex(e =>
                e.Name.EndsWith(".csf", StringComparison.OrdinalIgnoreCase));
            if (csfIdx < 0) return null;

            byte[] vanillaCsf = sourceEntries[csfIdx].Data;
            int vanillaLabels = CsfCodec.ParseAll(vanillaCsf).Count;

            // Reuse the previous override's CSF so earlier edits survive, but only when it
            // is intact — a short CSF means labels were lost and would stay lost forever.
            byte[] baseCsf = vanillaCsf;
            var existingOverrideEntries = ReadOverrideEntries(outputPath);
            if (existingOverrideEntries != null)
            {
                int existingCsfIdx = existingOverrideEntries.FindIndex(e =>
                    e.Name.EndsWith(".csf", StringComparison.OrdinalIgnoreCase));
                if (existingCsfIdx >= 0)
                {
                    byte[] existingCsf = existingOverrideEntries[existingCsfIdx].Data;
                    if (CsfCodec.ParseAll(existingCsf).Count >= vanillaLabels)
                        baseCsf = existingCsf;
                }
            }

            // Apply new overrides on top of base CSF (which may already contain previous edits)
            byte[] patchedCsf = CsfCodec.ApplyOverrides(baseCsf, overrides);

            // Never ship a CSF with fewer labels than vanilla: the game renders every
            // dropped label as MISSING: '…' and its & hotkey stops working.
            if (CsfCodec.ParseAll(patchedCsf).Count < vanillaLabels) return null;

            var names = new List<string>();
            var bodies = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

            void Upsert(string name, byte[] data)
            {
                if (!bodies.ContainsKey(name))
                    names.Add(name);
                bodies[name] = data;
            }

            Upsert(sourceEntries[csfIdx].Name, patchedCsf);

            // Carry previously painted atlases forward so re-editing keeps earlier work.
            if (existingOverrideEntries != null)
            {
                foreach (var existing in existingOverrideEntries)
                {
                    if (existing.Name.EndsWith(".csf", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!IsOverrideTextureEntry(existing.Name))
                        continue;
                    Upsert(existing.Name, existing.Data);
                }
            }

            if (tgaPatches != null)
            {
                foreach (var kv in tgaPatches)
                    Upsert(kv.Key, kv.Value);
            }

            byte[] outBig = BigBuilder.Build(names, bodies);

            string backupPath = outputPath + ".bak";
            try
            {
                if (File.Exists(outputPath))
                    File.Copy(outputPath, backupPath, overwrite: true);
            }
            catch { /* best-effort */ }

            File.WriteAllBytes(outputPath, outBig);
            return outputPath;
        }
        catch { return null; }
    }

    /// <summary>
    /// Entries of a previously written override, or null when the file is missing,
    /// unreadable, or an accidental full-BIG copy (an old bug) rather than a minimal
    /// override. Judged by content — a 12-atlas override is legitimately ~50 MB.
    /// </summary>
    private static List<(string Name, byte[] Data)>? ReadOverrideEntries(string outputPath)
    {
        if (!File.Exists(outputPath)) return null;
        try
        {
            var entries = ExtractAllEntries(File.ReadAllBytes(outputPath));
            if (entries == null || entries.Count == 0) return null;

            foreach (var e in entries)
            {
                if (e.Name.EndsWith(".csf", StringComparison.OrdinalIgnoreCase)) continue;
                if (IsOverrideTextureEntry(e.Name)) continue;
                return null;   // carries unrelated payload → not ours to build on
            }
            return entries;
        }
        catch { return null; }
    }

    private static bool IsOverrideTextureEntry(string name)
        => name.StartsWith("Data\\", StringComparison.OrdinalIgnoreCase)
        && (name.EndsWith(".tga", StringComparison.OrdinalIgnoreCase)
         || name.EndsWith(".dds", StringComparison.OrdinalIgnoreCase));

    private static List<(string Name, byte[] Data)>? ExtractAllEntries(byte[] bigData)
    {
        try
        {
            using var ms = new MemoryStream(bigData);
            using var br = new BinaryReader(ms, Encoding.ASCII, leaveOpen: true);

            string magic = new(br.ReadChars(4));
            if (magic != "BIGF" && magic != "BIG4") return null;

            br.ReadUInt32();
            uint fileCount = ReadBeU32(br);
            ReadBeU32(br);

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
        while ((b = br.ReadByte()) != 0) sb.Append((char)b);
        return sb.ToString();
    }
}
