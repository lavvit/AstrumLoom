using static Raylib_cs.Raylib;

namespace AstrumLoom.RayLib;

/// <summary>
/// IMouse の raylib 実装。生の押下状態をそのまま返すのではなく、押下安定化（PressStabilityMs）と
/// タップ移動許容量（TapMoveTolerance）を挟んでタッチパッド特有の微小なブレを吸収してからボタン状態を確定する。
/// </summary>
public class RayLibMouse : IMouse
{
    /// <summary>マウスX座標。setするとraylib側のカーソル位置も即座に移動させる。</summary>
    public double X { get => _x; set { _x = (int)value; SetPoint(); } }
    /// <summary>マウスY座標。setするとraylib側のカーソル位置も即座に移動させる。</summary>
    public double Y { get => _y; set { _y = (int)value; SetPoint(); } }
    private void SetPoint() => SetMousePosition((int)X, (int)Y);
    /// <summary>直近フレームでのホイール移動量（差分）。</summary>
    public double Wheel { get; private set; }
    /// <summary>ホイール移動量の累積値。</summary>
    public double WheelTotal { get; private set; }

    /// <summary>指定ボタンが押された瞬間かどうか。</summary>
    public bool Push(MouseButton button) => _state[(int)button] == MouseState.Pressed;
    /// <summary>指定ボタンが押され続けているかどうか。</summary>
    public bool Hold(MouseButton button) => _state[(int)button] == MouseState.Held;
    /// <summary>指定ボタンが離された瞬間かどうか。</summary>
    public bool Left(MouseButton button) => _state[(int)button] == MouseState.Released;

    private int _x, _y;
    private MouseState[] _state = new MouseState[3];
    /// <summary>マウスカーソルの表示状態と内部状態を初期化します。</summary>
    public void Init(bool visible)
    {
        ShowCursor(); // Raylib は表示/非表示 API が逆（Hide/Show）
        if (!visible) HideCursor();
        _prevMask = _curMask = 0;
        _prevWheel = _curWheel = 0;
        _downTickLeft = _downTickRight = _downTickMiddle = 0;
    }
    /// <summary>
    /// 毎フレーム呼び出し、raylibから生の入力を取得して安定化・ホイール差分・座標を更新します。
    /// </summary>
    public void Update()
    {
        _prevMask = _curMask;
        _prevWheel = _curWheel;

        long now = Environment.TickCount64;

        // 生の押下
        bool lRaw = IsMouseButtonDown(Raylib_cs.MouseButton.Left);
        bool rRaw = IsMouseButtonDown(Raylib_cs.MouseButton.Right);
        bool mRaw = IsMouseButtonDown(Raylib_cs.MouseButton.Middle);

        // 押下開始を記録（安定化用）。
        // RecordDown は _downTickX==0 のときしか基準位置を取り直さない（押しっぱなしの間、
        // ドラッグ判定の基準点を固定するため）。離した瞬間に _downTickX を 0 に戻さないと、
        // 次に別の場所を押しても「起動後最初に押した位置」が基準のままになり、
        // WithinTolerance が TapMoveTolerance(既定3px) を超えたと判定してクリックが一切
        // 取れなくなる。
        if (lRaw) RecordDown(MouseButton.Left, now); else _downTickLeft = 0;
        if (rRaw) RecordDown(MouseButton.Right, now); else _downTickRight = 0;
        if (mRaw) RecordDown(MouseButton.Middle, now); else _downTickMiddle = 0;

        // 安定化判定（必要なら）
        bool l = IsStableDown(MouseButton.Left, lRaw, now) && WithinTolerance(MouseButton.Left);
        bool r = IsStableDown(MouseButton.Right, rRaw, now) && WithinTolerance(MouseButton.Right);
        bool m = IsStableDown(MouseButton.Middle, mRaw, now) && WithinTolerance(MouseButton.Middle);

        _curMask = 0;
        if (l) _curMask |= 1 << 0;
        if (r) _curMask |= 1 << 1;
        if (m) _curMask |= 1 << 2;

        // ホイール：「フレーム差分」を返す
        float wheelDelta = GetMouseWheelMove();
        //Log.Debug($"Mouse Wheel Delta: {wheelDelta}");
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
        WheelTotal = _curWheel;
        Wheel = wheelDelta;

        _x = GetMouseX();
        _y = GetMouseY();
        for (int i = 0; i < 3; i++)
        {
            _state[i] = GetMouseState((MouseButton)i);
        }
    }

    /// <summary>前フレームと今フレームのビットマスクを比較し、Pressed/Held/Released/None を判定します。</summary>
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
    /// <summary>PressStabilityMs が経過するまでは押下と認めない安定化フィルタ。離した瞬間は即座に false を返す。</summary>
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

    /// <summary>押下開始時刻と座標を記録します（押しっぱなしの間は基準点を保持し続けるため上書きしない）。</summary>
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

    /// <summary>押下開始位置から TapMoveTolerance 以内に現在位置が収まっているかを判定します（ドラッグ誤爆防止）。</summary>
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
