using Microsoft.Xna.Framework;
using LineLogisticsGame.Geometry;
using Point = LineLogisticsGame.Geometry.Point;

namespace LineLogisticsGame.World
{
    // Where the cursor snapped: onto an existing Point, onto a Segment (at Position), or nowhere (free Position).
    public class SnapResult
    {
        public Vector2 Position;
        public Point Point;
        public StyledSegment Segment;

        public bool OnSomething => Point != null || Segment != null;
    }
}
