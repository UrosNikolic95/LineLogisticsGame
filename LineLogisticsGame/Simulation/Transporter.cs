using System.Collections.Generic;
using LineLogisticsGame.Geometry;
using LineLogisticsGame.World;

namespace LineLogisticsGame.Simulation
{
    // A dot that drives along the lines, picking up resources from green lines and delivering them to red lines.
    // The resources it carries are dragged behind it as wagons.
    public class Transporter : MovingPoint
    {
        public float Distance;          // from Segment.A
        public StyledSegment Segment { get => (StyledSegment)current; set => current = value; }

        public int Capacity = 1;
        public int Load;                // resources it is carrying
        public int CargoType = 1;       // their type (it only ever carries one type at a time)
        public int FirstLoaded;         // wagon carrying the first of them (the ones in front were unloaded)
        public bool Carrying => Load > 0;
        public bool Full => Load >= Capacity;

        public List<Leg> Route = new List<Leg>();
        public StyledSegment PickupSegment;  // green line it is scheduled on
        public StyledSegment DropSegment;    // red line it is scheduled on
        public StyledSegment Stop;           // road it stops on to load or unload
        public int DropReserved;        // empty spaces reserved there
        public float Waiting;           // seconds until it looks for work again
        public Transporter Blocker;     // the one in its way, while it can't move on
        public float BlockedTime;       // seconds it has been stuck behind it
        public float CurrentSpeed;      // how fast it last drove: its own speed, or that of a slower one it was following
        // resources it is pulling off a green line, and how far behind it each one was when it hooked them (see Tow)
        public List<(Resource Resource, int Wagon, float From)> Towing = new List<(Resource, int, float)>();
        public StyledSegment TowSegment;    // the green line they come from
        public float TowEntrance;       // how far behind it that line's B end was when it hooked them
        public float Towed;             // how far it has driven since
        public float TowLength;         // how far it drives until they are on its wagons

        // where it has been, newest last; the wagons follow it
        public List<(StyledSegment Segment, float Distance)> Trail = new List<(StyledSegment, float)>();

        public Depot Home;              // depot it belongs to and goes back to when there's no work (null: placed by hand; when idle it joins the nearest depot)
        public bool Returning;          // driving home
    }
}
