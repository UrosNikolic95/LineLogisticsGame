using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using LineLogisticsGame.Geometry;
using LineLogisticsGame.Simulation;
using LineLogisticsGame.Tools;
using LineLogisticsGame.UI;
using LineLogisticsGame.World;

namespace LineLogisticsGame.Screens
{
    // Line drawing editor. Tools and actions are picked from the palette on the right.
    // Producer builds stations for the resource selected on the palette; what it needs is shown under the status line.
    // Consumer builds stations that consume every resource.
    // Keyboard shortcuts:
    //   1 Road, 2 Producer, 3 Consumer, 4 Polyline, 5 Select/move, 6 Erase, 7 Transporter, 8 Depot   Space: pause
    //   Right click: deselect the tool (unless the tool uses it: cancelling, removing a transporter or depot)
    //   Shift: snap angle   G: grid   L: snap to lines (split)
    //   Middle mouse drag: pan   Wheel or +/-: zoom (wheel only when the tool does not use it)   Mouse at a screen edge: scroll
    //   Ctrl+Z / Ctrl+Y: undo / redo   Delete: clear   Esc: cancel (or open the in-game menu when idle)   F5: save
    //   Transporter tool: wheel or Up/Down: capacity   Shift+wheel or Left/Right: speed (also on the palette)
    //   Depot tool: the same, plus Ctrl+wheel or PageUp/PageDown: interval between transporters,
    //     Alt+wheel or Home/End: most transporters out at once (over a depot: changes that depot)
    public class GameScreen
    {
        private readonly LineDrawing drawing = new LineDrawing();
        private readonly Camera camera = new Camera();

        // for saving and loading (see SaveGame)
        public LineDrawing Drawing => drawing;
        public Camera Camera => camera;
        public Rectangle MenuButton => menuButton;
        private readonly TransportSystem transport;
        private readonly Action openMenu;   // the in-game menu (Menu button or Esc)
        private const int MenuButtonWidth = 70, MenuButtonHeight = 24;
        private Rectangle menuButton;
        private bool menuButtonHovered;
        private readonly ITool[] tools;
        private readonly ToolPalette palette;
        private ITool currentTool;
        private readonly ITool noTool = new NoTool();
        private int selectedResource = 1;
        private string status = "";

        // frames drawn per second, counted over half a second of real time
        private readonly Stopwatch fpsClock = Stopwatch.StartNew();
        private int framesCounted;
        private float fps;

        // "+points" over where a resource was consumed, floating up and fading out
        private class PointsPopup
        {
            public Vector2 Position;    // world
            public string Text;
            public float Age;
        }
        private readonly List<PointsPopup> popups = new List<PointsPopup>();
        private const float PopupLife = 1.2f, PopupRise = 40f;  // seconds shown, screen pixels risen over that time

        public GameScreen(TransportSystem transport, Action openMenu)
        {
            this.transport = transport;
            this.openMenu = openMenu;
            transport.PointsEarned = (position, points) => popups.Add(new PointsPopup { Position = position, Text = $"+{points:N0}" });

            var transporterTool = new TransporterTool(transport);
            var depotTool = new DepotTool(transport);
            var producerTool = new StationTool("Producer", ProducerLines);
            tools = new ITool[]
            {
                new LineTool("Road", Color.White),
                producerTool,
                new StationTool("Consumer", () => new[] { new StationTool.Line(ResourceRecipes.ConsumerColor, false, LineKind.Consumer, 0) }),
                new PolylineTool(), new SelectTool(), new EraseTool(), transporterTool, depotTool
            };
            currentTool = tools[0];

            // one button per resource, choosing the resource new producers are for (shown with the producer tool)
            var resourceChoices = Enumerable.Range(1, ResourceRecipes.Count).Select(type => new ToolPalette.Choice
            {
                Name = $"Resource {type}",
                IsSelected = () => selectedResource == type,
                Select = () => selectedResource = type,
                DrawIcon = bounds => TransportSystem.DrawResourceIcon(bounds.Center.ToVector2(), 10.5f, type),
                IsVisible = () => currentTool == producerTool
            }).ToArray();

            // settings for new transporters: the depot tool's (for the depots it places) while it is selected,
            // otherwise the transporter tool's; shown only with those tools
            bool depotSettings() => currentTool == depotTool;
            bool transporterSettings() => currentTool == transporterTool || currentTool == depotTool;
            var steppers = new[]
            {
                new ToolPalette.Stepper
                {
                    Name = "Transporter capacity",
                    Label = "Capacity",
                    Value = () => (depotSettings() ? depotTool.Capacity : transporterTool.Capacity).ToString(),
                    ValueColor = () => transport.ResourceColor,
                    Change = steps => { if (depotSettings()) depotTool.ChangeCapacity(steps); else transporterTool.ChangeCapacity(steps); },
                    IsVisible = transporterSettings
                },
                new ToolPalette.Stepper
                {
                    Name = "Transporter speed",
                    Label = "Speed",
                    Value = () => ((int)(depotSettings() ? depotTool.Speed : transporterTool.Speed)).ToString(),
                    ValueColor = () => transport.TransporterColor,
                    Change = steps => { if (depotSettings()) depotTool.ChangeSpeed(steps); else transporterTool.ChangeSpeed(steps); },
                    IsVisible = transporterSettings
                },
                new ToolPalette.Stepper
                {
                    Name = "Depot interval (seconds between transporters)",
                    Label = "Interval (s)",
                    Value = () => DepotTool.FormatInterval(depotTool.Interval),
                    ValueColor = () => Color.White,
                    Change = depotTool.ChangeInterval,
                    IsVisible = depotSettings
                },
                new ToolPalette.Stepper
                {
                    Name = "Depot limit (transporters out at once)",
                    Label = "Limit",
                    Value = () => depotTool.MaxTransporters.ToString(),
                    ValueColor = () => Color.White,
                    Change = depotTool.ChangeLimit,
                    IsVisible = depotSettings
                }
            };
            palette = new ToolPalette(tools, () => currentTool, SetTool, resourceChoices, steppers, drawing);
        }

        // A producer of the selected resource: an input line for every resource it requires, then its output line.
        private StationTool.Line[] ProducerLines()
        {
            return transport.Recipes.RequirementsOf(selectedResource)
                .Select(type => new StationTool.Line(ResourceRecipes.TakingColorOf(type), false, LineKind.Input, type))
                .Append(new StationTool.Line(ResourceRecipes.ColorOf(selectedResource), true, LineKind.Output, selectedResource))
                .ToArray();
        }

        private void SetTool(ITool tool)
        {
            if (tool == currentTool) return;
            currentTool.Cancel();
            currentTool = tool;
        }

        public void Update(GameTime gameTime, Input input, Viewport viewport, bool isActive)
        {
            if (input.KeyPressed(Keys.Escape))
            {
                if (currentTool.IsBusy) currentTool.Cancel();
                else openMenu();
            }

            HandleShortcuts(input);

            palette.Layout(viewport.Width, viewport.Height);

            // the wheel zooms unless it is over the palette or the tool uses it for its settings
            bool toolUsesWheel = currentTool is TransporterTool || currentTool is DepotTool;
            camera.Update(gameTime, input, viewport.Width, viewport.Height, isActive, !toolUsesWheel && !palette.Contains(input.ScreenMousePosition));
            input.Camera = camera;
            drawing.PixelSize = 1f / camera.Zoom;

            menuButton = new Rectangle(viewport.Width - ToolPalette.Width - MenuButtonWidth - 6, 4, MenuButtonWidth, MenuButtonHeight);
            menuButtonHovered = menuButton.Contains(input.ScreenMousePosition);

            if (isActive)
            {
                // Clicks on the palette and the menu button never reach the tool, but a drag that is already in progress keeps going.
                bool overPalette = palette.Contains(input.ScreenMousePosition) || menuButtonHovered;
                palette.Update(input, !currentTool.IsBusy);
                // a right click the tool doesn't use itself deselects it
                if (!overPalette && input.RightPressed && !currentTool.UsesRightClick(input))
                    SetTool(noTool);
                else if (!overPalette || currentTool.IsBusy)
                    currentTool.Update(input, drawing);
                if (menuButtonHovered && input.LeftPressed && !currentTool.IsBusy) openMenu();
            }

            transport.Update(gameTime, drawing);

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            foreach (var popup in popups) popup.Age += dt;
            popups.RemoveAll(p => p.Age >= PopupLife);

            string hint = palette.HoveredName != null ? $"  >> {palette.HoveredName}" : "";
            if (currentTool is TransporterTool transporterTool)
                hint = $" | New: capacity {transporterTool.Capacity} (wheel), speed {transporterTool.Speed} (Shift+wheel){hint}";
            else if (currentTool is DepotTool depotTool)
                hint = $" | New: capacity {depotTool.Capacity} (wheel), speed {depotTool.Speed} (Shift+wheel), " +
                       $"interval {DepotTool.FormatInterval(depotTool.Interval)}s (Ctrl+wheel), limit {depotTool.MaxTransporters} (Alt+wheel){hint}";
            status = $"FPS: {fps:0} | Tool: {currentTool.Name} | Grid: {(drawing.GridEnabled ? "on" : "off")} | Zoom: {camera.Zoom * 100:0}% " +
                           $"| Lines: {drawing.Segments.Count} | Resources: {transport.Resources.Count} | Transporters: {transport.Transporters.Count} | Depots: {transport.Depots.Count}{(transport.Paused ? " (paused)" : "")}{hint}";
        }

        private void HandleShortcuts(Input input)
        {
            Keys[] toolKeys = { Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5, Keys.D6, Keys.D7, Keys.D8 };
            for (int i = 0; i < toolKeys.Length; i++)
            {
                if (input.KeyPressed(toolKeys[i])) SetTool(tools[i]);
            }

            if (input.Ctrl && input.KeyPressed(Keys.Z)) { currentTool.Cancel(); drawing.Undo(); }
            if (input.Ctrl && input.KeyPressed(Keys.Y)) { currentTool.Cancel(); drawing.Redo(); }

            if (input.KeyPressed(Keys.Delete) && drawing.Segments.Count > 0)
            {
                currentTool.Cancel();
                drawing.SaveUndo();
                drawing.Clear();
            }

            if (input.KeyPressed(Keys.G)) drawing.GridEnabled = !drawing.GridEnabled;
            if (input.KeyPressed(Keys.L)) drawing.SnapToLines = !drawing.SnapToLines;
            if (input.KeyPressed(Keys.Space)) transport.Paused = !transport.Paused;
        }

        public void Draw(SpriteBatch spriteBatch, SpriteFont font, Input input, Viewport viewport)
        {
            framesCounted++;
            double seconds = fpsClock.Elapsed.TotalSeconds;
            if (seconds >= 0.5)
            {
                fps = (float)(framesCounted / seconds);
                framesCounted = 0;
                fpsClock.Restart();
            }

            drawing.DrawGrid(camera, viewport.Width, viewport.Height);

            // the working area goes through the camera; soft edges stay the same width on screen at any zoom
            float feather = DrawShape.Feather;
            DrawShape.View = camera.View;
            DrawShape.Feather = feather * drawing.PixelSize;
            drawing.Draw();
            transport.Draw(drawing);
            if (currentTool.IsBusy || (!palette.Contains(input.ScreenMousePosition) && !menuButtonHovered))
                currentTool.Draw(input, drawing);
            DrawShape.View = Matrix.Identity;
            DrawShape.Feather = feather;
            DrawPopups(spriteBatch, font);

            // laid out again in case the tool (and so the options shown) changed during the update
            palette.Layout(viewport.Width, viewport.Height);
            palette.Draw(spriteBatch, font);
            int statusHeight = DrawStatus(spriteBatch, font, viewport);
            int pointsBottom = DrawPoints(spriteBatch, font, statusHeight + 8);
            DrawRequirements(spriteBatch, font, pointsBottom + 8);
        }

        private void DrawPopups(SpriteBatch spriteBatch, SpriteFont font)
        {
            if (popups.Count == 0) return;
            DrawShape.Flush();
            spriteBatch.Begin();
            foreach (var popup in popups)
            {
                float progress = popup.Age / PopupLife;
                Vector2 size = font.MeasureString(popup.Text);
                Vector2 position = camera.WorldToScreen(popup.Position) - new Vector2(size.X / 2f, size.Y + PopupRise * progress);
                position = new Vector2(MathF.Round(position.X), MathF.Round(position.Y));
                float alpha = 1f - progress;
                spriteBatch.DrawString(font, popup.Text, position + Vector2.One, Color.Black * alpha);
                spriteBatch.DrawString(font, popup.Text, position, Color.Gold * alpha);
            }
            spriteBatch.End();
        }

        // "Points: N" on a dark box under the status line; returns the box's bottom
        private int DrawPoints(SpriteBatch spriteBatch, SpriteFont font, int top)
        {
            const int padding = 6;
            string text = $"Points: {transport.Points:N0}";
            Vector2 size = font.MeasureString(text);
            var box = new Rectangle(8, top, (int)size.X + padding * 2, font.LineSpacing + padding * 2);
            DrawShape.DrawFilledRectangle(box, Color.Black * 0.6f);

            DrawShape.Flush();
            spriteBatch.Begin();
            spriteBatch.DrawString(font, text, new Vector2(box.X + padding, box.Y + padding), Color.Gold);
            spriteBatch.End();
            return box.Bottom;
        }

        // "Resource (icon)  needs (icon) (icon)" on a dark box under the status line, for the resource selected on the palette
        private void DrawRequirements(SpriteBatch spriteBatch, SpriteFont font, int top)
        {
            const int padding = 8, iconSize = 24, gap = 4;
            const float radius = 9f;
            var needs = transport.Recipes.RequirementsOf(selectedResource);
            string label = "Resource", needsLabel = needs.Count == 0 ? "needs nothing" : "needs";
            string pointsLabel = $"worth {transport.Recipes.PointsOf(selectedResource):N0} pts";
            float labelWidth = font.MeasureString(label).X, needsWidth = font.MeasureString(needsLabel).X;
            float pointsWidth = font.MeasureString(pointsLabel).X;

            int width = (int)(padding + labelWidth + gap + iconSize + padding * 2 + needsWidth + gap + needs.Count * (iconSize + gap)
                              + padding + pointsWidth + padding);
            int height = iconSize + padding;
            var box = new Rectangle(8, top, width, height);
            DrawShape.DrawFilledRectangle(box, Color.Black * 0.6f);

            float centerY = box.Center.Y;
            float x = box.X + padding + labelWidth + gap;
            TransportSystem.DrawResourceIcon(new Vector2(x + iconSize / 2f, centerY), radius, selectedResource);
            x += iconSize + padding * 2;
            float needsX = x;
            x += needsWidth + gap;
            foreach (int type in needs)
            {
                TransportSystem.DrawResourceIcon(new Vector2(x + iconSize / 2f, centerY), radius, type);
                x += iconSize + gap;
            }

            float textY = MathF.Round(centerY - font.LineSpacing / 2f);
            DrawShape.Flush();
            spriteBatch.Begin();
            spriteBatch.DrawString(font, label, new Vector2(box.X + padding, textY), Color.White);
            spriteBatch.DrawString(font, needsLabel, new Vector2(needsX, textY), Color.White * (needs.Count == 0 ? 0.5f : 0.8f));
            spriteBatch.DrawString(font, pointsLabel, new Vector2(x + padding, textY), Color.Gold);
            spriteBatch.End();
        }

        // status line on a dark bar across the top of the drawing area; returns the bar's height
        private int DrawStatus(SpriteBatch spriteBatch, SpriteFont font, Viewport viewport)
        {
            const int padding = 6;
            int barHeight = font.LineSpacing + padding * 2;
            DrawShape.DrawFilledRectangle(new Rectangle(0, 0, viewport.Width - ToolPalette.Width, barHeight), Color.Black * 0.6f);

            Button.DrawBox(menuButton, menuButtonHovered);

            DrawShape.Flush();
            spriteBatch.Begin();
            spriteBatch.DrawString(font, status, new Vector2(padding + 4, padding), Color.White);
            Button.DrawLabel(spriteBatch, font, menuButton, "Menu");
            spriteBatch.End();
            return barHeight;
        }
    }
}
