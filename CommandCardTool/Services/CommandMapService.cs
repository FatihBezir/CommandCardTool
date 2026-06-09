using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LauncherWinUI.Services;

// ── Data records ─────────────────────────────────────────────────────────────

/// <summary>Key binding read from a CommandMap block.</summary>
internal record CommandMapEntry(string Key, string Modifiers);

// ── Parser ───────────────────────────────────────────────────────────────────

internal static class CommandMapParser
{
    /// <summary>
    /// Reads Key and Modifiers for the given command from ini text.
    /// Supports both "CommandMap NAME" and "CommandMap = NAME" forms.
    /// </summary>
    public static CommandMapEntry? ParseBlock(string iniText, string command)
    {
        string? block = ExtractBlock(iniText, command);
        if (block == null) return null;

        string key = Regex.Match(block, @"Key\s*=\s*(\S+)", RegexOptions.IgnoreCase)
                         .Groups[1].Value;
        string mod = Regex.Match(block, @"Modifiers\s*=\s*(\S+)", RegexOptions.IgnoreCase)
                         .Groups[1].Value;

        return new CommandMapEntry(
            Key:       string.IsNullOrEmpty(key) ? "KEY_I" : key,
            Modifiers: string.IsNullOrEmpty(mod) ? "NONE"  : mod);
    }

    /// <summary>
    /// Replaces or appends a CommandMap block in ini text.
    /// </summary>
    public static string UpsertBlock(string iniText, string command, string key, string modifiers,
                                     string useableIn = "GAME")
    {
        string newBlock =
            $"CommandMap {command}\n" +
            $"  Key = {key}\n" +
            $"  Transition = DOWN\n" +
            $"  Modifiers = {modifiers}\n" +
            $"  UseableIn = {useableIn}\n" +
            "End";

        string esc = Regex.Escape(command);

        // Form 1: CommandMap NAME  …  End
        var re1 = new Regex(
            $@"CommandMap\s+{esc}\b[\s\S]*?^\s*End\s*$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

        // Form 2: CommandMap = NAME  …  End
        var re2 = new Regex(
            $@"CommandMap\s*=\s*{esc}\s*(?:\r\n|\n|\r)([\s\S]*?)^\s*End\s*$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

        if (re1.IsMatch(iniText)) return re1.Replace(iniText, newBlock);
        if (re2.IsMatch(iniText)) return re2.Replace(iniText, newBlock);

        // Append at end
        return iniText.TrimEnd() + "\n\n" + newBlock + "\n";
    }

    // ─ helpers ──────────────────────────────────────────────────────────────

    private static string? ExtractBlock(string iniText, string command)
    {
        string esc = Regex.Escape(command);

        var re1 = new Regex(
            $@"CommandMap\s+{esc}\b([\s\S]*?)^\s*End\s*$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

        var re2 = new Regex(
            $@"CommandMap\s*=\s*{esc}\s*(?:\r\n|\n|\r)([\s\S]*?)^\s*End\s*$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

        Match m = re1.Match(iniText);
        if (!m.Success) m = re2.Match(iniText);
        return m.Success ? m.Groups[1].Value : null;
    }
}

// ── Locator — scans BIG for CommandMap blocks ────────────────────────────────

internal static class CommandMapLocator
{
    // Shared BIG entry index: name → (offset, size)
    private static readonly Dictionary<string, (int Offset, int Size)> _idx
        = new(StringComparer.OrdinalIgnoreCase);

    private static string? _bigPath;

    /// <summary>Initialize with a BIG file path (typically INIZH.big).</summary>
    public static void Load(string bigPath)
    {
        _bigPath = bigPath;
        _idx.Clear();
        if (!File.Exists(bigPath)) return;
        try { IndexBig(bigPath, _idx); } catch { }
    }

    /// <summary>
    /// Scans the loaded BIG for SELECT_NEXT_IDLE_WORKER and PLACE_BEACON blocks.
    /// Returns entry names alongside parsed entries (null if not found).
    /// </summary>
    public static (CommandMapEntry? IdleWorker, string? IdleWorkerPath,
                   CommandMapEntry? Beacon,     string? BeaconPath)
        FindAll()
    {
        if (_bigPath == null || !File.Exists(_bigPath))
            return (null, null, null, null);

        // Prioritise entries whose names contain "commandmap"
        var allIni = _idx.Keys
            .Where(k => k.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var ordered = allIni
            .OrderByDescending(k => k.ToLower().Contains("commandmap") ? 1 : 0)
            .ToList();

        CommandMapEntry? iw = null; string? iwPath = null;
        CommandMapEntry? bc = null; string? bcPath = null;

        foreach (var name in ordered)
        {
            try
            {
                byte[]? raw = ReadEntry(_bigPath, _idx, name);
                if (raw == null) continue;
                string text = Encoding.UTF8.GetString(raw);

                if (iw == null)
                {
                    var found = CommandMapParser.ParseBlock(text, "SELECT_NEXT_IDLE_WORKER");
                    if (found != null) { iw = found; iwPath = name; }
                }
                if (bc == null)
                {
                    var found = CommandMapParser.ParseBlock(text, "PLACE_BEACON");
                    if (found != null) { bc = found; bcPath = name; }
                }
                if (iw != null && bc != null) break;
            }
            catch { }
        }

        return (iw, iwPath, bc, bcPath);
    }

    // ─ helpers ──────────────────────────────────────────────────────────────

    internal static void IndexBig(string path, Dictionary<string, (int, int)> dest)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);

        var magic = new string(br.ReadChars(4));
        if (magic != "BIGF" && magic != "BIG4") return;
        br.ReadUInt32();                        // archive size (LE)
        int count = (int)ReadBeU32(br);         // file count  (BE)
        ReadBeU32(br);                          // header size (BE)

        for (int i = 0; i < count; i++)
        {
            int offset = (int)ReadBeU32(br);
            int size   = (int)ReadBeU32(br);
            string name = ReadNullStr(br);
            dest.TryAdd(name, (offset, size));
        }
    }

    internal static byte[]? ReadEntry(string bigPath,
        Dictionary<string, (int Offset, int Size)> index, string name)
    {
        if (!index.TryGetValue(name, out var e)) return null;
        using var fs = File.OpenRead(bigPath);
        fs.Seek(e.Offset, SeekOrigin.Begin);
        byte[] buf = new byte[e.Size];
        int read = 0;
        while (read < buf.Length)
        {
            int n = fs.Read(buf, read, buf.Length - read);
            if (n == 0) break;
            read += n;
        }
        return buf;
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

// ── Editor — writes updated CommandMap block back into BIG ───────────────────

internal static class CommandMapEditor
{
    public readonly record struct CommandMapUpdate(
        string Command,
        string Key,
        string Modifiers,
        string UseableIn = "GAME");

    /// <summary>
    /// Updates (or appends) a CommandMap block inside a BIG file and returns the
    /// new BIG bytes.  Returns null if the target entry is not found.
    /// </summary>
    public static byte[]? SaveEntry(
        string bigPath,
        string targetEntryName,
        string command,
        string key,
        string modifiers,
        string useableIn = "GAME")
    {
        if (!File.Exists(bigPath)) return null;

        var idx = new Dictionary<string, (int Offset, int Size)>(StringComparer.OrdinalIgnoreCase);
        CommandMapLocator.IndexBig(bigPath, idx);

        if (!idx.ContainsKey(targetEntryName)) return null;

        // Read all entries, update the target one
        var bodies = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, _) in idx)
        {
            byte[]? raw = CommandMapLocator.ReadEntry(bigPath, idx, name);
            if (raw == null) continue;

            if (string.Equals(name, targetEntryName, StringComparison.OrdinalIgnoreCase))
            {
                string text    = Encoding.UTF8.GetString(raw);
                string updated = CommandMapParser.UpsertBlock(text, command, key, modifiers, useableIn);
                bodies[name] = Encoding.UTF8.GetBytes(updated);
            }
            else
            {
                bodies[name] = raw;
            }
        }

        return BigBuilder.Build(idx.Keys.ToList(), bodies);
    }

    /// <summary>
    /// Updates several CommandMap blocks inside one INI entry and returns rebuilt BIG bytes.
    /// </summary>
    public static byte[]? SaveEntries(
        string bigPath,
        string targetEntryName,
        IEnumerable<CommandMapUpdate> updates)
    {
        if (!File.Exists(bigPath)) return null;

        var updateList = updates.ToList();
        if (updateList.Count == 0) return null;

        var idx = new Dictionary<string, (int Offset, int Size)>(StringComparer.OrdinalIgnoreCase);
        CommandMapLocator.IndexBig(bigPath, idx);

        if (!idx.ContainsKey(targetEntryName)) return null;

        var bodies = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, _) in idx)
        {
            byte[]? raw = CommandMapLocator.ReadEntry(bigPath, idx, name);
            if (raw == null) continue;

            if (string.Equals(name, targetEntryName, StringComparison.OrdinalIgnoreCase))
            {
                string text = Encoding.UTF8.GetString(raw);
                foreach (var update in updateList)
                    text = CommandMapParser.UpsertBlock(
                        text, update.Command, update.Key, update.Modifiers, update.UseableIn);
                bodies[name] = Encoding.UTF8.GetBytes(text);
            }
            else
            {
                bodies[name] = raw;
            }
        }

        return BigBuilder.Build(idx.Keys.ToList(), bodies);
    }
}

// ── CommandButton patcher — fixes ButtonImage in CommandButton.ini ────────────

internal static class CommandButtonPatcher
{
    /// <summary>
    /// In INIZH.big (or the supplied bigPath), patches ButtonImage inside
    /// CommandButton blocks whose names match buttonNamePattern.
    /// Returns the new BIG bytes and count of patched lines.
    /// </summary>
    public static (byte[]? NewBig, int PatchedCount) PatchButtonImage(
        string bigPath,
        string buttonNamePattern,
        string oldImage,
        string newImage)
    {
        if (!File.Exists(bigPath)) return (null, 0);

        var idx = new Dictionary<string, (int Offset, int Size)>(StringComparer.OrdinalIgnoreCase);
        CommandMapLocator.IndexBig(bigPath, idx);

        // Find CommandButton.ini
        string? cbName = idx.Keys.FirstOrDefault(k =>
            k.ToLower().Contains("commandbutton") &&
            k.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
        if (cbName == null) return (null, 0);

        byte[]? raw = CommandMapLocator.ReadEntry(bigPath, idx, cbName);
        if (raw == null) return (null, 0);

        string text  = Encoding.UTF8.GetString(raw);
        var lines    = text.Split('\n');
        string patLower = buttonNamePattern.ToLower();
        bool inBlock = false;
        int patched  = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();

            if (trimmed.StartsWith("CommandButton ", StringComparison.OrdinalIgnoreCase))
            {
                inBlock = trimmed.ToLower().Contains(patLower);
            }
            else if (Regex.IsMatch(trimmed, @"^\s*End\s*$", RegexOptions.IgnoreCase))
            {
                inBlock = false;
            }
            else if (inBlock && trimmed.StartsWith("ButtonImage", StringComparison.OrdinalIgnoreCase))
            {
                int eqIdx = lines[i].IndexOf('=');
                if (eqIdx < 0) continue;

                // Strip inline comment
                string val = lines[i][(eqIdx + 1)..].Split(';')[0].Trim();
                if (string.Equals(val, oldImage, StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = lines[i][..(eqIdx + 1)] + " " + newImage;
                    patched++;
                }
            }
        }

        if (patched == 0) return (null, 0);

        byte[] newIni = Encoding.UTF8.GetBytes(string.Join('\n', lines));
        var bodies    = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, _) in idx)
        {
            byte[]? entry = CommandMapLocator.ReadEntry(bigPath, idx, name);
            if (entry == null) continue;
            bodies[name] = string.Equals(name, cbName, StringComparison.OrdinalIgnoreCase)
                ? newIni : entry;
        }

        return (BigBuilder.Build(idx.Keys.ToList(), bodies), patched);
    }
}

// ── BigBuilder — constructs a BIGF archive from named byte arrays ─────────────

internal static class BigBuilder
{
    /// <summary>
    /// Builds a BIGF archive from the supplied file bodies.
    /// Entry order follows <paramref name="names"/>.
    /// </summary>
    public static byte[] Build(List<string> names, Dictionary<string, byte[]> bodies)
    {
        // ── Pass 1: compute header size ──────────────────────────────────────
        // Header = 4 magic + 4 archiveSize + 4 count + 4 headerSize
        //        + per entry: 4 offset + 4 size + name bytes + 1 null
        int headerSize = 16;
        foreach (var name in names)
            headerSize += 4 + 4 + Encoding.ASCII.GetByteCount(name) + 1;

        // ── Pass 2: assign offsets ───────────────────────────────────────────
        int dataOffset = headerSize;
        var offsets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            offsets[name]  = dataOffset;
            dataOffset    += bodies.TryGetValue(name, out var b) ? b.Length : 0;
        }

        int totalSize = dataOffset;

        // ── Pass 3: write ────────────────────────────────────────────────────
        using var ms  = new MemoryStream(totalSize);
        using var bw  = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

        // Magic "BIGF"
        bw.Write(new[] { (byte)'B', (byte)'I', (byte)'G', (byte)'F' });

        // Archive size LE u32
        bw.Write((uint)totalSize);

        // Count BE u32
        WriteBeU32(bw, (uint)names.Count);

        // Header size BE u32
        WriteBeU32(bw, (uint)headerSize);

        // Entry headers
        foreach (var name in names)
        {
            int sz = bodies.TryGetValue(name, out var bd) ? bd.Length : 0;
            WriteBeU32(bw, (uint)offsets[name]);
            WriteBeU32(bw, (uint)sz);
            bw.Write(Encoding.ASCII.GetBytes(name));
            bw.Write((byte)0);
        }

        // Entry data
        foreach (var name in names)
        {
            if (bodies.TryGetValue(name, out var data) && data.Length > 0)
                bw.Write(data);
        }

        return ms.ToArray();
    }

    private static void WriteBeU32(BinaryWriter bw, uint value)
    {
        byte[] b = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        bw.Write(b);
    }
}
