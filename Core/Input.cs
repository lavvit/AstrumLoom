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

    public static bool Push(this Key key) => _input.GetKeyDown(key);
    public static bool Hold(this Key key) => _input.GetKey(key);
    public static bool Left(this Key key) => _input.GetKeyUp(key);

    public static bool Typing => _textEnter.IsActive;

    public static void ActivateText(ref string value, TextInputOptions? options = null)
    {
        if (Typing) return;
        _textEnter.Update(ref value, options ?? new TextInputOptions());
    }

    public static void DrawText(double x, double y, Color? color = null, IFont? font = null)
    {
        _textEnter.Draw(x, y, color, font);
    }
    public static void DrawText(double x, double y, object text, Color? color = null, IFont? font = null)
    {
        if (Typing)
            _textEnter.Draw(x, y, color, font);
        else font.Draw((int)x, (int)y, text, color, point: ReferencePoint.TopLeft);
    }

    public static string GetText(ref string value)
    {
        string text = value;
        if (!Typing) return "";
        if (_textEnter.Update(ref value))
        {
            return text != value ? value : "";
        }
        return "";
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
    private bool _isActive;
    private TextInputOptions _options;


    private double _caretTimer;

    // コンストラクタで ITime を受け取れるなら保持しておく
    private readonly ITime _time;

    public TextInputOptions Option => _options;

    public TextEnter(ITextInput impl, ITime time)
    {
        _impl = impl;
        _options = new();
        _time = time;
    }

    /// <summary>
    /// SeaDrop の Enter() 相当。
    /// true が返ったフレームで value に確定済み文字列が入る。
    /// </summary>
    public bool Update(ref string value)
        => Update(ref value, _options);
    public bool Update(ref string value, TextInputOptions options)
    {
        // まだ入力を開始していない → 開始する
        if (!_isActive)
        {
            _isActive = true;
            _options = options with { InitialText = value };
            _impl.Begin(_options);
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
                _isActive = false;
                return true;

            case KeyInputState.Canceled:
            case KeyInputState.Error:
            default:
                // キャンセル or 失敗
                _impl.Cancel();
                _isActive = false;
                return false;
        }
    }

    public void Draw(double x, double y, Color? color = null, IFont? font = null)
    {
        if (!_isActive) return;
        _impl.Draw(x, y, color, font ?? Drawing.G.DefaultFont, _caretTimer < 0.6);
    }

    public bool IsActive => _isActive;
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
    public static Color GetKeyColor(Key key) => key.Hold() ? Color.Yellow : Color.DimGray;
    public static Color GetKeyFontColor(Key key) => Color.VisibleColor(GetKeyColor(key));

    public static void Draw(double x, double y, int size = 20, KeyType type = KeyType.JPTKL, IFont? font = null)
    {
        // 設計（擬似コード）
        // 1. 基本パラメータを設定（boxSize, tx, ty, isfull, font）
        // 2. 行ごとに描画命令のリストを作る（Key, ラベル可, 幅, 次のXへの加算倍率）
        // 3. リストを順に処理するヘルパーを作る：描画処理を選択（通常/ラベル/Enter/NumEnter）、描画後に x2 を advance する
        // 4. isfull に依存するキー列は条件によって追加する
        // 5. 既存の DrawKey / DrawEnterKey / DrawNumEnterKey を再利用する

        double x2 = x;
        double y2 = y;
        double boxSize = 2.0 * size;
        double tx = 1.0 * size;
        double ty = 0.66 * size;
        bool isfull = (int)type % 2 == 1;
        IFont f = font ?? Drawing.G.DefaultFont;

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
            px += s.Advance * boxSize;
        }

        // 1行目
        DrawRow(ref x2, y2, new[]
        {
            new KeySpec(Key.Esc, null, 1.0, 2.075, RenderKind.Default),
            new KeySpec(Key.F1, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.F2, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.F3, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.F4, null, 1.0, 2.075, RenderKind.Default),
            new KeySpec(Key.F5, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.F6, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.F7, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.F8, null, 1.0, 2.075, RenderKind.Default),
            new KeySpec(Key.F9, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.F10, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.F11, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.F12, null, 1.0, 1.5, RenderKind.Default),
            new KeySpec(Key.PrintScr, "PRT", 1.0, 1.25, RenderKind.Labeled),
            new KeySpec(Key.Scroll, "SCR", 1.0, 1.25, RenderKind.Labeled),
            new KeySpec(Key.Pause, "PAU", 1.0, 0.0, RenderKind.Labeled),
        });

        // 2行目
        x2 = x;
        y2 += 1.5 * boxSize;
        var row2 = new List<KeySpec>
        {
            new KeySpec(Key.漢字, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.Key_1, "1", 1.0, 1.25, RenderKind.Labeled),
            new KeySpec(Key.Key_2, "2", 1.0, 1.25, RenderKind.Labeled),
            new KeySpec(Key.Key_3, "3", 1.0, 1.25, RenderKind.Labeled),
            new KeySpec(Key.Key_4, "4", 1.0, 1.25, RenderKind.Labeled),
            new KeySpec(Key.Key_5, "5", 1.0, 1.25, RenderKind.Labeled),
            new KeySpec(Key.Key_6, "6", 1.0, 1.25, RenderKind.Labeled),
            new KeySpec(Key.Key_7, "7", 1.0, 1.25, RenderKind.Labeled),
            new KeySpec(Key.Key_8, "8", 1.0, 1.25, RenderKind.Labeled),
            new KeySpec(Key.Key_9, "9", 1.0, 1.25, RenderKind.Labeled),
            new KeySpec(Key.Key_0, "0", 1.0, 1.25, RenderKind.Labeled),
            new KeySpec(Key.Minus, "-", 1.0, 1.25, RenderKind.Labeled),
            new KeySpec(Key.Prevtrack, "^", 1.0, 1.25, RenderKind.Labeled),
            new KeySpec(Key.Yen, @"\", 1.0, 1.25, RenderKind.Labeled),
            new KeySpec(Key.Back, "←", 1.0, 1.5, RenderKind.Labeled),
            new KeySpec(Key.Insert, "Ins", 1.0, 1.25, RenderKind.Labeled),
            new KeySpec(Key.Home, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.PgUp, null, 1.0, 0.0, RenderKind.Default),
        };
        DrawRow(ref x2, y2, row2.ToArray());

        if (isfull)
        {
            x2 += 1.5 * boxSize;
            DrawKey(Key.NumPad_NumLock, "NUM", f, x2, y2, tx, ty, boxSize);
            x2 += 1.25 * boxSize;
            DrawKey(Key.NumPad_Divide, "/", f, x2, y2, tx, ty, boxSize);
            x2 += 1.25 * boxSize;
            DrawKey(Key.NumPad_Multiply, "*", f, x2, y2, tx, ty, boxSize);
        }

        // 3行目
        x2 = x;
        y2 += 1.5 * boxSize;
        DrawRow(ref x2, y2, new[]
        {
            new KeySpec(Key.Tab, null, 1.5, 1.75, RenderKind.Default),
            new KeySpec(Key.Q, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.W, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.E, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.R, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.T, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.Y, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.U, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.I, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.O, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.P, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.At, "@", 1.0, 1.25, RenderKind.Labeled),
            new KeySpec(Key.LBracket, "[", 1.0, 1.25, RenderKind.Labeled),
            new KeySpec(Key.Enter, null, 1.25, 2.25, RenderKind.Enter),
            new KeySpec(Key.Delete, "Del", 1.0, 1.25, RenderKind.Labeled),
            new KeySpec(Key.End, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.PgDn, null, 1.0, 0.0, RenderKind.Default),
        });

        if (isfull)
        {
            x2 += 1.5 * boxSize;
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
        DrawRow(ref x2, y2, new[]
        {
            new KeySpec(Key.CapsLock, "Caps", 2.0, 2.25, RenderKind.Labeled),
            new KeySpec(Key.A, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.S, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.D, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.F, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.G, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.H, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.J, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.K, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.L, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.SemiColon, ";", 1.0, 1.25, RenderKind.Labeled),
            new KeySpec(Key.Colon, ":", 1.0, 1.25, RenderKind.Labeled),
            new KeySpec(Key.RBracket, "]", 1.0, 0.0, RenderKind.Labeled),
        });

        if (isfull)
        {
            x2 += 7.0 * boxSize;
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
        DrawRow(ref x2, y2, new[]
        {
            new KeySpec(Key.LShift, "Shift", 2.75, 3.0, RenderKind.Labeled),
            new KeySpec(Key.Z, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.X, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.C, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.V, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.B, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.N, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.M, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.Comma, ",", 1.0, 1.25, RenderKind.Labeled),
            new KeySpec(Key.Period, ".", 1.0, 1.25, RenderKind.Labeled),
            new KeySpec(Key.Slash, "/", 1.0, 1.25, RenderKind.Labeled),
            new KeySpec(Key.BackSlash, @"\", 1.0, 1.25, RenderKind.Labeled),
            new KeySpec(Key.RShift, "Shift", 1.75, 3.5, RenderKind.Labeled),
            new KeySpec(Key.Up, "↑", 1.0, 0.0, RenderKind.Labeled),
        });

        if (isfull)
        {
            x2 += 2.75 * boxSize;
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
        DrawRow(ref x2, y2, new[]
        {
            new KeySpec(Key.LCtrl, "Ctrl", 1.5, 1.75, RenderKind.Labeled),
            new KeySpec(Key.LWindows, "Win", 1.5, 1.75, RenderKind.Labeled),
            new KeySpec(Key.LAlt, "Alt", 1.5, 1.75, RenderKind.Labeled),
            new KeySpec(Key.無変換, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.Space, null, 3.75, 4.0, RenderKind.Default),
            new KeySpec(Key.変換, null, 1.0, 1.25, RenderKind.Default),
            new KeySpec(Key.かな, null, 1.5, 1.75, RenderKind.Default),
            new KeySpec(Key.RAlt, "Alt", 1.5, 1.75, RenderKind.Labeled),
            new KeySpec(Key.RWindows, "Win", 1.5, 1.75, RenderKind.Labeled),
            new KeySpec(Key.RCtrl, "Ctrl", 1.5, 2.0, RenderKind.Labeled),
            new KeySpec(Key.Left, "←", 1.0, 1.25, RenderKind.Labeled),
            new KeySpec(Key.Down, "↓", 1.0, 1.25, RenderKind.Labeled),
            new KeySpec(Key.Right, "→", 1.0, 0.0, RenderKind.Labeled),
        });

        if (isfull)
        {
            x2 += 1.5 * boxSize;
            DrawKey(Key.NumPad_0, "0", f, x2, y2, tx, ty, boxSize, 2.25);
            x2 += 2.5 * boxSize;
            DrawKey(Key.NumPad_Decimal, ".", f, x2, y2, tx, ty, boxSize);
        }
    }

    // キー仕様を表すシンプルなローカル型
    record KeySpec(Key Key, string? Label, double Width, double Advance, RenderKind Kind);
    enum RenderKind { Default, Labeled, Enter, NumEnter }


    static void DrawKey(Key key, IFont font, double x2, double y2, double tx, double ty, double boxSize, double width = 1.0)
    {
        Drawing.Box(x2, y2, boxSize * width, boxSize, GetKeyColor(key));
        font.Draw(Drawing.G, (int)(x2 + tx * width), (int)(y2 + ty), $"{key}", GetKeyFontColor(key), point: ReferencePoint.Center);
    }
    static void DrawKey(Key key, string keyname, IFont font, double x2, double y2, double tx, double ty, double boxSize, double width = 1.0)
    {
        Drawing.Box(x2, y2, boxSize * width, boxSize, GetKeyColor(key));
        font.Draw(Drawing.G, (int)(x2 + tx * width), (int)(y2 + ty), keyname, GetKeyFontColor(key), point: ReferencePoint.Center);
    }
    static void DrawEnterKey(Key key, IFont font, double x2, double y2, double tx, double ty, double boxSize, double width = 1.0)
    {
        Drawing.Box(x2, y2, boxSize * width, boxSize, GetKeyColor(key));
        Drawing.Box(x2 + 0.5 * boxSize, y2, boxSize * width, 2.5 * boxSize, GetKeyColor(key));
        font.Draw(Drawing.G, (int)(x2 + tx * width + 0.25 * boxSize), (int)(y2 + ty), $"{key}", GetKeyFontColor(key), point: ReferencePoint.Center);
    }
    static void DrawNumEnterKey(Key key, IFont font, double x2, double y2, double tx, double ty, double boxSize, double width = 1.0)
    {
        string keyname = "Enter";
        Drawing.Box(x2, y2, boxSize * width, 2.5 * boxSize, GetKeyColor(key));
        font.Draw(Drawing.G, (int)(x2 + tx * width + 0.25 * boxSize), (int)(y2 + ty), keyname, GetKeyFontColor(key), point: ReferencePoint.Center);
    }
}

public enum KeyType
{
    JPTKL,
    JPFull,
    ESTKL,
    ESFull,
}