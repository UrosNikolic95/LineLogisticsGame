using System;
using Microsoft.Xna.Framework;

namespace LineLogisticsGame.Geometry
{
    public static class VectorHelpers
    {
        public static Vector2 FromAngle(float radians, float magnitude = 1f)
        {
            return new Vector2((float)Math.Cos(radians) * magnitude, (float)Math.Sin(radians) * magnitude);
        }

        public static float ToAngle(Vector2 vector)
        {
            return (float)Math.Atan2(vector.Y, vector.X);
        }

        public static float FromDegreesToRadians(float degrees)
        {
            return degrees * ((float)Math.PI / 180f);
        }

        public static float ProjectionFactor(Vector2 ab, Vector2 ac)
        {
            if (ab.X == 0 && ab.Y == 0) throw new Exception("Vector ab cannot be zero.");
            return Vector2.Dot(ab, ac) / Vector2.Dot(ab, ab);
        }

        public static float ProjectionFactor(Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 ab = b - a;
            Vector2 ac = c - a;
            return ProjectionFactor(ab, ac);
        }

        public static Vector2 Ortogonal(Vector2 ab)
        {
            return new Vector2(-ab.Y, ab.X);
        }

        public static bool LineIntersection(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2, out Vector2 p)
        {
            float A1 = a2.Y - a1.Y;
            float B1 = a1.X - a2.X;
            float C1 = A1 * a1.X + B1 * a1.Y;
            float A2 = b2.Y - b1.Y;
            float B2 = b1.X - b2.X;
            float C2 = A2 * b1.X + B2 * b1.Y;
            float determinant = A1 * B2 - A2 * B1;
            if (determinant == 0)
            {
                p = Vector2.Zero;
                return false;
            }
            else
            {
                float x = (B2 * C1 - B1 * C2) / determinant;
                float y = (A1 * C2 - A2 * C1) / determinant;
                p = new Vector2(x, y);
                return true;
            }
        }

        public static bool InBox(Vector2 a, Vector2 b, Vector2 c)
        {
            float xMin = a.X > b.X ? b.X : a.X;
            float yMin = a.Y > b.Y ? b.Y : a.Y;
            float xMax = a.X > b.X ? a.X : b.X;
            float yMax = a.Y > b.Y ? a.Y : b.Y;
            return c.X >= xMin && c.X <= xMax && c.Y >= yMin && c.Y <= yMax;
        }

        public static Vector2 Midpoint(Vector2 a, Vector2 b)
        {
            return new Vector2((a.X + b.X) / 2f, (a.Y + b.Y) / 2f);
        }
    }
}
