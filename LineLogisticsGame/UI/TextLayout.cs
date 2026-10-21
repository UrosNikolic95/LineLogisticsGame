using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace LineLogisticsGame.UI
{
    // Centers text on its letters rather than on SpriteFont.MeasureString's box, which has room above the capitals and below
    // the baseline for accents and descenders and so puts labels off center. Horizontally it centers the ink of the string itself;
    // vertically the band from the top of the capitals to the baseline, so every label sits the same way whatever its letters.
    public static class TextLayout
    {
        private static readonly Dictionary<SpriteFont, (float Top, float Baseline)> capBands = new Dictionary<SpriteFont, (float, float)>();

        // where to draw text (top left, as for DrawString) so it is centered on center; rounded to whole pixels
        public static Vector2 Centered(SpriteFont font, string text, Vector2 center)
        {
            var (left, right) = InkSpan(font, text);
            var (top, baseline) = CapBand(font);
            return new Vector2(MathF.Round(center.X - (left + right) / 2f), MathF.Round(center.Y - (top + baseline) / 2f));
        }

        // the same, for text placed only vertically: the y to draw it at so it is centered on centerY
        public static float CenteredY(SpriteFont font, float centerY)
        {
            var (top, baseline) = CapBand(font);
            return MathF.Round(centerY - (top + baseline) / 2f);
        }

        // from where DrawString puts the first letter's ink to where the last one's ends (a single line, like the labels)
        private static (float Left, float Right) InkSpan(SpriteFont font, string text)
        {
            var glyphs = font.GetGlyphs();
            float x = 0, left = float.MaxValue, right = float.MinValue;
            bool first = true;
            foreach (char c in text)
            {
                if (!glyphs.TryGetValue(c, out var glyph) && !(font.DefaultCharacter is char d && glyphs.TryGetValue(d, out glyph))) continue;
                // the same steps as SpriteBatch.DrawString
                if (first)
                {
                    x = Math.Max(glyph.LeftSideBearing, 0);
                    first = false;
                }
                else x += font.Spacing + glyph.LeftSideBearing;

                if (glyph.BoundsInTexture.Width > 0 && !char.IsWhiteSpace(c))
                {
                    float inkLeft = x + glyph.Cropping.X;
                    left = Math.Min(left, inkLeft);
                    right = Math.Max(right, inkLeft + glyph.BoundsInTexture.Width);
                }
                x += glyph.Width + glyph.RightSideBearing;
            }
            if (left > right)
            {
                float width = font.MeasureString(text).X;
                return (0, width);
            }
            return (left, right);
        }

        // how far below the top of a line the capitals start and the baseline is, from the letter H
        private static (float Top, float Baseline) CapBand(SpriteFont font)
        {
            if (capBands.TryGetValue(font, out var band)) return band;
            band = font.GetGlyphs().TryGetValue('H', out var h) && h.BoundsInTexture.Height > 0
                ? (h.Cropping.Y, h.Cropping.Y + h.BoundsInTexture.Height)
                : (0, font.LineSpacing);
            capBands[font] = band;
            return band;
        }
    }
}
