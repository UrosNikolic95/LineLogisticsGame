using System.Collections.Generic;
using Microsoft.Xna.Framework;
using LineLogisticsGame.Geometry;
using LineLogisticsGame.World;

namespace LineLogisticsGame.Simulation
{
    // A resource dot lying on a line. Its type (1..ResourceRecipes.Count) is produced on output lines ("green lines")
    // and consumed on input lines of its type and on consumer lines, which take every type ("red lines").
    public class Resource
    {
        public int Type = 1;
        public StyledSegment Segment;
        public float Distance;          // from Segment.A
        public Vector2 Position;
        public Transporter ReservedBy;  // transporter on its way to pick it up
        public List<Vector2> Path;      // just unloaded: points it still rolls through before its spot on the red line (null once there)
        public float Speed;             // how fast it rolls along its path: as fast as the transporter that loaded or unloaded it
    }
}
