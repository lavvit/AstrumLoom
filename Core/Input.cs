namespace AstrumLoom;

public interface IInput
{
    bool GetKey(Key key);
    bool GetKeyDown(Key key);
    bool GetKeyUp(Key key);
}
public static class KeyInput
{
    private static IInput _input { get; set; } = null!;
    private static TextEnter _textEnter { get; set; } = null!;
    internal static void Initialize(IInput input, TextEnter textEnter)
    {
        _input = input;
        _textEnter = textEnter;
    }

    public static bool Push(this Key key) => !Typing && _input.GetKeyDown(key);
    public static bool Hold(this Key key) => !Typing && _input.GetKey(key);
    public static bool Left(this Key key) => !Typing && _input.GetKeyUp(key);

    public static bool Shift => Key.LShift.Hold() || Key.RShift.Hold();
    public static bool Ctrl => Key.LCtrl.Hold() || Key.RCtrl.Hold();
    public static bool Alt => Key.LAlt.Hold() || Key.RAlt.Hold();

    public static bool TryParse(string keyString, out Key key)
    {
        try
        {
            key = Enum.Parse<Key>(keyString, ignoreCase: true);
            return true;
        }
        catch
        {
            key = Key.None;
            return false;
        }
    }

    public static Key Parse(string keyString) => TryParse(keyString, out var key) ? key : Key.None;

    public static IEnumerable<Key> GetAllKeys()
    {
        foreach (var key in Enum.GetValues<Key>())
        {
            if (key != Key.None)
                yield return key;
        }
    }
    public static IEnumerable<Key> GetPressedKeys()
    {
        foreach (var key in GetAllKeys())
        {
            if (key.Hold())
                yield return key;
        }
    }

    private static Dictionary<Key, double> _pressedFrameCounts = [];
    private static Dictionary<Key, double> _lastRepeatTimes = [];
    internal static void Update(double deltaTime)
    {
        double time = deltaTime;
        // キー押下のTime処理
        foreach (var key in GetAllKeys())
        {
            if (key.Hold())
            {
                if (!_pressedFrameCounts.ContainsKey(key))
                {
                    _pressedFrameCounts[key] = 0;
                }
                _pressedFrameCounts[key] += time * 1000.0;
            }
            else if (_pressedFrameCounts.ContainsKey(key))
            {
                _pressedFrameCounts.Remove(key);
                _lastRepeatTimes.Remove(key);
            }
        }
    }
    public static double PressedFrameCount(Key key)
        => _pressedFrameCounts.TryGetValue(key, out double time) ? time : 0;

    public static bool Repeat(this Key key, int intervalMs) => Repeat(key, intervalMs, intervalMs);
    public static bool Repeat(this Key key, int interval, int delay)
    {
        if (!key.Hold()) return false;
        // 経過フレーム数を取得
        double frames = PressedFrameCount(key);
        // 最初の delay フレームは無視
        if (frames <= delay) return key.Push();
        // delay フレーム以降、interval ごとに true を返す

        // 初回発火
        if (!_lastRepeatTimes.ContainsKey(key))
        {
            _lastRepeatTimes[key] = frames;
            return true;
        }

        if (frames - _lastRepeatTimes[key] >= interval)
        {
            _lastRepeatTimes[key] = frames;
            return true;
        }
        return false;
    }

    public static bool Typing => _textEnter.IsActive;

    public static void ActivateText(ref string value, TextInputOptions? options = null)
    {
        if (Typing) return;
        _textEnter.Update(ref value, options ?? new TextInputOptions());
    }

    public static void DrawText(double x, double y, Color? color = null, IFont? font = null) => _textEnter.Draw(x, y, color, font);
    public static void DrawText(double x, double y, object text, Color? color = null, IFont? font = null)
    {
        if (Typing)
            _textEnter.Draw(x, y, color, font);
        else font.Draw((int)x, (int)y, text, color, point: ReferencePoint.TopLeft);
    }

    public static string GetText(ref string value)
    {
        string text = value;
        return !Typing ? "" : _textEnter.Update(ref value) ? text != value ? value : "" : "";
    }

    public static bool Enter(ref string value)
        => Enter(ref value, out _);
    public static bool Enter(ref string value, out string result)
    {
        if (!Typing)
        {
            result = "";
            return false;
        }
        result = GetText(ref value);
        return !string.IsNullOrEmpty(result);
    }
}
public enum Key
{
    // 数字キー（テンキー、一般・数字）
    Key_0,
    Key_1,
    Key_2,
    Key_3,
    Key_4,
    Key_5,
    Key_6,
    Key_7,
    Key_8,
    Key_9,

    // アルファベットキー（QWERTYUIOP...）
    Q,
    W,
    E,
    R,
    T,
    Y,
    U,
    I,
    O,
    P,

    A,
    S,
    D,
    F,
    G,
    H,
    J,
    K,
    L,

    Z,
    X,
    C,
    V,
    B,
    N,
    M,

    // その他一般キー（[];:'\',.<>?/）
    At,
    SemiColon,
    Colon,
    LBracket,
    RBracket,
    Comma,
    Period,
    Slash,
    BackSlash,

    // マイナス-=
    Minus,
    // チルダ^~
    Prevtrack,
    // 円マーク\|
    Yen,

    // カーソルキー
    Up,
    Down,
    Left,
    Right,

    // 確定（Enter）、キャンセル（Esc）
    Enter,
    Esc,
    // スペース
    Space,
    // バックスペース
    Back,

    // Fキー
    F1,
    F2,
    F3,
    F4,
    F5,
    F6,
    F7,
    F8,
    F9,
    F10,
    F11,
    F12,

    // 各種特殊キー
    Insert,
    Delete,
    Home,
    End,
    PgUp,
    PgDn,
    // プリントスクリーン
    PrintScr,
    // スクロールロック
    Scroll,
    // ポーズ
    Pause,

    // IME
    変換,
    無変換,
    漢字,
    かな,

    // Tab
    Tab,
    // CapsLock
    CapsLock,

    // 修飾キー
    LShift,
    LCtrl,
    LAlt,
    LWindows,

    RShift,
    RCtrl,
    RAlt,
    RWindows,

    // テンキーの各数字
    NumPad_0,
    NumPad_1,
    NumPad_2,
    NumPad_3,
    NumPad_4,
    NumPad_5,
    NumPad_6,
    NumPad_7,
    NumPad_8,
    NumPad_9,

    // テンキーの乗算、除算、減算、加算
    NumPad_Multiply,
    NumPad_Divide,
    NumPad_Subtract,
    NumPad_Add,
    // テンキーの区切り
    NumPad_NumLock,
    // テンキーの小数点
    NumPad_Decimal,
    // テンキーのエンター
    NumPad_Enter,

    // 定義されていないキー
    None = -1,
}

public sealed class TextEnter
{
    private readonly ITextInput _impl;
    private double _caretTimer;

    // コンストラクタで ITime を受け取れるなら保持しておく
    private readonly ITime _time;

    public TextInputOptions Option { get; private set; }

    public TextEnter(ITextInput impl, ITime time)
    {
        _impl = impl;
        Option = new();
        _time = time;
    }

    /// <summary>
    /// SeaDrop の Enter() 相当。
    /// true が返ったフレームで value に確定済み文字列が入る。
    /// </summary>
    public bool Update(ref string value)
        => Update(ref value, Option);
    public bool Update(ref string value, TextInputOptions options)
    {
        // まだ入力を開始していない → 開始する
        if (!IsActive)
        {
            IsActive = true;
            Option = options with { InitialText = value };
            _impl.Begin(Option);
            return false;
        }

        // 入力中の更新
        _impl.Update();

        // 既存の Update ロジックに加えて
        _caretTimer += _time.DeltaTime;   // プロパティ名は実装に合わせて

        // 必要なら 0〜1 の範囲に折り返し
        if (_caretTimer > 1.0)
            _caretTimer -= 1.0;

        // バックエンドの状態を見る
        switch (_impl.KeyState)
        {
            case KeyInputState.Typing:
                // まだ入力中
                return false;

            case KeyInputState.Finished:
                // 確定
                value = _impl.Text;
                _impl.Cancel();      // ハンドル開放
                IsActive = false;
                return true;

            case KeyInputState.Canceled:
            case KeyInputState.Error:
            default:
                // キャンセル or 失敗
                _impl.Cancel();
                IsActive = false;
                return false;
        }
    }

    public void Draw(double x, double y, Color? color = null, IFont? font = null)
    {
        if (!IsActive) return;
        _impl.Draw(x, y, color, font ?? Drawing.DefaultFont, _caretTimer < 0.6);
    }

    public bool IsActive { get; private set; }
}

public sealed record TextInputOptions
{
    public string InitialText { get; init; } = "";
    public ulong MaxLength { get; init; } = 256;
    public bool EscapeCancelable { get; init; } = true;
    public bool SingleByteOnly { get; init; } = false;
    public bool NumberOnly { get; init; } = false;
    public bool DoubleOnly { get; init; } = false;
    public bool MultiLine { get; init; } = false;
}

public readonly struct TextSelection
{
    public int Start { get; }
    public int End { get; }
    public TextSelection(int start, int end)
    {
        Start = start;
        End = end;
    }
}

/// <summary>
/// キー入力の状態。
/// </summary>
public enum KeyInputState { Typing, Finished, Canceled, Error = -1 }
public interface ITextInput
{
    bool IsActive { get; }
    string Text { get; }
    KeyInputState KeyState { get; }
    int Cursor { get; }
    TextSelection Selection { get; }

    void Begin(TextInputOptions options);
    void Cancel();   // ESC など
    void Commit();   // Enter 確定（必要なら）

    /// <summary>IMEの変換状態更新・キー入力処理</summary>
    void Update();

    /// <summary>キャレットや選択範囲を含めて描画</summary>
    void Draw(double x, double y, Color? color, IFont font, bool caret);
}

public class KeyBoard
{
    public static Color GetKeyColor(Key key) => key.Hold() ?
        new Rainbow((float)(55 + KeyInput.PressedFrameCount(key) / 60.0 % 360.0)).From()
        : Color.DimGray;
    public static Color GetKeyFontColor(Key key) => Color.VisibleColor(GetKeyColor(key));

    public static void Draw(double x, double y, int size = 20, KeyType type = KeyType.JPTKL, IFont? font = null)
    {
        // 設計（擬似コード）
        // 1. 基本パラメータを設定（boxSize, tx, ty, isfull, font）
        // 2. 行ごとに描画命令のリストを作る（Key, ラベル可, 幅, 次のXへの加算倍率）
        // 3. リストを順に処理するヘルパーを作る：描画処理を選択（通常/ラベル/Enter/NumEnter）、描画後に x2 を advance する
        // 4. isfull に依存するキー列は条件によって追加する
        // 5. 既存の DrawKey / DrawEnterKey / DrawNumEnterKey を再利用する
        // 6. 英字配列 (ESTKL, ESFull) の場合、ラベル解決は ResolveLabel を通して行う

        double x2 = x;
        double y2 = y;
        double boxSize = 2.0 * size;
        double tx = 1.0 * size;
        double ty = 0.66 * size;
        bool isfull = (int)type % 2 == 1;
        bool isjp = (int)type < 2;
        var f = font ?? Drawing.DefaultFont;

        // 行ごとのキー列を定義して反復処理するユーティリティ
        void DrawRow(ref double rx, double ry, KeySpec[] specs)
        {
            foreach (var s in specs) DrawSpec(ref rx, ref ry, s);
        }

        // ローカルヘルパー：1つ描画して x2 を進める
        void DrawSpec(ref double px, ref double py, KeySpec s)
        {
            switch (s.Kind)
            {
                case RenderKind.Enter:
                    DrawEnterKey(s.Key, f, px, py, tx, ty, boxSize, s.Width);
                    break;
                case RenderKind.NumEnter:
                    DrawNumEnterKey(s.Key, f, px, py, tx, ty, boxSize, s.Width);
                    break;
                case RenderKind.Labeled:
                    DrawKey(s.Key, s.Label!, f, px, py, tx, ty, boxSize, s.Width);
                    break;
                default:
                    DrawKey(s.Key, f, px, py, tx, ty, boxSize, s.Width);
                    break;
            }
            px += (s.Width + s.Advance) * boxSize;
        }

        // 1行目
        DrawRow(ref x2, y2,
        [
            GetKeySpec(Key.Esc, 1.5),
            GetKeySpec(Key.F1),
            GetKeySpec(Key.F2),
            GetKeySpec(Key.F3),
            GetKeySpec(Key.F4, 0.875),
            GetKeySpec(Key.F5),
            GetKeySpec(Key.F6),
            GetKeySpec(Key.F7),
            GetKeySpec(Key.F8, 0.875),
            GetKeySpec(Key.F9),
            GetKeySpec(Key.F10),
            GetKeySpec(Key.F11),
            GetKeySpec(Key.F12, 1),
            GetKeySpec(Key.PrintScr, "PRT"),
            GetKeySpec(Key.Scroll, "SCR"),
            GetKeySpec(Key.Pause, "PAU", 0.0),
        ]);

        // 2行目
        x2 = x;
        y2 += 1.5 * boxSize;
        DrawRow(ref x2, y2, isjp ? [
            GetKeySpec(Key.漢字),
            GetKeySpec(Key.Key_1, "1"),
            GetKeySpec(Key.Key_2, "2"),
            GetKeySpec(Key.Key_3, "3"),
            GetKeySpec(Key.Key_4, "4"),
            GetKeySpec(Key.Key_5, "5"),
            GetKeySpec(Key.Key_6, "6"),
            GetKeySpec(Key.Key_7, "7"),
            GetKeySpec(Key.Key_8, "8"),
            GetKeySpec(Key.Key_9, "9"),
            GetKeySpec(Key.Key_0, "0"),
            GetKeySpec(Key.Minus, "-"),
            GetKeySpec(Key.Prevtrack, "^"),
            GetKeySpec(Key.Yen, @"\"),
            GetKeySpec(Key.Back, "←", 1),
            GetKeySpec(Key.Insert, "Ins"),
            GetKeySpec(Key.Home),
            GetKeySpec(Key.PgUp)
        ] : [
            GetKeySpec(Key.At, "`"),
            GetKeySpec(Key.Key_1, "1"),
            GetKeySpec(Key.Key_2, "2"),
            GetKeySpec(Key.Key_3, "3"),
            GetKeySpec(Key.Key_4, "4"),
            GetKeySpec(Key.Key_5, "5"),
            GetKeySpec(Key.Key_6, "6"),
            GetKeySpec(Key.Key_7, "7"),
            GetKeySpec(Key.Key_8, "8"),
            GetKeySpec(Key.Key_9, "9"),
            GetKeySpec(Key.Key_0, "0"),
            GetKeySpec(Key.Minus, "-"),
            GetKeySpec(Key.SemiColon, "="),
            GetKeySpec(Key.Back, "←", 2.25, 1),
            GetKeySpec(Key.Insert, "Ins"),
            GetKeySpec(Key.Home),
            GetKeySpec(Key.PgUp)
            ]);

        if (isfull)
        {
            x2 += 0.5 * boxSize;
            DrawKey(Key.NumPad_NumLock, "NUM", f, x2, y2, tx, ty, boxSize);
            x2 += 1.25 * boxSize;
            DrawKey(Key.NumPad_Divide, "/", f, x2, y2, tx, ty, boxSize);
            x2 += 1.25 * boxSize;
            DrawKey(Key.NumPad_Multiply, "*", f, x2, y2, tx, ty, boxSize);
        }

        // 3行目
        x2 = x;
        y2 += 1.5 * boxSize;
        DrawRow(ref x2, y2, isjp ? [
            GetKeySpec(Key.Tab, 1.75, 0.25),
            GetKeySpec(Key.Q),
            GetKeySpec(Key.W),
            GetKeySpec(Key.E),
            GetKeySpec(Key.R),
            GetKeySpec(Key.T),
            GetKeySpec(Key.Y),
            GetKeySpec(Key.U),
            GetKeySpec(Key.I),
            GetKeySpec(Key.O),
            GetKeySpec(Key.P),
            GetKeySpec(Key.At, "@"),
            GetKeySpec(Key.LBracket, "["),
            new(Key.Enter, null, 1.0, 1.5, RenderKind.Enter),
            GetKeySpec(Key.Delete, "Del"),
            GetKeySpec(Key.End),
            GetKeySpec(Key.PgDn),
        ] : [
            GetKeySpec(Key.Tab, 1.75, 0.25),
            GetKeySpec(Key.Q),
            GetKeySpec(Key.W),
            GetKeySpec(Key.E),
            GetKeySpec(Key.R),
            GetKeySpec(Key.T),
            GetKeySpec(Key.Y),
            GetKeySpec(Key.U),
            GetKeySpec(Key.I),
            GetKeySpec(Key.O),
            GetKeySpec(Key.P),
            GetKeySpec(Key.LBracket, "["),
            GetKeySpec(Key.RBracket, "]"),
            GetKeySpec(Key.Yen, @"\", 1.5, 1),
            GetKeySpec(Key.Delete, "Del"),
            GetKeySpec(Key.End),
            GetKeySpec(Key.PgDn),
            ]);

        if (isfull)
        {
            x2 += 0.5 * boxSize;
            DrawKey(Key.NumPad_7, "7", f, x2, y2, tx, ty, boxSize);
            x2 += 1.25 * boxSize;
            DrawKey(Key.NumPad_8, "8", f, x2, y2, tx, ty, boxSize);
            x2 += 1.25 * boxSize;
            DrawKey(Key.NumPad_9, "9", f, x2, y2, tx, ty, boxSize);
            x2 += 1.25 * boxSize;
            DrawKey(Key.NumPad_Subtract, "-", f, x2, y2, tx, ty, boxSize);
        }

        // 4行目
        x2 = x;
        y2 += 1.5 * boxSize;
        DrawRow(ref x2, y2, isjp ? [
            GetKeySpec(Key.CapsLock, "Caps", 2.0, 0.25),
            GetKeySpec(Key.A),
            GetKeySpec(Key.S),
            GetKeySpec(Key.D),
            GetKeySpec(Key.F),
            GetKeySpec(Key.G),
            GetKeySpec(Key.H),
            GetKeySpec(Key.J),
            GetKeySpec(Key.K),
            GetKeySpec(Key.L),
            GetKeySpec(Key.SemiColon, ";"),
            GetKeySpec(Key.Colon, ":"),
            GetKeySpec(Key.RBracket, "]", 2.5)
        ] : [
            GetKeySpec(Key.CapsLock, "Caps", 2.0, 0.25),
            GetKeySpec(Key.A),
            GetKeySpec(Key.S),
            GetKeySpec(Key.D),
            GetKeySpec(Key.F),
            GetKeySpec(Key.G),
            GetKeySpec(Key.H),
            GetKeySpec(Key.J),
            GetKeySpec(Key.K),
            GetKeySpec(Key.L),
            GetKeySpec(Key.Colon, ";"),
            GetKeySpec(Key.Prevtrack, "'"),
            GetKeySpec(Key.Enter, 2.5, 1)
            ]);

        if (isfull)
        {
            x2 += 4.25 * boxSize;
            DrawKey(Key.NumPad_4, "4", f, x2, y2, tx, ty, boxSize);
            x2 += 1.25 * boxSize;
            DrawKey(Key.NumPad_5, "5", f, x2, y2, tx, ty, boxSize);
            x2 += 1.25 * boxSize;
            DrawKey(Key.NumPad_6, "6", f, x2, y2, tx, ty, boxSize);
            x2 += 1.25 * boxSize;
            DrawKey(Key.NumPad_Add, "+", f, x2, y2, tx, ty, boxSize);
        }

        // 5行目
        x2 = x;
        y2 += 1.5 * boxSize;
        DrawRow(ref x2, y2, isjp ? [
            GetKeySpec(Key.LShift, "Shift", 2.75, 0.25),
            GetKeySpec(Key.Z),
            GetKeySpec(Key.X),
            GetKeySpec(Key.C),
            GetKeySpec(Key.V),
            GetKeySpec(Key.B),
            GetKeySpec(Key.N),
            GetKeySpec(Key.M),
            GetKeySpec(Key.Comma, ","),
            GetKeySpec(Key.Period, "."),
            GetKeySpec(Key.Slash, "/"),
            GetKeySpec(Key.BackSlash, @"\"),
            GetKeySpec(Key.RShift, "Shift", 1.75, 2.25),
            GetKeySpec(Key.Up, "↑")
        ] : [
            GetKeySpec(Key.LShift, "Shift", 2.75, 0.25),
            GetKeySpec(Key.Z),
            GetKeySpec(Key.X),
            GetKeySpec(Key.C),
            GetKeySpec(Key.V),
            GetKeySpec(Key.B),
            GetKeySpec(Key.N),
            GetKeySpec(Key.M),
            GetKeySpec(Key.Comma, ","),
            GetKeySpec(Key.Period, "."),
            GetKeySpec(Key.Slash, "/"),
            GetKeySpec(Key.RShift, "Shift", 3, 2.25),
            GetKeySpec(Key.Up, "↑")
            ]);

        if (isfull)
        {
            x2 += 1.75 * boxSize;
            DrawKey(Key.NumPad_1, "1", f, x2, y2, tx, ty, boxSize);
            x2 += 1.25 * boxSize;
            DrawKey(Key.NumPad_2, "2", f, x2, y2, tx, ty, boxSize);
            x2 += 1.25 * boxSize;
            DrawKey(Key.NumPad_3, "3", f, x2, y2, tx, ty, boxSize);
            x2 += 1.25 * boxSize;
            DrawNumEnterKey(Key.NumPad_Enter, f, x2, y2, tx, ty, boxSize);
        }

        // 6行目
        x2 = x;
        y2 += 1.5 * boxSize;
        DrawRow(ref x2, y2, isjp ? [
            GetKeySpec(Key.LCtrl, "Ctrl", 1.5, 0.25),
            GetKeySpec(Key.LWindows, "Win", 1.5, 0.25),
            GetKeySpec(Key.LAlt, "Alt", 1.5, 0.25),
            GetKeySpec(Key.無変換),
            GetKeySpec(Key.Space, 3.75, 0.25),
            GetKeySpec(Key.変換),
            GetKeySpec(Key.かな, 1.5, 0.25),
            GetKeySpec(Key.RAlt, "Alt", 1.5, 0.25),
            GetKeySpec(Key.RWindows, "Win", 1.5, 0.25),
            GetKeySpec(Key.RCtrl, "Ctrl", 1.5, 1),
            GetKeySpec(Key.Left, "←"),
            GetKeySpec(Key.Down, "↓"),
            GetKeySpec(Key.Right, "→"),
        ] : [
            GetKeySpec(Key.LCtrl, "Ctrl", 1.5, 0.25),
            GetKeySpec(Key.LWindows, "Win", 1.5, 0.25),
            GetKeySpec(Key.LAlt, "Alt", 1.5, 0.25),
            GetKeySpec(Key.Space, 8, 0.25),
            GetKeySpec(Key.RAlt, "Alt", 1.5, 0.25),
            GetKeySpec(Key.RWindows, "Win", 1.5, 0.25),
            GetKeySpec(Key.RCtrl, "Ctrl", 1.5, 1),
            GetKeySpec(Key.Left, "←"),
            GetKeySpec(Key.Down, "↓"),
            GetKeySpec(Key.Right, "→"),
            ]);

        if (isfull)
        {
            x2 += 0.5 * boxSize;
            DrawKey(Key.NumPad_0, "0", f, x2, y2, tx, ty, boxSize, 2.25);
            x2 += 2.5 * boxSize;
            DrawKey(Key.NumPad_Decimal, ".", f, x2, y2, tx, ty, boxSize);
        }
    }

    // キー仕様を表すシンプルなローカル型
    private record KeySpec(Key Key, string? Label, double Width, double Advance, RenderKind Kind);

    private static KeySpec GetKeySpec(Key key, string label, double width, double advance)
        => new(key, label, width, advance, RenderKind.Labeled);
    private static KeySpec GetKeySpec(Key key, string label, double advance)
        => new(key, label, 1.0, advance, RenderKind.Labeled);
    private static KeySpec GetKeySpec(Key key, string label)
        => new(key, label, 1.0, 0.25, RenderKind.Labeled);
    private static KeySpec GetKeySpec(Key key, double width, double advance)
        => new(key, null, width, advance, RenderKind.Default);
    private static KeySpec GetKeySpec(Key key, double advance)
        => new(key, null, 1.0, advance, RenderKind.Default);
    private static KeySpec GetKeySpec(Key key)
        => new(key, null, 1.0, 0.25, RenderKind.Default);

    private enum RenderKind
    { Default, Labeled, Enter, NumEnter }

    private static void DrawKey(Key key, IFont font, double x2, double y2, double tx, double ty, double boxSize, double width = 1.0)
    {
        Drawing.Box(x2, y2, boxSize * width, boxSize, GetKeyColor(key));
        font.Draw((int)(x2 + tx * width), (int)(y2 + ty), $"{key}", GetKeyFontColor(key), point: ReferencePoint.Center);
    }
    private static void DrawKey(Key key, string keyname, IFont font, double x2, double y2, double tx, double ty, double boxSize, double width = 1.0)
    {
        Drawing.Box(x2, y2, boxSize * width, boxSize, GetKeyColor(key));
        font.Draw((int)(x2 + tx * width), (int)(y2 + ty), keyname, GetKeyFontColor(key), point: ReferencePoint.Center);
    }
    private static void DrawEnterKey(Key key, IFont font, double x2, double y2, double tx, double ty, double boxSize, double width = 1.0)
    {
        Drawing.Box(x2, y2, boxSize * width, boxSize, GetKeyColor(key));
        Drawing.Box(x2 + 0.5 * boxSize, y2, boxSize * width, 2.5 * boxSize, GetKeyColor(key));
        font.Draw((int)(x2 + tx * width + 0.25 * boxSize), (int)(y2 + ty), $"{key}", GetKeyFontColor(key), point: ReferencePoint.Center);
    }
    private static void DrawNumEnterKey(Key key, IFont font, double x2, double y2, double tx, double ty, double boxSize, double width = 1.0)
    {
        string keyname = "Enter";
        Drawing.Box(x2, y2, boxSize * width, 2.5 * boxSize, GetKeyColor(key));
        font.Draw((int)(x2 + tx * width + 0.25 * boxSize), (int)(y2 + ty), keyname, GetKeyFontColor(key), point: ReferencePoint.Center);
    }
}

public enum KeyType
{
    JPTKL,
    JPFull,
    ESTKL,
    ESFull,
}