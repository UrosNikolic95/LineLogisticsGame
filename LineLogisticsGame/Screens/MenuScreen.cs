using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using LineLogisticsGame.Geometry;
using Microsoft.Xna.Framework.Input;
using LineLogisticsGame.UI;

namespace LineLogisticsGame.Screens
{
    // A title over a column of buttons: the main menu and the in-game menu.
    // Pick with the mouse, or Up/Down and Enter. Options that can't be used right now (Continue and Load game
    // with no saves) are dimmed.
    public class MenuScreen
    {
        public class Option
        {
            public string Label;
            public Func<bool> IsEnabled = () => true;
            public Action Choose;
        }

        private const int ButtonWidth = 260, ButtonHeight = 48, ButtonGap = 14;

        private readonly Option[] options;
        private readonly Rectangle[] buttons;
        private int selected = -1;
        private Vector2 lastMouse;
        private readonly string title;
        private Vector2 titlePosition;

        public MenuScreen(string title, params Option[] options)
        {
            this.title = title;
            this.options = options;
            buttons = new Rectangle[options.Length];
        }

        public void Layout(int screenWidth, int screenHeight)
        {
            int height = options.Length * ButtonHeight + (options.Length - 1) * ButtonGap;
            int top = (screenHeight - height) / 2 + 40;
            for (int i = 0; i < options.Length; i++)
                buttons[i] = new Rectangle((screenWidth - ButtonWidth) / 2, top + i * (ButtonHeight + ButtonGap), ButtonWidth, ButtonHeight);
            titlePosition = new Vector2(screenWidth / 2f, top - 80);
        }

        public void Update(Input input)
        {
            if (selected >= 0 && !options[selected].IsEnabled()) selected = -1;

            // the mouse selects what it is over, but only when it moves, so it doesn't fight the keyboard
            Vector2 mouse = input.ScreenMousePosition;
            if (mouse != lastMouse)
            {
                selected = -1;
                for (int i = 0; i < buttons.Length; i++)
                    if (buttons[i].Contains(mouse) && options[i].IsEnabled()) selected = i;
                lastMouse = mouse;
            }

            if (input.KeyPressed(Keys.Down)) Step(1);
            if (input.KeyPressed(Keys.Up)) Step(-1);

            if (input.LeftPressed)
            {
                for (int i = 0; i < buttons.Length; i++)
                    if (buttons[i].Contains(mouse) && options[i].IsEnabled()) options[i].Choose();
            }
            else if (input.KeyPressed(Keys.Enter) && selected >= 0)
            {
                options[selected].Choose();
            }
        }

        // moves the selection to the next enabled option that way, wrapping around
        private void Step(int direction)
        {
            int start = selected < 0 ? (direction > 0 ? -1 : 0) : selected;
            for (int n = 1; n <= options.Length; n++)
            {
                int i = ((start + direction * n) % options.Length + options.Length) % options.Length;
                if (options[i].IsEnabled())
                {
                    selected = i;
                    return;
                }
            }
        }

        public void Draw(SpriteBatch spriteBatch, SpriteFont font, SpriteFont titleFont)
        {
            for (int i = 0; i < options.Length; i++)
                Button.DrawBox(buttons[i], i == selected, options[i].IsEnabled());

            DrawShape.Flush();
            spriteBatch.Begin();
            Vector2 position = titlePosition - titleFont.MeasureString(title) / 2f;
            spriteBatch.DrawString(titleFont, title, new Vector2(MathF.Round(position.X), MathF.Round(position.Y)), Color.White);
            for (int i = 0; i < options.Length; i++)
                Button.DrawLabel(spriteBatch, font, buttons[i], options[i].Label, options[i].IsEnabled());
            spriteBatch.End();
        }
    }
}
