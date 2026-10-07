using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using LineLogisticsGame.Geometry;

namespace LineLogisticsGame.UI
{
    // A labelled button in the style of the recipe screen's: a filled box that lights up when highlighted, dimmed when disabled.
    public static class Button
    {
        private static readonly Color Normal = new Color(70, 70, 84);
        private static readonly Color Highlighted = new Color(58, 96, 150);
        private static readonly Color Border = new Color(225, 225, 230);

        // the box; the label is drawn by DrawLabel, inside a SpriteBatch Begin/End
        public static void DrawBox(Rectangle bounds, bool highlighted, bool enabled = true)
        {
            DrawShape.DrawFilledRectangle(bounds, (enabled && highlighted ? Highlighted : Normal) * (enabled ? 1f : 0.5f));
            DrawShape.DrawRectangle(bounds, 1f, Border * (!enabled ? 0.2f : highlighted ? 0.8f : 0.4f));
        }

        public static void DrawLabel(SpriteBatch spriteBatch, SpriteFont font, Rectangle bounds, string label, bool enabled = true)
        {
            Vector2 position = bounds.Center.ToVector2() - font.MeasureString(label) / 2f;
            spriteBatch.DrawString(font, label, new Vector2(System.MathF.Round(position.X), System.MathF.Round(position.Y)), Color.White * (enabled ? 1f : 0.35f));
        }
    }
}
