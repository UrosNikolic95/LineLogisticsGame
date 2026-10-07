using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using LineLogisticsGame.Geometry;
using LineLogisticsGame.Simulation;
using LineLogisticsGame.UI;
using LineLogisticsGame.World;

namespace LineLogisticsGame.Tools
{
    // Left click on a road adds a depot (transporter building) there; right click on a depot removes it.
    // A depot sends out transporters with its own capacity and speed, at most one every Interval seconds.
    // Mouse wheel, Up/Down or the palette's capacity stepper sets the capacity, Shift+wheel, Left/Right or the speed stepper
    // the speed, Ctrl+wheel, PageUp/PageDown or the interval stepper the interval, Alt+wheel, Home/End or the limit stepper
    // how many transporters it may have out at once. With the mouse over a depot, wheel and keys change that depot instead
    // of the settings for new ones.
    public class DepotTool : ITool
    {
        public string Name => "Depot";
        public bool IsBusy => false;

        public const float MinInterval = 0.5f, MaxInterval = 10f, IntervalStep = 0.5f;
        public const int MinLimit = 1, MaxLimit = 99;

        public int Capacity = 3;
        public float Speed = 80f;
        public float Interval = 2f;
        public int MaxTransporters = 10;

        private readonly TransportSystem transport;

        public DepotTool(TransportSystem transport)
        {
            this.transport = transport;
        }

        public void ChangeCapacity(int steps) => Capacity = ClampCapacity(Capacity + steps);
        public void ChangeSpeed(int steps) => Speed = ClampSpeed(Speed + steps * TransporterTool.SpeedStep);
        public void ChangeInterval(int steps) => Interval = ClampInterval(Interval + steps * IntervalStep);
        public void ChangeLimit(int steps) => MaxTransporters = ClampLimit(MaxTransporters + steps);

        private static int ClampCapacity(int capacity) => Math.Clamp(capacity, TransporterTool.MinCapacity, TransporterTool.MaxCapacity);
        private static float ClampSpeed(float speed) => MathHelper.Clamp(speed, TransporterTool.MinSpeed, TransporterTool.MaxSpeed);
        private static float ClampInterval(float interval) => MathHelper.Clamp(interval, MinInterval, MaxInterval);
        private static int ClampLimit(int limit) => Math.Clamp(limit, MinLimit, MaxLimit);

        public void Update(Input input, LineDrawing drawing)
        {
            int capacityChange = 0, speedChange = 0, intervalChange = 0, limitChange = 0;
            if (input.Alt) limitChange += input.ScrollSteps;
            else if (input.Ctrl) intervalChange += input.ScrollSteps;
            else if (input.Shift) speedChange += input.ScrollSteps;
            else capacityChange += input.ScrollSteps;
            if (input.KeyPressed(Keys.Up)) capacityChange++;
            if (input.KeyPressed(Keys.Down)) capacityChange--;
            if (input.KeyPressed(Keys.Right)) speedChange++;
            if (input.KeyPressed(Keys.Left)) speedChange--;
            if (input.KeyPressed(Keys.PageUp)) intervalChange++;
            if (input.KeyPressed(Keys.PageDown)) intervalChange--;
            if (input.KeyPressed(Keys.Home)) limitChange++;
            if (input.KeyPressed(Keys.End)) limitChange--;

            Depot hovered = transport.FindDepot(input.MousePosition, 4f);
            if (hovered != null)
            {
                // its transporters already out come back to get the new capacity and speed once they are idle
                // (a lower limit doesn't call any back: it sends no more until fewer than that are out)
                hovered.Capacity = ClampCapacity(hovered.Capacity + capacityChange);
                hovered.Speed = ClampSpeed(hovered.Speed + speedChange * TransporterTool.SpeedStep);
                hovered.Interval = ClampInterval(hovered.Interval + intervalChange * IntervalStep);
                hovered.Waiting = Math.Min(hovered.Waiting, hovered.Interval);
                hovered.MaxTransporters = ClampLimit(hovered.MaxTransporters + limitChange);
            }
            else
            {
                ChangeCapacity(capacityChange);
                ChangeSpeed(speedChange);
                ChangeInterval(intervalChange);
                ChangeLimit(limitChange);
            }

            if (input.RightPressed)
            {
                if (hovered != null) transport.RemoveDepot(hovered);
                return;
            }

            if (!input.LeftPressed || hovered != null) return;

            StyledSegment segment = RoadAt(input.MousePosition, drawing);
            if (segment != null)
                transport.AddDepot(segment, input.MousePosition, Capacity, Speed, Interval, MaxTransporters);
        }

        // depots only go on roads
        private StyledSegment RoadAt(Vector2 position, LineDrawing drawing)
        {
            StyledSegment segment = drawing.FindSegment(position, drawing.SnapRadius);
            return segment != null && transport.IsWalkable(segment) ? segment : null;
        }

        // The depot that would be placed, on the road under the mouse, or the one under the mouse (which right click removes),
        // with a "capacity speed interval" label under it.
        public void Draw(Input input, LineDrawing drawing)
        {
            Depot hovered = transport.FindDepot(input.MousePosition, 4f);
            if (hovered != null)
            {
                DrawShape.DrawCircle(hovered.Position, transport.DepotRadius + 4f, 2f, Color.Red);
                DrawSettings(hovered.Position, hovered.Capacity, hovered.Speed, hovered.Interval);
                return;
            }

            StyledSegment segment = RoadAt(input.MousePosition, drawing);
            Vector2 position = input.MousePosition;
            if (segment != null)
            {
                Vector2 a = segment.A.position, b = segment.B.position;
                position = a + (b - a) * MathHelper.Clamp(VectorHelpers.ProjectionFactor(a, b, position), 0f, 1f);
            }
            transport.DrawDepot(position, segment != null ? 0.6f : 0.3f);
            DrawSettings(position, Capacity, Speed, Interval);
            // what a placed depot shows above it: none of its transporters out of at most MaxTransporters
            TransportSystem.DrawLabel(position - new Vector2(0, transport.DepotRadius + 12f),
                ("0", transport.TransporterColor), ("/", Color.White * 0.5f), (MaxTransporters.ToString(), Color.White));
        }

        // under the depot, since the count of its transporters out is shown above it
        private void DrawSettings(Vector2 position, int capacity, float speed, float interval)
        {
            TransportSystem.DrawLabel(position + new Vector2(0, transport.DepotRadius + 14f),
                (capacity.ToString(), transport.ResourceColor), (" ", Color.White),
                (((int)speed).ToString(), transport.TransporterColor), (" ", Color.White),
                (FormatInterval(interval), Color.White));
        }

        public static string FormatInterval(float interval) => interval.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

        public void Cancel() { }

        // a depot on a road with a transporter leaving it
        public void DrawIcon(Rectangle bounds, Color color)
        {
            Vector2 center = bounds.Center.ToVector2();
            DrawShape.DrawLine(new Vector2(bounds.Left + 6, center.Y), new Vector2(bounds.Right - 6, center.Y), color, 2f);
            transport.DrawDepot(center + new Vector2(-8f, 0), 1f);
            DrawShape.DrawFilledCircle(center + new Vector2(12f, 0), 5f, 1.5f, transport.TransporterColor, Color.White);
        }
    }
}
