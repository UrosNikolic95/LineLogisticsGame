using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using LineLogisticsGame.Geometry;
using LineLogisticsGame.Simulation;
using LineLogisticsGame.UI;

namespace LineLogisticsGame.Screens
{
    // Shown before the game: a table with a row for every produced resource and a column for every consumed one.
    // Checking a box makes the row's resource require the column's. Only the lower triangle can be checked
    // (a resource can only require lower ones), so the diagonal and the upper triangle are locked.
    // Randomize (or R) fills the table with a random production tree. Start (or Enter) starts the game with these recipes.
    public class RecipeScreen
    {
        private const int Cell = 44;
        private const int Box = 22;
        private const int NeedsWidth = 150;     // column right of the table listing each row's requirements

        private static readonly Color CellColor = new Color(44, 44, 54);
        private static readonly Color LockedColor = new Color(36, 36, 42);
        private static readonly Color GridColor = new Color(70, 70, 84);
        private static readonly Color HoverColor = new Color(70, 70, 84);
        private static readonly Color CheckedColor = new Color(58, 96, 150);
        private static readonly Color TextColor = new Color(225, 225, 230);

        private readonly ResourceRecipes recipes;
        private readonly Action start;
        private Rectangle table;        // the cells, without the header row and column
        private Rectangle startButton;
        private Rectangle randomizeButton;
        private (int Produced, int Consumed)? hoveredCell;
        private bool startHovered;
        private bool randomizeHovered;
        private readonly Random random = new Random();

        public RecipeScreen(ResourceRecipes recipes, Action start)
        {
            this.recipes = recipes;
            this.start = start;
        }

        public void Layout(int screenWidth, int screenHeight)
        {
            int size = Cell * ResourceRecipes.Count;
            int totalWidth = Cell + size + NeedsWidth;
            int left = (screenWidth - totalWidth) / 2 + Cell;
            int top = Math.Max(Cell + 90, (screenHeight - size - 70) / 2 + Cell / 2);
            table = new Rectangle(left, top, size, size);
            randomizeButton = new Rectangle(screenWidth / 2 - 170, table.Bottom + 20, 160, 40);
            startButton = new Rectangle(screenWidth / 2 + 10, table.Bottom + 20, 160, 40);
        }

        private Rectangle CellBounds(int produced, int consumed) =>
            new Rectangle(table.X + (consumed - 1) * Cell, table.Y + (produced - 1) * Cell, Cell, Cell);

        public void Update(Input input)
        {
            hoveredCell = null;
            Vector2 mouse = input.MousePosition;
            if (table.Contains(mouse))
            {
                int consumed = (int)(mouse.X - table.X) / Cell + 1;
                int produced = (int)(mouse.Y - table.Y) / Cell + 1;
                if (ResourceRecipes.CanRequire(produced, consumed)) hoveredCell = (produced, consumed);
            }
            startHovered = startButton.Contains(mouse);
            randomizeHovered = randomizeButton.Contains(mouse);

            if (input.LeftPressed)
            {
                if (hoveredCell is (int p, int c)) recipes.Toggle(p, c);
                else if (startHovered) start();
                else if (randomizeHovered) recipes.Randomize(random);
            }
            if (input.KeyPressed(Keys.R)) recipes.Randomize(random);
            if (input.KeyPressed(Keys.Enter)) start();
        }

        public void Draw(SpriteBatch spriteBatch, SpriteFont font)
        {
            var texts = new List<(string Text, Vector2 Position, Color Color)>();
            void Text(string text, Vector2 center, Color color) =>
                texts.Add((text, center - font.MeasureString(text) / 2f, color));

            Text("Resource requirements", new Vector2((randomizeButton.Left + startButton.Right) / 2f, table.Y - Cell - 70), Color.White);
            Text("Check the resources (columns) each resource (row) is made of. Unchecked rows are produced from nothing.",
                new Vector2((randomizeButton.Left + startButton.Right) / 2f, table.Y - Cell - 42), TextColor * 0.7f);
            Text("consumed", new Vector2(table.Center.X, table.Y - Cell - 12), TextColor * 0.7f);
            Text("produced", new Vector2(table.X - Cell / 2f, table.Y - Cell - 12), TextColor * 0.7f);
            Text("needs", new Vector2(table.Right + NeedsWidth / 2f, table.Y - Cell / 2f), TextColor * 0.7f);

            for (int type = 1; type <= ResourceRecipes.Count; type++)
            {
                DrawSwatch(new Vector2(table.X + (type - 0.5f) * Cell, table.Y - Cell / 2f), type, font, texts);
                DrawSwatch(new Vector2(table.X - Cell / 2f, table.Y + (type - 0.5f) * Cell), type, font, texts);

                var needs = recipes.RequirementsOf(type);
                Text(needs.Count == 0 ? "-" : string.Join(", ", needs),
                    new Vector2(table.Right + NeedsWidth / 2f, table.Y + (type - 0.5f) * Cell), needs.Count == 0 ? TextColor * 0.4f : TextColor);
            }

            for (int produced = 1; produced <= ResourceRecipes.Count; produced++)
            {
                for (int consumed = 1; consumed <= ResourceRecipes.Count; consumed++)
                    DrawCell(produced, consumed);
            }
            DrawShape.DrawRectangle(table, 1f, GridColor);

            DrawButton(randomizeButton, randomizeHovered, "Randomize", Text);
            DrawButton(startButton, startHovered, "Start", Text);

            DrawShape.Flush();
            spriteBatch.Begin();
            foreach (var (text, position, color) in texts)
                spriteBatch.DrawString(font, text, new Vector2(MathF.Round(position.X), MathF.Round(position.Y)), color);
            spriteBatch.End();
        }

        private static void DrawButton(Rectangle bounds, bool hovered, string label, Action<string, Vector2, Color> text)
        {
            DrawShape.DrawFilledRectangle(bounds, hovered ? CheckedColor : HoverColor);
            DrawShape.DrawRectangle(bounds, 1f, TextColor * (hovered ? 0.8f : 0.4f));
            text(label, bounds.Center.ToVector2(), Color.White);
        }

        private void DrawCell(int produced, int consumed)
        {
            Rectangle bounds = CellBounds(produced, consumed);
            Vector2 center = bounds.Center.ToVector2();

            if (produced == consumed)
            {
                // locked: a resource can't require itself
                DrawShape.DrawFilledRectangle(bounds, LockedColor);
                DrawShape.DrawLine(new Vector2(bounds.Left + 10, bounds.Bottom - 10), new Vector2(bounds.Right - 10, bounds.Top + 10), GridColor, 1.5f);
            }
            else if (!ResourceRecipes.CanRequire(produced, consumed))
            {
                // locked: the upper triangle mirrors what the lower one says
                DrawShape.DrawFilledRectangle(bounds, LockedColor);
            }
            else
            {
                bool hovered = hoveredCell == (produced, consumed);
                DrawShape.DrawFilledRectangle(bounds, hovered ? HoverColor : CellColor);

                var box = new Rectangle((int)center.X - Box / 2, (int)center.Y - Box / 2, Box, Box);
                if (recipes.Requires(produced, consumed))
                {
                    DrawShape.DrawFilledRectangle(box, CheckedColor);
                    DrawShape.DrawLine(center + new Vector2(-6, 0), center + new Vector2(-2, 5), Color.White, 2.5f);
                    DrawShape.DrawLine(center + new Vector2(-2, 5), center + new Vector2(7, -6), Color.White, 2.5f);
                }
                DrawShape.DrawRectangle(box, 1.5f, TextColor * (hovered ? 0.9f : 0.6f));
            }

            DrawShape.DrawRectangle(new Rectangle(bounds.X, bounds.Y, bounds.Width + 1, bounds.Height + 1), 1f, GridColor);
        }

        // a resource dot in its color with its number on it
        private static void DrawSwatch(Vector2 center, int type, SpriteFont font, List<(string, Vector2, Color)> texts)
        {
            Color color = ResourceRecipes.ColorOf(type);
            DrawShape.DrawFilledCircle(center, 13f, 1.5f, color, Color.Black);
            string text = type.ToString();
            bool dark = color.R * 0.299f + color.G * 0.587f + color.B * 0.114f < 120f;
            texts.Add((text, center - font.MeasureString(text) / 2f, dark ? Color.White : Color.Black));
        }
    }
}
