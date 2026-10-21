using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LineLogisticsGame.Simulation
{
    // What the transport system produced, consumed and earned, in samples of SampleInterval seconds of running (unpaused) time.
    // Consumed counts both what producers took from their input lines and what consumer lines consumed.
    // Only the last MaxSamples samples are kept; Totals count everything since the game started.
    // Saved with the game (see SaveGame), so everything here is a public property.
    public class Statistics
    {
        public const float SampleInterval = 10f;
        public const int MaxSamples = 100;

        public class Sample
        {
            public int[] Produced { get; set; } = new int[ResourceRecipes.Count + 1];     // by type (index 0 unused)
            public int[] Consumed { get; set; } = new int[ResourceRecipes.Count + 1];
            public int[] Transported { get; set; } = new int[ResourceRecipes.Count + 1];  // unloaded by transporters where they took them
            public int Trips { get; set; }      // deliveries transporters finished
            public long Points { get; set; }    // the total at the end of the sample

            [JsonIgnore]
            public bool IsValid => Produced?.Length == ResourceRecipes.Count + 1 && Consumed?.Length == ResourceRecipes.Count + 1
                                   && Transported?.Length == ResourceRecipes.Count + 1;
        }

        public List<Sample> Samples { get; set; } = new List<Sample>();   // finished samples, oldest first
        public Sample Current { get; set; } = new Sample();                // the one being filled
        public Sample Totals { get; set; } = new Sample();                 // everything since the game started (Points unused)
        public int Dropped { get; set; }        // samples deleted to keep MaxSamples
        public long StartPoints { get; set; }   // the total at the start of the first kept sample
        public float Time { get; set; }         // into the current sample
        public bool Started { get; set; }

        // when the kept samples start and end, in seconds of running time since the game started
        [JsonIgnore] public float StartTime => Dropped * SampleInterval;
        [JsonIgnore] public float EndTime => (Dropped + Samples.Count) * SampleInterval;
        public float SampleEnd(int index) => (Dropped + index + 1) * SampleInterval;

        [JsonIgnore]
        public bool IsValid => Samples != null && Current?.IsValid == true && Totals?.IsValid == true && Samples.TrueForAll(s => s?.IsValid == true);

        public void Produced(int type)
        {
            Current.Produced[type]++;
            Totals.Produced[type]++;
        }

        public void Consumed(int type)
        {
            Current.Consumed[type]++;
            Totals.Consumed[type]++;
        }

        public void Transported(int type, int count)
        {
            Current.Transported[type] += count;
            Totals.Transported[type] += count;
        }

        public void Trip()
        {
            Current.Trips++;
            Totals.Trips++;
        }

        public void Advance(float dt, long points)
        {
            if (!Started)
            {
                StartPoints = points;
                Started = true;
            }
            Time += dt;
            Current.Points = points;
            if (Time < SampleInterval) return;
            Time -= SampleInterval;
            Samples.Add(Current);
            Current = new Sample { Points = points };

            while (Samples.Count > MaxSamples)
            {
                StartPoints = Samples[0].Points;
                Samples.RemoveAt(0);
                Dropped++;
            }
        }
    }
}
