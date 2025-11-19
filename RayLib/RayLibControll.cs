using static Raylib_cs.Raylib;

namespace AstrumLoom.RayLib;

public class RayLibMouse : IMouse
{
    public double X { get => _x; set { _x = (int)value; SetPoint(); } }
    public double Y { get => _y; set { _y = (int)value; SetPoint(); } }
    private void SetPoint() => SetMousePosition((int)X, (int)Y);
    public double Wheel { get; private set; }
    public double WheelTotal { get; private set; }

    public bool Push(MouseButton button) => _state[(int)button] == MouseState.Pressed;
    public bool Hold(MouseButton button) => _state[(int)button] == MouseState.Held;
    public bool Left(MouseButton button) => _state[(int)button] == MouseState.Released;

    private int _x, _y;
    private MouseState[] _state = new MouseState[3];
    public void Init(bool visible)
    {
        ShowCursor(); // Raylib は表示/非表示 API が逆（Hide/Show）
        if (!visible) HideCursor();
        _prevMask = _curMask = 0;
        _prevWheel = _curWheel = 0;
        _downTickLeft = _downTickRight = _downTickMiddle = 0;
    }
    public void Update()
    {
        _prevMask = _curMask;
        _prevWheel = _curWheel;

        long now = Environment.TickCount64;

        // 生の押下
        bool lRaw = IsMouseButtonDown(Raylib_cs.MouseButton.Left);
        bool rRaw = IsMouseButtonDown(Raylib_cs.MouseButton.Right);
        bool mRaw = IsMouseButtonDown(Raylib_cs.MouseButton.Middle);

        // 押下開始を記録（安定化用）
        if (lRaw) RecordDown(MouseButton.Left, now);
        if (rRaw) RecordDown(MouseButton.Right, now);
        if (mRaw) RecordDown(MouseButton.Middle, now);

        // 安定化判定（必要なら）
        bool l = IsStableDown(MouseButton.Left, lRaw, now) && WithinTolerance(MouseButton.Left);
        bool r = IsStableDown(MouseButton.Right, rRaw, now) && WithinTolerance(MouseButton.Right);
        bool m = IsStableDown(MouseButton.Middle, mRaw, now) && WithinTolerance(MouseButton.Middle);

        _curMask = 0;
        if (l) _curMask |= 1 << 0;
        if (r) _curMask |= 1 << 1;
        if (m) _curMask |= 1 << 2;

        // ホイール：Raylib は「フレーム差分」を返す
        float wheelDelta = GetMouseWheelMove();
        if (WheelMergeMs > 0 && Math.Abs(wheelDelta) > 0)
        {
            // 軽い統合（タッチパッドの細切れイベントをまとめる）
            // ここでは単純に加算保持のみ。必要ならタイムスタンプ管理で一定時間内を合算にする。
            _curWheel = _prevWheel + wheelDelta;
        }
        else
        {
            _curWheel = _prevWheel + wheelDelta;
        }

        _x = GetMouseX();
        _y = GetMouseY();
        WheelTotal = _curWheel;
        Wheel = WheelTotal - _prevWheel;
        for (int i = 0; i < 3; i++)
        {
            _state[i] = GetMouseState((MouseButton)i);
        }
    }

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

    // ====== 設定（タッチパッドゆらぎ対策）======
    /// <summary>Pressed と判定するための最小押下時間(ms)。0 で即時。</summary>
    public static int PressStabilityMs = 0;
    /// <summary>押下直後の移動許容量(px)。超えると「ドラッグ始動」とみなしてもOK。</summary>
    public static float TapMoveTolerance = 3f;
    /// <summary>ホイールの連続イベントをマージする時間(ms)。0で無効。</summary>
    public static int WheelMergeMs = 35;

    // ====== 内部状態 ======
    private static int _prevMask, _curMask;
    private static float _prevWheel, _curWheel;
    private static long _downTickLeft, _downTickRight, _downTickMiddle;
    private static System.Numerics.Vector2 _downPosLeft, _downPosRight, _downPosMiddle;

    // 押下/解放のエッジ検出 & 安定化
    private static bool IsStableDown(MouseButton button, bool rawDown, long now)
    {
        if (PressStabilityMs <= 0) return rawDown;

        long t = button switch
        {
            MouseButton.Left => _downTickLeft,
            MouseButton.Right => _downTickRight,
            _ => _downTickMiddle
        };

        if (rawDown)
        {
            if (t == 0) t = now;
            return now - t >= PressStabilityMs;
        }
        else
        {
            t = 0;
            return false;
        }
    }

    private static void RecordDown(MouseButton button, long now)
    {
        var pos = GetMousePosition();
        switch (button)
        {
            case MouseButton.Left:
                if (_downTickLeft == 0) { _downTickLeft = now; _downPosLeft = pos; }
                break;
            case MouseButton.Right:
                if (_downTickRight == 0) { _downTickRight = now; _downPosRight = pos; }
                break;
            case MouseButton.Middle:
                if (_downTickMiddle == 0) { _downTickMiddle = now; _downPosMiddle = pos; }
                break;
        }
    }

    private static bool WithinTolerance(MouseButton b)
    {
        if (TapMoveTolerance <= 0) return true;
        var now = GetMousePosition();
        var refPos = b switch
        {
            MouseButton.Left => _downPosLeft,
            MouseButton.Right => _downPosRight,
            MouseButton.Middle => _downPosMiddle,
            _ => now
        };
        float dx = now.X - refPos.X;
        float dy = now.Y - refPos.Y;
        return dx * dx + dy * dy <= TapMoveTolerance * TapMoveTolerance;
    }
}
