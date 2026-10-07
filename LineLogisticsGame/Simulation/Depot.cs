using Microsoft.Xna.Framework;
using LineLogisticsGame.Geometry;
using LineLogisticsGame.World;

namespace LineLogisticsGame.Simulation
{
    // A transporter building: a dot on a road. It sends out a transporter for every job it finds (see TransportSystem.UpdateDepots),
    // at most one every Interval seconds and no more than MaxTransporters out at once, and takes them back in when they run out of work.
    // When placed it adopts transporters that belong to no depot; any of its transporters that differ from its Capacity and Speed
    // come to it when idle to be refitted (see TransportSystem.ArriveHome).
    public class Depot
    {
        public StyledSegment Segment;
        public float Distance;          // from Segment.A
        public Vector2 Position;
        public int Capacity;            // of the transporters it sends out
        public float Speed;
        public float Interval;          // seconds between sending transporters out
        public int MaxTransporters;     // of its transporters out at once
        public float Waiting;           // seconds until it looks for work again
    }
}
