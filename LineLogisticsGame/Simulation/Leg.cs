using LineLogisticsGame.Geometry;
using LineLogisticsGame.World;

namespace LineLogisticsGame.Simulation
{
    // One piece of a route: move along Segment from one distance (from A) to another.
    // float.PositiveInfinity means "the B end", so routes stay correct when a line is moved or resized.
    public class Leg
    {
        public StyledSegment Segment;
        public float From, To;
    }
}
