using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace LauncherWinUI.Services;

internal static class CommandCardHotkeyService
{
    private static readonly Regex TrailingHotkeyParen = new(
        @"\s*\(\s*&[A-Za-z0-9]\s*\)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal readonly record struct SlotBinding(
        string LabelCsfId,
        string ImageCsfId,
        string Army,
        string LabelText);

    public static void SlotNumberToGrid(int slotNumber1Based, out int row, out int col)
    {
        int i = Math.Clamp(slotNumber1Based, 1, 14) - 1;
        row = i / 7;
        col = i % 7;
    }

    /// <summary>Removes &amp; markers and a trailing (&amp;X) hotkey suffix.</summary>
    public static string StripHotkeyMarkup(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        string s = TrailingHotkeyParen.Replace(text, "");
        s = s.Replace("&", "");
        return s.Trim();
    }

    /// <summary>Places the hotkey at the end, e.g. "Control Rods" + F → "Control Rods (&amp;F)".</summary>
    public static string ApplyHotkeyCharToLabel(string text, char key)
    {
        string plain = StripHotkeyMarkup(text);
        if (key == '\0') return plain;
        char upper = char.ToUpperInvariant(key);
        return $"{plain} (&{upper})";
    }

    /// <summary>Sets the letter after &amp;, or appends (&amp;KEY) when no marker exists.</summary>
    public static string SetHotkeyCharInLabel(string text, char key)
    {
        if (key == '\0') return StripHotkeyMarkup(text);

        char upper = char.ToUpperInvariant(key);
        int idx = HotkeyPainter.FindHotkeyMarkerIndex(text);
        if (idx >= 0 && idx + 1 < text.Length)
        {
            char[] chars = text.ToCharArray();
            chars[idx + 1] = upper;
            return new string(chars);
        }

        return ApplyHotkeyCharToLabel(text, upper);
    }

    /// <summary>
    /// General-variant label from vanilla CSF hotkey position — e.g.
    /// «Tunnel &amp;Network» + «Toxin Network» → «Toxin &amp;Network» (not «Toxin Network (&amp;N)»).
    /// </summary>
    public static string GraftVariantLabelFromVanilla(string variantDisplayPlain, string vanillaTextWithHotkey)
    {
        variantDisplayPlain = StripHotkeyMarkup(variantDisplayPlain).Trim();
        if (string.IsNullOrEmpty(variantDisplayPlain)) return variantDisplayPlain;

        int ampIdx = HotkeyPainter.FindHotkeyMarkerIndex(vanillaTextWithHotkey);
        if (ampIdx < 0 || ampIdx + 1 >= vanillaTextWithHotkey.Length)
        {
            char hk = HotkeyPainter.ExtractHotkeyChar(vanillaTextWithHotkey);
            return hk == '\0' ? variantDisplayPlain : ApplyHotkeyCharToLabel(variantDisplayPlain, hk);
        }

        string vanillaPlain = StripHotkeyMarkup(vanillaTextWithHotkey);
        int plainIdx = 0;
        for (int i = 0; i <= ampIdx; i++)
        {
            if (vanillaTextWithHotkey[i] != '&')
                plainIdx++;
        }

        if (plainIdx >= vanillaPlain.Length)
        {
            char hk = char.ToUpperInvariant(vanillaTextWithHotkey[ampIdx + 1]);
            return ApplyHotkeyCharToLabel(variantDisplayPlain, hk);
        }

        string vanillaSuffix = vanillaPlain[plainIdx..];
        if (variantDisplayPlain.EndsWith(vanillaSuffix, StringComparison.OrdinalIgnoreCase)
         && variantDisplayPlain.Length >= vanillaSuffix.Length)
        {
            string variantPrefix = variantDisplayPlain[..^vanillaSuffix.Length];
            return variantPrefix + "&" + vanillaSuffix;
        }

        char fallback = char.ToUpperInvariant(vanillaTextWithHotkey[ampIdx + 1]);
        return ApplyHotkeyCharToLabel(variantDisplayPlain, fallback);
    }

    /// <summary>
    /// Assigns a keyboard hotkey to a general-variant display name using vanilla &amp; placement when available.
    /// </summary>
    public static string ApplyHotkeyToVariantLabel(string variantText, char key, string? vanillaTextWithHotkey)
    {
        if (key == '\0') return StripHotkeyMarkup(variantText);

        string plain = StripHotkeyMarkup(variantText);
        if (!string.IsNullOrEmpty(vanillaTextWithHotkey)
         && HotkeyPainter.FindHotkeyMarkerIndex(vanillaTextWithHotkey) >= 0)
        {
            string grafted = GraftVariantLabelFromVanilla(plain, vanillaTextWithHotkey);
            return SetHotkeyCharInLabel(grafted, key);
        }

        return SetHotkeyCharInLabel(plain, key);
    }

    /// <summary>Exactly one &amp; hotkey; letter after &amp;; at least one character after that letter.</summary>
    public static bool TryValidateLabel(string text, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Label cannot be empty.";
            return false;
        }

        if (HotkeyPainter.CountHotkeyMarkers(text) == 0)
        {
            error = "Add & before the shortcut letter (e.g. Control Rods (&F) or Chem&ical).";
            return false;
        }

        if (HotkeyPainter.CountHotkeyMarkers(text) > 1)
        {
            error = "Only one & hotkey marker is allowed.";
            return false;
        }

        int idx = HotkeyPainter.FindHotkeyMarkerIndex(text);
        if (idx + 1 >= text.Length)
        {
            error = "A shortcut letter must follow &.";
            return false;
        }

        if (idx + 2 >= text.Length)
        {
            error = "At least one character must appear after the shortcut letter.";
            return false;
        }

        return true;
    }

    /// <summary>Aynı atlas dosyasındaki birden fazla bölgeye sırayla kısayol harfi boyar (tek stil).</summary>
    public static Dictionary<string, byte[]> BuildTgaPatches(
        IEnumerable<SlotBinding> bindings,
        HotkeyStyle? style = null)
        => BuildTgaPatches(bindings.Select(b => (b, style ?? HotkeyStyle.Default)));

    /// <summary>Her binding için ayrı stil kullanarak atlas yamaları oluşturur.</summary>
    public static Dictionary<string, byte[]> BuildTgaPatches(
        IEnumerable<(SlotBinding Binding, HotkeyStyle Style)> styledBindings,
        IProgress<int>? progress = null)
    {
        var bindings = styledBindings as IReadOnlyList<(SlotBinding Binding, HotkeyStyle Style)>
            ?? styledBindings.ToList();
        int total = bindings.Count;
        var atlasBytes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < total; i++)
        {
            var (b, style) = bindings[i];
            char hk = HotkeyPainter.ExtractHotkeyChar(b.LabelText);
            if (hk != '\0')
            {
                var info = ButtonImageReader.ResolveTgaSlot(b.ImageCsfId, b.Army);
                if (info != null)
                {
                    if (!atlasBytes.TryGetValue(info.EntryName, out byte[]? bytes))
                    {
                        bytes = ButtonImageReader.ReadTgaEntry(info.EntryName);
                    }

                    if (bytes != null)
                    {
                        byte[] painted = TgaPatcher.PaintHotkey(
                            bytes, info.Left, info.Top, info.Right, info.Bottom, hk, style);
                        atlasBytes[info.EntryName] = painted;

                        foreach (var twinName in ButtonImageReader.FindTwinEntryNames(info.EntryName))
                        {
                            if (atlasBytes.ContainsKey(twinName)) continue;
                            byte[]? twinRaw = ButtonImageReader.ReadTgaEntry(twinName);
                            if (twinRaw == null) continue;
                            atlasBytes[twinName] = TgaPatcher.PaintHotkey(
                                twinRaw, info.Left, info.Top, info.Right, info.Bottom, hk, style);
                        }
                    }
                }
            }

            progress?.Report(i + 1);
        }

        return atlasBytes;
    }
}
