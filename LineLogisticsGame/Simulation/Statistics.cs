using System.Collections.Generic;
using System.Linq;

namespace LineLogisticsGame.Simulation
{
    // What the transport system produced, consumed and earned, in samples of SampleInterval seconds of running (unpaused) time.
    // Consumed counts both what producers took from their input lines and what consumer lines consumed.
    // Kept for the current session only (not saved).
    public class Statistics
    {
        public const float SampleInterval = 10f;

        public class Sample
        {
            public int[] Produced = new int[ResourceRecipes.Count + 1];     // by type (index 0 unused)
            public int[] Consumed = new int[ResourceRecipes.Count + 1];
            public int[] Transported = new int[ResourceRecipes.Count + 1];  // unloaded by transporters where they took them
            public int Trips;       // deliveries transporters finished
            public long Points;     // the total at the end of the sample
        }

        public readonly List<Sample> Samples = new List<Sample>();   // finished samples, oldest first
        public Sample Current = new Sample();                        // the one being filled
        public long StartPoints;    // the total when recording started
        private float time;         // into the current sample
        private bool started;

        public float Elapsed => Samples.Count * SampleInterval + time;

        public void Produced(int type) => Current.Produced[type]++;
        public void Consumed(int type) => Current.Consumed[type]++;
        public void Transported(int type, int count) => Current.Transported[type] += count;
        public void Trip() => Current.Trips++;

        public void Advance(float dt, long points)
        {
            if (!started)
            {
                StartPoints = points;
                started = true;
            }
            time += dt;
            Current.Points = points;
            if (time < SampleInterval) return;
            time -= SampleInterval;
            Samples.Add(Current);
            Current = new Sample { Points = points };
        }

        public int TotalProduced(int type) => Samples.Sum(s => s.Produced[type]) + Current.Produced[type];
        public int TotalConsumed(int type) => Samples.Sum(s => s.Consumed[type]) + Current.Consumed[type];
        public int TotalTransported(int type) => Samples.Sum(s => s.Transported[type]) + Current.Transported[type];
        public int TotalTrips => Samples.Sum(s => s.Trips) + Current.Trips;
    }
}
