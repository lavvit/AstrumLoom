using static Raylib_cs.Raylib;
namespace AstrumLoom.RayLib;

public class RayLibMouse : BufferedMouse
{
    protected override void SetNativePosition(int x, int y) => SetMousePosition(x, y);
    protected override void SetNativeVisible(bool visible) { if (visible) ShowCursor(); else HideCursor(); }
    public override void Buffer()
    {
        int buttons = (IsMouseButtonDown(Raylib_cs.MouseButton.Left) ? 1 : 0)
            | (IsMouseButtonDown(Raylib_cs.MouseButton.Right) ? 2 : 0)
            | (IsMouseButtonDown(Raylib_cs.MouseButton.Middle) ? 4 : 0);
        Sample(GetMouseX(), GetMouseY(), GetMouseWheelMove(), buttons);
    }
    [Obsolete("Raw input is buffered without a stability delay.")] public static int PressStabilityMs;
    [Obsolete("Dragging must preserve the held button.")] public static float TapMoveTolerance;
    [Obsolete("Wheel events are accumulated until the next update.")] public static int WheelMergeMs;
}
