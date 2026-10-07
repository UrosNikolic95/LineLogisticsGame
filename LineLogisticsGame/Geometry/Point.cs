using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace LineLogisticsGame.Geometry
{
    public class Point
    {
        public Vector2 position;

        public List<Point> connections = new List<Point>();
    }
}
