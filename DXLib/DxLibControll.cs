using static DxLibDLL.DX;

namespace AstrumLoom.DXLib;

public class DxLibMouse : IMouse
{
    public double X { get => _x; set { _x = (int)value; SetPoint(); } }
    public double Y { get => _y; set { _y = (int)value; SetPoint(); } }
    private void SetPoint() => SetMousePoint((int)X, (int)Y);
    public double Wheel { get; private set; }
    public double WheelTotal { get; private set; }

    public bool Push(MouseButton button) => _state[(int)button] == MouseState.Pressed;
    public bool Hold(MouseButton button) => _state[(int)button] == MouseState.Held;
    public bool Left(MouseButton button) => _state[(int)button] == MouseState.Released;

    private int _x, _y;
    private MouseState[] _state = new MouseState[3];
    public void Init(bool visible)
    {
        SetMouseDispFlag(visible ? 1 : 0);
        _prevMask = _curMask = 0;
        _prevWheel = _curWheel = 0;
    }
    public void Update()
    {
        _prevMask = _curMask;
        _prevWheel = _curWheel;

        _curMask = GetMouseInput();
        double wheel = GetMouseWheelRotVolF();

        _curWheel = (float)wheel;

        GetMousePoint(out _x, out _y);
        WheelTotal = _curWheel;
        Wheel = _curWheel - _prevWheel;

        for (int i = 0; i < _state.Length; i++)
        {
            _state[i] = GetMouseState((MouseButton)i);
        }
    }

    private static int _prevMask, _curMask;
    private static float _prevWheel, _curWheel;

    private static MouseState GetMouseState(MouseButton button)
    {
        int bit = button switch
        {
            MouseButton.Left => 1 << 0,
            MouseButton.Right => 1 << 1,
            _ => 1 << 2,
        };
        bool cur = (_curMask & bit) != 0;
        bool prev = (_prevMask & bit) != 0;

        return cur ? prev ? MouseState.Held : MouseState.Pressed : prev ? MouseState.Released : MouseState.None;
    }
}
