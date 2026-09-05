using System.Collections.Concurrent;
using System.Text;

using Raylib_cs;

using static Raylib_cs.Raylib;

namespace AstrumLoom.RayLib;

/// <summary>
/// IInput の raylib 実装。キーボードの押下状態をバッファリングし、押した瞬間/離した瞬間を
/// フレーム単位で判定できるようにする。Key（Core側の抽象キー種別）と raylib の KeyboardKey の対応表を持つ。
/// </summary>
internal sealed class RayLibInput : IInput
{
    // Key の全要素配列（固定）
    private readonly Key[] _keys;
    // Key → _keys 上の位置。GetBufferedState が毎回 Array.IndexOf で線形探索しないための表。
    private readonly Dictionary<Key, int> _index;
    // 押下エッジを取りこぼさないための共通バッファ。Buffer()（メインスレッド）と
    // Update()（更新スレッド）が別の回数で回っても押下/解放が消えないようにする。
    private readonly KeyEdgeBuffer _buffer;

    public RayLibInput()
    {
        _keys = Enum.GetValues<Key>();
        _index = new Dictionary<Key, int>(_keys.Length);
        for (int i = 0; i < _keys.Length; i++) _index[_keys[i]] = i;
        _buffer = new KeyEdgeBuffer(_keys.Length);
    }

    // raylibのネイティブなキー入力ポンプ（PollInputEvents、EndDrawing経由）はメインスレッドでしか
    // 起きないため、文字キューはBuffer()（メインスレッドから呼ばれる）でここに退避しておく。
    // RayLibTextInput.Update()は更新スレッドから呼ばれうるが、GetCharPressed()を直接叩かず
    // このスレッドセーフなキューを読むだけにする。
    private readonly ConcurrentQueue<char> _charQueue = new();

    // 毎フレーム一度呼び出して内部バッファを更新する
    public void Buffer()
    {
        // 現在のキー状態を取得して取り込む
        for (int i = 0; i < _keys.Length; i++)
        {
            var rk = ToRayKey(_keys[i]);
            _buffer.Sample(i, rk != KeyboardKey.Null && IsKeyDown(rk));
        }

        // 文字入力キューをポンプ（メインスレッドでのみ安全に呼べるGetCharPressed）
        int key = GetCharPressed();
        while (key != 0)
        {
            _charQueue.Enqueue((char)key);
            key = GetCharPressed();
        }
    }

    /// <summary>Buffer()でポンプ済みの文字入力を1件取り出す。RayLibTextInputが更新スレッドから呼ぶ想定。</summary>
    public bool PopChar(out char c) => _charQueue.TryDequeue(out c);
    /// <summary>Buffer()で取り込んだ押下状態から、各キーの遷移状態(1/2/-1/0)を確定します。</summary>
    public void Update() => _buffer.Commit();

    // バッファベースでキーの遷移状態を取得する（外部向けヘルパー）
    public int GetBufferedState(Key key)
        => _index.TryGetValue(key, out int idx) ? _buffer.GetState(idx) : 0;

    /// <summary>指定キーが押されている（押した瞬間・押しっぱなし含む）かどうか。</summary>
    public bool GetKey(Key key)
    {
        var rk = ToRayKey(key);
        return rk != KeyboardKey.Null && GetBufferedState(key) > 0;
    }

    /// <summary>指定キーが押された瞬間かどうか。</summary>
    public bool GetKeyDown(Key key)
    {
        var rk = ToRayKey(key);
        return rk != KeyboardKey.Null && GetBufferedState(key) == 1;
    }

    /// <summary>指定キーが離された瞬間かどうか。</summary>
    public bool GetKeyUp(Key key)
    {
        var rk = ToRayKey(key);
        return rk != KeyboardKey.Null && GetBufferedState(key) < 0;
    }

    /// <summary>Core側の抽象キー種別をraylibのKeyboardKeyへ変換します。対応が無いキーはNullを返します。</summary>
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

/// <summary>
/// ITextInput の raylib 実装。Raylib には組み込みのテキスト入力管理機能がないため、
/// 文字入力・カーソル・確定/キャンセル判定を自前で持つ簡易IME的な実装。
/// </summary>
internal sealed class RayLibTextInput : ITextInput
{
    // Raylib には組み込みのテキスト入力管理機能がないため、
    // 独自実装が必要になる。
    // ここでは簡易的な実装例を示す。
    //
    // Enter/Esc/Backspace/文字入力は、raylibのIsKeyPressed/GetCharPressedを直接叩くのではなく
    // RayLibInputが Buffer()（メインスレッド）で確定させたバッファ済み状態経由で読む。
    // UseMultiThreadUpdate=true構成では、このクラスのUpdate()/KeyStateは更新スレッドから呼ばれる一方、
    // raylibのネイティブなキー状態配列・文字キューの実ポンプはメインスレッドのEndDrawing内でしか
    // 起きないため、直読みだとスレッド間の排他が無いまま同じネイティブ状態を触ることになる
    // （VirtualInputによる合成入力もInputBridge層＝バッファ済み状態経由でしか効かないため、
    // 直読みのままだとselftestから文字入力を通す手段も無い）。
    private readonly RayLibInput _input;
    private StringBuilder _textBuilder = new();
    private TextInputOptions _options = new();
    public RayLibTextInput(RayLibInput input) => _input = input;
    public bool IsActive { get; private set; }
    public string Text => _textBuilder.ToString();
    public int Cursor { get; private set; }
    public TextSelection Selection { get; private set; } = new(0, 0);
    /// <summary>テキスト入力を開始します。初期文字列をセットし、カーソルを末尾に置きます。</summary>
    public void Begin(TextInputOptions options)
    {
        _options = options;
        _textBuilder.Clear();
        _textBuilder.Append(options.InitialText);
        Cursor = options.InitialText.Length;
        Selection = new TextSelection(Cursor, Cursor);
        IsActive = true;
    }
    /// <summary>EscapeCancelableが有効な場合のみ、入力を中断してアクティブ状態を解除します。</summary>
    public void Cancel()
    {
        if (_options.EscapeCancelable)
        {
            IsActive = false;
        }
    }
    /// <summary>入力内容を確定し、アクティブ状態を解除します。</summary>
    public void Commit() => IsActive = false;
    /// <summary>Enter/Escapeの押下から、現在の入力状態（継続中/確定/キャンセル/非アクティブ）を判定します。</summary>
    public KeyInputState KeyState
    {
        get
        {
            if (!IsActive) return KeyInputState.Error;

            // Enter 確定（raylibのIsKeyPressed直読みではなく、Buffer()で確定済みのバッファ状態を見る）
            if (_input.GetBufferedState(Key.Enter) == 1)
                return KeyInputState.Finished;

            // Esc キャンセル
            return _options.EscapeCancelable &&
                _input.GetBufferedState(Key.Esc) == 1
                ? KeyInputState.Canceled
                : KeyInputState.Typing;
        }
    }

    /// <summary>入力中の文字・バックスペースをraylibから取得し、テキストとカーソル位置に反映します。</summary>
    public void Update()
    {
        if (!IsActive) return;
        // GetCharPressed()を直接叩かず、RayLibInput.Buffer()（メインスレッド）が
        // 事前にポンプ済みのスレッドセーフなキューから読む。
        while (_input.PopChar(out char c))
        {
            // 簡易的に文字数（UTF-16 char数）制限のみ考慮
            if ((ulong)_textBuilder.Length < _options.MaxLength)
            {
                _textBuilder.Insert(Cursor, c);
                Cursor++;
            }
        }
        // バックスペース処理（Buffer()で確定済みのバッファ状態を見る）
        if (_input.GetBufferedState(Key.Back) == 1 && Cursor > 0)
        {
            _textBuilder.Remove(Cursor - 1, 1);
            Cursor--;
        }
    }
    /// <summary>入力中のテキストと、必要ならキャレット（カーソル位置の縦線）を描画します。</summary>
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
