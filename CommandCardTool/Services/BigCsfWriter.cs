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
            var sourceEntries = ExtractAllEntries(File.ReadAllBytes(sourceBigPath));
            if (sourceEntries == null || sourceEntries.Count == 0) return null;

            int csfIdx = sourceEntries.FindIndex(e =>
                e.Name.EndsWith(".csf", StringComparison.OrdinalIgnoreCase));
            if (csfIdx < 0) return null;

            // CSF base always from vanilla EnglishZH.big — CsfCodec keeps all ~6422 labels.
            byte[] patchedCsf = CsfCodec.ApplyOverrides(sourceEntries[csfIdx].Data, overrides);

            var names = new List<string>();
            var bodies = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

            void Upsert(string name, byte[] data)
            {
                if (!bodies.ContainsKey(name))
                    names.Add(name);
                bodies[name] = data;
            }

            Upsert(sourceEntries[csfIdx].Name, patchedCsf);

            if (File.Exists(outputPath))
            {
                byte[] existingBytes = File.ReadAllBytes(outputPath);
                var existingEntries = ExtractAllEntries(existingBytes);
                // Ignore accidental full-BIG copies (old bug); keep only true minimal overrides.
                bool looksMinimal = existingEntries != null
                                 && existingEntries.Count <= 40
                                 && existingBytes.Length < 15_000_000;
                if (looksMinimal && existingEntries != null)
                {
                    foreach (var existing in existingEntries)
                    {
                        if (existing.Name.EndsWith(".csf", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (!IsOverrideTextureEntry(existing.Name))
                            continue;
                        Upsert(existing.Name, existing.Data);
                    }
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
