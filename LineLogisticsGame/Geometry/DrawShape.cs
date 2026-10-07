using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace LineLogisticsGame.Geometry
{
    // Draws primitives directly on the GPU (no textures).
    // Call Initialize once in LoadContent. Draw calls must be outside SpriteBatch.Begin/End.
    // Shapes are collected into one triangle list and sent to the GPU in a single draw call when Flush is called,
    // when View changes, or when the list gets long. Call Flush before every SpriteBatch.Begin (so text lands on top of
    // the shapes drawn before it) and at the end of the frame.
    // Shapes entirely off screen are skipped.
    public static class DrawShape
    {
        private static GraphicsDevice graphicsDevice;
        private static BasicEffect effect;

        private const int MaxVertices = 3 * 20000;
        private static VertexPositionColor[] vertices = new VertexPositionColor[3 * 1024];
        private static int vertexCount;

        // Minimum number of segments for a full circle; large circles get more so they stay round.
        public static int Segments = 16;

        // Width in pixels of the soft edge that fades to transparent (anti-aliasing). 0 gives hard edges.
        public static float Feather = 1.2f;

        // World-to-screen transform applied to everything drawn (identity for screen-space UI).
        public static Matrix View
        {
            get => view;
            set
            {
                if (value == view) return;
                Flush();
                view = value;
                scale = new Vector2(value.M11, value.M12).Length();
            }
        }
        private static Matrix view = Matrix.Identity;
        private static float scale = 1f;    // screen pixels per unit under View

        // cos/sin around a full circle, per segment count
        private static readonly Dictionary<int, Vector2[]> unitCircles = new Dictionary<int, Vector2[]>();

        public static void Initialize(GraphicsDevice gd)
        {
            if (graphicsDevice == null)
            {
                graphicsDevice = gd;
                effect = new BasicEffect(gd)
                {
                    VertexColorEnabled = true,
                    World = Matrix.Identity,
                    View = Matrix.Identity
                };
            };
        }

        // Sends everything collected so far to the GPU.
        public static void Flush()
        {
            if (vertexCount == 0) return;
            if (graphicsDevice == null) throw new Exception("DrawShape.Initialize must be called before drawing.");

            Viewport viewport = graphicsDevice.Viewport;
            effect.View = view;
            effect.Projection = Matrix.CreateOrthographicOffCenter(0, viewport.Width, viewport.Height, 0, 0, 1);

            graphicsDevice.RasterizerState = RasterizerState.CullNone;
            graphicsDevice.BlendState = BlendState.AlphaBlend;
            graphicsDevice.DepthStencilState = DepthStencilState.None;

            foreach (var pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                graphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, vertexCount / 3);
            }
            vertexCount = 0;
        }

        // Makes room for count more vertices.
        private static void Reserve(int count)
        {
            if (vertexCount + count > MaxVertices) Flush();
            if (vertexCount + count > vertices.Length)
                Array.Resize(ref vertices, Math.Max(vertices.Length * 2, vertexCount + count));
        }

        private static void Add(Vector2 position, Color color)
        {
            vertices[vertexCount++] = new VertexPositionColor(new Vector3(position, 0), color);
        }

        // Whether a box (corners in world units) is entirely off the screen.
        private static bool OffScreen(Vector2 min, Vector2 max)
        {
            if (graphicsDevice == null) return false;
            Viewport viewport = graphicsDevice.Viewport;
            Vector2 a = Vector2.Transform(min, view), b = Vector2.Transform(max, view);
            return Math.Max(a.X, b.X) < 0 || Math.Min(a.X, b.X) > viewport.Width
                || Math.Max(a.Y, b.Y) < 0 || Math.Min(a.Y, b.Y) > viewport.Height;
        }

        private static bool CircleOffScreen(Vector2 center, float radius)
        {
            Vector2 extent = new Vector2(radius + Feather);
            return OffScreen(center - extent, center + extent);
        }

        public static void DrawFilledCircle(Vector2 center, float radius, float borderWidth, Color fill, Color border)
        {
            if (CircleOffScreen(center, radius)) return;

            float innerRadius = radius - borderWidth;
            bool hasBorder = borderWidth > 0;
            if (innerRadius > 0)
            {
                // with a border the fill runs solid up to it; without one it fades out at the edge
                float solidRadius = hasBorder ? innerRadius : Math.Max(0, innerRadius - Feather / 2f);
                int segments = SegmentsFor(innerRadius);
                Vector2[] unit = UnitCircle(segments);
                Reserve(segments * 3);
                for (int i = 0; i < segments; i++)
                {
                    Add(center, fill);
                    Add(center + unit[i] * solidRadius, fill);
                    Add(center + unit[i + 1] * solidRadius, fill);
                }

                if (!hasBorder && Feather > 0)
                    DrawBand(center, solidRadius, innerRadius + Feather / 2f, fill, Color.Transparent, 0, MathHelper.TwoPi, segments);
            }

            // border sits inside the radius, so the total size stays equal to radius
            if (hasBorder)
                DrawRing(center, radius - borderWidth / 2f, borderWidth, 0, MathHelper.TwoPi, border, fadeInner: innerRadius <= 0);
        }

        public static void DrawCircle(Vector2 center, float radius, float width, Color border)
        {
            if (CircleOffScreen(center, radius + width / 2f)) return;
            DrawRing(center, radius, width, 0, MathHelper.TwoPi, border);
        }

        // start and end are points on the circle; arc goes clockwise (on screen) from start to end
        public static void DrawArc(Vector2 center, float radius, Vector2 start, Vector2 end, float borderWidth, Color border)
        {
            if (CircleOffScreen(center, radius + borderWidth / 2f)) return;

            float startAngle = VectorHelpers.ToAngle(start - center);
            float endAngle = VectorHelpers.ToAngle(end - center);
            float sweep = endAngle - startAngle;
            if (sweep <= 0) sweep += MathHelper.TwoPi;

            DrawRing(center, radius, borderWidth, startAngle, sweep, border);
        }

        // Solid core with a soft edge on both long sides, so the line looks smooth at any angle.
        public static void DrawLine(Vector2 start, Vector2 end, Color color, float width)
        {
            Vector2 direction = end - start;
            if (direction == Vector2.Zero) return;
            Vector2 extent = new Vector2(width / 2f + Feather);
            if (OffScreen(Vector2.Min(start, end) - extent, Vector2.Max(start, end) + extent)) return;

            EdgeProfile(width, ref color, out float core, out float edge);
            Vector2 normal = Vector2.Normalize(VectorHelpers.Ortogonal(direction));
            Vector2 coreOffset = normal * core;
            Vector2 edgeOffset = normal * edge;
            Color clear = Color.Transparent;

            if (core > 0)
                AddQuad(start + coreOffset, end + coreOffset, start - coreOffset, end - coreOffset, color, color);
            if (edge > core)
            {
                AddQuad(start + edgeOffset, end + edgeOffset, start + coreOffset, end + coreOffset, clear, color);
                AddQuad(start - edgeOffset, end - edgeOffset, start - coreOffset, end - coreOffset, clear, color);
            }
        }

        // Half-widths of the solid core and of the outer soft edge for a stroke of the given width.
        // Strokes thinner than the feather get fainter instead of thinner, which keeps them smooth.
        private static void EdgeProfile(float width, ref Color color, out float core, out float edge)
        {
            float half = width / 2f;
            if (Feather <= 0)
            {
                core = edge = half;
                return;
            }
            core = Math.Max(0, half - Feather / 2f);
            edge = core + Feather;
            if (width < Feather) color *= width / Feather;
        }

        // Two triangles: outer pair (a1, a2) in outerColor, inner pair (b1, b2) in innerColor.
        private static void AddQuad(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2, Color outerColor, Color innerColor)
        {
            Reserve(6);
            Add(a1, outerColor);
            Add(a2, outerColor);
            Add(b1, innerColor);
            Add(b1, innerColor);
            Add(a2, outerColor);
            Add(b2, innerColor);
        }

        // ~3 screen pixels per segment, so big circles don't look like polygons and small ones don't waste triangles
        private static int SegmentsFor(float radius, float sweep = MathHelper.TwoPi)
        {
            int full = Math.Clamp((int)Math.Ceiling(MathHelper.TwoPi * radius * scale / 3f), Segments, 1024);
            return Math.Max(1, (int)Math.Ceiling(full * sweep / MathHelper.TwoPi));
        }

        // segments + 1 points around the unit circle, the last one equal to the first
        private static Vector2[] UnitCircle(int segments)
        {
            if (unitCircles.TryGetValue(segments, out var points)) return points;
            points = new Vector2[segments + 1];
            for (int i = 0; i <= segments; i++)
                points[i] = VectorHelpers.FromAngle(MathHelper.TwoPi * i / segments, 1f);
            unitCircles[segments] = points;
            return points;
        }

        public static void DrawFilledRectangle(Rectangle rectangle, Color color)
        {
            FillQuad(rectangle.Left, rectangle.Top, rectangle.Width, rectangle.Height, color);
        }

        // border sits inside the rectangle
        public static void DrawRectangle(Rectangle rectangle, float borderWidth, Color color)
        {
            float x = rectangle.Left, y = rectangle.Top, w = rectangle.Width, h = rectangle.Height;
            FillQuad(x, y, w, borderWidth, color);
            FillQuad(x, y + h - borderWidth, w, borderWidth, color);
            FillQuad(x, y + borderWidth, borderWidth, h - 2 * borderWidth, color);
            FillQuad(x + w - borderWidth, y + borderWidth, borderWidth, h - 2 * borderWidth, color);
        }

        public static void DrawArrow(Vector2 start, Vector2 end, Color color, float width, float headLength = 8f)
        {
            Vector2 direction = end - start;
            if (direction == Vector2.Zero) return;

            DrawLine(start, end, color, width);
            float angle = VectorHelpers.ToAngle(-direction);
            DrawLine(end, end + VectorHelpers.FromAngle(angle + MathHelper.PiOver4, headLength), color, width);
            DrawLine(end, end + VectorHelpers.FromAngle(angle - MathHelper.PiOver4, headLength), color, width);
        }

        private static void FillQuad(float x, float y, float width, float height, Color color)
        {
            if (width <= 0 || height <= 0) return;
            Vector2 topLeft = new Vector2(x, y), topRight = new Vector2(x + width, y);
            Vector2 bottomLeft = new Vector2(x, y + height), bottomRight = new Vector2(x + width, y + height);
            if (OffScreen(topLeft, bottomRight)) return;
            AddQuad(topLeft, topRight, bottomLeft, bottomRight, color, color);
        }

        // Band of given width centered on radius, with soft outer (and optionally inner) edges
        private static void DrawRing(Vector2 center, float radius, float width, float startAngle, float sweep, Color color, bool fadeInner = true)
        {
            EdgeProfile(width, ref color, out float core, out float edge);
            int segments = SegmentsFor(radius + edge, sweep);

            float solidInner = fadeInner ? radius - core : radius - width / 2f;
            DrawBand(center, solidInner, radius + core, color, color, startAngle, sweep, segments);
            DrawBand(center, radius + core, radius + edge, color, Color.Transparent, startAngle, sweep, segments);
            if (fadeInner)
                DrawBand(center, radius - edge, radius - core, Color.Transparent, color, startAngle, sweep, segments);
        }

        // Ring section between two radii, colors blended from inner to outer (no gaps at joints)
        private static void DrawBand(Vector2 center, float innerRadius, float outerRadius, Color innerColor, Color outerColor, float startAngle, float sweep, int segments)
        {
            innerRadius = Math.Max(0, innerRadius);
            if (outerRadius <= innerRadius) return;

            // full circles reuse the cached cos/sin table
            Vector2[] unit = startAngle == 0 && sweep == MathHelper.TwoPi ? UnitCircle(segments) : null;
            Vector2 Direction(int i) => unit != null ? unit[i] : VectorHelpers.FromAngle(startAngle + sweep * i / segments, 1f);

            Reserve(segments * 6);
            Vector2 previous = Direction(0);
            for (int i = 1; i <= segments; i++)
            {
                Vector2 next = Direction(i);
                Vector2 outer1 = center + previous * outerRadius, inner1 = center + previous * innerRadius;
                Vector2 outer2 = center + next * outerRadius, inner2 = center + next * innerRadius;
                Add(outer1, outerColor);
                Add(inner1, innerColor);
                Add(outer2, outerColor);
                Add(inner1, innerColor);
                Add(outer2, outerColor);
                Add(inner2, innerColor);
                previous = next;
            }
        }
    }
}
