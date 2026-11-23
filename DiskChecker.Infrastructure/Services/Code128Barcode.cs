using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace DiskChecker.Infrastructure.Services;

/// <summary>
/// ISO/IEC 15417 Code 128 (Code B prefix followed by Code C digit pairs),
/// with modulo-103 checksum and the mandatory 10-module quiet zones.
/// Bars are vector rectangles on the PDF, not a low-resolution image.
/// </summary>
internal static class Code128Barcode
{
    // Element widths, alternating bar/space, indexed by Code 128 symbol value.
    // 0..105: 11 modules; 106 (stop): 13 modules.
    private static readonly string[] Patterns =
    {
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213", "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132", "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211", "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313", "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331", "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111", "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214", "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111", "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141", "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141", "114131", "311141", "411131", "211412", "211214", "211232", "2331112"
    };

    internal static int[] Encode(string value)
    {
        if (value == null || value.Length != 18 ||
            (value[..2] != "DS" && value[..2] != "DL") ||
            value.Skip(2).Any(c => c < '0' || c > '9'))
            throw new ArgumentException("Barcode ID must be DS/DL followed by exactly 16 digits.", nameof(value));

        // Begin in Code Set B for the two-letter namespace, then switch to
        // Code Set C for eight pairs of digits (much shorter on paper).
        var codes = new List<int>(14) { 104, value[0] - 32, value[1] - 32, 99 };
        for (int i = 2; i < value.Length; i += 2)
            codes.Add((value[i] - '0') * 10 + (value[i + 1] - '0'));

        int sum = codes[0];
        for (int i = 1; i < codes.Count; i++)
            sum += i * codes[i];
        codes.Add(sum % 103);
        codes.Add(106);
        return codes.ToArray();
    }

    internal static int TotalModules(string value) =>
        Encode(value).Sum(code => Patterns[code].Sum(width => width - '0'));

    internal static void Draw(SKCanvas canvas, string value, float x, float y, float width, float height)
    {
        int[] codes = Encode(value);
        const int quiet = 10; // both sides; no other text may intrude
        int modules = codes.Sum(code => Patterns[code].Sum(width => width - '0'));
        float moduleWidth = width / (modules + 2 * quiet);
        float cursor = x + quiet * moduleWidth;

        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false, Style = SKPaintStyle.Fill };
        foreach (int code in codes)
        {
            string pattern = Patterns[code];
            for (int i = 0; i < pattern.Length; i++)
            {
                float elementWidth = (pattern[i] - '0') * moduleWidth;
                if (i % 2 == 0)
                    canvas.DrawRect(cursor, y, elementWidth, height, paint);
                cursor += elementWidth;
            }
        }
    }
}
