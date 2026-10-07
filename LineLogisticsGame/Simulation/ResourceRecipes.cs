using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace LineLogisticsGame.Simulation
{
    // Which resources each resource is made of. Types are 1..Count; a type can only require lower types,
    // so nothing ends up requiring itself. A type requiring nothing is produced from nothing.
    public class ResourceRecipes
    {
        public const int Count = 10;

        private static readonly Color[] colors =
        {
            Color.Gold, Color.Aquamarine, Color.HotPink, Color.DarkOrange, Color.LawnGreen,
            Color.MediumOrchid, Color.Crimson, Color.Chocolate, Color.Teal, Color.Khaki
        };

        // [produced - 1, consumed - 1]
        private readonly bool[,] requires = new bool[Count, Count];

        public static Color ColorOf(int type) => colors[(type - 1) % colors.Length];

        // input lines are a darker shade of the color of the resource they take
        public static Color TakingColorOf(int type) => Color.Lerp(ColorOf(type), Color.Black, 0.45f);

        // consumer lines take every resource
        public static readonly Color ConsumerColor = new Color(170, 40, 40);

        public static bool CanRequire(int produced, int consumed) => consumed < produced;

        public bool Requires(int produced, int consumed) => requires[produced - 1, consumed - 1];

        public void Set(int produced, int consumed, bool value)
        {
            if (CanRequire(produced, consumed)) requires[produced - 1, consumed - 1] = value;
        }

        public void Toggle(int produced, int consumed) => Set(produced, consumed, !Requires(produced, consumed));

        public List<int> RequirementsOf(int produced) =>
            Enumerable.Range(1, Count).Where(consumed => Requires(produced, consumed)).ToList();

        // Points a resource is worth when consumed: the points of the resources it requires, plus one, doubled.
        // Raw resources are worth 2.
        public long PointsOf(int type)
        {
            long sum = 1;
            foreach (int required in RequirementsOf(type)) sum += PointsOf(required);
            return sum * 2;
        }

        public void Clear() => System.Array.Clear(requires);

        // A random production tree: the first few types are raw (made of nothing), every other type is made of
        // 1-3 lower types, and every type except the last is used by at least one higher type.
        public void Randomize(System.Random random)
        {
            Clear();
            int rawCount = random.Next(2, 4);
            for (int produced = rawCount + 1; produced <= Count; produced++)
            {
                int needCount = random.Next(1, System.Math.Min(3, produced - 1) + 1);
                foreach (int consumed in Enumerable.Range(1, produced - 1).OrderBy(_ => random.Next()).Take(needCount))
                    Set(produced, consumed, true);
            }

            // hook unused types into a random higher type so nothing is left out of the tree
            for (int consumed = 1; consumed < Count; consumed++)
            {
                bool used = Enumerable.Range(consumed + 1, Count - consumed).Any(produced => Requires(produced, consumed));
                if (!used) Set(random.Next(System.Math.Max(consumed + 1, rawCount + 1), Count + 1), consumed, true);
            }
        }
    }
}
