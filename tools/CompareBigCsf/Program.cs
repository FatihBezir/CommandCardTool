using System.Security.Cryptography;
using System.Text;
using LauncherWinUI.Services;

static void Analyze(string title, string path)
{
    Console.WriteLine($"\n========== {title} ==========");
    if (!File.Exists(path)) { Console.WriteLine("NOT FOUND"); return; }
    var fi = new FileInfo(path);
    Console.WriteLine($"Size: {fi.Length:N0} bytes");

    var entries = BigArchiveIndex.ReadEntries(path);
    int csf = entries.Count(e => e.Name.EndsWith(".csf", StringComparison.OrdinalIgnoreCase));
    int tga = entries.Count(e => e.Name.EndsWith(".tga", StringComparison.OrdinalIgnoreCase));
    Console.WriteLine($"Entries: total={entries.Count} csf={csf} tga={tga}");

    var labels = BigCsfReader.ReadFromBig(path);
    Console.WriteLine($"CSF labels (reader): {labels.Count:N0}");

    string? csfName = entries.FirstOrDefault(e => e.Name.EndsWith(".csf", StringComparison.OrdinalIgnoreCase)).Name;
    if (csfName != null)
    {
        var idx = BigArchiveIndex.ReadEntries(path).First(e => e.Name == csfName);
        byte[]? raw = BigArchiveIndex.ReadEntryBytes(path, idx.Offset, idx.Size);
        if (raw != null)
        {
            Console.WriteLine($"CSF entry: {csfName} raw={raw.Length:N0} md5={Convert.ToHexString(MD5.HashData(raw))[..16]}");
            using var ms = new MemoryStream(raw);
            using var br = new BinaryReader(ms, Encoding.ASCII);
            if (new string(br.ReadChars(4)) == " FSC")
            {
                br.ReadUInt32(); // ver
                uint numLbl = br.ReadUInt32();
                uint numStr = br.ReadUInt32();
                Console.WriteLine($"CSF header: labels={numLbl} strings={numStr}");
            }
        }
    }

    foreach (var key in new[] { "Chem_ConstructGLATunnelNetwork", "Chem_ToolTipGLABuildTunnelNetwork", "ConstructGLATunnelNetwork" })
    {
        var hit = labels.FirstOrDefault(kv => kv.Key.Contains(key, StringComparison.OrdinalIgnoreCase));
        if (hit.Key != null) Console.WriteLine($"  {hit.Key} = \"{hit.Value}\"");
        else Console.WriteLine($"  [{key}] NOT FOUND");
    }
}

string web = @"c:\Users\MSI\Downloads\EnglishZH (1).big";
string tool = @"d:\SteamLibrary\steamapps\common\Command & Conquer Generals - Zero Hour\!EnglishZH.big";
string van = @"d:\SteamLibrary\steamapps\common\Command & Conquer Generals - Zero Hour\EnglishZH.big";

Analyze("WEB", web);
Analyze("TOOL !EnglishZH", tool);
Analyze("VANILLA", van);

// Test CsfCodec round-trip on vanilla CSF
{
    string vanPath = @"d:\SteamLibrary\steamapps\common\Command & Conquer Generals - Zero Hour\EnglishZH.big";
    var vanEntries = BigArchiveIndex.ReadEntries(vanPath);
    var csfMeta = vanEntries.First(e => e.Name.EndsWith(".csf", StringComparison.OrdinalIgnoreCase));
    byte[]? raw = BigArchiveIndex.ReadEntryBytes(vanPath, csfMeta.Offset, csfMeta.Size);
    if (raw != null)
    {
        var parsed = CsfCodec.ParseAll(raw);
        byte[] rewritten = CsfCodec.WriteAll(
            BitConverter.ToUInt32(raw, 4),
            BitConverter.ToUInt32(raw, 16),
            BitConverter.ToUInt32(raw, 20),
            parsed);
        using var ms = new MemoryStream(rewritten);
        using var br = new BinaryReader(ms);
        br.ReadChars(4);
        br.ReadUInt32();
        uint lbl = br.ReadUInt32();
        uint str = br.ReadUInt32();
        Console.WriteLine($"\nCsfCodec round-trip: parsed={parsed.Count} rewritten labels={lbl} strings={str} size={rewritten.Length} (was {raw.Length})");
    }
}
// Minimal save test (CSF complete, only override assets)
{
    string vanPath = @"d:\SteamLibrary\steamapps\common\Command & Conquer Generals - Zero Hour\EnglishZH.big";
    var ov = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["CONTROLBAR:Chem_ConstructGLATunnelNetwork"] = "Toxin &Network",
        ["CONTROLBAR:Chem_ToolTipGLABuildTunnelNetwork"] =
            "Base defense and underground tunnel. Units can enter the Tunnel Network and exit at any other Tunnel Network",
    };
    string? outPath = BigCsfWriter.RebuildAll(vanPath, ov, null);
    if (outPath != null)
    {
        var fi = new FileInfo(outPath);
        var ent = BigArchiveIndex.ReadEntries(outPath);
        var csf = ent.First(e => e.Name.EndsWith(".csf", StringComparison.OrdinalIgnoreCase));
        byte[]? raw = BigArchiveIndex.ReadEntryBytes(outPath, csf.Offset, csf.Size);
        int n = raw != null ? CsfCodec.ParseAll(raw).Count : 0;
        Console.WriteLine($"\nMINIMAL SAVE: size={fi.Length:N0} entries={ent.Count} csfLabels={n} (vanilla has 6422+)");
        File.Delete(outPath);
    }
}
