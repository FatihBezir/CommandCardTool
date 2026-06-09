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
    /// <summary>EXE klasörü (veya ZH_GAME_DIR / game_path.txt override).</summary>
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

    /// <summary>Merges every .csf found in load-order BIGs; first file wins per label key.</summary>
    public static Dictionary<string, string> BuildMergedCsfLabels(string gameDir)
    {
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var bigPath in GetSortedBigPaths(gameDir))
        {
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
