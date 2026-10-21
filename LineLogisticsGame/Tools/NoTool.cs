using Microsoft.Xna.Framework;
using LineLogisticsGame.UI;
using LineLogisticsGame.World;

namespace LineLogisticsGame.Tools
{
    // Stands in for "no tool selected" (after a right click deselects the tool): clicks do nothing.
    public class NoTool : ITool
    {
        public string Name => "None";
        public bool IsBusy => false;

        public void Update(Input input, LineDrawing drawing) { }

        public void Draw(Input input, LineDrawing drawing) { }

        public void Cancel() { }

        public void DrawIcon(Rectangle bounds, Color color) { }
    }
}
