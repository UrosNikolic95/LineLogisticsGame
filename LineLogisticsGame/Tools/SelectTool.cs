using Microsoft.Xna.Framework;
using LineLogisticsGame.Geometry;
using LineLogisticsGame.UI;
using LineLogisticsGame.World;
using Point = LineLogisticsGame.Geometry.Point;

namespace LineLogisticsGame.Tools
{
    // Drag a point to move it (dropping it on another point or on a line joins them), or drag a line to move the whole line.
    public class SelectTool : ITool
    {
        public string Name => "Select";
        public bool IsBusy => draggedPoint != null || draggedSegment != null;

        private Point draggedPoint;
        private SnapResult dropSnap;
        private StyledSegment draggedSegment;
        private Vector2 lastMouse;

        public void Update(Input input, LineDrawing drawing)
        {
            if (!IsBusy && input.LeftPressed)
            {
                draggedPoint = drawing.FindPoint(input.MousePosition, drawing.SnapRadius);
                if (draggedPoint == null)
                    draggedSegment = drawing.FindSegment(input.MousePosition, 4f);
                if (IsBusy) drawing.SaveUndo();
                lastMouse = input.MousePosition;
                return;
            }

            if (!IsBusy) return;

            if (draggedPoint != null)
            {
                Vector2? anchor = draggedPoint.connections.Count == 1 ? draggedPoint.connections[0].position : null;
                dropSnap = drawing.Snap(input.MousePosition, anchor, input.Shift, draggedPoint);
                draggedPoint.position = dropSnap.Position;
            }
            else
            {
                Vector2 delta = input.MousePosition - lastMouse;
                draggedSegment.A.position += delta;
                draggedSegment.B.position += delta;
            }
            lastMouse = input.MousePosition;

            if (input.LeftReleased)
            {
                if (draggedPoint != null && dropSnap != null && dropSnap.OnSomething)
                    drawing.MergePoints(draggedPoint, drawing.Commit(dropSnap));
                Cancel();
            }
        }

        public void Draw(Input input, LineDrawing drawing)
        {
            foreach (var point in drawing.Points)
                LineDrawing.DrawHandle(point.position, new Color(255, 255, 255, 80));

            if (draggedSegment != null)
            {
                LineDrawing.DrawHandle(draggedSegment.A.position, Color.Yellow);
                LineDrawing.DrawHandle(draggedSegment.B.position, Color.Yellow);
                return;
            }

            if (draggedPoint != null && dropSnap != null && dropSnap.OnSomething)
            {
                LineDrawing.DrawSnap(dropSnap);
                return;
            }

            Point hovered = draggedPoint ?? drawing.FindPoint(input.MousePosition, drawing.SnapRadius);
            if (hovered != null)
            {
                LineDrawing.DrawHandle(hovered.position, Color.Yellow);
                return;
            }

            StyledSegment segment = drawing.FindSegment(input.MousePosition, 4f);
            if (segment != null)
                DrawShape.DrawLine(segment.A.position, segment.B.position, Color.Yellow * 0.5f, segment.Width + 4f);
        }

        public void Cancel()
        {
            draggedPoint = null;
            dropSnap = null;
            draggedSegment = null;
        }

        // "move" icon: arrows in four directions around a handle
        public void DrawIcon(Rectangle bounds, Color color)
        {
            Vector2 center = bounds.Center.ToVector2();
            float reach = bounds.Width / 2f - 10f;
            foreach (var direction in new[] { Vector2.UnitX, -Vector2.UnitX, Vector2.UnitY, -Vector2.UnitY })
                DrawShape.DrawArrow(center + direction * 6f, center + direction * reach, color, 2f, 5f);
            DrawShape.DrawCircle(center, 4f, 2f, color);
        }
    }
}
