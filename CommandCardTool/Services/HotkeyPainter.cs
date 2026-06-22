using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LauncherWinUI.Services;

/// <summary>
/// Visual style for the hotkey letter painted on button images.
/// </summary>
internal record HotkeyStyle(Color LetterColor, string FontFamily, Color BoxColor, byte BoxAlpha = 199, double LetterSizeRatio = 0.85)
{
    public static readonly HotkeyStyle Default = new(Colors.White, "Arial", Colors.Black);
}

/// <summary>
/// Draws a hotkey letter on a cropped button icon BitmapSource,
/// matching the web app's drawLetterOnImage() behaviour exactly:
///   • Semi-transparent black box (alpha=199) in the bottom-left corner.
///   • Bold letter centred inside the box (font/color configurable via HotkeyStyle).
///   • Box width  = max(floor(btnW × 0.28), 16)
///   • Box height = max(floor(btnH × 0.30), 16)
///   • boxX = left edge (0 when source is already a cropped icon)
///   • boxY = bottom - boxH
/// </summary>
internal static class HotkeyPainter
{
    /// <summary>
    /// Overlays the hotkey letter onto <paramref name="source"/> and returns a new frozen BitmapSource.
    /// Returns <paramref name="source"/> unchanged when <paramref name="letter"/> is '\0'.
    /// Pass a <paramref name="style"/> to customise the letter colour and font; defaults to white Arial.
    /// </summary>
    public static BitmapSource Paint(BitmapSource source, char letter, HotkeyStyle? style = null)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (letter == '\0') return source;

        var s = style ?? HotkeyStyle.Default;

        double w = source.PixelWidth;
        double h = source.PixelHeight;

        // Box scales with letter size (default ratio = 0.85)
        double sizeScale = s.LetterSizeRatio / 0.85;
        double boxW = Math.Max(Math.Floor(w * 0.28 * sizeScale), 12);
        double boxH = Math.Max(Math.Floor(h * 0.30 * sizeScale), 12);
        double boxX = 0;          // left edge of icon
        double boxY = h - boxH;   // bottom edge of icon

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // 1. Draw original icon
            dc.DrawImage(source, new Rect(0, 0, w, h));

            // 2. Semi-transparent box
            dc.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(s.BoxAlpha, s.BoxColor.R, s.BoxColor.G, s.BoxColor.B)),
                null,
                new Rect(boxX, boxY, boxW, boxH));

            // 3. Bold letter centred in the box
            double fontSize = Math.Max(Math.Floor(boxH * s.LetterSizeRatio), 8.0);
            var typeface = new Typeface(
                new FontFamily(s.FontFamily),
                FontStyles.Normal,
                FontWeights.Bold,
                FontStretches.Normal);

            var ft = new FormattedText(
                letter.ToString().ToUpperInvariant(),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                new SolidColorBrush(s.LetterColor),
                1.0);  // pixelsPerDip=1 (96 DPI baseline)

            // Centre horizontally and vertically inside the box
            double tx = boxX + (boxW - ft.Width)  / 2.0;
            double ty = boxY + (boxH - ft.Height) / 2.0;
            dc.DrawText(ft, new Point(tx, ty));
        }

        double dpiX = source.DpiX > 0 ? source.DpiX : 96.0;
        double dpiY = source.DpiY > 0 ? source.DpiY : 96.0;

        var rtb = new RenderTargetBitmap(
            (int)w, (int)h, dpiX, dpiY, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    /// <summary>Counts unescaped &amp; hotkey markers (not &amp;&amp;).</summary>
    public static int CountHotkeyMarkers(string rawLabel)
    {
        if (string.IsNullOrEmpty(rawLabel)) return 0;
        int count = 0;
        for (int i = 0; i < rawLabel.Length - 1; i++)
        {
            if (rawLabel[i] != '&') continue;
            if (rawLabel[i + 1] == '&') { i++; continue; }
            count++;
        }
        return count;
    }

    /// <summary>Index of the hotkey &amp; marker, or -1.</summary>
    public static int FindHotkeyMarkerIndex(string rawLabel)
    {
        if (string.IsNullOrEmpty(rawLabel)) return -1;
        for (int i = 0; i < rawLabel.Length - 1; i++)
        {
            if (rawLabel[i] != '&') continue;
            if (rawLabel[i + 1] == '&') { i++; continue; }
            return i;
        }
        return -1;
    }

    /// <summary>
    /// Extracts the hotkey char from a CSF raw string containing '&amp;X' sequences.
    /// Returns '\0' when no hotkey marker is present.
    /// </summary>
    public static char ExtractHotkeyChar(string rawLabel)
    {
        int idx = FindHotkeyMarkerIndex(rawLabel);
        if (idx < 0 || idx + 1 >= rawLabel.Length) return '\0';
        return char.ToUpperInvariant(rawLabel[idx + 1]);
    }
}
