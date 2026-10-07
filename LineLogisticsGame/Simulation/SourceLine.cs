using System.Collections.Generic;

namespace LineLogisticsGame.Simulation
{
    // Pickups are scheduled per green line, not per resource: the line keeps the transporters coming for it,
    // and they always get its last resources (farthest from A), so nobody has to drive through the others.
    // Each scheduled transporter reserves as many resources as it has room for.
    public class SourceLine
    {
        public List<Transporter> Scheduled = new List<Transporter>();
        public int ResourceCount;
        public int ReservedCount;
    }
}
