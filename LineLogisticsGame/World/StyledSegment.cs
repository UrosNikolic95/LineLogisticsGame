using Microsoft.Xna.Framework;
using LineLogisticsGame.Geometry;

namespace LineLogisticsGame.World
{
    public class StyledSegment : Segment
    {
        public Color Color;
        public float Width;
        public int Group;   // lines built together as one unit share it (0: none); kept through splits and undo
        public LineKind Kind;
        public int Type;    // resource type of Output and Input lines
    }
}
