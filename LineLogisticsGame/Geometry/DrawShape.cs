using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace LineLogisticsGame.Geometry
{
    // Draws primitives directly on the GPU (no textures).
    // Call Initialize once in LoadContent. Draw calls must be outside SpriteBatch.Begin/End.
    public static class DrawShape
    {
        private static GraphicsDevice graphicsDevice;
        private static BasicEffect effect;

        // Minimum number of segments for a full circle; large circles get more so they stay round.
        public static int Segments = 64;

        // Width in pixels of the soft edge that fades to transparent (anti-aliasing). 0 gives hard edges.
        public static float Feather = 1.2f;

        // World-to-screen transform applied to everything drawn (identity for screen-space UI).
        public static Matrix View = Matrix.Identity;

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

        public static void DrawFilledCircle(Vector2 center, float radius, float borderWidth, Color fill, Color border)
        {
            float innerRadius = radius - borderWidth;
            bool hasBorder = borderWidth > 0;
            if (innerRadius > 0)
            {
                // with a border the fill runs solid up to it; without one it fades out at the edge
                float solidRadius = hasBorder ? innerRadius : Math.Max(0, innerRadius - Feather / 2f);
                int segments = SegmentsFor(innerRadius);
                var verts = new VertexPositionColor[segments * 3];
                for (int i = 0; i < segments; i++)
                {
                    float a1 = MathHelper.TwoPi * i / segments;
                    float a2 = MathHelper.TwoPi * (i + 1) / segments;
                    verts[i * 3] = Vertex(center, fill);
                    verts[i * 3 + 1] = Vertex(center + VectorHelpers.FromAngle(a1, solidRadius), fill);
                    verts[i * 3 + 2] = Vertex(center + VectorHelpers.FromAngle(a2, solidRadius), fill);
                }
                Render(PrimitiveType.TriangleList, verts, segments);

                if (!hasBorder && Feather > 0)
                    DrawBand(center, solidRadius, innerRadius + Feather / 2f, fill, Color.Transparent, 0, MathHelper.TwoPi, segments);
            }

            // border sits inside the radius, so the total size stays equal to radius
            if (hasBorder)
                DrawRing(center, radius - borderWidth / 2f, borderWidth, 0, MathHelper.TwoPi, border, fadeInner: innerRadius <= 0);
        }

        public static void DrawCircle(Vector2 center, float radius, float width, Color border)
        {
            DrawRing(center, radius, width, 0, MathHelper.TwoPi, border);
        }

        // start and end are points on the circle; arc goes clockwise (on screen) from start to end
        public static void DrawArc(Vector2 center, float radius, Vector2 start, Vector2 end, float borderWidth, Color border)
        {
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

            EdgeProfile(width, ref color, out float core, out float edge);
            Vector2 normal = Vector2.Normalize(VectorHelpers.Ortogonal(direction));
            Vector2 coreOffset = normal * core;
            Vector2 edgeOffset = normal * edge;
            Color clear = Color.Transparent;

            var verts = new List<VertexPositionColor>(18);
            if (core > 0)
                AddQuad(verts, start + coreOffset, end + coreOffset, start - coreOffset, end - coreOffset, color, color);
            if (edge > core)
            {
                AddQuad(verts, start + edgeOffset, end + edgeOffset, start + coreOffset, end + coreOffset, clear, color);
                AddQuad(verts, start - edgeOffset, end - edgeOffset, start - coreOffset, end - coreOffset, clear, color);
            }
            Render(PrimitiveType.TriangleList, verts.ToArray(), verts.Count / 3);
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
        private static void AddQuad(List<VertexPositionColor> verts, Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2, Color outerColor, Color innerColor)
        {
            verts.Add(Vertex(a1, outerColor));
            verts.Add(Vertex(a2, outerColor));
            verts.Add(Vertex(b1, innerColor));
            verts.Add(Vertex(b1, innerColor));
            verts.Add(Vertex(a2, outerColor));
            verts.Add(Vertex(b2, innerColor));
        }

        // ~3px per segment, so big circles don't look like polygons
        private static int SegmentsFor(float radius, float sweep = MathHelper.TwoPi)
        {
            int full = Math.Clamp((int)Math.Ceiling(MathHelper.TwoPi * radius / 3f), Segments, 1024);
            return Math.Max(1, (int)Math.Ceiling(full * sweep / MathHelper.TwoPi));
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
            var quad = new[]
            {
                Vertex(new Vector2(x, y), color),
                Vertex(new Vector2(x + width, y), color),
                Vertex(new Vector2(x, y + height), color),
                Vertex(new Vector2(x + width, y + height), color)
            };
            Render(PrimitiveType.TriangleStrip, quad, 2);
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

            var verts = new VertexPositionColor[(segments + 1) * 2];
            for (int i = 0; i <= segments; i++)
            {
                float angle = startAngle + sweep * i / segments;
                verts[i * 2] = Vertex(center + VectorHelpers.FromAngle(angle, outerRadius), outerColor);
                verts[i * 2 + 1] = Vertex(center + VectorHelpers.FromAngle(angle, innerRadius), innerColor);
            }
            Render(PrimitiveType.TriangleStrip, verts, segments * 2);
        }

        private static VertexPositionColor Vertex(Vector2 position, Color color)
        {
            return new VertexPositionColor(new Vector3(position, 0), color);
        }

        private static void Render(PrimitiveType type, VertexPositionColor[] verts, int primitiveCount)
        {
            if (graphicsDevice == null) throw new Exception("DrawShape.Initialize must be called before drawing.");

            Viewport viewport = graphicsDevice.Viewport;
            effect.View = View;
            effect.Projection = Matrix.CreateOrthographicOffCenter(0, viewport.Width, viewport.Height, 0, 0, 1);

            graphicsDevice.RasterizerState = RasterizerState.CullNone;
            graphicsDevice.BlendState = BlendState.AlphaBlend;
            graphicsDevice.DepthStencilState = DepthStencilState.None;

            foreach (var pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                graphicsDevice.DrawUserPrimitives(type, verts, 0, primitiveCount);
            }
        }
    }
}
