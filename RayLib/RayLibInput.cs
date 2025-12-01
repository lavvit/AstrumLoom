using System.Text;

using Raylib_cs;

using static Raylib_cs.Raylib;

namespace AstrumLoom.RayLib;

internal sealed class RayLibInput : IInput
{
    // Key の全要素配列（固定）
    private readonly Key[] _keys;
    // 各キーの現在/前フレーム押下状態
    private readonly bool[] _now;
    private readonly bool[] _prev;
    // 各キーの状態遷移（1=押下開始, 2=保持, -1=離鍵, 0=非押下）
    private int[] _state;

    public RayLibInput()
    {
        _keys = Enum.GetValues<Key>();
        int len = _keys.Length;
        _now = new bool[len];
        _prev = new bool[len];
        _state = new int[len];
    }

    // 毎フレーム一度呼び出して内部バッファを更新する
    public void Buffer()
    {
        // 1フレーム前の状態を保存
        Array.Copy(_now, _prev, _now.Length);

        // 現在のキー状態を取得
        for (int i = 0; i < _keys.Length; i++)
        {
            var k = _keys[i];
            var rk = ToRayKey(k);
            bool isDown = rk != KeyboardKey.Null && IsKeyDown(rk);
            _now[i] = isDown;
        }
    }
    public void Update()
    {
        for (int i = 0; i < _state.Length; i++)
        {
            _state[i] = _now[i] ? (_state[i] < 1 ? 1 : 2) : (_state[i] > 0 ? -1 : 0);
        }
    }

    // バッファベースでキーの遷移状態を取得する（外部向けヘルパー）
    public int GetBufferedState(Key key)
    {
        int idx = Array.IndexOf(_keys, key);
        return idx >= 0 ? _state[idx] : 0;
    }

    public bool GetKey(Key key)
    {
        var rk = ToRayKey(key);
        return rk != KeyboardKey.Null && GetBufferedState(key) > 0;
    }

    public bool GetKeyDown(Key key)
    {
        var rk = ToRayKey(key);
        return rk != KeyboardKey.Null && GetBufferedState(key) == 1;
    }

    public bool GetKeyUp(Key key)
    {
        var rk = ToRayKey(key);
        return rk != KeyboardKey.Null && GetBufferedState(key) < 0;
    }

    private static KeyboardKey ToRayKey(Key key) => key switch
    {
        // 数字キー
        Key.Key_0 => KeyboardKey.Zero,
        Key.Key_1 => KeyboardKey.One,
        Key.Key_2 => KeyboardKey.Two,
        Key.Key_3 => KeyboardKey.Three,
        Key.Key_4 => KeyboardKey.Four,
        Key.Key_5 => KeyboardKey.Five,
        Key.Key_6 => KeyboardKey.Six,
        Key.Key_7 => KeyboardKey.Seven,
        Key.Key_8 => KeyboardKey.Eight,
        Key.Key_9 => KeyboardKey.Nine,

        // アルファベット
        Key.Q => KeyboardKey.Q,
        Key.W => KeyboardKey.W,
        Key.E => KeyboardKey.E,
        Key.R => KeyboardKey.R,
        Key.T => KeyboardKey.T,
        Key.Y => KeyboardKey.Y,
        Key.U => KeyboardKey.U,
        Key.I => KeyboardKey.I,
        Key.O => KeyboardKey.O,
        Key.P => KeyboardKey.P,

        Key.A => KeyboardKey.A,
        Key.S => KeyboardKey.S,
        Key.D => KeyboardKey.D,
        Key.F => KeyboardKey.F,
        Key.G => KeyboardKey.G,
        Key.H => KeyboardKey.H,
        Key.J => KeyboardKey.J,
        Key.K => KeyboardKey.K,
        Key.L => KeyboardKey.L,

        Key.Z => KeyboardKey.Z,
        Key.X => KeyboardKey.X,
        Key.C => KeyboardKey.C,
        Key.V => KeyboardKey.V,
        Key.B => KeyboardKey.B,
        Key.N => KeyboardKey.N,
        Key.M => KeyboardKey.M,

        // 記号類（Shift で変化するものは基本キーにマップ）
        Key.At => KeyboardKey.Apostrophe, // Raylib に存在しない場合は Null に置き換えてください
        Key.SemiColon => KeyboardKey.Semicolon,
        Key.Colon => KeyboardKey.Semicolon,
        Key.LBracket => KeyboardKey.LeftBracket,
        Key.RBracket => KeyboardKey.RightBracket,
        Key.Comma => KeyboardKey.Comma,
        Key.Period => KeyboardKey.Period,
        Key.Slash => KeyboardKey.Slash,
        Key.BackSlash => KeyboardKey.Backslash,

        Key.Minus => KeyboardKey.Minus,
        Key.Prevtrack => KeyboardKey.Grave, // チルダ系（`~`）は Grave にマップ
        Key.Yen => KeyboardKey.Backslash, // 日本配列の円記号はバックスラッシュに近いキーへマップ

        // カーソル
        Key.Up => KeyboardKey.Up,
        Key.Down => KeyboardKey.Down,
        Key.Left => KeyboardKey.Left,
        Key.Right => KeyboardKey.Right,

        // 決定/キャンセル/空白/バックスペース
        Key.Enter => KeyboardKey.Enter,
        Key.Esc => KeyboardKey.Escape,
        Key.Space => KeyboardKey.Space,
        Key.Back => KeyboardKey.Backspace,

        // Fキー
        Key.F1 => KeyboardKey.F1,
        Key.F2 => KeyboardKey.F2,
        Key.F3 => KeyboardKey.F3,
        Key.F4 => KeyboardKey.F4,
        Key.F5 => KeyboardKey.F5,
        Key.F6 => KeyboardKey.F6,
        Key.F7 => KeyboardKey.F7,
        Key.F8 => KeyboardKey.F8,
        Key.F9 => KeyboardKey.F9,
        Key.F10 => KeyboardKey.F10,
        Key.F11 => KeyboardKey.F11,
        Key.F12 => KeyboardKey.F12,

        // 各種特殊キー
        Key.Insert => KeyboardKey.Insert,
        Key.Delete => KeyboardKey.Delete,
        Key.Home => KeyboardKey.Home,
        Key.End => KeyboardKey.End,
        Key.PgUp => KeyboardKey.PageUp,
        Key.PgDn => KeyboardKey.PageDown,
        Key.PrintScr => KeyboardKey.PrintScreen,
        Key.Scroll => KeyboardKey.ScrollLock,
        Key.Pause => KeyboardKey.Pause,

        // IME 関係（Raylib に無ければ Null）
        Key.変換 => KeyboardKey.Null,
        Key.無変換 => KeyboardKey.Null,
        Key.漢字 => KeyboardKey.Null,
        Key.かな => KeyboardKey.Null,

        // Tab / CapsLock
        Key.Tab => KeyboardKey.Tab,
        Key.CapsLock => KeyboardKey.CapsLock,

        // 修飾キー
        Key.LShift => KeyboardKey.LeftShift,
        Key.LCtrl => KeyboardKey.LeftControl,
        Key.LAlt => KeyboardKey.LeftAlt,
        Key.LWindows => KeyboardKey.LeftSuper,

        Key.RShift => KeyboardKey.RightShift,
        Key.RCtrl => KeyboardKey.RightControl,
        Key.RAlt => KeyboardKey.RightAlt,
        Key.RWindows => KeyboardKey.RightSuper,

        // テンキー
        Key.NumPad_0 => KeyboardKey.Kp0,
        Key.NumPad_1 => KeyboardKey.Kp1,
        Key.NumPad_2 => KeyboardKey.Kp2,
        Key.NumPad_3 => KeyboardKey.Kp3,
        Key.NumPad_4 => KeyboardKey.Kp4,
        Key.NumPad_5 => KeyboardKey.Kp5,
        Key.NumPad_6 => KeyboardKey.Kp6,
        Key.NumPad_7 => KeyboardKey.Kp7,
        Key.NumPad_8 => KeyboardKey.Kp8,
        Key.NumPad_9 => KeyboardKey.Kp9,

        Key.NumPad_Multiply => KeyboardKey.KpMultiply,
        Key.NumPad_Divide => KeyboardKey.KpDivide,
        Key.NumPad_Subtract => KeyboardKey.KpSubtract,
        Key.NumPad_Add => KeyboardKey.KpAdd,
        Key.NumPad_NumLock => KeyboardKey.NumLock,
        Key.NumPad_Decimal => KeyboardKey.KpDecimal,
        Key.NumPad_Enter => KeyboardKey.KpEnter,

        // 未定義は Null
        _ => KeyboardKey.Null,
    };
}

internal sealed class RayLibTextInput : ITextInput
{
    // Raylib には組み込みのテキスト入力管理機能がないため、
    // 独自実装が必要になる。
    // ここでは簡易的な実装例を示す。
    private StringBuilder _textBuilder = new();
    private TextInputOptions _options = new();
    public bool IsActive { get; private set; }
    public string Text => _textBuilder.ToString();
    public int Cursor { get; private set; }
    public TextSelection Selection { get; private set; } = new(0, 0);
    public void Begin(TextInputOptions options)
    {
        _options = options;
        _textBuilder.Clear();
        _textBuilder.Append(options.InitialText);
        Cursor = options.InitialText.Length;
        Selection = new TextSelection(Cursor, Cursor);
        IsActive = true;
    }
    public void Cancel()
    {
        if (_options.EscapeCancelable)
        {
            IsActive = false;
        }
    }
    public void Commit() => IsActive = false;
    public KeyInputState KeyState
    {
        get
        {
            if (!IsActive) return KeyInputState.Error;

            // Enter 確定
            if (IsKeyPressed(KeyboardKey.Enter))
                return KeyInputState.Finished;

            // Esc キャンセル
            return _options.EscapeCancelable &&
                IsKeyPressed(KeyboardKey.Escape)
                ? KeyInputState.Canceled
                : KeyInputState.Typing;
        }
    }

    public void Update()
    {
        if (!IsActive) return;
        int key = GetCharPressed();
        while (key != 0)
        {
            char c = (char)key;
            // 簡易的にバイト数制限のみ考慮
            if ((ulong)_textBuilder.Length < _options.MaxLength)
            {
                _textBuilder.Insert(Cursor, c);
                Cursor++;
            }
            key = GetCharPressed();
        }
        // バックスペース処理
        if (IsKeyPressed(KeyboardKey.Backspace) && Cursor > 0)
        {
            _textBuilder.Remove(Cursor - 1, 1);
            Cursor--;
        }
    }
    public void Draw(double x = 0, double y = 0, Color? color = null, IFont font = null!, bool caret = true)
    {
        if (!IsActive) return;
        string displayText = _textBuilder.ToString();
        var drawColor = color ?? Color.Black;
        font.Draw(x, y, displayText, drawColor);
        // キャレットの描画（簡易的に固定幅フォントを想定）
        if (!caret) return;
        (int caretX, int height) = font.Measure(displayText[..Cursor]);
        if (height == 0) height = font.Measure("aあ").height;
        Drawing.Line((int)x + caretX, (int)y, 0, height, drawColor, thickness: 2);
    }
}
