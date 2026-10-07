using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using LineLogisticsGame.World;

namespace LineLogisticsGame.UI
{
    // Keeps current and previous keyboard/mouse state so tools can detect presses and releases.
    public class Input
    {
        private KeyboardState keyboard, previousKeyboard;
        private MouseState mouse, previousMouse;

        public void Update()
        {
            previousKeyboard = keyboard;
            previousMouse = mouse;
            keyboard = Keyboard.GetState();
            mouse = Mouse.GetState();
        }

        // When set, MousePosition is in world coordinates (what the tools work in); the palette and other UI use ScreenMousePosition.
        public Camera Camera;

        public Vector2 ScreenMousePosition => mouse.Position.ToVector2();
        public Vector2 MousePosition => Camera?.ScreenToWorld(ScreenMousePosition) ?? ScreenMousePosition;

        public bool LeftDown => mouse.LeftButton == ButtonState.Pressed;
        public bool LeftPressed => LeftDown && previousMouse.LeftButton == ButtonState.Released;
        public bool LeftReleased => !LeftDown && previousMouse.LeftButton == ButtonState.Pressed;
        public bool MiddleDown => mouse.MiddleButton == ButtonState.Pressed;
        public bool MiddlePressed => MiddleDown && previousMouse.MiddleButton == ButtonState.Released;
        public bool RightPressed => mouse.RightButton == ButtonState.Pressed && previousMouse.RightButton == ButtonState.Released;

        // wheel notches turned since the last frame, positive away from the user
        public int ScrollSteps => (mouse.ScrollWheelValue - previousMouse.ScrollWheelValue) / 120;

        public bool KeyDown(Keys key) => keyboard.IsKeyDown(key);
        public bool KeyPressed(Keys key) => keyboard.IsKeyDown(key) && previousKeyboard.IsKeyUp(key);

        public bool Shift => KeyDown(Keys.LeftShift) || KeyDown(Keys.RightShift);
        public bool Ctrl => KeyDown(Keys.LeftControl) || KeyDown(Keys.RightControl);
        public bool Alt => KeyDown(Keys.LeftAlt) || KeyDown(Keys.RightAlt);
    }
}
