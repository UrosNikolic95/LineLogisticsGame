using Microsoft.Xna.Framework;
using LineLogisticsGame.Geometry;
using LineLogisticsGame.UI;
using LineLogisticsGame.World;
using Point = LineLogisticsGame.Geometry.Point;

namespace LineLogisticsGame.Tools
{
    // Press, drag, release to draw a single line (a road) in the tool's color.
    // Shift snaps the angle. Starting or ending on an existing line splits that line at the snapped point.
    public class LineTool : ITool
    {
        public string Name { get; }
        public bool IsBusy => start != null;

        private readonly Color color;
        private SnapResult start;

        public LineTool(string name, Color color)
        {
            Name = name;
            this.color = color;
        }

        private SnapResult SnapCursor(Input input, LineDrawing drawing)
        {
            if (start == null) return drawing.Snap(input.MousePosition, null, false);
            return drawing.Snap(input.MousePosition, start.Position, input.Shift, start.Point, start.Segment);
        }

        public void Update(Input input, LineDrawing drawing)
        {
            if (start == null && input.LeftPressed)
            {
                start = SnapCursor(input, drawing);
            }
            else if (start != null && input.RightPressed)
            {
                Cancel();
            }
            else if (start != null && input.LeftReleased)
            {
                SnapResult end = SnapCursor(input, drawing);
                if (Vector2.Distance(start.Position, end.Position) > 2f)
                {
                    drawing.SaveUndo();
                    drawing.AddSegment(drawing.Commit(start), drawing.Commit(end), color);
                }
                Cancel();
            }
        }

        public void Draw(Input input, LineDrawing drawing)
        {
            SnapResult snap = SnapCursor(input, drawing);
            if (start != null)
            {
                LineDrawing.DrawStyledLine(start.Position, snap.Position, color * 0.6f, drawing.CurrentWidth);
                LineDrawing.DrawSnap(start);
            }
            LineDrawing.DrawSnap(snap);
        }

        public void Cancel()
        {
            start = null;
        }

        // a line in the tool's color (roads use the palette's icon color)
        public void DrawIcon(Rectangle bounds, Color iconColor)
        {
            Color c = color == Color.White ? iconColor : color;
            int size = System.Math.Min(bounds.Width, bounds.Height) - 24;
            bounds = new Rectangle(bounds.Center.X - size / 2, bounds.Center.Y - size / 2, size, size);
            var a = new Vector2(bounds.Left, bounds.Bottom);
            var b = new Vector2(bounds.Right, bounds.Top);
            LineDrawing.DrawStyledLine(a, b, c, 3f);
            DrawShape.DrawFilledCircle(a, 3.5f, 0, c, c);
            DrawShape.DrawFilledCircle(b, 3.5f, 0, c, c);
        }
    }
}
