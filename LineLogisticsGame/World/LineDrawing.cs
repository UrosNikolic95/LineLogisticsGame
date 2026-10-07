using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using LineLogisticsGame.Geometry;
using Point = LineLogisticsGame.Geometry.Point;

namespace LineLogisticsGame.World
{
    // Holds all drawn lines. Segments share Point objects, so lines joined at a point stay connected when it moves.
    public class LineDrawing
    {
        public List<Point> Points = new List<Point>();
        public List<StyledSegment> Segments = new List<StyledSegment>();

        public Color CurrentColor = Color.White;
        public float CurrentWidth = 3f;

        // Snap and pick distances are in screen pixels; PixelSize (world units per pixel, 1 / zoom) converts them.
        public float SnapRadius = 10f;
        public float PixelSize = 1f;
        public float AngleStepDegrees = 15f;
        public bool GridEnabled = false;
        public float GridSize = 20f;

        // Ending a line on an existing line snaps to it and splits that line in two at the new point.
        public bool SnapToLines = true;

        private int lastGroup;

        private const int MaxUndo = 100;
        private readonly LinkedList<Snapshot> undo = new LinkedList<Snapshot>();
        private readonly Stack<Snapshot> redo = new Stack<Snapshot>();

        public Point FindPoint(Vector2 position, float radius, Point exclude = null)
        {
            Point best = null;
            float bestDistance = radius * PixelSize;
            foreach (var point in Points)
            {
                if (point == exclude) continue;
                float distance = Vector2.Distance(point.position, position);
                if (distance <= bestDistance)
                {
                    best = point;
                    bestDistance = distance;
                }
            }
            return best;
        }

        // excludePoint skips every segment touching that point; excludeSegment skips that one segment
        public StyledSegment FindSegment(Vector2 position, float tolerance, Point excludePoint = null, StyledSegment excludeSegment = null)
        {
            StyledSegment best = null;
            float bestDistance = float.MaxValue;
            foreach (var segment in Segments)
            {
                if (segment == excludeSegment) continue;
                if (excludePoint != null && (segment.A == excludePoint || segment.B == excludePoint)) continue;

                float distance = DistanceToSegment(segment.A.position, segment.B.position, position);
                if (distance <= tolerance * PixelSize + segment.Width / 2f && distance < bestDistance)
                {
                    best = segment;
                    bestDistance = distance;
                }
            }
            return best;
        }

        public static float DistanceToSegment(Vector2 a, Vector2 b, Vector2 c)
        {
            if (a == b) return Vector2.Distance(a, c);
            float t = MathHelper.Clamp(VectorHelpers.ProjectionFactor(a, b, c), 0f, 1f);
            return Vector2.Distance(a + (b - a) * t, c);
        }

        // Resolves the cursor to a final position: existing point > line (if SnapToLines) > angle snap > grid > raw.
        // anchor is where the line being drawn starts (used by angle snap).
        // exclude/excludeSegment: the point and segment the line starts from (or the point being dragged),
        // so the cursor doesn't snap back onto them.
        public SnapResult Snap(Vector2 raw, Vector2? anchor, bool angleSnap, Point exclude = null, StyledSegment excludeSegment = null)
        {
            Point point = FindPoint(raw, SnapRadius, exclude);
            if (point != null) return new SnapResult { Position = point.position, Point = point };

            Vector2 position = raw;
            bool angled = angleSnap && anchor.HasValue && raw != anchor.Value;
            if (angled)
            {
                Vector2 direction = raw - anchor.Value;
                float step = VectorHelpers.FromDegreesToRadians(AngleStepDegrees);
                float angle = MathF.Round(VectorHelpers.ToAngle(direction) / step) * step;
                position = anchor.Value + VectorHelpers.FromAngle(angle, direction.Length());
            }
            else if (GridEnabled)
            {
                position = new Vector2(MathF.Round(raw.X / GridSize) * GridSize, MathF.Round(raw.Y / GridSize) * GridSize);
            }

            if (SnapToLines)
            {
                StyledSegment segment = FindSegment(raw, SnapRadius, exclude, excludeSegment);
                if (segment != null && PointOnSegment(segment, raw, angled ? anchor.Value : null, position, out Vector2 onLine))
                {
                    // landing right on an end is the same as snapping to that end point
                    if (Vector2.Distance(onLine, segment.A.position) < 1f) return new SnapResult { Position = segment.A.position, Point = segment.A };
                    if (Vector2.Distance(onLine, segment.B.position) < 1f) return new SnapResult { Position = segment.B.position, Point = segment.B };
                    return new SnapResult { Position = onLine, Segment = segment };
                }
            }

            return new SnapResult { Position = position };
        }

        // With angle snap, the point is where the angled ray from the anchor crosses the segment (keeps both the angle and the line).
        // Otherwise it is the closest point on the segment to the cursor.
        private bool PointOnSegment(StyledSegment segment, Vector2 raw, Vector2? anchor, Vector2 angledPosition, out Vector2 result)
        {
            Vector2 a = segment.A.position, b = segment.B.position;

            if (anchor.HasValue)
            {
                if (VectorHelpers.LineIntersection(anchor.Value, angledPosition, a, b, out result)
                    && VectorHelpers.InBox(a, b, result)
                    && Vector2.Distance(result, raw) <= SnapRadius * PixelSize * 3f)
                    return true;
                return false;
            }

            float t = MathHelper.Clamp(VectorHelpers.ProjectionFactor(a, b, raw), 0f, 1f);
            result = a + (b - a) * t;
            return true;
        }

        // Turns a snap into a real point: the existing point, a new point splitting the snapped line, or a new free point.
        // Call after SaveUndo, because splitting changes the drawing.
        public Point Commit(SnapResult snap)
        {
            if (snap.Point != null) return snap.Point;
            if (snap.Segment != null && Segments.Contains(snap.Segment)) return SplitSegment(snap.Segment, snap.Position);
            return new Point { position = snap.Position };
        }

        // Replaces segment A-B with A-P and P-B (same style), returning P.
        public Point SplitSegment(StyledSegment segment, Vector2 position)
        {
            var point = new Point { position = position };
            int index = Segments.IndexOf(segment);
            Segments.RemoveAt(index);
            Segments.Insert(index, new StyledSegment { A = segment.A, B = point, Color = segment.Color, Width = segment.Width, Group = segment.Group, Kind = segment.Kind, Type = segment.Type });
            Segments.Insert(index + 1, new StyledSegment { A = point, B = segment.B, Color = segment.Color, Width = segment.Width, Group = segment.Group, Kind = segment.Kind, Type = segment.Type });
            Points.Add(point);
            RebuildConnections();
            return point;
        }

        public StyledSegment AddSegment(Point a, Point b, Color? color = null)
        {
            if (a == b || a.position == b.position) return null;
            if (Segments.Any(s => (s.A == a && s.B == b) || (s.A == b && s.B == a))) return null;

            if (!Points.Contains(a)) Points.Add(a);
            if (!Points.Contains(b)) Points.Add(b);

            var segment = new StyledSegment { A = a, B = b, Color = color ?? CurrentColor, Width = CurrentWidth };
            Segments.Add(segment);
            RebuildConnections();
            return segment;
        }

        // A Group id no line has had yet.
        public int NewGroup() => ++lastGroup;

        public void RemoveSegment(StyledSegment segment)
        {
            Segments.Remove(segment);
            RebuildConnections();
            Points.RemoveAll(p => p.connections.Count == 0);
        }

        // Replaces every use of 'from' with 'into', dropping segments that collapse or duplicate.
        public void MergePoints(Point from, Point into)
        {
            foreach (var segment in Segments)
            {
                if (segment.A == from) segment.A = into;
                if (segment.B == from) segment.B = into;
            }
            var kept = new List<StyledSegment>();
            foreach (var segment in Segments)
            {
                if (segment.A == segment.B) continue;
                if (kept.Any(k => (k.A == segment.A && k.B == segment.B) || (k.A == segment.B && k.B == segment.A))) continue;
                kept.Add(segment);
            }
            Segments = kept;
            Points.Remove(from);
            RebuildConnections();
            Points.RemoveAll(p => p.connections.Count == 0);
        }

        public void Clear()
        {
            Points.Clear();
            Segments.Clear();
        }

        private void RebuildConnections()
        {
            foreach (var point in Points) point.connections.Clear();
            foreach (var segment in Segments)
            {
                segment.A.connections.Add(segment.B);
                segment.B.connections.Add(segment.A);
            }
        }

        #region Undo / Redo

        private record Snapshot(Vector2[] Points, (int A, int B, Color Color, float Width, int Group, LineKind Kind, int Type)[] Segments);

        // Call before every change so it can be undone.
        public void SaveUndo()
        {
            undo.AddLast(TakeSnapshot());
            if (undo.Count > MaxUndo) undo.RemoveFirst();
            redo.Clear();
        }

        public void Undo()
        {
            if (undo.Count == 0) return;
            redo.Push(TakeSnapshot());
            Restore(undo.Last.Value);
            undo.RemoveLast();
        }

        public void Redo()
        {
            if (redo.Count == 0) return;
            undo.AddLast(TakeSnapshot());
            Restore(redo.Pop());
        }

        private Snapshot TakeSnapshot()
        {
            return new Snapshot(
                Points.Select(p => p.position).ToArray(),
                Segments.Select(s => (Points.IndexOf(s.A), Points.IndexOf(s.B), s.Color, s.Width, s.Group, s.Kind, s.Type)).ToArray());
        }

        private void Restore(Snapshot snapshot)
        {
            Points = snapshot.Points.Select(p => new Point { position = p }).ToList();
            Segments = snapshot.Segments
                .Select(s => new StyledSegment { A = Points[s.A], B = Points[s.B], Color = s.Color, Width = s.Width, Group = s.Group, Kind = s.Kind, Type = s.Type })
                .ToList();
            RebuildConnections();
        }

        // Replaces every line with the ones of a saved game; the undo history starts over.
        public void Load(Vector2[] points, (int A, int B, Color Color, float Width, int Group, LineKind Kind, int Type)[] segments)
        {
            Restore(new Snapshot(points, segments));
            undo.Clear();
            redo.Clear();
            lastGroup = Segments.Select(s => s.Group).DefaultIfEmpty(0).Max();
        }

        #endregion

        #region Drawing

        // Drawn in screen space as hard 1px lines on whole pixels, so they stay thin and faint at any zoom.
        // When lines get closer than a few pixels only every 2nd, 4th, ... is drawn so zooming out doesn't flood the screen.
        public void DrawGrid(Camera camera, int width, int height)
        {
            if (!GridEnabled) return;
            var color = new Color(255, 255, 255, 9);
            float step = GridSize;
            while (step * camera.Zoom < 8f) step *= 2f;

            Vector2 topLeft = camera.ScreenToWorld(Vector2.Zero), bottomRight = camera.ScreenToWorld(new Vector2(width, height));
            for (float x = MathF.Ceiling(topLeft.X / step) * step; x <= bottomRight.X; x += step)
            {
                int screenX = (int)MathF.Floor(camera.WorldToScreen(new Vector2(x, 0)).X);
                DrawShape.DrawFilledRectangle(new Rectangle(screenX, 0, 1, height), color);
            }
            for (float y = MathF.Ceiling(topLeft.Y / step) * step; y <= bottomRight.Y; y += step)
            {
                int screenY = (int)MathF.Floor(camera.WorldToScreen(new Vector2(0, y)).Y);
                DrawShape.DrawFilledRectangle(new Rectangle(0, screenY, width, 1), color);
            }
        }

        public void Draw()
        {
            foreach (var segment in Segments)
                DrawStyledLine(segment.A.position, segment.B.position, segment.Color, segment.Width);
        }

        // Thick lines get round caps so joints between connected segments have no gaps.
        public static void DrawStyledLine(Vector2 a, Vector2 b, Color color, float width)
        {
            DrawShape.DrawLine(a, b, color, width);
            if (width > 2f)
            {
                DrawShape.DrawFilledCircle(a, width / 2f, 0, color, color);
                DrawShape.DrawFilledCircle(b, width / 2f, 0, color, color);
            }
        }

        public static void DrawHandle(Vector2 position, Color color)
        {
            DrawShape.DrawCircle(position, 6f, 2f, color);
        }

        // Yellow handle on an existing point; cyan handle and highlighted line where a line will be split.
        public static void DrawSnap(SnapResult snap)
        {
            if (snap.Point != null)
            {
                DrawHandle(snap.Position, Color.Yellow);
            }
            else if (snap.Segment != null)
            {
                DrawShape.DrawLine(snap.Segment.A.position, snap.Segment.B.position, Color.Cyan * 0.3f, snap.Segment.Width + 6f);
                DrawHandle(snap.Position, Color.Cyan);
            }
        }

        #endregion
    }
}
