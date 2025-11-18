using System.Text;

using static DxLibDLL.DX;

namespace AstrumLoom.DXLib;

internal sealed class DxLibInput : IInput
{
    private readonly byte[] _now = new byte[256];
    private readonly byte[] _prev = new byte[256];

    public void Update()
    {
        // 1フレーム前の状態を保存
        Array.Copy(_now, _prev, _now.Length);

        // 現在のキー状態を取得
        GetHitKeyStateAll(_now);
    }

    public bool GetKey(Key key)
    {
        int code = ToDxKeyCode(key);
        return code >= 0 && _now[code] != 0;
    }

    public bool GetKeyDown(Key key)
    {
        int code = ToDxKeyCode(key);
        return code >= 0 && _now[code] != 0 && _prev[code] == 0;
    }

    public bool GetKeyUp(Key key)
    {
        int code = ToDxKeyCode(key);
        return code >= 0 && _now[code] == 0 && _prev[code] != 0;
    }

    private static int ToDxKeyCode(Key key) => key switch
    {
        // Numbers
        Key.Key_0 => KEY_INPUT_0,
        Key.Key_1 => KEY_INPUT_1,
        Key.Key_2 => KEY_INPUT_2,
        Key.Key_3 => KEY_INPUT_3,
        Key.Key_4 => KEY_INPUT_4,
        Key.Key_5 => KEY_INPUT_5,
        Key.Key_6 => KEY_INPUT_6,
        Key.Key_7 => KEY_INPUT_7,
        Key.Key_8 => KEY_INPUT_8,
        Key.Key_9 => KEY_INPUT_9,

        // Alphabet
        Key.A => KEY_INPUT_A,
        Key.B => KEY_INPUT_B,
        Key.C => KEY_INPUT_C,
        Key.D => KEY_INPUT_D,
        Key.E => KEY_INPUT_E,
        Key.F => KEY_INPUT_F,
        Key.G => KEY_INPUT_G,
        Key.H => KEY_INPUT_H,
        Key.I => KEY_INPUT_I,
        Key.J => KEY_INPUT_J,
        Key.K => KEY_INPUT_K,
        Key.L => KEY_INPUT_L,
        Key.M => KEY_INPUT_M,
        Key.N => KEY_INPUT_N,
        Key.O => KEY_INPUT_O,
        Key.P => KEY_INPUT_P,
        Key.Q => KEY_INPUT_Q,
        Key.R => KEY_INPUT_R,
        Key.S => KEY_INPUT_S,
        Key.T => KEY_INPUT_T,
        Key.U => KEY_INPUT_U,
        Key.V => KEY_INPUT_V,
        Key.W => KEY_INPUT_W,
        Key.X => KEY_INPUT_X,
        Key.Y => KEY_INPUT_Y,
        Key.Z => KEY_INPUT_Z,


        Key.At => KEY_INPUT_AT,
        Key.SemiColon => KEY_INPUT_SEMICOLON,
        Key.Colon => KEY_INPUT_COLON,
        Key.LBracket => KEY_INPUT_LBRACKET,
        Key.RBracket => KEY_INPUT_RBRACKET,
        Key.Comma => KEY_INPUT_COMMA,
        Key.Period => KEY_INPUT_PERIOD,
        Key.Slash => KEY_INPUT_SLASH,
        Key.BackSlash => KEY_INPUT_BACKSLASH,
        Key.Minus => KEY_INPUT_MINUS,
        Key.Prevtrack => KEY_INPUT_PREVTRACK,
        Key.Yen => KEY_INPUT_YEN,

        Key.Up => KEY_INPUT_UP,
        Key.Down => KEY_INPUT_DOWN,
        Key.Left => KEY_INPUT_LEFT,
        Key.Right => KEY_INPUT_RIGHT,

        Key.Enter => KEY_INPUT_RETURN,
        Key.Esc => KEY_INPUT_ESCAPE,
        Key.Space => KEY_INPUT_SPACE,
        Key.Back => KEY_INPUT_BACK,

        Key.F1 => KEY_INPUT_F1,
        Key.F2 => KEY_INPUT_F2,
        Key.F3 => KEY_INPUT_F3,
        Key.F4 => KEY_INPUT_F4,
        Key.F5 => KEY_INPUT_F5,
        Key.F6 => KEY_INPUT_F6,
        Key.F7 => KEY_INPUT_F7,
        Key.F8 => KEY_INPUT_F8,
        Key.F9 => KEY_INPUT_F9,
        Key.F10 => KEY_INPUT_F10,
        Key.F11 => KEY_INPUT_F11,
        Key.F12 => KEY_INPUT_F12,

        Key.Insert => KEY_INPUT_INSERT,
        Key.Delete => KEY_INPUT_DELETE,
        Key.Home => KEY_INPUT_HOME,
        Key.End => KEY_INPUT_END,
        Key.PgUp => KEY_INPUT_PGUP,
        Key.PgDn => KEY_INPUT_PGDN,
        Key.PrintScr => KEY_INPUT_SYSRQ,
        Key.Scroll => KEY_INPUT_SCROLL,
        Key.Pause => KEY_INPUT_PAUSE,
        Key.Tab => KEY_INPUT_TAB,
        Key.CapsLock => KEY_INPUT_CAPSLOCK,

        Key.変換 => KEY_INPUT_CONVERT,
        Key.無変換 => KEY_INPUT_NOCONVERT,
        Key.漢字 => KEY_INPUT_KANJI,
        Key.かな => KEY_INPUT_KANA,

        Key.LShift => KEY_INPUT_LSHIFT,
        Key.LCtrl => KEY_INPUT_LCONTROL,
        Key.LAlt => KEY_INPUT_LALT,
        Key.LWindows => KEY_INPUT_LWIN,
        Key.RShift => KEY_INPUT_RSHIFT,
        Key.RCtrl => KEY_INPUT_RCONTROL,
        Key.RAlt => KEY_INPUT_RALT,
        Key.RWindows => KEY_INPUT_RWIN,

        Key.NumPad_0 => KEY_INPUT_NUMPAD0,
        Key.NumPad_1 => KEY_INPUT_NUMPAD1,
        Key.NumPad_2 => KEY_INPUT_NUMPAD2,
        Key.NumPad_3 => KEY_INPUT_NUMPAD3,
        Key.NumPad_4 => KEY_INPUT_NUMPAD4,
        Key.NumPad_5 => KEY_INPUT_NUMPAD5,
        Key.NumPad_6 => KEY_INPUT_NUMPAD6,
        Key.NumPad_7 => KEY_INPUT_NUMPAD7,
        Key.NumPad_8 => KEY_INPUT_NUMPAD8,
        Key.NumPad_9 => KEY_INPUT_NUMPAD9,

        Key.NumPad_Multiply => KEY_INPUT_MULTIPLY,
        Key.NumPad_Divide => KEY_INPUT_DIVIDE,
        Key.NumPad_Subtract => KEY_INPUT_SUBTRACT,
        Key.NumPad_Add => KEY_INPUT_ADD,
        Key.NumPad_NumLock => KEY_INPUT_NUMLOCK,
        Key.NumPad_Decimal => KEY_INPUT_DECIMAL,
        Key.NumPad_Enter => KEY_INPUT_NUMPADENTER,

        // 未対応キーは -1 を返す
        _ => -1,
    };
}

internal sealed class DxLibTextInput : ITextInput
{
    private readonly int _candidateCount = 10;

    /// <summary>
    /// 文字列入力の初期化を行う。
    /// </summary>
    public void Begin(TextInputOptions opt)
    {
        MaximumLength = opt.MaxLength;
        Builder = new StringBuilder((int)MaximumLength);
        Builder.Clear();
        Builder.Append(opt.InitialText);

        Handle = MakeKeyInput(MaximumLength,
            opt.EscapeCancelable ? 1 : 0,
            opt.SingleByteOnly ? 1 : 0,
            opt.NumberOnly ? 1 : 0,
            opt.DoubleOnly ? 1 : 0,
            opt.MultiLine ? 1 : 0);
        // MakeKeyInput が失敗した場合は Handle が <= 0 になる可能性がある
        if (Handle > 0)
        {
            SetActiveKeyInput(Handle);
            SetUseIMEFlag(TRUE);
            SetKeyInputStringColor(
                (ulong)DxLibGraphics.ToDxColor(Color.Snow),
                (ulong)DxLibGraphics.ToDxColor(Color.White),
                (ulong)DxLibGraphics.ToDxColor(Color.Snow),
                (ulong)DxLibGraphics.ToDxColor(Color.Yellow),
                (ulong)DxLibGraphics.ToDxColor(Color.Cyan),
                (ulong)DxLibGraphics.ToDxColor(Color.Yellow),
                (ulong)DxLibGraphics.ToDxColor(Color.Green),
                (ulong)DxLibGraphics.ToDxColor(Color.LightGray),
                (ulong)DxLibGraphics.ToDxColor(Color.Gray),
                (ulong)DxLibGraphics.ToDxColor(Color.White),
                (ulong)DxLibGraphics.ToDxColor(Color.Black),
                (ulong)DxLibGraphics.ToDxColor(Color.WhiteSmoke),
                (ulong)DxLibGraphics.ToDxColor(Color.Blue),
                (ulong)DxLibGraphics.ToDxColor(Color.Cyan)
            );
            SetKeyInputString(Builder.ToString(), Handle);
            Cursor = Builder.Length;
        }
        Selection = new TextSelection(-1, -1);


        // ここで MakeKeyInput / SetActiveKeyInput / SetUseIMEFlag などを呼ぶ
        IsActive = true;
    }

    public void Draw(double x = 0, double y = 0, Color? color = null, IFont font = null!, bool caret = true)
    {
        if (!IsActive) return;

        // Read current text once and clamp indices to avoid slicing exceptions
        string currentText = Text ?? "";
        int safeCursor = Math.Clamp(Cursor, 0, currentText.Length);
        var sel = Selection;
        int selStart = Math.Clamp(sel.Start, 0, currentText.Length);
        int selEnd = Math.Clamp(sel.End, 0, currentText.Length);

        if (selStart >= 0 && selStart != selEnd)
        {
            double startPos = font.Measure(currentText[..selStart]).width;
            string texwidth = selEnd > selStart ? currentText[selStart..selEnd] : currentText[selEnd..selStart];
            var (sw, sh) = font.Measure(texwidth);
            double end = sw * (selEnd > selStart ? 1 : -1);
            Drawing.Box(startPos + x, y, end, sh, Color.Blue);
        }

        string text = currentText[..safeCursor];
        var (w, h) = font.Measure(text);
        if (h == 0) h = font.Measure("aあ").height; // Fallback height

        // Protect Builder null when calling native GetIMEInputModeStr
        var tmpBuilder = Builder ?? new StringBuilder();
        int imeMode = 0;
        try { imeMode = GetIMEInputModeStr(tmpBuilder); } catch { imeMode = 0; }
        if (caret)
        {
            Color cursorcol;
            switch (imeMode)
            {
                case 0:
                    cursorcol = Color.Yellow;
                    break;
                default:
                    cursorcol = color ?? Color.White;
                    break;
            }
            Drawing.Box(w + x, y, 2, h, cursorcol);
        }
        font.Draw(x, y, currentText, color);
        double scale = h / 16.0;
        ChangeFontType(DX_FONTTYPE_ANTIALIASING_EDGE_8X8);
        DrawIMEInputExtendString(w + (int)x, (int)y, scale, scale, _candidateCount, 1);
        ChangeFontType(DX_FONTTYPE_NORMAL);
    }

    /// <summary>
    /// 文字入力を終了する。
    /// </summary>
    public void Cancel()
    {
        if (IsActive && Handle > 0)
        {
            var result = DeleteKeyInput(Handle);
            if (result == 0)
            {
                IsActive = false;
                Builder = null;
                Handle = -1;
            }
        }
        SetUseIMEFlag(FALSE);
    }
    public void Commit()
    {

    }
    public void Update()
    {
        if (!IsActive) return;
        if (Text.Contains('\u0001'))
        {
            Text = Text[..Text.IndexOf('\u0001')];
            Selection = new(0, Text.Length);
        }
        if (GetDragFileNum() > 0)
        {
            StringBuilder sb = new StringBuilder("", 256);
            if (GetDragFilePath(sb) == 0)
            {
                Text += sb.ToString();
            }
        }
    }

    /// <summary>
    /// 現在のキー入力の状態。
    /// </summary>
    public KeyInputState KeyState
    {
        get
        {
            if (!IsActive || Handle <= 0) return KeyInputState.Error;
            var result = CheckKeyInput(Handle);
            switch (result)
            {
                case 0:
                    return KeyInputState.Typing;
                case 1:
                    return KeyInputState.Finished;
                case 2:
                    return KeyInputState.Canceled;
                case -1:
                default:
                    return KeyInputState.Error;
            }
        }
    }

    /// <summary>
    /// テキスト。
    /// </summary>
    public string Text
    {
        get
        {
            if (!IsActive || Builder == null || Handle <= 0) return "";
            GetKeyInputString(Builder, Handle);
            return Builder.ToString();
        }

        set
        {
            if (!IsActive || Handle <= 0) return;
            SetKeyInputString(value, Handle);
        }
    }

    /// <summary>
    /// 現在位置。
    /// </summary>
    public int Cursor
    {
        get => (!IsActive || Handle <= 0) ? 0 : GetKeyInputCursorPosition(Handle); set { if (IsActive && Handle > 0) SetKeyInputCursorPosition(value, Handle); }
    }

    /// <summary>
    /// 選択範囲。
    /// </summary>
    public TextSelection Selection
    {
        get
        {
            if (!IsActive || Handle <= 0) return new TextSelection(-1, -1);
            GetKeyInputSelectArea(out var s, out var e, Handle);
            return new TextSelection(s, e);
        }

        set { if (IsActive && Handle > 0) SetKeyInputSelectArea(value.Start, value.End, Handle); }
    }

    /// <summary>
    /// 有効かどうか。
    /// </summary>
    public bool IsActive { get; private set; }

    private StringBuilder? Builder;
    private int Handle = -1;
    private ulong MaximumLength;
}