using System.Collections.Generic;
using System.Linq;

namespace LineLogisticsGame.Simulation
{
    // Deliveries are scheduled per red line too: transporters heading there reserve its empty spaces,
    // so no more resources are brought to it than it has room for.
    public class SinkLine
    {
        public List<Transporter> Scheduled = new List<Transporter>();
        public int FreeCount;           // empty spaces on the line
        public bool DeliveredAtB;       // the end deliveries last came in through; resources are consumed at the other end
        public int ReservedCount => Scheduled.Sum(t => t.DropReserved);
        public int UnscheduledCount => FreeCount - ReservedCount;
    }
}
