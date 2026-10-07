using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using LineLogisticsGame.Geometry;
using LineLogisticsGame.Simulation;
using LineLogisticsGame.Tools;
using LineLogisticsGame.World;

namespace LineLogisticsGame.UI
{
    // Vertical panel on the right side of the screen: tools, steppers and actions, selected by clicking.
    public class ToolPalette
    {
        private class Button
        {
            public string Name;
            public Rectangle Bounds;
            public Action OnClick;          // null: only shows something
            public Func<bool> IsSelected = () => false;
            public Action<Rectangle, Color> DrawIcon;
        }

        // One of a set of options (like the resource stations are built for), shown as a small square button.
        public class Choice
        {
            public string Name;
            public Func<bool> IsSelected;
            public Action Select;
            public Action<Rectangle> DrawIcon;
        }

        // A value changed with - and + buttons, shown between them.
        public class Stepper
        {
            public string Name;
            public Func<string> Value;
            public Func<Color> ValueColor;
            public Action<int> Change;
        }

        public const int Width = 96;
        private const int ToolButtonHeight = 40;
        private const int ChoiceColumns = 3;
        private const int ChoiceGap = 4;
        private const int StepperHeight = 24;
        private const int StepButtonWidth = 16;
        private const int Padding = 8;
        private const int Gap = 6;
        private const int SectionGap = 14;

        private static readonly Color PanelColor = new Color(44, 44, 54);
        private static readonly Color BorderColor = new Color(70, 70, 84);
        private static readonly Color HoverColor = new Color(70, 70, 84);
        private static readonly Color SelectedColor = new Color(58, 96, 150);
        private static readonly Color IconColor = new Color(225, 225, 230);

        private readonly List<Button> buttons = new List<Button>();
        private readonly int toolCount;
        private readonly int stepperCount;
        private readonly int choiceCount;
        private Rectangle panel;
        private Button hovered;

        // Name of the button under the mouse, or null.
        public string HoveredName => hovered?.Name;

        public ToolPalette(ITool[] tools, Func<ITool> getTool, Action<ITool> setTool, Choice[] choices, Stepper[] steppers, LineDrawing drawing)
        {
            toolCount = tools.Length;
            stepperCount = steppers.Length;
            choiceCount = choices.Length;

            foreach (var tool in tools)
            {
                buttons.Add(new Button
                {
                    Name = tool.Name,
                    OnClick = () => setTool(tool),
                    IsSelected = () => getTool() == tool,
                    DrawIcon = tool.DrawIcon
                });
            }

            foreach (var choice in choices)
            {
                buttons.Add(new Button
                {
                    Name = choice.Name,
                    OnClick = choice.Select,
                    IsSelected = choice.IsSelected,
                    DrawIcon = (bounds, _) => choice.DrawIcon(bounds)
                });
            }

            foreach (var stepper in steppers)
            {
                buttons.Add(new Button
                {
                    Name = $"{stepper.Name} -",
                    OnClick = () => stepper.Change(-1),
                    DrawIcon = (bounds, color) => DrawPlusMinusIcon(bounds, color, false)
                });
                buttons.Add(new Button
                {
                    Name = stepper.Name,
                    DrawIcon = (bounds, _) => TransportSystem.DrawLabel(bounds.Center.ToVector2(), (stepper.Value(), stepper.ValueColor()))
                });
                buttons.Add(new Button
                {
                    Name = $"{stepper.Name} +",
                    OnClick = () => stepper.Change(1),
                    DrawIcon = (bounds, color) => DrawPlusMinusIcon(bounds, color, true)
                });
            }

            buttons.Add(new Button
            {
                Name = "Grid",
                OnClick = () => drawing.GridEnabled = !drawing.GridEnabled,
                IsSelected = () => drawing.GridEnabled,
                DrawIcon = DrawGridIcon
            });
            buttons.Add(new Button
            {
                Name = "Snap to lines (split)",
                OnClick = () => drawing.SnapToLines = !drawing.SnapToLines,
                IsSelected = () => drawing.SnapToLines,
                DrawIcon = DrawSnapToLinesIcon
            });
            buttons.Add(new Button
            {
                Name = "Clear",
                OnClick = () =>
                {
                    if (drawing.Segments.Count == 0) return;
                    getTool().Cancel();
                    drawing.SaveUndo();
                    drawing.Clear();
                },
                DrawIcon = DrawClearIcon
            });
            buttons.Add(new Button
            {
                Name = "Undo",
                OnClick = () => { getTool().Cancel(); drawing.Undo(); },
                DrawIcon = (bounds, color) => DrawUndoRedoIcon(bounds, color, -1)
            });
            buttons.Add(new Button
            {
                Name = "Redo",
                OnClick = () => { getTool().Cancel(); drawing.Redo(); },
                DrawIcon = (bounds, color) => DrawUndoRedoIcon(bounds, color, 1)
            });
        }

        public bool Contains(Vector2 position) => panel.Contains(position);

        public void Layout(int screenWidth, int screenHeight)
        {
            panel = new Rectangle(screenWidth - Width, 0, Width, screenHeight);

            int full = Width - Padding * 2;
            int half = (full - Gap) / 2;
            int left = panel.X + Padding;
            int y = Padding;
            int index = 0;

            // tools: large, one per row
            for (int i = 0; i < toolCount; i++, index++)
            {
                buttons[index].Bounds = new Rectangle(left, y, full, ToolButtonHeight);
                y += ToolButtonHeight + Gap;
            }

            // choices: small, several per row
            if (choiceCount > 0)
            {
                int size = (full - ChoiceGap * (ChoiceColumns - 1)) / ChoiceColumns;
                y = LayoutGrid(index, choiceCount, left, y + SectionGap - Gap, size, ChoiceColumns, ChoiceGap) + Gap;
                index += choiceCount;
            }

            // steppers: - value +, one per row
            if (stepperCount > 0) y += SectionGap - Gap;
            for (int i = 0; i < stepperCount; i++, index += 3)
            {
                buttons[index].Bounds = new Rectangle(left, y, StepButtonWidth, StepperHeight);
                buttons[index + 1].Bounds = new Rectangle(left + StepButtonWidth, y, full - StepButtonWidth * 2, StepperHeight);
                buttons[index + 2].Bounds = new Rectangle(left + full - StepButtonWidth, y, StepButtonWidth, StepperHeight);
                y += StepperHeight + Gap;
            }

            // actions: two per row
            y += SectionGap - Gap;
            LayoutGrid(index, buttons.Count - index, left, y, half, 2, Gap);
        }

        private int LayoutGrid(int start, int count, int left, int y, int size, int columns, int gap)
        {
            for (int i = 0; i < count; i++)
            {
                int column = i % columns;
                int row = i / columns;
                buttons[start + i].Bounds = new Rectangle(left + column * (size + gap), y + row * (size + gap), size, size);
            }
            int rows = (count + columns - 1) / columns;
            return y + rows * (size + gap) - gap;
        }

        // Call every frame so the hover state clears when the mouse leaves; acceptClicks is false while a tool is mid-drag.
        public void Update(Input input, bool acceptClicks)
        {
            hovered = null;
            if (!acceptClicks || !Contains(input.ScreenMousePosition)) return;

            foreach (var button in buttons)
            {
                if (button.Bounds.Contains(input.ScreenMousePosition))
                {
                    hovered = button;
                    break;
                }
            }

            if (hovered != null && input.LeftPressed)
                hovered.OnClick?.Invoke();
        }

        public void Draw()
        {
            DrawShape.DrawFilledRectangle(panel, PanelColor);
            DrawShape.DrawFilledRectangle(new Rectangle(panel.X, panel.Y, 1, panel.Height), BorderColor);

            foreach (var button in buttons)
            {
                if (button.IsSelected())
                    DrawShape.DrawFilledRectangle(button.Bounds, SelectedColor);
                else if (button == hovered && button.OnClick != null)
                    DrawShape.DrawFilledRectangle(button.Bounds, HoverColor);

                button.DrawIcon(button.Bounds, IconColor);

                if (button == hovered && button.OnClick != null)
                    DrawShape.DrawRectangle(button.Bounds, 1f, IconColor * 0.6f);
            }
        }

        private static void DrawGridIcon(Rectangle bounds, Color color)
        {
            bounds.Inflate(-6, -6);
            DrawShape.DrawRectangle(bounds, 1.5f, color);
            float third = bounds.Width / 3f;
            for (int i = 1; i < 3; i++)
            {
                DrawShape.DrawLine(new Vector2(bounds.Left + third * i, bounds.Top), new Vector2(bounds.Left + third * i, bounds.Bottom), color, 1f);
                DrawShape.DrawLine(new Vector2(bounds.Left, bounds.Top + third * i), new Vector2(bounds.Right, bounds.Top + third * i), color, 1f);
            }
        }

        // a line with a new line ending on it
        private static void DrawSnapToLinesIcon(Rectangle bounds, Color color)
        {
            Vector2 center = bounds.Center.ToVector2();
            float y = center.Y + 5f;
            DrawShape.DrawLine(new Vector2(bounds.Left + 5, y), new Vector2(bounds.Right - 5, y), color, 2f);
            DrawShape.DrawLine(new Vector2(center.X - 5f, bounds.Top + 6), new Vector2(center.X, y), color, 1.5f);
            DrawShape.DrawCircle(new Vector2(center.X, y), 3f, 1.5f, Color.Cyan);
        }

        private static void DrawPlusMinusIcon(Rectangle bounds, Color color, bool plus)
        {
            Vector2 center = bounds.Center.ToVector2();
            DrawShape.DrawLine(center - new Vector2(4f, 0), center + new Vector2(4f, 0), color, 2f);
            if (plus) DrawShape.DrawLine(center - new Vector2(0, 4f), center + new Vector2(0, 4f), color, 2f);
        }

        // empty page with a slash
        private static void DrawClearIcon(Rectangle bounds, Color color)
        {
            bounds.Inflate(-7, -6);
            DrawShape.DrawRectangle(bounds, 1.5f, color);
            DrawShape.DrawLine(new Vector2(bounds.Left, bounds.Bottom), new Vector2(bounds.Right, bounds.Top), Color.OrangeRed, 2f);
        }

        // hooked arrow; direction -1 points left (undo), 1 points right (redo)
        private static void DrawUndoRedoIcon(Rectangle bounds, Color color, int direction)
        {
            Vector2 center = bounds.Center.ToVector2();
            var tail = new Vector2(center.X - direction * 6f, center.Y + 5f);
            var bend = new Vector2(center.X - direction * 6f, center.Y - 3f);
            var tip = new Vector2(center.X + direction * 7f, center.Y - 3f);
            DrawShape.DrawLine(tail, bend, color, 2f);
            DrawShape.DrawArrow(bend, tip, color, 2f, 5f);
        }
    }
}
