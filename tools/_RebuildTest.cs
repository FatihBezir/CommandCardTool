using LauncherWinUI.Services;
string van = @"d:\SteamLibrary\steamapps\common\Command & Conquer Generals - Zero Hour\EnglishZH.big";
string testOut = BigCsfWriter.GetOutputPath(van) + ".test";
// write to temp by copying logic - just call RebuildAll to real ! path test
var ov = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) {
  ["CONTROLBAR:Chem_ConstructGLATunnelNetwork"] = "Toxin &Network",
  ["CONTROLBAR:Chem_ToolTipGLABuildTunnelNetwork"] = "Base defense and underground tunnel. Units can enter the Tunnel Network and exit at any other Tunnel Network"
};
string? path = BigCsfWriter.RebuildAll(van, ov, null);
if (path == null) { Console.WriteLine("FAIL"); return; }
var fi = new FileInfo(path);
var entries = BigArchiveIndex.ReadEntries(path);
var raw = BigArchiveIndex.ReadEntryBytes(path, entries.First(e=>e.Name.EndsWith(".csf")).Offset, entries.First(e=>e.Name.EndsWith(".csf")).Size);
var parsed = CsfCodec.ParseAll(raw!);
Console.WriteLine($"Output: {path}");
Console.WriteLine($"Size: {fi.Length:N0} entries: {entries.Count} csfLabels: {parsed.Count}");
