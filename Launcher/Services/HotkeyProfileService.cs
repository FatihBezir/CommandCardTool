using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LauncherWinUI.Services;

/// <summary>
/// Ready-made hotkey label profiles (CSF text with &amp; shortcuts) from game BIG files.
/// </summary>
internal static class HotkeyProfileService
{
    internal const string ProfileCurrent = "Current CSF";
    internal const string ProfileLeikeze = "Leikeze";

    private static readonly Dictionary<string, string> ScienceToControlBarKey =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["science:usapaladin"]              = "controlbar:constructamericatankpaladin",
            ["science:usastealthfighter"]       = "controlbar:constructamericajetstealthfighter",
            ["science:usapathfinder"]           = "controlbar:constructamericainfantrypathfinder",
            ["science:usaspydrone"]             = "controlbar:spydrone",
            ["science:chinanukelauncher"]       = "controlbar:constructchinavehiclenukelauncher",
            ["science:glascudlauncher"]         = "controlbar:constructglavehiclescudlauncher",
            ["science:glamaruadertank"]         = "controlbar:constructglatankmarauder",
            ["science:glahijacker"]             = "controlbar:constructglainfantryhijacker",
            ["science:glatechnicaltraining"]    = "controlbar:constructglavehicletechnical",
        };

    private static readonly string[][] LinkGroups =
    {
        new[] { "controlbar:paradrop", "science:usaparadrop1", "science:usaparadrop2", "science:usaparadrop3" },
        new[] { "controlbar:emergencyrepair", "science:emergencyrepair1", "science:emergencyrepair2", "science:emergencyrepair3" },
        new[] { "controlbar:a10thunderboltmissilestrike", "science:usaa10strike1", "science:usaa10strike2", "science:usaa10strike3" },
        new[] { "controlbar:frenzy", "science:chinafrenzy", "science:chinafrenzy2", "science:chinafrenzy3" },
        new[] { "controlbar:cashhack", "science:chinacashhack1", "science:chinacashhack2", "science:chinacashhack3" },
        new[] { "controlbar:artillerybarrage", "science:chinaartillerybarrage", "science:chinaartillerybarrage2", "science:chinaartillerybarrage3" },
        new[] { "controlbar:carpetbomb", "science:chinacarpetbomb", "science:nuke_chinacarpetbomb" },
        new[] { "controlbar:ambush", "science:glarebelambush1", "science:glarebelambush2", "science:glarebelambush3" },
        new[] { "controlbar:sneakattack", "science:glasneakattack" },
        new[] { "controlbar:anthraxbomb", "science:glaanthraxbomb" },
        new[] { "controlbar:gpsscrambler", "science:gpsscrambler" },
        new[] { "science:chinaclustermines", "controlbar:clustermines" },
        new[] { "controlbar:spydrone", "science:usaspydrone" },
        new[] { "controlbar:constructamericainfantrypathfinder", "science:usapathfinder" },
        new[] { "controlbar:leafletdrop", "controlbar:leafletdropshort", "science:usaleafletdrop" },
        new[] { "controlbar:spectregunshipfromshortcut", "controlbar:spectregunship" },
        new[] { "controlbar:daisycutter", "science:usadaisycutter" },
    };

    private static readonly string[] VariantPrefixes =
        { "chem_", "demo_", "infa_", "nuke_", "lazr_", "supw_", "airf_", "slth_", "stlh_", "tox_" };

    public static string? FindLeikezeCsfBigPath(string gameDir)
    {
        if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir)) return null;

        string direct = Path.Combine(gameDir, "!HotkeysLeikezeZH.big");
        if (File.Exists(direct)) return direct;

        return GameBigStack.GetSortedBigPaths(gameDir)
            .FirstOrDefault(p =>
            {
                string name = Path.GetFileName(p);
                return name.Contains("HotkeysLeikeze", StringComparison.OrdinalIgnoreCase)
                    && name.Contains("ZH", StringComparison.OrdinalIgnoreCase)
                    && !name.Contains("Indicators", StringComparison.OrdinalIgnoreCase)
                    && BigCsfReader.ReadFromBig(p).Count > 0;
            });
    }

    public static Dictionary<string, string> LoadProfileLabels(string profileName, string gameDir)
    {
        if (!string.Equals(profileName, ProfileLeikeze, StringComparison.OrdinalIgnoreCase))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string? path = FindLeikezeCsfBigPath(gameDir);
        if (path == null) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return BigCsfReader.ReadFromBig(path);
    }

    public static string? TryGetProfileLabelText(
        IReadOnlyDictionary<string, string> profile,
        string labelCsfId,
        string? imageCsfId = null)
    {
        foreach (var candidate in BuildLookupCandidates(labelCsfId, imageCsfId))
        {
            if (TryGetDirect(profile, candidate, out var text))
                return text;
        }
        return null;
    }

    public static char? TryGetProfileHotkey(
        IReadOnlyDictionary<string, string> profile,
        string labelCsfId,
        string? imageCsfId = null)
    {
        string? text = TryGetProfileLabelText(profile, labelCsfId, imageCsfId);
        if (text == null) return null;
        char hk = HotkeyPainter.ExtractHotkeyChar(text);
        return hk == '\0' ? null : hk;
    }

    public static bool HasVariantPrefix(string bareLower)
    {
        foreach (var pfx in VariantPrefixes)
            if (bareLower.StartsWith(pfx, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    public static string? TryGetVariantTooltipKey(string bareLower)
        => bareLower.ToLowerInvariant() switch
        {
            "chem_constructglatunnelnetwork" => "CONTROLBAR:Chem_ToolTipGLABuildTunnelNetwork",
            "chem_constructglainfantryrebel" => "controlbar:chem_tooltipglabuildrebel",
            "chem_constructglainfantryterrorist" => "controlbar:chem_tooltipglabuildterrorist",
            "demo_constructglademotrap" => "controlbar:tooltipglabuilddemotrap",
            _ => null,
        };

    private static IEnumerable<string> BuildLookupCandidates(string labelCsfId, string? imageCsfId)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();

        void AddId(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            string norm = NormalizeKey(id);
            if (seen.Add(norm)) list.Add(norm);
        }

        AddId(labelCsfId);
        AddId(imageCsfId);

        string normLabel = NormalizeKey(labelCsfId);
        foreach (var group in LinkGroups)
        {
            bool inGroup = false;
            foreach (var member in group)
            {
                if (string.Equals(NormalizeKey(member), normLabel, StringComparison.OrdinalIgnoreCase)
                 || (imageCsfId != null && string.Equals(NormalizeKey(member), NormalizeKey(imageCsfId), StringComparison.OrdinalIgnoreCase)))
                { inGroup = true; break; }
            }
            if (!inGroup) continue;
            foreach (var member in group) AddId(member);
            break;
        }

        AddId(ScienceToControlBarKey.GetValueOrDefault(normLabel));
        if (imageCsfId != null)
            AddId(ScienceToControlBarKey.GetValueOrDefault(NormalizeKey(imageCsfId)));

        string bare = BareKey(normLabel);
        if (CsfVariantKeys.TryGetCommandBarPascalBare(bare, out var pascal))
            AddId("CONTROLBAR:" + pascal);

        return list;
    }

    private static bool TryGetDirect(IReadOnlyDictionary<string, string> profile, string normalizedId, out string text)
    {
        if (profile.TryGetValue(normalizedId, out text!) && HotkeyPainter.ExtractHotkeyChar(text) != '\0')
            return true;

        string bare = BareKey(normalizedId);
        foreach (var kv in profile)
        {
            if (!string.Equals(BareKey(kv.Key), bare, StringComparison.OrdinalIgnoreCase)) continue;
            if (HotkeyPainter.ExtractHotkeyChar(kv.Value) == '\0') continue;
            text = kv.Value;
            return true;
        }

        text = "";
        return false;
    }

    private static string NormalizeKey(string csfId)
    {
        int ci = csfId.IndexOf(':');
        if (ci >= 0)
            return csfId[..ci].ToLowerInvariant() + ":" + csfId[(ci + 1)..].ToLowerInvariant();
        return "controlbar:" + csfId.ToLowerInvariant();
    }

    private static string BareKey(string k)
    {
        int ci = k.IndexOf(':');
        return ci >= 0 ? k[(ci + 1)..] : k;
    }
}
