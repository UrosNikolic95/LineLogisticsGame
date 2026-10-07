using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using LineLogisticsGame.UI;

namespace LineLogisticsGame.World
{
    // View onto the working area: Position is the world point at the top left of the screen, Zoom is screen pixels per world unit.
    // Middle mouse drag pans, the wheel (or +/-) zooms around the cursor, and holding the mouse at a screen edge scrolls.
    public class Camera
    {
        public Vector2 Position;
        public float Zoom = 1f;

        public const float MinZoom = 0.2f, MaxZoom = 5f;
        private const float ZoomStep = 1.15f;
        private const int EdgeSize = 6;             // pixels from the screen edge where pushing starts
        private const float EdgeSpeed = 900f;       // screen pixels per second

        private bool panning;
        private Vector2 panStart;

        public Matrix View => Matrix.CreateTranslation(-Position.X, -Position.Y, 0) * Matrix.CreateScale(Zoom, Zoom, 1);

        public Vector2 ScreenToWorld(Vector2 screen) => Position + screen / Zoom;
        public Vector2 WorldToScreen(Vector2 world) => (world - Position) * Zoom;

        // wheel: whether the mouse wheel zooms (false while the tool uses it for its own settings)
        public void Update(GameTime gameTime, Input input, int screenWidth, int screenHeight, bool isActive, bool wheel)
        {
            if (!isActive)
            {
                panning = false;
                return;
            }

            Vector2 mouse = input.ScreenMousePosition;

            // middle mouse drag: the world point under the cursor stays under it
            if (input.MiddlePressed)
            {
                panning = true;
                panStart = ScreenToWorld(mouse);
            }
            if (!input.MiddleDown) panning = false;
            if (panning) Position = panStart - mouse / Zoom;

            int zoomSteps = (wheel ? input.ScrollSteps : 0)
                + (input.KeyPressed(Keys.OemPlus) || input.KeyPressed(Keys.Add) ? 1 : 0)
                - (input.KeyPressed(Keys.OemMinus) || input.KeyPressed(Keys.Subtract) ? 1 : 0);
            if (zoomSteps != 0) ZoomAt(mouse, Zoom * MathF.Pow(ZoomStep, zoomSteps));

            // pushing the edge, only while the cursor is inside the window
            bool inside = mouse.X >= 0 && mouse.Y >= 0 && mouse.X < screenWidth && mouse.Y < screenHeight;
            if (inside && !panning)
            {
                Vector2 direction = Vector2.Zero;
                if (mouse.X < EdgeSize) direction.X = -1;
                else if (mouse.X >= screenWidth - EdgeSize) direction.X = 1;
                if (mouse.Y < EdgeSize) direction.Y = -1;
                else if (mouse.Y >= screenHeight - EdgeSize) direction.Y = 1;
                Position += direction * EdgeSpeed * (float)gameTime.ElapsedGameTime.TotalSeconds / Zoom;
            }
        }

        // changes the zoom keeping the world point under screenPoint in place
        public void ZoomAt(Vector2 screenPoint, float zoom)
        {
            Vector2 world = ScreenToWorld(screenPoint);
            Zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
            Position = world - screenPoint / Zoom;
        }
    }
}
