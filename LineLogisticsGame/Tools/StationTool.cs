using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using LineLogisticsGame.Geometry;
using LineLogisticsGame.UI;
using LineLogisticsGame.World;
using Point = LineLogisticsGame.Geometry.Point;

namespace LineLogisticsGame.Tools
{
    // Click A, click B, click C: a road from A to B with a production (or consumption) line every Spacing pixels,
    // orthogonal to the road, on the same side as C and reaching as far from the road as C is.
    // A station can also be built of groups of several lines (like a producer's input lines and output line): the lines of a group
    // stand Spacing apart, groups stand twice as far apart, and every group gets its own Group id.
    // The lines are given by a function, so they can change between stations (like the selected resource of a producer).
    // Shift snaps the road's angle; A and B snap to existing points and lines like the road tool. Right click cancels.
    // If any line would cross or touch an existing line, the station is not built (its lines are previewed red).
    public class StationTool : ITool
    {
        public float Spacing = 20f;
        public Color RoadColor = Color.White;

        public string Name { get; }
        public bool IsBusy => start != null;

        // One line of the group. Output lines: transporters load at their B end, so it goes on the road.
        // Lines taking deliveries: their A end goes on the road.
        public record Line(Color Color, bool EndOnRoadIsB, LineKind Kind, int Type);

        private readonly Func<Line[]> group;
        private SnapResult start, end;

        public StationTool(string name, Func<Line[]> group)
        {
            Name = name;
            this.group = group;
        }

        private SnapResult SnapCursor(Input input, LineDrawing drawing)
        {
            if (start == null) return drawing.Snap(input.MousePosition, null, false);
            return drawing.Snap(input.MousePosition, start.Position, input.Shift, start.Point, start.Segment);
        }

        public void Update(Input input, LineDrawing drawing)
        {
            if (IsBusy && input.RightPressed)
            {
                Cancel();
                return;
            }
            if (!input.LeftPressed) return;

            if (start == null)
            {
                start = SnapCursor(input, drawing);
            }
            else if (end == null)
            {
                SnapResult snap = SnapCursor(input, drawing);
                if (Vector2.Distance(start.Position, snap.Position) > 2f) end = snap;
            }
            else
            {
                Build(drawing, input.MousePosition);
                Cancel();
            }
        }

        private void Build(LineDrawing drawing, Vector2 c)
        {
            Line[] lines = group();
            var rows = Layout(lines, start.Position, end.Position, c);
            if (!CanBuild(drawing, rows)) return;
            drawing.SaveUndo();
            Point a = drawing.Commit(start);
            Point b = drawing.Commit(end);

            Point previous = a;
            int groupId = 0;
            foreach (var (junction, tip, index) in rows)
            {
                if (index == 0 && lines.Length > 1) groupId = drawing.NewGroup();
                var onRoad = new Point { position = junction };
                var away = new Point { position = tip };
                drawing.AddSegment(previous, onRoad, RoadColor);
                Line line = lines[index];
                StyledSegment segment = line.EndOnRoadIsB
                    ? drawing.AddSegment(away, onRoad, line.Color)
                    : drawing.AddSegment(onRoad, away, line.Color);
                if (segment != null)
                {
                    segment.Group = groupId;
                    segment.Kind = line.Kind;
                    segment.Type = line.Type;
                }
                previous = onRoad;
            }
            drawing.AddSegment(previous, b, RoadColor);
        }

        // Production and consumption lines may not cross or touch any existing line.
        private static bool CanBuild(LineDrawing drawing, List<(Vector2 Junction, Vector2 Tip, int Line)> rows)
        {
            foreach (var (junction, tip, _) in rows)
                if (drawing.TouchesAnything(junction, tip)) return false;
            return true;
        }

        // Where each line meets the road, where it ends and which line of the group it is. None when C is (almost) on the road.
        // Only whole groups are laid out, and every line has some road after it, so transporters can drive past it.
        private List<(Vector2 Junction, Vector2 Tip, int Line)> Layout(Line[] lines, Vector2 a, Vector2 b, Vector2 c)
        {
            var rows = new List<(Vector2, Vector2, int)>();
            float length = Vector2.Distance(a, b);
            if (length < 1f || lines.Length == 0) return rows;

            Vector2 direction = (b - a) / length;
            Vector2 normal = new Vector2(-direction.Y, direction.X);
            float reach = Vector2.Dot(c - a, normal);
            if (Math.Abs(reach) < 2f) return rows;

            float groupStep = lines.Length * Spacing + (lines.Length > 1 ? Spacing : 0);
            for (float d = Spacing; d + (lines.Length - 1) * Spacing < length - 0.5f; d += groupStep)
            {
                for (int line = 0; line < lines.Length; line++)
                {
                    Vector2 junction = a + direction * (d + line * Spacing);
                    rows.Add((junction, junction + normal * reach, line));
                }
            }
            return rows;
        }

        public void Draw(Input input, LineDrawing drawing)
        {
            SnapResult snap = end == null ? SnapCursor(input, drawing) : null;
            if (start != null)
            {
                Vector2 b = end?.Position ?? snap.Position;
                LineDrawing.DrawStyledLine(start.Position, b, RoadColor * 0.6f, drawing.CurrentWidth);
                if (end != null)
                {
                    Line[] lines = group();
                    var rows = Layout(lines, start.Position, end.Position, input.MousePosition);
                    bool allowed = CanBuild(drawing, rows);
                    foreach (var (junction, tip, line) in rows)
                        LineDrawing.DrawStyledLine(junction, tip, (allowed ? lines[line].Color : Color.Red) * 0.6f, drawing.CurrentWidth);
                    LineDrawing.DrawSnap(end);
                }
                LineDrawing.DrawSnap(start);
            }
            if (snap != null) LineDrawing.DrawSnap(snap);
        }

        public void Cancel()
        {
            start = end = null;
        }

        // a road with three lines standing on it (or one group of several)
        public void DrawIcon(Rectangle bounds, Color iconColor)
        {
            Line[] lines = group();
            bounds.Inflate(-12, -12);
            float y = bounds.Bottom;
            LineDrawing.DrawStyledLine(new Vector2(bounds.Left, y), new Vector2(bounds.Right, y), iconColor, 3f);
            int count = lines.Length > 1 ? lines.Length : 3;
            float width = count > 5 ? 2f : 3f;
            for (int i = 0; i < count; i++)
            {
                float x = bounds.Left + bounds.Width * (i + 1) / (count + 1f);
                LineDrawing.DrawStyledLine(new Vector2(x, y), new Vector2(x, bounds.Top), lines[i % lines.Length].Color, width);
            }
        }
    }
}
