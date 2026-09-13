using static DxLibDLL.DX;
namespace AstrumLoom.DXLib;

public class DxLibMouse : BufferedMouse
{
    protected override void SetNativePosition(int x, int y) => SetMousePoint(x, y);
    protected override void SetNativeVisible(bool visible) => SetMouseDispFlag(visible ? 1 : 0);
    public override void Buffer()
    {
        if (GetMousePoint(out int x, out int y) != 0) return;
        Sample(x, y, GetMouseWheelRotVolF(), GetMouseInput() & 7);
    }
    [Obsolete("Raw input is buffered without a stability delay.")] public static int PressStabilityMs;
    [Obsolete("Dragging must preserve the held button.")] public static float TapMoveTolerance;
    [Obsolete("Wheel events are accumulated until the next update.")] public static int WheelMergeMs;
}
