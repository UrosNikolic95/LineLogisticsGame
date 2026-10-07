using Microsoft.Xna.Framework;
using LineLogisticsGame.Geometry;
using LineLogisticsGame.UI;
using LineLogisticsGame.World;
using Point = LineLogisticsGame.Geometry.Point;

namespace LineLogisticsGame.Tools
{
    // Click to add points one after another. Right click / Enter finishes; clicking the first point closes the shape.
    public class PolylineTool : ITool
    {
        public string Name => "Polyline";
        public bool IsBusy => start != null || last != null;

        // First click, not committed until the first line is made (so cancelling never splits a line).
        private SnapResult start;
        private Point first;
        private Point last;

        private Vector2 AnchorPosition => last?.position ?? start.Position;

        private SnapResult SnapCursor(Input input, LineDrawing drawing)
        {
            if (!IsBusy) return drawing.Snap(input.MousePosition, null, false);
            return drawing.Snap(input.MousePosition, AnchorPosition, input.Shift, last ?? start.Point, last == null ? start.Segment : null);
        }

        public void Update(Input input, LineDrawing drawing)
        {
            if (IsBusy && (input.RightPressed || input.KeyPressed(Microsoft.Xna.Framework.Input.Keys.Enter)))
            {
                Cancel();
                return;
            }

            if (!input.LeftPressed) return;

            SnapResult snap = SnapCursor(input, drawing);
            if (!IsBusy)
            {
                start = snap;
                return;
            }
            if (snap.Position == AnchorPosition) return;

            drawing.SaveUndo();
            if (last == null)
                first = last = drawing.Commit(start);
            Point point = drawing.Commit(snap);
            drawing.AddSegment(last, point);

            // Closed the loop, or ran into an existing point/line: the chain is done.
            if (point == first || snap.OnSomething)
                Cancel();
            else
                last = point;
        }

        public void Draw(Input input, LineDrawing drawing)
        {
            SnapResult snap = SnapCursor(input, drawing);
            if (IsBusy)
            {
                LineDrawing.DrawStyledLine(AnchorPosition, snap.Position, drawing.CurrentColor * 0.6f, drawing.CurrentWidth);
                LineDrawing.DrawHandle(first?.position ?? start.Position, Color.CornflowerBlue);
            }
            LineDrawing.DrawSnap(snap);
        }

        public void Cancel()
        {
            start = null;
            first = last = null;
        }

        public void DrawIcon(Rectangle bounds, Color color)
        {
            bounds.Inflate(-11, -13);
            float step = bounds.Width / 3f;
            var points = new[]
            {
                new Vector2(bounds.Left, bounds.Bottom),
                new Vector2(bounds.Left + step, bounds.Top),
                new Vector2(bounds.Left + step * 2, bounds.Bottom),
                new Vector2(bounds.Right, bounds.Top)
            };
            for (int i = 0; i < points.Length - 1; i++)
                LineDrawing.DrawStyledLine(points[i], points[i + 1], color, 3f);
        }
    }
}
