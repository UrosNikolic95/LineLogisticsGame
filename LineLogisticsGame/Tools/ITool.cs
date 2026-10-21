using Microsoft.Xna.Framework;
using LineLogisticsGame.UI;
using LineLogisticsGame.World;

namespace LineLogisticsGame.Tools
{
    public interface ITool
    {
        string Name { get; }

        // True while the tool is in the middle of an operation (Escape cancels it instead of exiting).
        bool IsBusy { get; }

        void Update(Input input, LineDrawing drawing);

        // Draws previews/highlights on top of the drawing.
        void Draw(Input input, LineDrawing drawing);

        void Cancel();

        // True when a right click at the mouse is for the tool itself (cancelling, removing what is under it);
        // otherwise a right click deselects the tool.
        bool UsesRightClick(Input input) => IsBusy;

        // Icon shown on the tool's palette button.
        void DrawIcon(Rectangle bounds, Color color);
    }
}
