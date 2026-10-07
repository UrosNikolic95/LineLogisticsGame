using System;
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
    //   Shift: snap angle   G: grid   L: snap to lines (split)
    //   Middle mouse drag: pan   Wheel or +/-: zoom (wheel only when the tool does not use it)   Mouse at a screen edge: scroll
    //   Ctrl+Z / Ctrl+Y: undo / redo   Delete: clear   Esc: cancel (or exit when idle)
    //   Transporter tool: wheel or Up/Down: capacity   Shift+wheel or Left/Right: speed (also on the palette)
    //   Depot tool: the same, plus Ctrl+wheel or PageUp/PageDown: interval between transporters,
    //     Alt+wheel or Home/End: most transporters out at once (over a depot: changes that depot)
    public class GameScreen
    {
        private readonly LineDrawing drawing = new LineDrawing();
        private readonly Camera camera = new Camera();
        private readonly TransportSystem transport;
        private readonly Action exit;
        private readonly ITool[] tools;
        private readonly ToolPalette palette;
        private ITool currentTool;
        private int selectedResource = 1;
        private string status = "";

        public GameScreen(TransportSystem transport, Action exit)
        {
            this.transport = transport;
            this.exit = exit;

            var transporterTool = new TransporterTool(transport);
            var depotTool = new DepotTool(transport);
            tools = new ITool[]
            {
                new LineTool("Road", Color.White),
                new StationTool("Producer", ProducerLines),
                new StationTool("Consumer", () => new[] { new StationTool.Line(ResourceRecipes.ConsumerColor, false, LineKind.Consumer, 0) }),
                new PolylineTool(), new SelectTool(), new EraseTool(), transporterTool, depotTool
            };
            currentTool = tools[0];

            // one button per resource, choosing the resource new stations are for
            var resourceChoices = Enumerable.Range(1, ResourceRecipes.Count).Select(type => new ToolPalette.Choice
            {
                Name = $"Resource {type}",
                IsSelected = () => selectedResource == type,
                Select = () => selectedResource = type,
                DrawIcon = bounds => TransportSystem.DrawResourceIcon(bounds.Center.ToVector2(), 10.5f, type)
            }).ToArray();

            // settings for new transporters: the depot tool's (for the depots it places) while it is selected,
            // otherwise the transporter tool's
            bool depotSettings() => currentTool == depotTool;
            var steppers = new[]
            {
                new ToolPalette.Stepper
                {
                    Name = "Transporter capacity",
                    Value = () => (depotSettings() ? depotTool.Capacity : transporterTool.Capacity).ToString(),
                    ValueColor = () => transport.ResourceColor,
                    Change = steps => { if (depotSettings()) depotTool.ChangeCapacity(steps); else transporterTool.ChangeCapacity(steps); }
                },
                new ToolPalette.Stepper
                {
                    Name = "Transporter speed",
                    Value = () => ((int)(depotSettings() ? depotTool.Speed : transporterTool.Speed)).ToString(),
                    ValueColor = () => transport.TransporterColor,
                    Change = steps => { if (depotSettings()) depotTool.ChangeSpeed(steps); else transporterTool.ChangeSpeed(steps); }
                },
                new ToolPalette.Stepper
                {
                    Name = "Depot interval (seconds between transporters)",
                    Value = () => DepotTool.FormatInterval(depotTool.Interval),
                    ValueColor = () => Color.White,
                    Change = depotTool.ChangeInterval
                },
                new ToolPalette.Stepper
                {
                    Name = "Depot limit (transporters out at once)",
                    Value = () => depotTool.MaxTransporters.ToString(),
                    ValueColor = () => Color.White,
                    Change = depotTool.ChangeLimit
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
                else exit();
            }

            HandleShortcuts(input);

            palette.Layout(viewport.Width, viewport.Height);

            // the wheel zooms unless it is over the palette or the tool uses it for its settings
            bool toolUsesWheel = currentTool is TransporterTool || currentTool is DepotTool;
            camera.Update(gameTime, input, viewport.Width, viewport.Height, isActive, !toolUsesWheel && !palette.Contains(input.ScreenMousePosition));
            input.Camera = camera;
            drawing.PixelSize = 1f / camera.Zoom;

            if (isActive)
            {
                // Clicks on the palette never reach the tool, but a drag that is already in progress keeps going.
                bool overPalette = palette.Contains(input.ScreenMousePosition);
                palette.Update(input, !currentTool.IsBusy);
                if (!overPalette || currentTool.IsBusy)
                    currentTool.Update(input, drawing);
            }

            transport.Update(gameTime, drawing);

            string hint = palette.HoveredName != null ? $"  >> {palette.HoveredName}" : "";
            if (currentTool is TransporterTool transporterTool)
                hint = $" | New: capacity {transporterTool.Capacity} (wheel), speed {transporterTool.Speed} (Shift+wheel){hint}";
            else if (currentTool is DepotTool depotTool)
                hint = $" | New: capacity {depotTool.Capacity} (wheel), speed {depotTool.Speed} (Shift+wheel), " +
                       $"interval {DepotTool.FormatInterval(depotTool.Interval)}s (Ctrl+wheel), limit {depotTool.MaxTransporters} (Alt+wheel){hint}";
            status = $"Points: {transport.Points:N0} | Tool: {currentTool.Name} | Grid: {(drawing.GridEnabled ? "on" : "off")} | Zoom: {camera.Zoom * 100:0}% " +
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
            drawing.DrawGrid(camera, viewport.Width, viewport.Height);

            // the working area goes through the camera; soft edges stay the same width on screen at any zoom
            float feather = DrawShape.Feather;
            DrawShape.View = camera.View;
            DrawShape.Feather = feather * drawing.PixelSize;
            drawing.Draw();
            transport.Draw(drawing);
            if (currentTool.IsBusy || !palette.Contains(input.ScreenMousePosition))
                currentTool.Draw(input, drawing);
            DrawShape.View = Matrix.Identity;
            DrawShape.Feather = feather;

            palette.Draw();
            int statusHeight = DrawStatus(spriteBatch, font, viewport);
            DrawRequirements(spriteBatch, font, statusHeight + 8);
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

            spriteBatch.Begin();
            spriteBatch.DrawString(font, status, new Vector2(padding + 4, padding), Color.White);
            spriteBatch.End();
            return barHeight;
        }
    }
}
