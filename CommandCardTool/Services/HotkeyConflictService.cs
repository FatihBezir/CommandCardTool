using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LauncherWinUI.Services;

/// <summary>
/// Two buttons on one command card answering to the same key: in game only one of
/// them fires, so a layout containing such a pair must not be written to the BIG.
/// </summary>
internal static class HotkeyConflictService
{
    /// <param name="Card">Display name, e.g. "USA General / USA Dozer".</param>
    internal readonly record struct SlotEntry(string Card, string CsfId, string LabelText);

    internal readonly record struct Conflict(string Card, char Key, IReadOnlyList<string> Labels);

    /// <summary>Every card where two distinct buttons share a hotkey letter.</summary>
    public static List<Conflict> Find(IEnumerable<SlotEntry> slots)
    {
        var byCard = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var slot in slots)
        {
            if (!byCard.TryGetValue(slot.Card, out var seen))
                byCard[slot.Card] = seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // The same button can occupy several slots of a card — keep one entry per
            // button, otherwise it would always "conflict" with itself.
            seen[slot.CsfId] = slot.LabelText;
        }

        var result = new List<Conflict>();
        foreach (var (card, buttons) in byCard)
        {
            var byKey = new Dictionary<char, List<string>>();
            foreach (var (_, text) in buttons)
            {
                char key = HotkeyPainter.ExtractHotkeyChar(text);
                if (key == '\0') continue;
                if (!byKey.TryGetValue(key, out var list))
                    byKey[key] = list = new List<string>();
                list.Add(CommandCardHotkeyService.StripHotkeyMarkup(text));
            }

            foreach (var (key, labels) in byKey)
                if (labels.Count > 1)
                    result.Add(new Conflict(card, key, labels));
        }

        return result;
    }

    /// <summary>
    /// Conflicts in <paramref name="current"/> that <paramref name="baseline"/> did not
    /// already have. Vanilla ships 20 of them — every GLA worker card lists
    /// «S&amp;upply Stash» next to «Fake S&amp;upply Stash» — so blocking on the raw set
    /// would make saving impossible even with no edits at all.
    /// </summary>
    public static List<Conflict> FindNew(IEnumerable<SlotEntry> baseline, IEnumerable<SlotEntry> current)
    {
        var known = new HashSet<string>(Find(baseline).Select(Identity), StringComparer.OrdinalIgnoreCase);
        return Find(current).Where(c => !known.Contains(Identity(c))).ToList();
    }

    private static string Identity(Conflict c)
        => c.Card + "|" + c.Key + "|" + string.Join(",", c.Labels.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));

    public static string Describe(IReadOnlyList<Conflict> conflicts, int max = 6)
    {
        var sb = new StringBuilder();
        foreach (var c in conflicts.Take(max))
            sb.AppendLine($"• {c.Card} — [{c.Key}]: {string.Join(" ↔ ", c.Labels)}");
        if (conflicts.Count > max)
            sb.AppendLine($"• … +{conflicts.Count - max}");
        return sb.ToString().TrimEnd();
    }
}
