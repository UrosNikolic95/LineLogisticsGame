using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using LineLogisticsGame.Geometry;
using LineLogisticsGame.Simulation;
using LineLogisticsGame.UI;
using LineLogisticsGame.World;

namespace LineLogisticsGame.Tools
{
    // Left click on a line adds a transporter there; right click on a transporter removes it.
    // Before placing: mouse wheel, Up/Down or the palette's capacity stepper sets the capacity,
    // Shift+wheel, Left/Right or the palette's speed stepper sets the speed.
    public class TransporterTool : ITool
    {
        public string Name => "Transporter";
        public bool IsBusy => false;

        public const int MinCapacity = 1, MaxCapacity = 9;
        public const float MinSpeed = 20f, MaxSpeed = 300f, SpeedStep = 10f;

        public int Capacity = 3;
        public float Speed = 80f;

        private readonly TransportSystem transport;

        public TransporterTool(TransportSystem transport)
        {
            this.transport = transport;
        }

        public void ChangeCapacity(int steps) => Capacity = Math.Clamp(Capacity + steps, MinCapacity, MaxCapacity);
        public void ChangeSpeed(int steps) => Speed = MathHelper.Clamp(Speed + steps * SpeedStep, MinSpeed, MaxSpeed);

        public void Update(Input input, LineDrawing drawing)
        {
            int capacityChange = 0, speedChange = 0;
            if (input.Shift) speedChange += input.ScrollSteps;
            else capacityChange += input.ScrollSteps;
            if (input.KeyPressed(Keys.Up)) capacityChange++;
            if (input.KeyPressed(Keys.Down)) capacityChange--;
            if (input.KeyPressed(Keys.Right)) speedChange++;
            if (input.KeyPressed(Keys.Left)) speedChange--;
            ChangeCapacity(capacityChange);
            ChangeSpeed(speedChange);

            if (input.RightPressed)
            {
                Transporter transporter = transport.FindTransporter(input.MousePosition, 4f);
                if (transporter != null) transport.RemoveTransporter(transporter);
                return;
            }

            if (!input.LeftPressed) return;

            StyledSegment segment = RoadAt(input.MousePosition, drawing);
            if (segment != null)
                transport.AddTransporter(segment, input.MousePosition, Capacity, Speed);
        }

        // transporters only go on roads
        private StyledSegment RoadAt(Vector2 position, LineDrawing drawing)
        {
            StyledSegment segment = drawing.FindSegment(position, drawing.SnapRadius);
            return segment != null && transport.IsWalkable(segment) ? segment : null;
        }

        // Ghost of the transporter that would be placed: its empty wagons behind it (toward the line's A end)
        // and a "capacity speed" label above it.
        public void Draw(Input input, LineDrawing drawing)
        {
            Transporter hovered = transport.FindTransporter(input.MousePosition, 4f);
            if (hovered != null)
            {
                DrawShape.DrawCircle(hovered.position, hovered.radius + 4f, 2f, Color.Red);
                DrawSettings(hovered.position, hovered.Capacity, hovered.speed);
                return;
            }

            StyledSegment segment = RoadAt(input.MousePosition, drawing);
            Vector2 position = input.MousePosition;
            Vector2 back = -Vector2.UnitX;
            if (segment != null)
            {
                Vector2 a = segment.A.position, b = segment.B.position;
                float t = MathHelper.Clamp(VectorHelpers.ProjectionFactor(a, b, input.MousePosition), 0f, 1f);
                position = a + (b - a) * t;
                if (a != b) back = Vector2.Normalize(a - b);
            }

            float alpha = segment != null ? 0.6f : 0.3f;
            for (int i = Capacity - 1; i >= 0; i--)
                transport.DrawWagon(position + back * (transport.FirstWagonOffset + i * transport.WagonStep), false, alpha * 1.5f);
            DrawShape.DrawFilledCircle(position, transport.TransporterRadius, 2f, transport.TransporterColor * alpha, Color.White * alpha);
            DrawSettings(position, Capacity, Speed);
        }

        private void DrawSettings(Vector2 position, int capacity, float speed)
        {
            TransportSystem.DrawLabel(position - new Vector2(0, transport.TransporterRadius + 16f),
                (capacity.ToString(), transport.ResourceColor), (" ", Color.White), (((int)speed).ToString(), transport.TransporterColor));
        }

        public void Cancel() { }

        public void DrawIcon(Rectangle bounds, Color color)
        {
            Vector2 center = bounds.Center.ToVector2();
            DrawShape.DrawLine(new Vector2(bounds.Left + 6, center.Y), new Vector2(bounds.Right - 6, center.Y), color, 2f);
            DrawShape.DrawFilledCircle(center + new Vector2(-14f, 0), 4f, 1f, Color.Gold, Color.Black);
            DrawShape.DrawFilledCircle(center + new Vector2(-7f, 0), 4f, 1f, Color.Gold, Color.Black);
            DrawShape.DrawFilledCircle(center + new Vector2(4f, 0), 8f, 2f, Color.DeepSkyBlue, Color.White);
        }
    }
}
