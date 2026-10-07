using Microsoft.Xna.Framework;
using LineLogisticsGame.Geometry;
using LineLogisticsGame.UI;
using LineLogisticsGame.World;

namespace LineLogisticsGame.Tools
{
    // Click or drag over lines to delete them.
    public class EraseTool : ITool
    {
        public string Name => "Erase";
        public bool IsBusy => false;

        public void Update(Input input, LineDrawing drawing)
        {
            if (!input.LeftDown) return;

            StyledSegment segment = drawing.FindSegment(input.MousePosition, 4f);
            if (segment == null) return;

            drawing.SaveUndo();
            drawing.RemoveSegment(segment);
        }

        public void Draw(Input input, LineDrawing drawing)
        {
            StyledSegment segment = drawing.FindSegment(input.MousePosition, 4f);
            if (segment != null)
                DrawShape.DrawLine(segment.A.position, segment.B.position, Color.Red * 0.7f, segment.Width + 4f);
            DrawShape.DrawCircle(input.MousePosition, 4f, 1f, Color.Red);
        }

        public void Cancel() { }

        public void DrawIcon(Rectangle bounds, Color color)
        {
            int size = System.Math.Min(bounds.Width, bounds.Height) - 28;
            bounds = new Rectangle(bounds.Center.X - size / 2, bounds.Center.Y - size / 2, size, size);
            DrawShape.DrawLine(new Vector2(bounds.Left, bounds.Top), new Vector2(bounds.Right, bounds.Bottom), color, 3f);
            DrawShape.DrawLine(new Vector2(bounds.Left, bounds.Bottom), new Vector2(bounds.Right, bounds.Top), color, 3f);
        }
    }
}
