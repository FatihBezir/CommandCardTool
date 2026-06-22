using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LauncherWinUI.Services;

/// <summary>
/// Zero Hour loads every .big in the game folder in alphabetical order.
/// For duplicate archive paths, the earlier file wins (e.g. !Hotkeys… before EnglishZH.big).
/// </summary>
internal static class GameBigStack
{
    /// <summary>EXE folder or auto-detected Steam Zero Hour install.</summary>
    public static string? DiscoverGameDirectory()
    {
        string dir = GameDirectory.Get();
        return Directory.Exists(dir) ? dir : null;
    }

    public static IReadOnlyList<string> GetSortedBigPaths(string gameDir)
        => Directory.EnumerateFiles(gameDir, "*.big", SearchOption.TopDirectoryOnly)
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>First matching BIG in load order wins (mod ! files beat vanilla).</summary>
    public static Dictionary<string, (string BigPath, int Offset, int Size)> BuildEntryIndex(
        string gameDir,
        Func<string, bool>? nameFilter = null)
    {
        var index = new Dictionary<string, (string, int, int)>(StringComparer.OrdinalIgnoreCase);
        foreach (var bigPath in GetSortedBigPaths(gameDir))
        {
            try
            {
                foreach (var (name, offset, size) in BigArchiveIndex.ReadEntries(bigPath))
                {
                    if (nameFilter != null && !nameFilter(name)) continue;
                    index.TryAdd(name, (bigPath, offset, size));
                }
            }
            catch { /* skip corrupt archive */ }
        }
        return index;
    }

    /// <summary>Ready-made hotkey profile BIGs — excluded from default CSF merge; loaded only when a profile is selected.</summary>
    public static bool IsHotkeyProfileBig(string bigPath)
    {
        string name = Path.GetFileName(bigPath);
        return name.Contains("HotkeysLeikeze", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Merges every .csf found in load-order BIGs; first file wins per label key.</summary>
    /// <param name="excludeProfileBigs">When true, skips !HotkeysLeikeze*.big so «Current CSF» uses the normal game stack only.</param>
    public static Dictionary<string, string> BuildMergedCsfLabels(string gameDir, bool excludeProfileBigs = true)
    {
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var bigPath in GetSortedBigPaths(gameDir))
        {
            if (excludeProfileBigs && IsHotkeyProfileBig(bigPath)) continue;
            foreach (var kv in BigCsfReader.ReadFromBig(bigPath))
                labels.TryAdd(kv.Key, kv.Value);
        }
        return labels;
    }

    /// <summary>Vanilla English CSF carrier for rebuild/save (non-! file preferred).</summary>
    public static string? FindVanillaEnglishBig(string gameDir)
    {
        string? fallback = null;
        foreach (var bigPath in GetSortedBigPaths(gameDir))
        {
            var name = Path.GetFileName(bigPath);
            if (name.StartsWith("!", StringComparison.Ordinal)) continue;
            if (!BigCsfReader.ReadFromBig(bigPath).Any()) continue;
            if (name.Contains("English", StringComparison.OrdinalIgnoreCase))
                return bigPath;
            fallback ??= bigPath;
        }
        return fallback;
    }
}
