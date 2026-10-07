using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using LineLogisticsGame.Geometry;

namespace LineLogisticsGame.Screens
{
    // The first screen: a small "made by" with "Uroš Nikolić" below it, for Duration seconds (fading in and out), then the menu.
    public class SplashScreen
    {
        public const float Duration = 5f;
        private const float Fade = 0.8f;
        private const float LineGap = 6f;
        private const string Caption = "made by";
        private const string Name = "Uroš Nikolić";

        private readonly Action done;
        private float time;

        public SplashScreen(Action done)
        {
            this.done = done;
        }

        public void Update(GameTime gameTime)
        {
            if (time >= Duration) return;
            time += (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (time >= Duration) done();
        }

        // captionFont is the small one for "made by", nameFont the large one for the name
        public void Draw(SpriteBatch spriteBatch, SpriteFont captionFont, SpriteFont nameFont, Viewport viewport)
        {
            float alpha = MathHelper.Clamp(Math.Min(time, Duration - time) / Fade, 0f, 1f);
            Vector2 captionSize = captionFont.MeasureString(Caption), nameSize = nameFont.MeasureString(Name);

            // both lines together are centered on the screen
            float top = (viewport.Height - captionSize.Y - LineGap - nameSize.Y) / 2f;
            var captionPosition = new Vector2((viewport.Width - captionSize.X) / 2f, top);
            var namePosition = new Vector2((viewport.Width - nameSize.X) / 2f, top + captionSize.Y + LineGap);

            DrawShape.Flush();
            spriteBatch.Begin();
            spriteBatch.DrawString(captionFont, Caption, Round(captionPosition), Color.White * (alpha * 0.75f));
            spriteBatch.DrawString(nameFont, Name, Round(namePosition), Color.White * alpha);
            spriteBatch.End();
        }

        private static Vector2 Round(Vector2 v) => new Vector2(MathF.Round(v.X), MathF.Round(v.Y));
    }
}
