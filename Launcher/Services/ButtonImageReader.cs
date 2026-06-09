using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LauncherWinUI.Services;

/// <summary>
/// Reads command-card button icons from C&amp;C ZH BIG archives.
///
/// Architecture (vanilla ZH):
///   EnglishZH.big  → TGA texture atlases  (SAUserInterface512_001-005, SN*, SS*, SU*)
///   INIZH.big      → MappedImage INI files (texture coords) + CommandButton INI files (label→image)
///
/// Call Load(gameDirectory) once. All .big files in the game folder are merged using
/// ZH alphabetical load order (! mods win over vanilla for duplicate paths).
/// Then call GetSlotImage(csfId, army) per slot.
/// </summary>
internal static class ButtonImageReader
{
    private record MappedDef(string Texture, int Left, int Top, int Right, int Bottom);

    private static string? _gameDir;

    // Entry indices: archive path → source BIG + offset (first in load order wins)
    private static readonly Dictionary<string, (string BigPath, int Offset, int Size)> _tgaEntries
        = new(StringComparer.OrdinalIgnoreCase);

    // Texture filename stem → winning archive entry. Some override BIGs use a
    // different internal path for the same atlas filename, so resolve by stem in
    // load order instead of falling back to a later vanilla EnglishZH entry.
    private static readonly Dictionary<string, (string EntryName, string BigPath, int Offset, int Size)> _tgaEntriesByStem
        = new(StringComparer.OrdinalIgnoreCase);

    // MappedImage name → atlas coords (from INIZH.big INIs)
    private static readonly Dictionary<string, MappedDef> _mapped
        = new(StringComparer.OrdinalIgnoreCase);

    // CSF text-label (lower) → ButtonImage name (from INIZH.big CommandButton blocks)
    private static readonly Dictionary<string, string> _cbLabel
        = new(StringComparer.OrdinalIgnoreCase);

    // Resolved image cache  "csfId|army" → BitmapSource (null = not found)
    private static readonly Dictionary<string, BitmapSource?> _cache
        = new(StringComparer.OrdinalIgnoreCase);

    private static bool _loaded;

    public static void Reload(string? gameOrBigPath = null)
    {
        _loaded = false;
        _tgaEntries.Clear();
        _tgaEntriesByStem.Clear();
        _mapped.Clear();
        _cbLabel.Clear();
        _cache.Clear();
        _gameDir = null;
        Load(gameOrBigPath);
    }

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Initialize from a game directory or any .big inside it. Safe to call multiple times.
    /// </summary>
    public static void Load(string? gameOrBigPath = null)
    {
        if (_loaded) return;
        _loaded = true;

        string? gameDir = null;
        if (!string.IsNullOrWhiteSpace(gameOrBigPath))
        {
            if (Directory.Exists(gameOrBigPath))
                gameDir = gameOrBigPath;
            else if (File.Exists(gameOrBigPath))
                gameDir = Path.GetDirectoryName(gameOrBigPath);
        }

        gameDir ??= GameBigStack.DiscoverGameDirectory();
        if (gameDir == null) return;

        _gameDir = gameDir;

        foreach (var bigPath in GameBigStack.GetSortedBigPaths(gameDir))
        {
            try
            {
                foreach (var (name, offset, size) in BigArchiveIndex.ReadEntries(bigPath))
                {
                    var ext = Path.GetExtension(name).ToLowerInvariant();
                    if (ext is ".tga" or ".dds")
                    {
                        if (_tgaEntries.TryAdd(name, (bigPath, offset, size)))
                        {
                            string stem = Path.GetFileNameWithoutExtension(name);
                            _tgaEntriesByStem.TryAdd(stem, (name, bigPath, offset, size));
                        }
                    }
                }
            }
            catch { /* skip */ }
        }

        ParseAllIni(gameDir);
    }

    /// <summary>Returns the button icon for a CSF slot id, or null if not available.</summary>
    public static BitmapSource? GetSlotImage(string csfId, string army = "")
    {
        if (_tgaEntriesByStem.Count == 0) return null;
        string key = csfId + "|" + army;
        if (_cache.TryGetValue(key, out var cached)) return cached;
        var result = Resolve(csfId, army);
        _cache[key] = result;
        return result;
    }

    /// <summary>Overwrites the cached image for a slot (e.g. after painting a hotkey letter).</summary>
    public static void UpdateCache(string csfId, string army, BitmapSource? bmp)
    {
        _cache[csfId + "|" + army] = bmp;
    }

    /// <summary>
    /// Returns the winning TGA/DDS entry name and the pixel crop rectangle for
    /// the given csfId, so callers can patch the atlas bytes directly.
    /// Returns null when the mapping cannot be resolved.
    /// </summary>
    public static TgaSlotInfo? ResolveTgaSlot(string csfId, string army)
    {
        // Reuse same resolution order as GetSlotImage
        string baseLabel = ExtractBaseLabel(csfId).ToLowerInvariant();
        string ns = csfId.Contains(':') ? csfId.Split(':')[0] : "controlbar";

        foreach (var adj in AdjustVariants(csfId, army))
        {
            string label = (adj.Contains(':') ? adj.Split(':')[^1] : adj).ToLowerInvariant();

            // 1. Manual override table
            if (_manualOverrides.TryGetValue(label, out var miName))
            {
                var info = ResolveEntryFromMappedName(miName);
                if (info != null) return info;
            }

            // 2. CommandButton INI lookup
            if (_cbLabel.TryGetValue(adj.ToLowerInvariant(), out var bi)
             || _cbLabel.TryGetValue(label, out bi))
            {
                foreach (var part in bi.Split(';'))
                {
                    var info = ResolveEntryFromMappedName(part.Trim());
                    if (info != null) return info;
                }
            }

            // 3. Base-label fallback
            if (baseLabel != label)
            {
                string baseId = ns + ":" + baseLabel;
                if (_cbLabel.TryGetValue(baseId, out var biBase)
                 || _cbLabel.TryGetValue(baseLabel, out biBase))
                {
                    foreach (var part in biBase.Split(';'))
                    {
                        var info = ResolveEntryFromMappedName(part.Trim());
                        if (info != null) return info;
                    }
                }
                if (_manualOverrides.TryGetValue(baseLabel, out var miBase))
                {
                    var info = ResolveEntryFromMappedName(miBase);
                    if (info != null) return info;
                }
            }
        }
        return null;
    }

    private static TgaSlotInfo? ResolveEntryFromMappedName(string miName)
    {
        if (!_mapped.TryGetValue(miName, out var def)) return null;
        if (_tgaEntriesByStem.Count == 0) return null;

        string texStem = Path.GetFileNameWithoutExtension(def.Texture).ToLowerInvariant();
        return _tgaEntriesByStem.TryGetValue(texStem, out var entry)
            ? new TgaSlotInfo(entry.EntryName, def.Left, def.Top, def.Right, def.Bottom)
            : null;
    }

    /// <summary>Reads the raw bytes of a TGA/DDS atlas from the winning BIG by entry name.</summary>
    public static byte[]? ReadTgaEntry(string entryName)
    {
        if (!_tgaEntries.TryGetValue(entryName, out var e)) return null;
        return BigArchiveIndex.ReadEntryBytes(e.BigPath, e.Offset, e.Size);
    }

    // ── INI parsing ─────────────────────────────────────────────────────────

    private static void ParseAllIni(string gameDir)
    {
        foreach (var bigPath in GameBigStack.GetSortedBigPaths(gameDir))
        {
            List<(string Name, int Offset, int Size)> entries;
            try { entries = BigArchiveIndex.ReadEntries(bigPath); }
            catch { continue; }

            foreach (var (name, offset, size) in entries)
            {
                if (!name.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) continue;
                var raw = BigArchiveIndex.ReadEntryBytes(bigPath, offset, size);
                if (raw == null) continue;
                try
                {
                    string text = Encoding.UTF8.GetString(raw);
                    if (text.Contains("MappedImage"))  ParseMappedImages(text);
                    if (text.Contains("CommandButton")) ParseCommandButtons(text);
                }
                catch { }
            }
        }
    }

    private static void ParseMappedImages(string iniText)
    {
        string curName = "", curTex = "";
        int l = 0, t = 0, r = 0, b = 0;
        bool hasCoords = false;

        foreach (var rawLine in iniText.Split('\n'))
        {
            var line = rawLine.Trim();

            if (line.StartsWith("MappedImage ", StringComparison.OrdinalIgnoreCase))
            {
                if (curName != "" && curTex != "" && hasCoords)
                    _mapped.TryAdd(curName, new MappedDef(curTex, l, t, r, b));
                curName = line[12..].Trim().Split(';')[0].Trim();
                curTex = ""; l = t = r = b = 0; hasCoords = false;
            }
            else if (line.StartsWith("Texture", StringComparison.OrdinalIgnoreCase)
                     && line.Contains('=')
                     && !line.StartsWith("TextureWidth", StringComparison.OrdinalIgnoreCase)
                     && !line.StartsWith("TextureHeight", StringComparison.OrdinalIgnoreCase))
            {
                curTex = line.Split('=', 2)[1].Split(';')[0].Trim();
            }
            else if (line.StartsWith("Coords", StringComparison.OrdinalIgnoreCase)
                     && line.Contains('='))
            {
                var val = line.Split('=', 2)[1];
                foreach (var part in val.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = part.Split(':');
                    if (kv.Length != 2 || !int.TryParse(kv[1].Trim(), out int v)) continue;
                    switch (kv[0].Trim().ToLowerInvariant())
                    {
                        case "left":   l = v; break;
                        case "top":    t = v; break;
                        case "right":  r = v; break;
                        case "bottom": b = v; hasCoords = true; break;
                    }
                }
            }
        }
        if (curName != "" && curTex != "" && hasCoords)
            _mapped.TryAdd(curName, new MappedDef(curTex, l, t, r, b));
    }

    private static void ParseCommandButtons(string iniText)
    {
        string curLabel = "", curBtnImg = "";

        foreach (var rawLine in iniText.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("CommandButton ", StringComparison.OrdinalIgnoreCase))
            {
                curLabel = ""; curBtnImg = "";
            }
            else if (line.Equals("End", StringComparison.OrdinalIgnoreCase)
                  || line.StartsWith("End ", StringComparison.OrdinalIgnoreCase))
            {
                if (curLabel != "" && curBtnImg != "")
                    _cbLabel.TryAdd(curLabel.ToLowerInvariant(), curBtnImg);
                curLabel = ""; curBtnImg = "";
            }
            else if (line.StartsWith("TextLabel", StringComparison.OrdinalIgnoreCase)
                     && line.Contains('='))
                curLabel = line.Split('=', 2)[1].Split(';')[0].Trim();
            else if (line.StartsWith("ButtonImage", StringComparison.OrdinalIgnoreCase)
                     && line.Contains('='))
                curBtnImg = line.Split('=', 2)[1].Split(';')[0].Trim();
        }
    }

    // Variant prefixes (same set as web extractBaseLabel)
    private static readonly string[] _variantPrefixes =
        { "nuke_", "infa_", "tank_", "airf_", "lazr_", "supw_", "chem_", "stlh_", "boss_", "demo_", "tox_" };

    /// <summary>Strips namespace and variant prefix — mirrors JS extractBaseLabel().</summary>
    private static string ExtractBaseLabel(string csfLabel)
    {
        string name = csfLabel.Contains(':') ? csfLabel.Split(':')[^1] : csfLabel;
        string lower = name.ToLowerInvariant();
        foreach (var pfx in _variantPrefixes)
            if (lower.StartsWith(pfx)) return name[pfx.Length..];
        return name;
    }

    // ── Image resolution ────────────────────────────────────────────────────

    private static BitmapSource? Resolve(string csfId, string army)
    {
        // Pre-compute base label (variant prefix stripped) like web extractBaseLabel()
        string baseLabel = ExtractBaseLabel(csfId).ToLowerInvariant();
        string ns = csfId.Contains(':') ? csfId.Split(':')[0] : "controlbar";

        foreach (var adj in AdjustVariants(csfId, army))
        {
            string label = (adj.Contains(':') ? adj.Split(':')[^1] : adj).ToLowerInvariant();

            // 1. Manual override table
            if (_manualOverrides.TryGetValue(label, out var miName))
            {
                var img = CropByMappedName(miName);
                if (img != null) return img;
            }

            // 2. CommandButton INI lookup — exact csfId or bare label
            if (_cbLabel.TryGetValue(adj.ToLowerInvariant(), out var bi)
             || _cbLabel.TryGetValue(label, out bi))
            {
                foreach (var part in bi.Split(';'))
                {
                    var img = CropByMappedName(part.Trim());
                    if (img != null) return img;
                }
            }

            // 3. Base-label fallback (web extractBaseLabel strategy):
            //    strip variant prefix from csfId and look up base in _cbLabel.
            //    e.g. "controlbar:nuke_constructchinapowerplant"
            //         → try "controlbar:constructchinapowerplant" which IS in _cbLabel.
            if (baseLabel != label)
            {
                string baseId = ns + ":" + baseLabel;
                if (_cbLabel.TryGetValue(baseId, out var biBase)
                 || _cbLabel.TryGetValue(baseLabel, out biBase))
                {
                    foreach (var part in biBase.Split(';'))
                    {
                        var img = CropByMappedName(part.Trim());
                        if (img != null) return img;
                    }
                }

                // Also check manual overrides with base label
                if (_manualOverrides.TryGetValue(baseLabel, out var miBase))
                {
                    var img = CropByMappedName(miBase);
                    if (img != null) return img;
                }
            }
        }
        return null;
    }

    private static IEnumerable<string> AdjustVariants(string csfId, string army)
    {
        string ns    = csfId.Contains(':') ? csfId.Split(':')[0] : "controlbar";
        string label = (csfId.Contains(':') ? csfId.Split(':')[^1] : csfId).ToLowerInvariant();

        string? prefix = null;
        if      (army.Contains("Superweapon") || army.Contains("Alexander")) prefix = "supw_";
        else if (army.Contains("Laser")       || army.Contains("Townes"))    prefix = "lazr_";
        else if (army.Contains("Air Force")   || army.Contains("Granger"))   prefix = "airf_";
        else if (army.Contains("Nuke")        || army.Contains("Tao"))       prefix = "nuke_";
        else if (army.Contains("Infantry")    || army.Contains("Shin"))      prefix = "infa_";
        else if (army.Contains("Tank")        || army.Contains("Kwai"))      prefix = "tank_";
        else if (army.Contains("Toxin")       || army.Contains("Thrax"))     prefix = "tox_";
        else if (army.Contains("Demo")        || army.Contains("Juhziz"))    prefix = "demo_";
        else if (army.Contains("Stealth")     || army.Contains("Kassad"))    prefix = "stlh_";

        if (prefix != null)
        {
            string varLabel = prefix + label;
            if (_manualOverrides.ContainsKey(varLabel) || _cbLabel.ContainsKey(ns + ":" + varLabel))
                yield return ns + ":" + varLabel;
        }
        yield return csfId;
    }

    private static BitmapSource? CropByMappedName(string name)
    {
        if (!_mapped.TryGetValue(name, out var def)) return null;
        return CropTga(def.Texture, def.Left, def.Top, def.Right, def.Bottom);
    }

    // ── TGA reading from the winning BIG in Zero Hour load order ─────────────

    private static BitmapSource? CropTga(string texture, int left, int top, int right, int bottom)
    {
        if (_tgaEntriesByStem.Count == 0) return null;

        // INI uses only the basename (e.g. "SAUserInterface512_001.tga").
        // BIG entries can have different internal paths, so match by stem and
        // use the first archive in game load order.
        string texStem = Path.GetFileNameWithoutExtension(texture).ToLowerInvariant();
        byte[]? raw = null;

        if (_tgaEntriesByStem.TryGetValue(texStem, out var entry))
            raw = BigArchiveIndex.ReadEntryBytes(entry.BigPath, entry.Offset, entry.Size);

        if (raw == null) return null;

        // DDS magic "DDS "
        if (raw.Length > 4 && raw[0] == 'D' && raw[1] == 'D' && raw[2] == 'S' && raw[3] == ' ')
            return CropDds(raw, left, top, right, bottom);

        return CropTgaBytes(raw, left, top, right, bottom);
    }

    private static BitmapSource? CropTgaBytes(byte[] data, int left, int top, int right, int bottom)
    {
        if (data.Length < 18) return null;

        int idLen      = data[0];
        int imageType  = data[2]; // 2 = uncompressed, 10 = RLE
        int width      = data[12] | (data[13] << 8);
        int height     = data[14] | (data[15] << 8);
        int pixelDepth = data[16];
        int descriptor = data[17];

        if (imageType != 2 && imageType != 10) return null;
        if (pixelDepth != 24 && pixelDepth != 32) return null;
        if (width == 0 || height == 0) return null;

        int bpp = pixelDepth / 8;
        int pos = 18 + idLen;
        if (data[1] == 1) // has color map
        {
            int cmapLen   = data[5] | (data[6] << 8);
            int cmapEntry = data[7];
            pos += cmapLen * (cmapEntry / 8);
        }

        bool bottomUp = (descriptor & 0x20) == 0; // bit5=0 → bottom-left origin
        var rgba = new byte[width * height * 4];

        if (imageType == 2) // uncompressed
        {
            for (int i = 0; i < width * height && pos + bpp <= data.Length; i++, pos += bpp)
            {
                int dstRow = bottomUp ? (height - 1 - i / width) : (i / width);
                int dstIdx = (dstRow * width + i % width) * 4;
                rgba[dstIdx]     = data[pos + 2]; // R
                rgba[dstIdx + 1] = data[pos + 1]; // G
                rgba[dstIdx + 2] = data[pos];     // B
                rgba[dstIdx + 3] = bpp == 4 ? data[pos + 3] : (byte)255;
            }
        }
        else // RLE (type 10)
        {
            int pi = 0;
            while (pi < width * height && pos < data.Length)
            {
                int hdr   = data[pos++];
                bool rle  = (hdr & 0x80) != 0;
                int count = (hdr & 0x7F) + 1;

                if (rle)
                {
                    if (pos + bpp > data.Length) break;
                    byte r = data[pos + 2], g = data[pos + 1], b2 = data[pos];
                    byte a = bpp == 4 ? data[pos + 3] : (byte)255;
                    pos += bpp;
                    for (int j = 0; j < count && pi < width * height; j++, pi++)
                    {
                        int dstRow = bottomUp ? (height - 1 - pi / width) : (pi / width);
                        int dstIdx = (dstRow * width + pi % width) * 4;
                        rgba[dstIdx] = r; rgba[dstIdx + 1] = g;
                        rgba[dstIdx + 2] = b2; rgba[dstIdx + 3] = a;
                    }
                }
                else
                {
                    for (int j = 0; j < count && pi < width * height && pos + bpp <= data.Length;
                         j++, pi++, pos += bpp)
                    {
                        int dstRow = bottomUp ? (height - 1 - pi / width) : (pi / width);
                        int dstIdx = (dstRow * width + pi % width) * 4;
                        rgba[dstIdx]     = data[pos + 2];
                        rgba[dstIdx + 1] = data[pos + 1];
                        rgba[dstIdx + 2] = data[pos];
                        rgba[dstIdx + 3] = bpp == 4 ? data[pos + 3] : (byte)255;
                    }
                }
            }
        }

        return CropPixels(rgba, width, height, left, top, right, bottom);
    }

    private static BitmapSource? CropDds(byte[] data, int left, int top, int right, int bottom)
    {
        if (data.Length < 128) return null;
        int height  = BitConverter.ToInt32(data, 12);
        int width   = BitConverter.ToInt32(data, 16);
        int pfFlags = BitConverter.ToInt32(data, 80);
        int fourCC  = BitConverter.ToInt32(data, 84);
        int rgbBits = BitConverter.ToInt32(data, 88);

        bool uncompressed = (pfFlags & 0x40) != 0 && fourCC == 0;
        if (!uncompressed || (rgbBits != 32 && rgbBits != 24)) return null;

        int bpp = rgbBits / 8;
        var rgba = new byte[width * height * 4];
        int pos = 128;

        for (int i = 0; i < width * height && pos + bpp <= data.Length; i++, pos += bpp)
        {
            int idx = i * 4;
            rgba[idx]     = data[pos + 2]; // R
            rgba[idx + 1] = data[pos + 1]; // G
            rgba[idx + 2] = data[pos];     // B
            rgba[idx + 3] = bpp == 4 ? data[pos + 3] : (byte)255;
        }

        return CropPixels(rgba, width, height, left, top, right, bottom);
    }

    private static BitmapSource? CropPixels(byte[] rgba, int width, int height,
                                             int left, int top, int right, int bottom)
    {
        // Coords in INI are on a 512-pixel grid — scale to actual texture size
        double scale = width / 512.0;
        int l = Math.Max(0,      (int)Math.Round(left   * scale));
        int t = Math.Max(0,      (int)Math.Round(top    * scale));
        int r = Math.Min(width,  (int)Math.Round(right  * scale));
        int b = Math.Min(height, (int)Math.Round(bottom * scale));
        int cw = r - l, ch = b - t;
        if (cw <= 0 || ch <= 0) return null;

        var bgra = new byte[cw * ch * 4];
        for (int row = 0; row < ch; row++)
        {
            for (int col = 0; col < cw; col++)
            {
                int src = ((row + t) * width + (col + l)) * 4;
                int dst = (row * cw + col) * 4;
                bgra[dst]     = rgba[src + 2]; // B
                bgra[dst + 1] = rgba[src + 1]; // G
                bgra[dst + 2] = rgba[src];     // R
                bgra[dst + 3] = rgba[src + 3]; // A
            }
        }

        return BitmapSource.Create(cw, ch, 96, 96, PixelFormats.Bgra32, null, bgra, cw * 4);
    }

    // ── Manual override table (ported from web big-utils.ts MANUAL_IMAGE_NAME_OVERRIDES) ──
    // Key = CSF id suffix (lower-case), Value = MappedImage name

    private static readonly Dictionary<string, string> _manualOverrides
        = new(StringComparer.OrdinalIgnoreCase)
    {
        // Common actions
        ["sell"]                             = "SSSell",
        ["setrallypoint"]                    = "SSSetRallyPoint",
        ["stop"]                             = "SSStop",
        ["evacuate"]                         = "SSEvacButton",
        ["structureexit"]                    = "SSEnter",
        ["capturebuilding"]                  = "SSCapture",
        ["attackmove"]                       = "SSAttackMove2",
        ["guard"]                            = "SSGuard",
        ["disarmminesatposition"]            = "SSDisarm",
        ["overcharge"]                       = "SSOvercharge",

        // ── USA ──────────────────────────────────────────────────────────────
        ["constructamericapowerplant"]               = "SAPowerPlant",
        ["supw_constructamericapowerplant"]          = "SAPowerPlantSW_L",
        ["constructamericapatriotbattery"]           = "SAPatriot",
        ["lazr_constructamericapatriotbattery"]      = "SALaserPatr",
        ["supw_constructamericapatriotbattery"]      = "SAMicroPat_L",
        ["constructamericavehiclechinook"]           = "SAChinook",
        ["airf_constructamericavehiclechinook"]      = "SAComChinok",
        ["constructamericainfantrymissiledefender"]  = "SAMissleDefender",
        ["constructamericainfantrypathfinder"]       = "SAPathFinder1",
        ["lasermissileattack"]                       = "SSLaserMissile",

        // USA star powers / strikes
        ["a10thunderboltmissilestrike"]  = "SSA10Attack",
        ["leafletdrop"]                  = "SAleaflet",
        ["leafletdropshort"]             = "SAleaflet",
        ["spectregunship"]               = "SASpGunship",
        ["spectregunshipfromshortcut"]   = "SASpGunship",
        ["paradrop"]                     = "SACParatroopers",
        ["daisycutter"]                  = "SACDaisyCutter",
        ["spydrone"]                     = "SAScout",
        ["spysatellite"]                 = "SSSpySat",
        ["nohotkeyspysatellite"]         = "SSSpySat",
        ["ciaintelligence"]              = "SSCIA",
        ["ciaintelligenceshortcut"]      = "SSCIA",
        ["emergencyrepair"]              = "SSRepair",
        ["emergencyrepair1"]             = "SSRepair",
        ["emergencyrepair2"]             = "SSRepair2",
        ["emergencyrepair3"]             = "SSRepairDrone",
        ["fireparticleuplinkcannon"]     = "SAParticleCnn",

        // USA numbered star powers
        ["usaa10strike1"]       = "SSA10Attack",
        ["usaa10strike2"]       = "SSA10Attack2",
        ["usaa10strike3"]       = "SAWarthog",
        ["usaparadrop1"]        = "SACParatroopers",
        ["usaparadrop2"]        = "SACParatroopers2",
        ["usaparadrop3"]        = "SACParatroopers3",
        ["usadaisycutter"]      = "SACDaisyCutter",
        ["usaleafletdrop"]      = "SAleaflet",
        ["usaspectregunship1"]  = "SASpGunship_L",
        ["usaspectregunship2"]  = "SASpGunship2_L",
        ["usaspectregunship3"]  = "SASpGunship3",
        ["usastealthfighter"]   = "SAStealth_L",
        ["usapaladin"]          = "SAPaladin_L",
        ["usaspydrone"]         = "SAScout",
        ["usapathfinder"]       = "SAPathFinder1",

        // USA battle plans
        ["initiatebattleplanbombardment"]      = "SSBattlePlanBombardment",
        ["initiatebattleplanholdtheline"]      = "SSBattlePlanHoldTheLine",
        ["initiatebattleplansearchanddestroy"] = "SSBattlePlanSearch",

        // USA upgrades
        ["upgradeamericaadvancedcontrolrods"]      = "SSControlRods",
        ["supw_upgradeamericaadvancedcontrolrods"] = "SACntrlRds",
        ["upgradeamericaflashbanggrenade"]         = "SSFlashBang",
        ["flashbanggrenademode"]                   = "SSFlashbang",
        ["upgradeamericarangercapturebuilding"]    = "SSCapture",
        ["upgradeamericamoab"]                     = "SSMOAB",
        ["upgradeamericaadvancedtraining"]         = "SSAdvancedTraining",
        ["advancedtraining"]                       = "SSAdvancedTraining",
        ["upgradeamericasupplylines"]              = "SSSupplyLines",
        ["upgradeamericachemicalsuits"]            = "SAchemsuit",
        ["upgradeamericacompositearmor"]           = "SSCompositeArmor",
        ["upgradeamericadronearmor"]               = "SSDroneArmor",
        ["upgradeamericasentrydronegun"]           = "SSSentryDroneGun",
        ["upgradeamericatowmissile"]               = "SSTowMissile",
        ["upgradecomancherocketpods"]              = "SSRocketPods",
        ["upgradeamericacountermeasures"]          = "SSCounterMeasures",
        ["upgradeamericalasermissiles"]            = "SSLaserMissile",
        ["upgradeamericabunkerbusters"]            = "SSBunkerBuster",

        // ── China ────────────────────────────────────────────────────────────
        ["upgradechinamines"]              = "SSMines",
        ["upgradechinaradar"]              = "SSRadarUpgrade",
        ["emppulse"]                       = "SSEMP",
        ["frenzy"]                         = "SNFrenzy01",
        ["artillerybarrage"]               = "SSBarrage",
        ["carpetbomb"]                     = "SNCBomber",
        ["neutronmissile"]                 = "SNNukeLauncher",
        ["neutronwarhead"]                 = "SNNeutShell",
        ["cashhack"]                       = "SSCashHack",
        ["nuke_constructchinapowerplant"]  = "SNAdvReactor",

        // China star powers
        ["chinaartillerybarrage"]          = "SSBarrage",
        ["chinaartillerybarrage2"]         = "SSBarrage2",
        ["chinaartillerybarrage3"]         = "SSBarrage3",
        ["chinacarpetbomb"]                = "SNCBomber",
        ["chinaemppulse"]                  = "SSEMP",
        ["chinacashhack1"]                 = "SSCashHack",
        ["chinacashhack2"]                 = "SSCashHack2",
        ["chinacashhack3"]                 = "SSCashHack3",
        ["chinafrenzy"]                    = "SNFrenzy01",
        ["chinafrenzy2"]                   = "SNFrenzy02",
        ["chinafrenzy3"]                   = "SNFrenzy03",
        ["chinaredguardtraining"]          = "SSHordeTraining",
        ["infa_chinaredguardtraining"]     = "SNMiniGunnerT",
        ["chinaclustermines"]              = "SSClusterMines",
        ["clustermines"]                   = "SSClusterMines",
        ["chinaartillerytraining"]         = "SSArtilleryTraining",
        ["nuke_chinacarpetbomb"]           = "SSNkeCrptBmb",
        ["nuke_constructglatankbattlemaster"] = "SNNukeBtleMstr_L",
        ["chinatankparadrop1"]             = "SACParatroopers",
        ["chinatankparadrop2"]             = "SACParatroopers2",
        ["chinatankparadrop3"]             = "SACParatroopers3",

        // China upgrades
        ["upgradechinachainguns"]          = "SSChainGuns",
        ["upgradechinablacknapalm"]        = "SSBlackNapalm",
        ["upgradechinaaircraftarmor"]      = "SSAircraftArmor",
        ["upgradechinanationalism"]        = "SSNationalism",
        ["upgradechinasubliminalmessaging"]= "SSSubliminal",
        ["upgradechinaisotopestability"]   = "SSIsotopeStability",
        ["upgradechinauraniumshells"]      = "SSUraniumShells",
        ["upgradechinanucleartanks"]       = "SSNuclearTanks",
        ["upgradechinaneutronshells"]      = "SSNeutronShells",
        ["upgradechinasatellitehackone"]   = "SNSatHackOne",

        // ── GLA ──────────────────────────────────────────────────────────────
        ["upgradeglaboobytrap"]            = "SSBoobyTrap",
        ["upgradeglarebelcapturebuilding"] = "SSCapture",
        ["upgradeglascorpionrocket"]       = "SSScorpionRocket",
        ["upgradeglacamonetting"]          = "SUcamo",
        ["upgradeglaradarvanscan"]         = "SSRadarVanScan",
        ["radarvanscan"]                   = "SSRadarVanScan",
        ["upgradeglaworkerfakecommandset"] = "SUSneakBuildMode",
        ["detonate"]                       = "SSDetonateDemo",
        ["detonatefakebuilding"]           = "SSDetonateDemo",
        ["suicideattack"]                  = "SUSuicideAttk",
        ["rebelambush"]                    = "SSGLAAmbush",
        ["anthraxbomb"]                    = "SSAnthraxBomb",
        ["gpscrambler"]                    = "SUGPS01",
        ["radarvanscansweep"]              = "SSRadarVanScan",

        // GLA star powers
        ["glatechnicaltraining"]           = "SSTechTraining",
        ["glarebelambush1"]                = "SSGLAAmbush",
        ["glarebelambush2"]                = "SSGLAAmbush2",
        ["glarebelambush3"]                = "SSGLAAmbush3",
        ["tox_star_glarebelambush2"]       = "SUToxAmbsh2",
        ["tox_star_glarebelambush3"]       = "SUToxAmbsh3",
        ["ambush"]                         = "SSGLAAmbush",
        ["sneakattack"]                    = "SUSneakAttack",
        ["glaanthraxbomb"]                 = "SSAnthraxBomb",
        ["glasneakattack"]                 = "SUSneakAttack",
        ["glacashbounty1"]                 = "SSCashBounty",
        ["glacashbounty2"]                 = "SSCashBounty2",
        ["glacashbounty3"]                 = "SSCashBounty3",
        ["glamaruadertank"]                = "SUMarauder_L",
        ["glascudlauncher"]                = "SUScudLauncher_L",
        ["glascudstormlaunched"]           = "SUScudLauncher_L",
        ["becomerealglaarmsdealer"]        = "SUArmsDealer_L",
        ["becomerealglabarracks"]          = "SUBarracks_L",
        ["becomerealglablackmarket"]       = "SUBlackMarket_L",
        ["becomerealglasupplystash"]       = "SUSupplyStash_L",
        ["constructglavehiclequadcannon"]  = "SuQuadCannon",
    };
}
