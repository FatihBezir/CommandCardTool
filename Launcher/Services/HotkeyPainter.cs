using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LauncherWinUI.Services;

/// <summary>
/// Draws a hotkey letter on a cropped button icon BitmapSource,
/// matching the web app's drawLetterOnImage() behaviour exactly:
///   • Semi-transparent black box (alpha=199) in the bottom-left corner.
///   • White bold Arial letter centred inside the box.
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
    /// </summary>
    public static BitmapSource Paint(BitmapSource source, char letter)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (letter == '\0') return source;

        double w = source.PixelWidth;
        double h = source.PixelHeight;

        // Mirrors JS: Math.max(Math.floor(btnW * 0.28), 16)
        double boxW = Math.Max(Math.Floor(w * 0.28), 16);
        double boxH = Math.Max(Math.Floor(h * 0.30), 16);
        double boxX = 0;          // left edge of icon
        double boxY = h - boxH;   // bottom edge of icon

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // 1. Draw original icon
            dc.DrawImage(source, new Rect(0, 0, w, h));

            // 2. Semi-transparent black box (a=199 → ~78% opacity)
            dc.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(199, 0, 0, 0)),
                null,
                new Rect(boxX, boxY, boxW, boxH));

            // 3. White bold Arial letter centred in the box
            double fontSize = Math.Max(Math.Floor(boxH * 0.85), 8.0);
            var typeface = new Typeface(
                new FontFamily("Arial"),
                FontStyles.Normal,
                FontWeights.Bold,
                FontStretches.Normal);

            var ft = new FormattedText(
                letter.ToString().ToUpperInvariant(),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                Brushes.White,
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
        return rawLabel[idx + 1];
    }
}
