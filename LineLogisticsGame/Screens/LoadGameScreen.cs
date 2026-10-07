using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using LineLogisticsGame.Geometry;
using Microsoft.Xna.Framework.Input;
using LineLogisticsGame.Persistence;
using LineLogisticsGame.UI;

namespace LineLogisticsGame.Screens
{
    // Every saved game, most recent first, with when it was saved and its points. Clicking one (or Up/Down and Enter) loads it;
    // the wheel scrolls a long list. Back (or Esc) goes back to the menu.
    public class LoadGameScreen
    {
        private const int RowWidth = 460, RowHeight = 44, RowGap = 8;

        private readonly Action<string> load;
        private readonly Action back;
        private List<(string Path, SaveGame Save)> saves = new List<(string, SaveGame)>();
        private Rectangle list;             // where rows are shown
        private Rectangle backButton;
        private int scroll;                 // rows scrolled past
        private int selected = -1;
        private Vector2 lastMouse;
        private Vector2 titlePosition;

        public LoadGameScreen(Action<string> load, Action back)
        {
            this.load = load;
            this.back = back;
        }

        // reads the saves folder again; call when the screen is opened
        public void Refresh()
        {
            saves = SaveGame.All();
            scroll = 0;
            selected = saves.Count > 0 ? 0 : -1;
        }

        private int VisibleRows => Math.Max(1, (list.Height + RowGap) / (RowHeight + RowGap));

        public void Layout(int screenWidth, int screenHeight)
        {
            int top = 140;
            list = new Rectangle((screenWidth - RowWidth) / 2, top, RowWidth, Math.Max(RowHeight, screenHeight - top - 120));
            backButton = new Rectangle((screenWidth - 160) / 2, list.Bottom + 30, 160, 40);
            titlePosition = new Vector2(screenWidth / 2f, top - 60);
            scroll = Math.Clamp(scroll, 0, Math.Max(0, saves.Count - VisibleRows));
        }

        private Rectangle RowBounds(int index) =>
            new Rectangle(list.X, list.Y + (index - scroll) * (RowHeight + RowGap), RowWidth, RowHeight);

        private bool IsVisible(int index) => index >= scroll && index < scroll + VisibleRows;

        public void Update(Input input)
        {
            Vector2 mouse = input.ScreenMousePosition;
            if (list.Contains(mouse)) scroll = Math.Clamp(scroll - input.ScrollSteps, 0, Math.Max(0, saves.Count - VisibleRows));

            if (mouse != lastMouse)
            {
                for (int i = 0; i < saves.Count; i++)
                    if (IsVisible(i) && RowBounds(i).Contains(mouse)) selected = i;
                lastMouse = mouse;
            }

            if (saves.Count > 0)
            {
                if (input.KeyPressed(Keys.Down)) selected = Math.Min(selected + 1, saves.Count - 1);
                if (input.KeyPressed(Keys.Up)) selected = Math.Max(selected - 1, 0);
                if (selected < scroll) scroll = selected;
                if (selected >= scroll + VisibleRows) scroll = selected - VisibleRows + 1;
            }

            if (input.KeyPressed(Keys.Escape)) back();
            else if (input.LeftPressed)
            {
                if (backButton.Contains(mouse)) back();
                for (int i = 0; i < saves.Count; i++)
                    if (IsVisible(i) && RowBounds(i).Contains(mouse)) load(saves[i].Path);
            }
            else if (input.KeyPressed(Keys.Enter) && selected >= 0)
            {
                load(saves[selected].Path);
            }
        }

        public void Draw(SpriteBatch spriteBatch, SpriteFont font, SpriteFont titleFont, Input input)
        {
            var texts = new List<(string Text, Vector2 Position, Color Color)>();

            for (int i = scroll; i < saves.Count && IsVisible(i); i++)
            {
                Rectangle row = RowBounds(i);
                Button.DrawBox(row, i == selected);
                SaveGame save = saves[i].Save;
                float y = row.Center.Y - font.LineSpacing / 2f;
                texts.Add((save.SavedAt.ToString("yyyy-MM-dd  HH:mm:ss"), new Vector2(row.X + 14, y), Color.White));
                string points = $"{save.Points:N0} pts";
                texts.Add((points, new Vector2(row.Right - 14 - font.MeasureString(points).X, y), Color.Gold));
            }
            if (saves.Count == 0)
                texts.Add(("No saved games", new Vector2(list.Center.X - font.MeasureString("No saved games").X / 2f, list.Y), Color.White * 0.6f));

            Button.DrawBox(backButton, backButton.Contains(input.ScreenMousePosition));

            DrawShape.Flush();
            spriteBatch.Begin();
            const string title = "Load game";
            Vector2 position = titlePosition - titleFont.MeasureString(title) / 2f;
            spriteBatch.DrawString(titleFont, title, new Vector2(MathF.Round(position.X), MathF.Round(position.Y)), Color.White);
            foreach (var (text, at, color) in texts)
                spriteBatch.DrawString(font, text, new Vector2(MathF.Round(at.X), MathF.Round(at.Y)), color);
            Button.DrawLabel(spriteBatch, font, backButton, "Back");
            spriteBatch.End();
        }
    }
}
