using System.Collections.Concurrent;

namespace AstrumLoom;

/// <summary>プラットフォームが実装するキーボード入力の最小インターフェース。KeyInputはこれをラップして使う。</summary>
public interface IInput
{
    void Buffer();
    void Update();
    bool GetKey(Key key);
    bool GetKeyDown(Key key);
    bool GetKeyUp(Key key);
}
/// <summary>
/// キーボード入力を管理する静的クラス。
/// </summary>
public static class KeyInput
{
    private static IInput _input { get; set; } = null!;
    private static TextEnter _textEnter { get; set; } = null!;
    internal static void Initialize(IInput input, TextEnter textEnter)
    {
        _input = input;
        _textEnter = textEnter;
    }

    /// <summary>
    /// 指定したキーが押された瞬間かどうかを取得します。
    /// </summary>
    /// <param name="key">キー</param>
    /// <returns>押された瞬間であれば true、それ以外は false</returns>
    public static bool Push(this Key key) => !Typing && _input.GetKeyDown(key);
    /// <summary>
    /// 指定したキーが押され続けているかどうかを取得します。
    /// </summary>
    /// <param name="key">キー</param>
    /// <returns>押され続けていれば true、それ以外は false</returns>
    public static bool Hold(this Key key) => !Typing && _input.GetKey(key);
    /// <summary>
    /// 指定したキーが離された瞬間かどうかを取得します。
    /// </summary>
    /// <param name="key">キー</param>
    /// <returns>離された瞬間であれば true、それ以外は false</returns>
    public static bool Left(this Key key) => !Typing && _input.GetKeyUp(key);
    /// <summary>
    /// 指定したキーの状態を取得します。
    /// 0x01: 押され続けている、0x02: 押された瞬間、0x04: 離された瞬間
    /// </summary>
    /// <param name="key">キー</param>
    /// <returns>キーの状態を表すビットフラグ</returns>
    public static int State(this Key key)
    {
        int state = 0;
        if (Hold(key)) state |= 0x01;
        if (Push(key)) state |= 0x02;
        if (Left(key)) state |= 0x04;
        return state;
    }

    /// <summary>
    /// shiftキーが押されているかどうかを取得します。
    /// </summary>
    public static bool Shift => Key.LShift.Hold() || Key.RShift.Hold();
    /// <summary>
    /// ctrlキーが押されているかどうかを取得します。
    /// </summary>
    public static bool Ctrl => Key.LCtrl.Hold() || Key.RCtrl.Hold();
    /// <summary>
    /// altキーが押されているかどうかを取得します。
    /// </summary>
    public static bool Alt => Key.LAlt.Hold() || Key.RAlt.Hold();

    /// <summary>
    /// 指定した文字列をキーに変換できるかどうかを試みます。
    /// </summary>
    /// <param name="keyString">キーの文字列</param>
    /// <param name="key">変換結果のキー</param>
    /// <returns>変換に成功した場合は true、それ以外は false</returns>
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
    /// <summary>
    /// 指定した文字列をキーに変換します。
    /// </summary>
    /// <param name="keyString">キーの文字列</param>
    /// <returns>変換結果のキー</returns>
    public static Key Parse(string keyString) => TryParse(keyString, out var key) ? key : Key.None;
    
    /// <summary>
    /// 全てのキーを列挙します（Key.None を除く）。
    /// </summary>
    /// <returns>全てのキーの列挙</returns>
    public static IEnumerable<Key> GetAllKeys()
    {
        foreach (var key in Enum.GetValues<Key>())
        {
            if (key != Key.None)
                yield return key;
        }
    }
    /// <summary>
    /// 押されている全てのキーを列挙します。
    /// </summary>
    /// <returns>押されているキーの列挙</returns>
    public static IEnumerable<Key> GetPressedKeys()
    {
        foreach (var key in GetAllKeys())
        {
            if (key.Hold())
                yield return key;
        }
    }

    // UseMultiThreadUpdate=true では更新スレッドが書き、描画スレッド（KeyBoard.Draw経由の
    // PressedFrameCount）が読む。素の Dictionary だとリサイズ中のバケット配列を読んで
    // 例外・誤値・最悪 TryGetValue が戻らない事態になるため ConcurrentDictionary にする。
    private static readonly ConcurrentDictionary<Key, double> _pressedFrameCounts = new();
    private static readonly ConcurrentDictionary<Key, double> _lastRepeatTimes = new();
    internal static void Update(double deltaTime)
    {
        _input.Update();
        double time = deltaTime;
        // キー押下のTime処理
        foreach (var key in GetAllKeys())
        {
            if (key.Hold())
            {
                if (AstrumCore.Active && key is not Key.かな and not Key.漢字 and not Key.変換 and not Key.無変換)
                    Sleep.WakeUp();
                if (!_pressedFrameCounts.ContainsKey(key))
                {
                    _pressedFrameCounts[key] = 0;
                }
                _pressedFrameCounts[key] += time * 1000.0;
            }
            else if (_pressedFrameCounts.ContainsKey(key))
            {
                _pressedFrameCounts.TryRemove(key, out _);
                _lastRepeatTimes.TryRemove(key, out _);
            }
        }
    }

    /// <summary>
    /// 指定したキーが押され続けている時間を取得します。
    /// </summary>
    /// <param name="key">キー</param>
    /// <returns>押され続けている時間（ミリ秒）</returns>
    public static double PressedFrameCount(Key key)
        => _pressedFrameCounts.TryGetValue(key, out double time) ? time : 0;

    /// <summary>
    /// 指定したキーがリピートされるかどうかを判定します（初回リピート間隔と通常リピート間隔を同じにする場合）。
    /// </summary>
    /// <param name="key">キー</param>
    /// <param name="intervalMs">リピート間隔（ミリ秒）</param>
    /// <returns>リピートされる場合は true、それ以外は false</returns>
    public static bool Repeat(this Key key, int intervalMs) => Repeat(key, intervalMs, intervalMs);
    /// <summary>
    /// 指定したキーがリピートされるかどうかを判定します。
    /// </summary>
    /// <param name="key">キー</param>
    /// <param name="interval">リピート間隔（ミリ秒）</param>
    /// <param name="delay">リピート開始までの遅延（ミリ秒）</param>
    /// <returns>リピートされる場合は true、それ以外は false</returns>
    public static bool Repeat(this Key key, int interval, int delay)
    {
        if (!key.Hold()) return false;
        // 経過フレーム数を取得
        double frames = PressedFrameCount(key);
        // 最初の delay フレームは無視
        if (frames <= delay) return key.Push();
        // delay フレーム以降、interval ごとに true を返す

        // 初回発火
        if (!_lastRepeatTimes.TryGetValue(key, out double last))
        {
            _lastRepeatTimes[key] = frames;
            return true;
        }

        // PressedFrameCount は KeyInput.Update が1フレームに1回しか進めないので、
        // 同一フレーム内で複数箇所（メニューAとB、Update側とDraw側など）が
        // 同じ key.Repeat() を見ると frames は毎回同じ値になる。
        // 以前は「読んだら _lastRepeatTimes を進めて消費する」実装だったため、
        // 先に評価された1人目だけが true を受け取り、同一フレームの2人目以降は
        // 直後に差分が0になって必ず false を返していた。
        // frames が前回発火時と同じ＝同一フレーム内の再評価なので、
        // 状態を進めずに前回と同じ結果（true）を返す。
        if (frames == last)
            return true;

        if (frames - last >= interval)
        {
            _lastRepeatTimes[key] = frames;
            return true;
        }
        return false;
    }

    /// <summary>
    /// テキスト入力がアクティブかどうかを取得します。
    /// </summary>
    public static bool Typing => _textEnter.IsActive;

    /// <summary>
    /// テキスト入力をアクティブにします。
    /// </summary>
    /// <param name="value">入力する文字列</param>
    /// <param name="options">テキスト入力のオプション</param>
    public static void ActivateText(ref string value, TextInputOptions? options = null)
    {
        if (Typing) return;
        _textEnter.Update(ref value, options ?? new TextInputOptions());
    }

    /// <summary>
    /// 指定した位置にテキストを描画します。
    /// </summary>
    /// <param name="x">描画位置のX座標</param>
    /// <param name="y">描画位置のY座標</param>
    /// <param name="color">テキストの色</param>
    /// <param name="font">使用するフォント</param>
    public static void DrawText(double x, double y, Color? color = null, IFont? font = null) => _textEnter.Draw(x, y, color, font);
    /// <summary>
    /// 指定した位置にテキストを描画します。
    /// </summary>
    /// <param name="x">描画位置のX座標</param>
    /// <param name="y">描画位置のY座標</param>
    /// <param name="text">描画するテキスト</param>
    /// <param name="color">テキストの色</param>
    /// <param name="font">使用するフォント</param>
    public static void DrawText(double x, double y, object text, Color? color = null, IFont? font = null)
    {
        if (Typing)
            _textEnter.Draw(x, y, color, font);
        else font.Draw((int)x, (int)y, text, color, point: ReferencePoint.TopLeft);
    }

    /// <summary>
    /// 指定した文字列の入力を取得します。
    /// </summary>
    /// <param name="value">入力する文字列</param>
    /// <returns>入力された文字列、または null</returns>
    public static string? GetText(ref string value)
    {
        // _textEnter.Update が true を返す（=KeyInputState.Finished、Enter で確定）ことと
        // 「元の文字列から変わったかどうか」は無関係。以前はここで text != value を見ていたため、
        // 何も編集せず Enter を押した場合や、編集して元の文字列に戻した場合に確定を取りこぼし、
        // null（＝未確定）を返してしまっていた。確定したフレームは常に確定後の value を返す。
        return !Typing ? null : _textEnter.Update(ref value) ? value : null;
    }
    public static bool Enter(ref string value)
        => Enter(ref value, out _);

    /// <summary>
    /// 入力文字が確定されたかどうかを判定します。
    /// </summary>
    /// <param name="value">入力する文字列</param>
    /// <param name="result">確定された文字列</param>
    /// <returns>入力文字が確定された場合は true、それ以外は false</returns>
    public static bool Enter(ref string value, out string result)
    {
        if (!Typing)
        {
            result = "";
            return false;
        }
        string? r = GetText(ref value);
        result = r ?? "";
        return r != null;
    }
    /// <summary>
    /// 入力をキャンセルします。
    /// </summary>
    public static void Cancel()
    {
        if (Typing)
            _textEnter.IsCancel = true;
    }

    /// <summary>
    /// TextEnter からだけ使う、Typing ゲートを通さない生のキー押下判定。
    /// Key.Esc.Push() は `!Typing &amp;&amp; ...` なので、TextEnter.Update の中
    /// （＝常に Typing==true の状態）から呼ぶと絶対に true にならない。
    /// ESC キャンセル判定用にゲート無しの経路を用意する。
    /// </summary>
    internal static bool RawGetKeyDown(Key key) => _input.GetKeyDown(key);
}
/// <summary>
/// キーボード上のキーの種類を表します。
/// </summary>
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
    At = 51,
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
    Up = 81,
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
    F1 = 101,
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
    Insert = 121,
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
    変換 = 141,
    無変換,
    漢字,
    かな,

    // Tab
    Tab,
    // CapsLock
    CapsLock,

    // 修飾キー
    LShift = 151,
    LCtrl,
    LAlt,
    LWindows,

    RShift,
    RCtrl,
    RAlt,
    RWindows,

    // テンキーの各数字
    NumPad_0 = 200,
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

/// <summary>IME等を介したテキスト入力セッションの状態機械。開始→更新→確定/キャンセルの流れをITextInput実装の上で管理する。</summary>
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
    /// <summary>未開始なら入力セッションを開始し、開始済みならESCキャンセル判定とバックエンド状態の反映を行う。確定した瞬間だけtrueを返す。</summary>
    public bool Update(ref string value, TextInputOptions options)
    {
        // まだ入力を開始していない → 開始する
        if (!IsActive)
        {
            IsActive = true;
            // 前セッションで KeyInput.Cancel() が呼ばれていた場合、IsCancel は true のまま
            // 残っている（このクラスの他の場所には false に戻す経路が無い）。ここでリセットしないと
            // 次フレームの Update が下の「if (IsCancel)」で即 Canceled 判定してしまい、
            // 以後すべてのセッションが開始直後に強制キャンセルされ続ける。
            IsCancel = false;
            Option = options with { InitialText = value };
            _impl.Begin(Option);
            return false;
        }
        // Key.Esc.Push() は !Typing が条件に入っているため、Typing==true の
        // このメソッド内から呼ぶと常に false になり、このESC分岐が到達不能だった。
        // Typing ゲートを通さない KeyInput.RawGetKeyDown で判定する。
        if (KeyInput.RawGetKeyDown(Key.Esc) && Option.EscapeCancelable)
        {
            // ESC キーでキャンセル
            _impl.Cancel();
            IsActive = false;
            return false;
        }

        // 入力中の更新
        _impl.Update();

        // 既存の Update ロジックに加えて
        _caretTimer += _time.DeltaTime;   // プロパティ名は実装に合わせて

        // 必要なら 0〜1 の範囲に折り返し
        if (_caretTimer > 1.0)
            _caretTimer -= 1.0;

        var state = _impl.KeyState;
        if (IsCancel)
            // 外部からキャンセル要求が来た場合
            state = KeyInputState.Canceled;

        // バックエンドの状態を見る
        switch (state)
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
    public bool IsCancel { get; set; }
}

/// <summary>テキスト入力セッションの動作を指定するオプション一式。</summary>
public sealed record TextInputOptions
{
    public string InitialText { get; init; } = "";
    public ulong MaxLength { get; init; } = 256;
    public bool EscapeCancelable { get; init; } = true;
    public bool SingleByteOnly { get; init; } = false;
    public bool NumberOnly { get; init; } = false;
    public bool DoubleOnly { get; init; } = false;
    public bool MultiLine { get; init; } = false;
    public bool EnableKana { get; init; } = false;
    public IMEColors IMEColorScheme { get; init; } = IMEColors.Light;

}

/// <summary>テキスト入力中の選択範囲（開始～終了インデックス）。</summary>
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

/// <summary>IME変換候補・入力欄の配色セット。Light/Darkの既定テーマを用意している。</summary>
public readonly struct IMEColors(
    Color compositionBackColor, //変換候補
    Color compositionFrameColor, //変換候補
    Color compositionFontColor,
    Color compositionSelectFontColor,
    Color inputBackColor,
    Color inputCursorColor)
{
    public Color CompositionBackColor { get; } = compositionBackColor;
    public Color CompositionFrameColor { get; } = compositionFrameColor;
    public Color CompositionFontColor { get; } = compositionFontColor;
    public Color CompositionSelectFontColor { get; } = compositionSelectFontColor;

    public Color CompositionEdgeColor => CompositionFontColor.VisibleColor();
    public Color CompositionSelectEdgeColor => CompositionSelectFontColor.VisibleColor();

    public Color InputBackColor { get; } = inputBackColor;
    public Color InputCursorColor { get; } = inputCursorColor;

    public static IMEColors Light => new(
        Color.White,
        Color.Gold,
        Color.Snow,
        Color.Khaki,
        Color.LightYellow,
        Color.Yellow);
    public static IMEColors Dark => new(
        Color.Black,
        Color.Blue,
        Color.AliceBlue,
        Color.MidnightBlue,
        Color.LightCyan,
        Color.Yellow);
}

/// <summary>
/// キー入力の状態。
/// </summary>
public enum KeyInputState { Typing, Finished, Canceled, Error = -1 }
/// <summary>プラットフォーム（IME含む）が実装するテキスト入力バックエンド。TextEnterがこれをラップする。</summary>
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

/// <summary>画面上に仮想キーボードを描くデバッグ/演出用のヘルパー。押されているキーを虹色で光らせる。</summary>
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