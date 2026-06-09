using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Globalization;

namespace LauncherWinUI.Services;

/// <summary>
/// Paints a hotkey letter directly onto a raw TGA atlas byte array at the
/// specified MappedImage crop region, and returns the modified bytes.
/// Mirrors the web site's drawLetterOnImage() logic.
/// </summary>
internal static class TgaPatcher
{
    /// <summary>
    /// Reads the TGA/DDS atlas, paints the hotkey letter at the given 512-grid
    /// coordinates, and returns the re-encoded bytes (same format as input).
    /// Returns the original bytes unchanged on failure.
    /// </summary>
    public static byte[] PaintHotkey(byte[] atlasBytes,
                                     int left, int top, int right, int bottom,
                                     char letter)
    {
        try
        {
            bool isDds = atlasBytes.Length > 4
                      && atlasBytes[0] == 'D' && atlasBytes[1] == 'D'
                      && atlasBytes[2] == 'S' && atlasBytes[3] == ' ';

            // Decode atlas to RGBA
            int atlasW, atlasH;
            byte[] rgba;
            if (isDds)
            {
                if (!DecodeDds(atlasBytes, out atlasW, out atlasH, out rgba))
                    return atlasBytes;
            }
            else
            {
                if (!DecodeTga(atlasBytes, out atlasW, out atlasH, out rgba))
                    return atlasBytes;
            }

            // Scale 512-grid coords to actual pixel coords
            double scale = atlasW / 512.0;
            int l = (int)Math.Round(left   * scale);
            int t = (int)Math.Round(top    * scale);
            int r = (int)Math.Round(right  * scale);
            int b = (int)Math.Round(bottom * scale);
            int cw = r - l, ch = b - t;
            if (cw <= 0 || ch <= 0) return atlasBytes;

            // Paint the hotkey using WPF rendering into a cropped bitmap
            var cropBmp = RgbaToBitmapSource(rgba, atlasW, atlasH, l, t, cw, ch);
            if (cropBmp == null) return atlasBytes;

            var painted = PaintLetter(cropBmp, letter);

            // Blit painted crop back into RGBA array
            var pixels = new byte[cw * ch * 4];
            painted.CopyPixels(pixels, cw * 4, 0);

            for (int row = 0; row < ch; row++)
            {
                for (int col = 0; col < cw; col++)
                {
                    int src = (row * cw + col) * 4;
                    int dst = ((t + row) * atlasW + (l + col)) * 4;
                    // WPF gives BGRA, our rgba array is RGBA
                    rgba[dst]     = pixels[src + 2]; // R
                    rgba[dst + 1] = pixels[src + 1]; // G
                    rgba[dst + 2] = pixels[src];     // B
                    rgba[dst + 3] = pixels[src + 3]; // A
                }
            }

            // Re-encode back to original format
            return isDds
                ? EncodeDdsUncompressed(atlasW, atlasH, rgba, atlasBytes)
                : EncodeTga(atlasW, atlasH, rgba, atlasBytes);
        }
        catch
        {
            return atlasBytes;
        }
    }

    // ── WPF letter painter ──────────────────────────────────────────────────

    private static BitmapSource PaintLetter(BitmapSource source, char letter)
    {
        double w = source.PixelWidth, h = source.PixelHeight;
        double boxW = Math.Max(Math.Floor(w * 0.28), 16);
        double boxH = Math.Max(Math.Floor(h * 0.30), 16);
        double boxY = h - boxH;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(source, new Rect(0, 0, w, h));
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(199, 0, 0, 0)),
                             null, new Rect(0, boxY, boxW, boxH));

            double fontSize = Math.Max(Math.Floor(boxH * 0.85), 8.0);
            var ft = new FormattedText(
                letter.ToString().ToUpperInvariant(),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Arial"), FontStyles.Normal,
                             FontWeights.Bold, FontStretches.Normal),
                fontSize, Brushes.White, 1.0);

            dc.DrawText(ft, new Point((boxW - ft.Width) / 2.0, boxY + (boxH - ft.Height) / 2.0));
        }

        var rtb = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    // ── RGBA helpers ────────────────────────────────────────────────────────

    private static BitmapSource? RgbaToBitmapSource(byte[] rgba, int w, int h,
                                                     int cropL, int cropT, int cw, int ch)
    {
        var bgra = new byte[cw * ch * 4];
        for (int row = 0; row < ch; row++)
        {
            for (int col = 0; col < cw; col++)
            {
                int src = ((cropT + row) * w + (cropL + col)) * 4;
                int dst = (row * cw + col) * 4;
                bgra[dst]     = rgba[src + 2]; // B
                bgra[dst + 1] = rgba[src + 1]; // G
                bgra[dst + 2] = rgba[src];     // R
                bgra[dst + 3] = rgba[src + 3]; // A
            }
        }
        return BitmapSource.Create(cw, ch, 96, 96, PixelFormats.Bgra32, null, bgra, cw * 4);
    }

    // ── TGA decode/encode ───────────────────────────────────────────────────

    private static bool DecodeTga(byte[] data, out int width, out int height, out byte[] rgba)
    {
        width = height = 0; rgba = Array.Empty<byte>();
        if (data.Length < 18) return false;

        int idLen     = data[0];
        int imageType = data[2];
        width         = data[12] | (data[13] << 8);
        height        = data[14] | (data[15] << 8);
        int bpp       = data[16] / 8;
        int desc      = data[17];
        bool bottomUp = (desc & 0x20) == 0;

        if ((imageType != 2 && imageType != 10) || (bpp != 3 && bpp != 4)) return false;
        if (width == 0 || height == 0) return false;

        int pos = 18 + idLen;
        if (data[1] == 1) { int ce = data[7]; pos += (data[5] | (data[6] << 8)) * (ce / 8); }

        rgba = new byte[width * height * 4];

        if (imageType == 2)
        {
            for (int i = 0; i < width * height && pos + bpp <= data.Length; i++, pos += bpp)
            {
                int row = bottomUp ? (height - 1 - i / width) : (i / width);
                int di = (row * width + i % width) * 4;
                rgba[di] = data[pos + 2]; rgba[di+1] = data[pos+1];
                rgba[di+2] = data[pos];   rgba[di+3] = bpp == 4 ? data[pos+3] : (byte)255;
            }
        }
        else // RLE
        {
            int pi = 0;
            while (pi < width * height && pos < data.Length)
            {
                int hdr = data[pos++]; bool rle = (hdr & 0x80) != 0; int cnt = (hdr & 0x7F) + 1;
                if (rle)
                {
                    if (pos + bpp > data.Length) break;
                    byte r2 = data[pos+2], g2 = data[pos+1], b2 = data[pos];
                    byte a2 = bpp == 4 ? data[pos+3] : (byte)255; pos += bpp;
                    for (int j = 0; j < cnt && pi < width*height; j++, pi++)
                    { int row = bottomUp?(height-1-pi/width):(pi/width); int di=(row*width+pi%width)*4;
                      rgba[di]=r2; rgba[di+1]=g2; rgba[di+2]=b2; rgba[di+3]=a2; }
                }
                else
                {
                    for (int j = 0; j < cnt && pi < width*height && pos+bpp<=data.Length; j++, pi++, pos+=bpp)
                    { int row = bottomUp?(height-1-pi/width):(pi/width); int di=(row*width+pi%width)*4;
                      rgba[di]=data[pos+2]; rgba[di+1]=data[pos+1]; rgba[di+2]=data[pos];
                      rgba[di+3]=bpp==4?data[pos+3]:(byte)255; }
                }
            }
        }
        return true;
    }

    private static byte[] EncodeTga(int width, int height, byte[] rgba, byte[] original)
    {
        // Preserve original header bytes (bytes 0-17) exactly, rewrite pixel data
        if (original.Length < 18) return original;
        int idLen = original[0];
        int bpp   = original[16] / 8;
        if (bpp != 3 && bpp != 4) return original;

        int headerLen = 18 + idLen;
        var result = new byte[headerLen + width * height * bpp];
        Array.Copy(original, result, headerLen);
        // Force uncompressed (type 2)
        result[2] = 2;

        bool bottomUp = (original[17] & 0x20) == 0;
        for (int i = 0; i < width * height; i++)
        {
            int row = bottomUp ? (height - 1 - i / width) : (i / width);
            int src = (row * width + i % width) * 4;
            int dst = headerLen + i * bpp;
            result[dst]     = rgba[src + 2]; // B
            result[dst + 1] = rgba[src + 1]; // G
            result[dst + 2] = rgba[src];     // R
            if (bpp == 4) result[dst + 3] = rgba[src + 3];
        }
        return result;
    }

    // ── DDS decode/encode (uncompressed BGRA/BGR only) ──────────────────────

    private static bool DecodeDds(byte[] data, out int width, out int height, out byte[] rgba)
    {
        width = height = 0; rgba = Array.Empty<byte>();
        if (data.Length < 128) return false;
        height  = BitConverter.ToInt32(data, 12);
        width   = BitConverter.ToInt32(data, 16);
        int pfFlags = BitConverter.ToInt32(data, 80);
        int fourCC  = BitConverter.ToInt32(data, 84);
        int rgbBits = BitConverter.ToInt32(data, 88);
        if ((pfFlags & 0x40) == 0 || fourCC != 0) return false;
        if (rgbBits != 32 && rgbBits != 24) return false;
        int bpp = rgbBits / 8;
        rgba = new byte[width * height * 4];
        int pos = 128;
        for (int i = 0; i < width * height && pos + bpp <= data.Length; i++, pos += bpp)
        {
            int di = i * 4;
            rgba[di] = data[pos+2]; rgba[di+1] = data[pos+1]; rgba[di+2] = data[pos];
            rgba[di+3] = bpp == 4 ? data[pos+3] : (byte)255;
        }
        return true;
    }

    private static byte[] EncodeDdsUncompressed(int width, int height, byte[] rgba, byte[] original)
    {
        if (original.Length < 128) return original;
        int rgbBits = BitConverter.ToInt32(original, 88);
        int bpp = rgbBits / 8;
        if (bpp != 3 && bpp != 4) return original;

        var result = new byte[128 + width * height * bpp];
        Array.Copy(original, result, 128); // preserve header
        int pos = 128;
        for (int i = 0; i < width * height; i++, pos += bpp)
        {
            int src = i * 4;
            result[pos]     = rgba[src + 2]; // B
            result[pos + 1] = rgba[src + 1]; // G
            result[pos + 2] = rgba[src];     // R
            if (bpp == 4) result[pos + 3] = rgba[src + 3];
        }
        return result;
    }
}
