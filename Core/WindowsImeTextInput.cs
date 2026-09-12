using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

namespace AstrumLoom;

/// <summary>
/// Windows の IMM32 を直接叩く ITextInput 実装。バックエンド（DxLib / raylib）に依存せず、
/// ウィンドウの HWND さえあれば日本語入力（未確定文字列・変換候補）が使える。
///
/// 役割分担:
///   ・変換候補リストの描画は OS（IME）に任せる。位置だけ ImmSetCandidateWindow でキャレットに追従させる。
///     自前で候補を描くと予測変換やサードパーティIME（Google日本語入力等）で破綻するため。
///   ・未確定文字列（コンポジション）と下線・キャレット・選択範囲は AstrumLoom 側の IFont で描く。
///     これにより DxLib の DrawIMEInputExtendString に頼らずに済み、両バックエンドで見た目が揃う。
///
/// スレッド: ウィンドウプロシージャはメッセージをポンプするスレッド（＝メインスレッド）で動き、
/// Update()/Draw() は更新スレッドから呼ばれうる。両者の受け渡しは _events（キュー）と _sync（ロック）で行い、
/// IMM32 の API はウィンドウプロシージャ側からしか呼ばない（必要なら PostMessage で依頼する）。
/// </summary>
internal sealed class WindowsImeTextInput : ITextInput, IDisposable
{
    /// <summary>編集操作。WM_KEYDOWN をウィンドウプロシージャ側で意味付けしてから積む。</summary>
    private enum EditCommand
    {
        Left, Right, Up, Down, Home, End,
        Backspace, Delete, NewLine,
        Commit, Cancel,
        SelectAll, Copy, Cut, Paste,
    }

    private enum EventKind { Char, Composition, Result, Command, Candidate }

    /// <summary>
    /// IME の変換候補リスト。IME が「候補はアプリ側で描け」と言ってきた時だけ中身が入る。
    /// Selection は Items 全体での絶対位置、PageStart/PageSize は今表示すべき範囲。
    /// </summary>
    private sealed record CandidateList(string[] Items, int Selection, int PageStart, int PageSize)
    {
        public static readonly CandidateList Empty = new([], 0, 0, 0);
    }

    private readonly record struct Event(
        EventKind Kind,
        string Text = "",
        int Cursor = 0,
        byte[]? Attributes = null,
        EditCommand Command = EditCommand.Left,
        bool Shift = false,
        CandidateList? Candidates = null);

    /// <summary>IME メッセージの流れを Log へ出す切り分け用スイッチ。環境変数 ASTRUM_IME_DIAG=1 で有効。</summary>
    private static readonly bool ImeDiagnostics =
        Environment.GetEnvironmentVariable("ASTRUM_IME_DIAG") == "1";

    private const nuint SubclassId = 0x41534C49; // "ASLI"

    private readonly nint _window;
    private readonly SubclassProc _proc;
    private readonly ConcurrentQueue<Event> _events = new();
    private readonly TextEditBuffer _buffer = new();
    private readonly Lock _sync = new();

    private TextInputOptions _options = new();
    private KeyInputState _state = KeyInputState.Typing;
    private volatile bool _active;
    private bool _disposed;

    // 未確定文字列・変換候補（_sync で保護）
    private CandidateList _candidates = CandidateList.Empty;
    private string _composition = "";
    private int _compositionCursor;
    private byte[] _compositionAttributes = [];

    // キャレットのクライアント座標（候補ウィンドウの位置決めに使う）。Draw() が書き、ウィンドウプロシージャが読む。
    private int _caretX;
    private int _caretTop;
    private int _caretBottom;

    // ウィンドウプロシージャのスレッドからのみ触るフィールド。
    // 自前で作った入力コンテキスト。DxLib のように既定で IME を切り離しているバックエンドでも
    // これを繋ぎ直せば日本語入力ができる（ImmGetContext が 0 のままにならない）。
    private nint _ownContext;
    // 入力開始前にウィンドウへ繋がっていた元のコンテキスト。破棄時に必ず戻す。
    private nint _originalContext;
    private bool _originalContextSaved;
    // 候補ウィンドウ位置の再入防止と、前回設定した位置。
    private bool _applyingCaretPosition;
    private int _appliedCaretX = int.MinValue;
    private int _appliedCaretTop = int.MinValue;
    private int _appliedCaretBottom = int.MinValue;

    /// <summary>
    /// サブクラス化に成功すれば実装を返す。Windows 以外・HWND 不明・失敗時は null。
    /// 呼び出し側はその場合バックエンド既定の ITextInput にフォールバックする。
    /// </summary>
    public static WindowsImeTextInput? TryCreate(nint window)
    {
        if (!OperatingSystem.IsWindows() || window == 0) return null;
        try
        {
            return new WindowsImeTextInput(window);
        }
        catch (Exception ex)
        {
            Log.Warning($"IMM32 テキスト入力を初期化できませんでした: {ex.Message}");
            return null;
        }
    }

    private WindowsImeTextInput(nint window)
    {
        _window = window;
        _proc = WindowProc;
        if (!SetWindowSubclass(window, _proc, SubclassId, 0))
            throw new InvalidOperationException("SetWindowSubclass failed.");
    }

    public bool IsActive => _active;

    public string Text
    {
        get { lock (_sync) return _buffer.Text; }
    }

    public int Cursor
    {
        get { lock (_sync) return _buffer.Caret; }
    }

    public TextSelection Selection
    {
        get { lock (_sync) return _buffer.Selection; }
    }

    /// <summary>Enter/Esc は WM_KEYDOWN からこのクラスが直接解釈するので、TextEnter 側の判定は不要。</summary>
    public bool HandlesKeysInternally => true;

    public KeyInputState KeyState => !_active ? KeyInputState.Error : _state;

    /// <summary>入力セッションを開始し、ウィンドウの IME を有効化する。</summary>
    public void Begin(TextInputOptions options)
    {
        lock (_sync)
        {
            _options = options;
            _buffer.MaxLength = options.MaxLength > int.MaxValue ? int.MaxValue : (int)options.MaxLength;
            _buffer.MultiLine = options.MultiLine;
            _buffer.Filter = MakeFilter(options);
            _buffer.Reset(options.InitialText);
            _composition = "";
            _compositionCursor = 0;
            _compositionAttributes = [];
            _candidates = CandidateList.Empty;
        }
        while (_events.TryDequeue(out _)) { }
        _appliedCaretX = _appliedCaretTop = _appliedCaretBottom = int.MinValue;
        _state = KeyInputState.Typing;
        _active = true;

        // 半角英数/数値限定のときは IME を無効化しておく（変換候補が出ても入力できないため）。
        bool allowIme = !options.SingleByteOnly && !options.NumberOnly && !options.DoubleOnly;
        RequestImeMode(allowIme ? (options.EnableKana ? ImeMode.EnableOpen : ImeMode.EnableClosed) : ImeMode.Disable);
    }

    private static Func<char, bool>? MakeFilter(TextInputOptions options)
    {
        if (options.NumberOnly) return c => char.IsAsciiDigit(c) || c == '-';
        if (options.DoubleOnly) return c => char.IsAsciiDigit(c) || c is '-' or '+' or '.' or 'e' or 'E';
        if (options.SingleByteOnly) return char.IsAscii;
        return null;
    }

    /// <summary>入力を打ち切り、IME を無効化して未確定文字列を捨てる。</summary>
    public void Cancel()
    {
        if (!_active) return;
        _active = false;
        lock (_sync)
        {
            _composition = "";
            _compositionCursor = 0;
            _compositionAttributes = [];
            _candidates = CandidateList.Empty;
        }
        RequestImeMode(ImeMode.Disable);
    }

    /// <summary>確定。実体は Cancel と同じで、未確定文字列を捨てて IME を無効化する。</summary>
    public void Commit() => Cancel();

    /// <summary>ウィンドウプロシージャが積んだ入力イベントを取り込み、編集バッファへ反映する。</summary>
    public void Update()
    {
        if (!_active) return;

        while (_events.TryDequeue(out var ev))
        {
            switch (ev.Kind)
            {
                case EventKind.Char:
                    lock (_sync) _buffer.Insert(ev.Text);
                    break;

                case EventKind.Composition:
                    lock (_sync)
                    {
                        _composition = ev.Text;
                        _compositionCursor = System.Math.Clamp(ev.Cursor, 0, ev.Text.Length);
                        _compositionAttributes = ev.Attributes ?? [];
                    }
                    break;

                case EventKind.Result:
                    lock (_sync)
                    {
                        _buffer.Insert(ev.Text);
                        _composition = "";
                        _compositionCursor = 0;
                        _compositionAttributes = [];
                        _candidates = CandidateList.Empty;
                    }
                    break;

                case EventKind.Candidate:
                    lock (_sync) _candidates = ev.Candidates ?? CandidateList.Empty;
                    break;

                case EventKind.Command:
                    // 確定/キャンセルは以降のイベントを読まずに抜ける。
                    if (ApplyCommand(ev.Command, ev.Shift)) return;
                    break;
            }
        }
    }

    /// <summary>編集コマンドを適用する。セッションを終える種類なら true。</summary>
    private bool ApplyCommand(EditCommand command, bool shift)
    {
        switch (command)
        {
            case EditCommand.Commit:
                _state = KeyInputState.Finished;
                return true;

            case EditCommand.Cancel:
                if (!_options.EscapeCancelable) return false;
                _state = KeyInputState.Canceled;
                return true;

            case EditCommand.Copy:
            case EditCommand.Cut:
                {
                    string selected;
                    lock (_sync)
                    {
                        selected = _buffer.SelectedText();
                        if (command == EditCommand.Cut && selected.Length > 0) _buffer.DeleteSelection();
                    }
                    if (selected.Length > 0) SetClipboardText(selected);
                    return false;
                }

            case EditCommand.Paste:
                {
                    string text = GetClipboardText();
                    if (text.Length > 0) lock (_sync) _buffer.Insert(text);
                    return false;
                }
        }

        lock (_sync)
        {
            switch (command)
            {
                case EditCommand.Left: _buffer.Move(-1, shift); break;
                case EditCommand.Right: _buffer.Move(1, shift); break;
                case EditCommand.Up: _buffer.MoveLine(-1, shift); break;
                case EditCommand.Down: _buffer.MoveLine(1, shift); break;
                case EditCommand.Home: _buffer.MoveHome(shift); break;
                case EditCommand.End: _buffer.MoveEnd(shift); break;
                case EditCommand.Backspace: _buffer.Backspace(); break;
                case EditCommand.Delete: _buffer.Delete(); break;
                case EditCommand.NewLine: _buffer.Insert("\n"); break;
                case EditCommand.SelectAll: _buffer.SelectAll(); break;
            }
        }
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _active = false;
        ApplyImeMode(ImeMode.Restore);
        RemoveWindowSubclass(_window, _proc, SubclassId);
    }

    // ------------------------------------------------------------------
    // 描画
    // ------------------------------------------------------------------

    /// <summary>描画位置を送るためのペン。行送りで X を戻すので参照型にしておく。</summary>
    private sealed class Pen
    {
        public double X;
        public double Y;
    }

    /// <summary>
    /// 確定済みテキスト・選択範囲・未確定文字列（下線付き）・キャレットを描画し、
    /// 併せて変換候補ウィンドウの表示位置をキャレットへ追従させる。
    /// </summary>
    public void Draw(double x = 0, double y = 0, Color? color = null, IFont font = null!, bool caret = true)
    {
        if (!_active) return;
        font ??= Drawing.DefaultFont;

        string text, composition;
        int caretIndex, compositionCursor;
        byte[] attributes;
        TextSelection selection;
        CandidateList candidates;
        lock (_sync)
        {
            text = _buffer.Text;
            caretIndex = System.Math.Clamp(_buffer.Caret, 0, text.Length);
            selection = _buffer.Selection;
            composition = _composition;
            compositionCursor = _compositionCursor;
            attributes = _compositionAttributes;
            candidates = _candidates;
        }

        var textColor = color ?? Color.Black;
        var scheme = _options.IMEColorScheme;
        double lineHeight = font.Measure("aあ").height;
        if (lineHeight <= 0) lineHeight = font.Spec.Size;

        string before = text[..caretIndex];
        string after = text[caretIndex..];
        bool composing = composition.Length > 0;

        // 背景（選択範囲・未確定文字列）を先に敷いてから文字を載せる。
        var pen = new Pen { X = x, Y = y };
        if (composing)
        {
            Walk(font, x, lineHeight, pen, before, null);
            WalkComposition(font, x, lineHeight, pen, composition, attributes,
                (attr, sx, sy, width) =>
                {
                    if (width <= 0) return;
                    bool target = IsTargetClause(attr);
                    Drawing.Box(sx, sy, width, lineHeight,
                        target ? scheme.CompositionFrameColor : scheme.InputBackColor,
                        opacity: target ? 0.35 : 0.2);
                    Drawing.Line(sx, sy + lineHeight - 1, width, 0,
                        target ? scheme.InputCursorColor : textColor,
                        thickness: target ? 2 : 1);
                });
        }
        else if (selection.End > selection.Start)
        {
            Walk(font, x, lineHeight, pen, text[..selection.Start], null);
            Walk(font, x, lineHeight, pen, text[selection.Start..selection.End],
                (_, sx, sy, width) =>
                {
                    if (width > 0) Drawing.Box(sx, sy, width, lineHeight, Color.RoyalBlue, opacity: 0.45);
                });
        }

        // 本文（確定部の前半 → 未確定部 → 確定部の後半）。
        pen.X = x;
        pen.Y = y;
        Walk(font, x, lineHeight, pen, before, (segment, sx, sy, _) => font.Draw(sx, sy, segment, textColor));
        if (composing)
            Walk(font, x, lineHeight, pen, composition, (segment, sx, sy, _) => font.Draw(sx, sy, segment, textColor));
        Walk(font, x, lineHeight, pen, after, (segment, sx, sy, _) => font.Draw(sx, sy, segment, textColor));

        // キャレット位置を求める（未確定中は IME が持つカーソル位置に合わせる）。
        var caretPen = new Pen { X = x, Y = y };
        Walk(font, x, lineHeight, caretPen, before, null);
        if (composing)
            Walk(font, x, lineHeight, caretPen, composition[..System.Math.Min(compositionCursor, composition.Length)], null);

        if (caret)
            Drawing.Box(caretPen.X, caretPen.Y, 2, lineHeight, composing ? scheme.InputCursorColor : textColor);

        // 変換候補ウィンドウをキャレットへ寄せる。ゲーム座標→クライアント座標の変換を挟む。
        UpdateCaretRect(caretPen.X, caretPen.Y, lineHeight);

        // IME が「候補はアプリ側で描け」と言ってきた時（DxLib バックエンド等）だけ自前で描く。
        // OS が候補 UI を出す環境では通知自体が来ないので candidates は空のままになり、
        // OS の候補ウィンドウ（予測変換なども効く本来の UI）がそのまま使われる。
        if (candidates.Items.Length > 0)
            DrawCandidates(font, candidates, caretPen.X, caretPen.Y, lineHeight, scheme);
    }

    /// <summary>変換候補リストをキャレットの近くに描く。画面外へはみ出す場合は上／左へ折り返す。</summary>
    private static void DrawCandidates(IFont font, CandidateList list,
        double caretX, double caretY, double lineHeight, IMEColors scheme)
    {
        int pageSize = list.PageSize > 0 ? list.PageSize : System.Math.Min(list.Items.Length, 9);
        int start = System.Math.Clamp(list.PageStart, 0, list.Items.Length - 1);
        int end = System.Math.Min(start + pageSize, list.Items.Length);
        if (end <= start) return;

        var rows = new (string Label, bool Selected)[end - start];
        double textWidth = 0;
        for (int i = start; i < end; i++)
        {
            string label = $"{i - start + 1}. {list.Items[i]}";
            rows[i - start] = (label, i == list.Selection);
            textWidth = System.Math.Max(textWidth, font.Measure(label).width);
        }

        const double padding = 6;
        double boxWidth = textWidth + padding * 2;
        double boxHeight = rows.Length * lineHeight + padding * 2;

        // 既定はキャレットの真下。下に入らなければ上へ、右に入らなければ左へ寄せる。
        double x = caretX;
        double y = caretY + lineHeight + 2;
        if (y + boxHeight > AstrumCore.Height) y = caretY - boxHeight - 2;
        if (x + boxWidth > AstrumCore.Width) x = AstrumCore.Width - boxWidth;
        x = System.Math.Max(0, x);
        y = System.Math.Max(0, y);

        var back = scheme.CompositionBackColor;
        var frame = scheme.CompositionFrameColor;
        Drawing.Box(x, y, boxWidth, boxHeight, back);
        Drawing.Box(x, y, boxWidth, boxHeight, frame, thickness: 2);

        for (int i = 0; i < rows.Length; i++)
        {
            double rowY = y + padding + i * lineHeight;
            var (label, selected) = rows[i];
            if (selected) Drawing.Box(x + 2, rowY, boxWidth - 4, lineHeight, frame);
            font.Draw(x + padding, rowY, label, selected
                ? Readable(scheme.CompositionSelectFontColor, frame)
                : Readable(scheme.CompositionFontColor, back));
        }
    }

    /// <summary>
    /// 背景に対して文字色のコントラストが足りなければ黒／白へ差し替える。
    /// IMEColors の既定値は DxLib のネイティブ候補描画向けに決められていて、
    /// そのまま使うと背景に埋もれる組み合わせ（白背景に Snow など）があるため。
    /// </summary>
    private static Color Readable(Color foreground, Color background)
    {
        static int Luma(Color c) => (c.R * 299 + c.G * 587 + c.B * 114) / 1000;
        return System.Math.Abs(Luma(foreground) - Luma(background)) < 64
            ? background.VisibleColor()
            : foreground;
    }

    private static bool IsTargetClause(byte attr) => attr is ATTR_TARGET_CONVERTED or ATTR_TARGET_NOTCONVERTED;

    /// <summary>文字列を改行で区切りながら走査し、区間ごとに位置と幅を通知してペンを進める。</summary>
    private static void Walk(IFont font, double originX, double lineHeight, Pen pen, string text,
        Action<string, double, double, double>? onSegment)
    {
        int start = 0;
        while (true)
        {
            int newLine = text.IndexOf('\n', start);
            string part = newLine < 0 ? text[start..] : text[start..newLine];
            double width = part.Length > 0 ? font.Measure(part).width : 0;
            onSegment?.Invoke(part, pen.X, pen.Y, width);
            pen.X += width;
            if (newLine < 0) return;
            pen.X = originX;
            pen.Y += lineHeight;
            start = newLine + 1;
        }
    }

    /// <summary>未確定文字列を、属性（変換対象かどうか）が同じ範囲ごとに区切って走査する。</summary>
    private static void WalkComposition(IFont font, double originX, double lineHeight, Pen pen,
        string composition, byte[] attributes, Action<byte, double, double, double> onSegment)
    {
        int start = 0;
        while (start < composition.Length)
        {
            byte attr = start < attributes.Length ? attributes[start] : ATTR_INPUT;
            int end = start + 1;
            while (end < composition.Length && (end < attributes.Length ? attributes[end] : ATTR_INPUT) == attr) end++;
            Walk(font, originX, lineHeight, pen, composition[start..end],
                (_, sx, sy, width) => onSegment(attr, sx, sy, width));
            start = end;
        }
    }

    /// <summary>ゲーム座標のキャレット矩形を、候補ウィンドウ用にクライアント座標へ変換して記録する。</summary>
    private void UpdateCaretRect(double caretX, double caretY, double lineHeight)
    {
        double scaleX = 1, scaleY = 1;
        if (GetClientRect(_window, out var rect))
        {
            int logicalWidth = AstrumCore.WindowConfig?.Width ?? 0;
            int logicalHeight = AstrumCore.WindowConfig?.Height ?? 0;
            if (logicalWidth > 0) scaleX = (rect.Right - rect.Left) / (double)logicalWidth;
            if (logicalHeight > 0) scaleY = (rect.Bottom - rect.Top) / (double)logicalHeight;
        }
        _caretX = (int)(caretX * scaleX);
        _caretTop = (int)(caretY * scaleY);
        _caretBottom = (int)((caretY + lineHeight) * scaleY);
    }

    // ------------------------------------------------------------------
    // ウィンドウプロシージャ
    // ------------------------------------------------------------------

    private nint WindowProc(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        // ネイティブのコールバック境界へ例外を漏らさない。
        try
        {
            // IME まわりは環境（バックエンド・IME の種類）で挙動が変わるので、
            // 切り分け用にメッセージの流れを覗けるようにしておく。
            if (ImeDiagnostics && message is WM_IME_NOTIFY or WM_IME_SETCONTEXT or WM_IME_STARTCOMPOSITION)
                Log.Debug($"IMEDIAG msg=0x{message:X} wParam=0x{wParam:X} lParam=0x{lParam:X} active={_active}");

            switch (message)
            {
                case WM_ASTRUM_IME:
                    ApplyImeMode((ImeMode)(int)wParam);
                    return 0;

                case WM_NCDESTROY:
                    Dispose();
                    break;

                case WM_IME_SETCONTEXT:
                    // OS 既定の未確定文字列ウィンドウは出さない（自前で描くため）。候補リストは出させる。
                    //
                    // ここで DefSubclassProc に流すとバックエンド（特に DxLib は変換候補を
                    // 自前で描く前提で IME の UI メッセージを握り潰す）のウィンドウプロシージャを
                    // 経由してしまい、OS の候補ウィンドウが出なくなる。IME のメッセージだけは
                    // バックエンドを飛ばして DefWindowProc へ直接渡す。
                    if (!_active) break;
                    return DefWindowProc(window, message, wParam, lParam & ~ISC_SHOWUICOMPOSITIONWINDOW);

                case WM_IME_STARTCOMPOSITION:
                    if (!_active) break;
                    ApplyCandidatePosition();
                    return 0;

                case WM_IME_COMPOSITION:
                    if (!_active) break;
                    ReadComposition(window, lParam);
                    ApplyCandidatePosition();
                    return 0;

                case WM_IME_ENDCOMPOSITION:
                    if (!_active) break;
                    _events.Enqueue(new Event(EventKind.Composition));
                    _events.Enqueue(new Event(EventKind.Candidate, Candidates: CandidateList.Empty));
                    return 0;

                case WM_IME_NOTIFY:
                    // IMN_SETCANDIDATEPOS / IMN_SETCOMPOSITIONWINDOW は
                    // こちらが位置を設定したこと自体の通知なので、ここで拾って設定し直すと
                    // 設定→通知→設定… と無限に往復してアプリが固まる。候補が開いた時だけ合わせる。
                    if (!_active) break;
                    switch (wParam)
                    {
                        // IMN_CHANGECANDIDATE / IMN_OPENCANDIDATE は「候補リストが変わったので
                        // アプリ側で描いてくれ」という通知。OS が候補 UI を出してくれる環境
                        //（raylib バックエンド等）ではそもそもこれが飛んでこないので、
                        // ここに来た時だけ自前描画に切り替わる形になる。
                        case IMN_OPENCANDIDATE:
                            ApplyCandidatePosition(force: true);
                            ReadCandidates(window);
                            break;
                        case IMN_CHANGECANDIDATE:
                            ReadCandidates(window);
                            break;
                        case IMN_CLOSECANDIDATE:
                            _events.Enqueue(new Event(EventKind.Candidate, Candidates: CandidateList.Empty));
                            break;
                    }
                    // WM_IME_SETCONTEXT と同じ理由でバックエンドを経由させない。
                    return DefWindowProc(window, message, wParam, lParam);

                case WM_IME_CHAR:
                    // 確定文字列は WM_IME_COMPOSITION の GCS_RESULTSTR で受け取っている。
                    // ここを DefWindowProc へ流すと WM_CHAR に化けて二重入力になるので握り潰す。
                    if (_active) return 0;
                    break;

                case WM_CHAR:
                case WM_SYSCHAR:
                    if (!_active) break;
                    _events.Enqueue(new Event(EventKind.Char, ((char)wParam).ToString()));
                    return 0;

                case WM_KEYDOWN:
                case WM_SYSKEYDOWN:
                case WM_KEYUP:
                case WM_SYSKEYUP:
                    if (!_active) break;
                    // VK_PROCESSKEY は「IME が食べたキー」。変換中の Enter/Esc/矢印をここで拾うと、
                    // 変換を確定したつもりが入力欄ごと確定してしまうので必ず無視する。
                    if (wParam != VK_PROCESSKEY
                        && message is WM_KEYDOWN or WM_SYSKEYDOWN
                        && TryMapCommand((int)wParam, out var command))
                    {
                        _events.Enqueue(new Event(EventKind.Command,
                            Command: command,
                            Shift: (GetKeyState(VK_SHIFT) & 0x8000) != 0));
                        return 0;
                    }
                    // 半角/全角・変換・無変換・かな等の IME 操作キーは、DefSubclassProc に流すと
                    // バックエンドのウィンドウプロシージャを経由する。DxLib は SetUseIMEFlag(0) を
                    // 前提に IME 系のメッセージを握り潰すため、半角/全角での切り替えが効かず、
                    // ローマ字がそのまま latin として入ってしまう。IME 操作キーだけは直接 OS へ渡す。
                    if (IsImeKey(wParam)) return DefWindowProc(window, message, wParam, lParam);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "IME ウィンドウプロシージャで例外が発生しました。");
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    /// <summary>IME の切り替え・変換に使われるキーかどうか。バックエンドを経由させずに OS へ渡す対象。</summary>
    private static bool IsImeKey(nuint virtualKey) => virtualKey is
        VK_PROCESSKEY or VK_KANA or VK_KANJI or VK_CONVERT or VK_NONCONVERT
        or VK_OEM_COPY or VK_OEM_AUTO or VK_OEM_ENLW or VK_OEM_BACKTAB;

    /// <summary>仮想キーコードを編集コマンドへ対応付ける。対応が無ければ false。</summary>
    private bool TryMapCommand(int virtualKey, out EditCommand command)
    {
        bool ctrl = (GetKeyState(VK_CONTROL) & 0x8000) != 0;
        if (ctrl)
        {
            switch (virtualKey)
            {
                case VK_A: command = EditCommand.SelectAll; return true;
                case VK_C: command = EditCommand.Copy; return true;
                case VK_X: command = EditCommand.Cut; return true;
                case VK_V: command = EditCommand.Paste; return true;
            }
        }

        switch (virtualKey)
        {
            case VK_LEFT: command = EditCommand.Left; return true;
            case VK_RIGHT: command = EditCommand.Right; return true;
            case VK_UP: command = EditCommand.Up; return true;
            case VK_DOWN: command = EditCommand.Down; return true;
            case VK_HOME: command = EditCommand.Home; return true;
            case VK_END: command = EditCommand.End; return true;
            case VK_BACK: command = EditCommand.Backspace; return true;
            case VK_DELETE: command = EditCommand.Delete; return true;
            case VK_ESCAPE: command = EditCommand.Cancel; return true;
            case VK_RETURN:
                // 複数行なら Enter は改行で、確定は Ctrl+Enter。単一行はそのまま確定。
                command = _options.MultiLine && !ctrl ? EditCommand.NewLine : EditCommand.Commit;
                return true;
            default:
                command = default;
                return false;
        }
    }

    /// <summary>WM_IME_COMPOSITION から確定文字列・未確定文字列・属性・カーソル位置を読み出して積む。</summary>
    private void ReadComposition(nint window, nint lParam)
    {
        nint context = ImmGetContext(window);
        if (context == 0) return;
        try
        {
            if ((lParam & GCS_RESULTSTR) != 0)
            {
                string result = GetCompositionString(context, GCS_RESULTSTR);
                if (result.Length > 0) _events.Enqueue(new Event(EventKind.Result, result));
            }

            if ((lParam & GCS_COMPSTR) != 0)
            {
                string composition = GetCompositionString(context, GCS_COMPSTR);
                byte[] attributes = GetCompositionBytes(context, GCS_COMPATTR);
                int cursor = ImmGetCompositionStringW(context, GCS_CURSORPOS, null, 0);
                _events.Enqueue(new Event(EventKind.Composition, composition,
                    cursor < 0 ? composition.Length : cursor, attributes));
            }
            else if ((lParam & GCS_RESULTSTR) != 0)
            {
                _events.Enqueue(new Event(EventKind.Composition));
            }
        }
        finally
        {
            ImmReleaseContext(window, context);
        }
    }

    /// <summary>IME から現在の変換候補リストを読み出してイベントに積む。</summary>
    private void ReadCandidates(nint window)
    {
        nint context = ImmGetContext(window);
        if (context == 0) return;
        try
        {
            _events.Enqueue(new Event(EventKind.Candidate, Candidates: ReadCandidateList(context)));
        }
        finally
        {
            ImmReleaseContext(window, context);
        }
    }

    /// <summary>
    /// CANDIDATELIST を読み出す。構造体は
    /// dwSize / dwStyle / dwCount / dwSelection / dwPageStart / dwPageSize / dwOffset[dwCount]
    /// が DWORD で並び、dwOffset[i] は構造体先頭から各候補文字列までのバイトオフセット。
    /// </summary>
    private static CandidateList ReadCandidateList(nint context)
    {
        int size = ImmGetCandidateListW(context, 0, 0, 0);
        if (size < HeaderSize) return CandidateList.Empty;

        nint buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (ImmGetCandidateListW(context, 0, buffer, (uint)size) < HeaderSize) return CandidateList.Empty;

            int count = Marshal.ReadInt32(buffer, 8);
            int selection = Marshal.ReadInt32(buffer, 12);
            int pageStart = Marshal.ReadInt32(buffer, 16);
            int pageSize = Marshal.ReadInt32(buffer, 20);
            if (count <= 0) return CandidateList.Empty;
            // 壊れた値でメモリを踏まないよう、バッファに収まる件数までに抑える。
            count = System.Math.Min(count, (size - HeaderSize) / 4);
            if (count <= 0) return CandidateList.Empty;

            var items = new string[count];
            for (int i = 0; i < count; i++)
            {
                int offset = Marshal.ReadInt32(buffer, HeaderSize + i * 4);
                items[i] = offset > 0 && offset < size ? Marshal.PtrToStringUni(buffer + offset) ?? "" : "";
            }
            return new CandidateList(items, selection, pageStart, pageSize);
        }
        catch (Exception ex)
        {
            Log.Warning($"変換候補の読み取りに失敗しました: {ex.Message}");
            return CandidateList.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>CANDIDATELIST の dwOffset[] が始まるまでのバイト数。</summary>
    private const int HeaderSize = 24;

    private static string GetCompositionString(nint context, uint index)
    {
        int bytes = ImmGetCompositionStringW(context, index, null, 0);
        if (bytes <= 0) return "";
        byte[] buffer = new byte[bytes];
        bytes = ImmGetCompositionStringW(context, index, buffer, (uint)bytes);
        return bytes <= 0 ? "" : Encoding.Unicode.GetString(buffer, 0, bytes);
    }

    private static byte[] GetCompositionBytes(nint context, uint index)
    {
        int bytes = ImmGetCompositionStringW(context, index, null, 0);
        if (bytes <= 0) return [];
        byte[] buffer = new byte[bytes];
        bytes = ImmGetCompositionStringW(context, index, buffer, (uint)bytes);
        if (bytes <= 0) return [];
        if (bytes < buffer.Length) Array.Resize(ref buffer, bytes);
        return buffer;
    }

    /// <summary>候補ウィンドウ・未確定文字列ウィンドウの位置をキャレットへ合わせる。</summary>
    private void ApplyCandidatePosition(bool force = false)
    {
        // ImmSetCompositionWindow / ImmSetCandidateWindow は WM_IME_NOTIFY を
        // このウィンドウへ送り返す。その通知の処理中にまた設定しないよう二重に止める。
        if (_applyingCaretPosition) return;
        int x = _caretX, top = _caretTop, bottom = _caretBottom;
        if (!force && x == _appliedCaretX && top == _appliedCaretTop && bottom == _appliedCaretBottom) return;

        nint context = ImmGetContext(_window);
        if (context == 0) return;
        _applyingCaretPosition = true;
        try
        {
            var form = new CompositionForm
            {
                Style = CFS_POINT,
                CurrentPosition = new Point { X = _caretX, Y = _caretTop },
            };
            ImmSetCompositionWindow(context, ref form);

            // CFS_EXCLUDE で「キャレット行には重ねないでくれ」と伝える。候補は行の上下へ自動で逃げる。
            var candidate = new CandidateForm
            {
                Index = 0,
                Style = CFS_EXCLUDE,
                CurrentPosition = new Point { X = _caretX, Y = _caretBottom },
                Area = new Rect { Left = _caretX, Top = _caretTop, Right = _caretX + 2, Bottom = _caretBottom },
            };
            ImmSetCandidateWindow(context, ref candidate);
        }
        finally
        {
            _applyingCaretPosition = false;
            _appliedCaretX = x;
            _appliedCaretTop = top;
            _appliedCaretBottom = bottom;
            ImmReleaseContext(_window, context);
        }
    }

    private enum ImeMode { Disable, EnableClosed, EnableOpen, Restore }

    /// <summary>IME の有効/無効切り替えをウィンドウプロシージャのスレッドへ依頼する。</summary>
    private void RequestImeMode(ImeMode mode)
    {
        // IMM32 はウィンドウの所有スレッドから呼ぶのが前提。別スレッドなら PostMessage で回す。
        if (GetCurrentThreadId() == GetWindowThreadProcessId(_window, 0)) ApplyImeMode(mode);
        else PostMessage(_window, WM_ASTRUM_IME, (nuint)(int)mode, 0);
    }

    /// <summary>ウィンドウの IME を有効化/無効化する。無効化時は入力コンテキストごと切り離す。</summary>
    private void ApplyImeMode(ImeMode mode)
    {
        if (mode is ImeMode.Disable or ImeMode.Restore)
        {
            // 変換途中なら捨てる。
            nint context = ImmGetContext(_window);
            if (context != 0)
            {
                ImmNotifyIME(context, NI_COMPOSITIONSTR, CPS_CANCEL, 0);
                ImmReleaseContext(_window, context);
            }

            // 非入力中はコンテキストを外して、ゲーム操作中に変換が始まらないようにする。
            // 破棄時（Restore）は、元々ウィンドウに繋がっていたものを戻してから自前のを捨てる。
            ImmAssociateContext(_window, mode == ImeMode.Restore ? _originalContext : 0);

            if (mode == ImeMode.Restore)
            {
                if (_ownContext != 0)
                {
                    ImmDestroyContext(_ownContext);
                    _ownContext = 0;
                }
                _originalContext = 0;
                _originalContextSaved = false;
            }
            return;
        }

        // DxLib は既定で IME をウィンドウから切り離している（SetUseIMEFlag の既定が FALSE）。
        // そのまま ImmGetContext を呼んでも 0 が返るだけで日本語入力ができないので、
        // 自前のコンテキストを作って繋ぎ直す。raylib でも同じ経路で動くため挙動が揃う。
        if (_ownContext == 0)
        {
            _ownContext = ImmCreateContext();
            if (_ownContext == 0)
            {
                Log.Warning("IME の入力コンテキストを作成できませんでした。日本語入力は無効になります。");
                return;
            }
        }

        nint previous = ImmAssociateContext(_window, _ownContext);
        if (!_originalContextSaved)
        {
            // 元が「繋がっていない(0)」ことも正常な状態なので、値ではなくフラグで覚える。
            _originalContext = previous == _ownContext ? 0 : previous;
            _originalContextSaved = true;
        }

        if (mode == ImeMode.EnableOpen)
        {
            // ImmCreateContext で作ったコンテキストの変換モードの既定は
            // IME_CMODE_ALPHANUMERIC（半角英数）で、開いていてもローマ字がそのまま入るだけになる。
            // かな入力を求められているので、全角ひらがな＋ローマ字入力を明示する。
            ImmSetConversionStatus(_ownContext,
                IME_CMODE_NATIVE | IME_CMODE_FULLSHAPE | IME_CMODE_ROMAN, IME_SMODE_NONE);
        }
        ImmSetOpenStatus(_ownContext, mode == ImeMode.EnableOpen);
    }

    // ------------------------------------------------------------------
    // クリップボード
    // ------------------------------------------------------------------

    private static string GetClipboardText()
    {
        if (!OpenClipboard(0)) return "";
        try
        {
            nint handle = GetClipboardData(CF_UNICODETEXT);
            if (handle == 0) return "";
            nint pointer = GlobalLock(handle);
            if (pointer == 0) return "";
            try
            {
                return Marshal.PtrToStringUni(pointer) ?? "";
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"クリップボードの読み取りに失敗しました: {ex.Message}");
            return "";
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static void SetClipboardText(string text)
    {
        if (!OpenClipboard(0)) return;
        try
        {
            EmptyClipboard();
            nint memory = GlobalAlloc(GMEM_MOVEABLE, (nuint)((text.Length + 1) * 2));
            if (memory == 0) return;
            nint pointer = GlobalLock(memory);
            if (pointer == 0)
            {
                GlobalFree(memory);
                return;
            }
            try
            {
                Marshal.Copy(text.ToCharArray(), 0, pointer, text.Length);
                Marshal.WriteInt16(pointer, text.Length * 2, 0);
            }
            finally
            {
                GlobalUnlock(memory);
            }
            // SetClipboardData が成功したらメモリの所有権は OS 側へ移るので解放しない。
            if (SetClipboardData(CF_UNICODETEXT, memory) == 0) GlobalFree(memory);
        }
        catch (Exception ex)
        {
            Log.Warning($"クリップボードへの書き込みに失敗しました: {ex.Message}");
        }
        finally
        {
            CloseClipboard();
        }
    }

    // ------------------------------------------------------------------
    // Win32 定義
    // ------------------------------------------------------------------

    private const uint WM_NCDESTROY = 0x0082;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_CHAR = 0x0102;
    private const uint WM_KEYUP = 0x0101;
    private const uint WM_SYSKEYDOWN = 0x0104;
    private const uint WM_SYSKEYUP = 0x0105;
    private const uint WM_SYSCHAR = 0x0106;
    private const uint WM_IME_STARTCOMPOSITION = 0x010D;
    private const uint WM_IME_ENDCOMPOSITION = 0x010E;
    private const uint WM_IME_COMPOSITION = 0x010F;
    private const uint WM_IME_SETCONTEXT = 0x0281;
    private const uint WM_IME_NOTIFY = 0x0282;
    private const uint WM_IME_CHAR = 0x0286;
    /// <summary>IME の有効/無効切り替えを所有スレッドへ依頼する独自メッセージ（WM_APP+0x41）。</summary>
    private const uint WM_ASTRUM_IME = 0x8000 + 0x41;

    // nint は const にできないので static readonly で置く。
    private static readonly nint ISC_SHOWUICOMPOSITIONWINDOW = unchecked((nint)0x80000000);

    private const uint GCS_COMPSTR = 0x0008;
    private const uint GCS_COMPATTR = 0x0010;
    private const uint GCS_CURSORPOS = 0x0080;
    private const uint GCS_RESULTSTR = 0x0800;

    private const byte ATTR_INPUT = 0;
    private const byte ATTR_TARGET_CONVERTED = 1;
    private const byte ATTR_TARGET_NOTCONVERTED = 3;

    private const uint CFS_POINT = 0x0002;
    private const uint CFS_EXCLUDE = 0x0080;

    /// <summary>変換候補ウィンドウが開いた通知（WM_IME_NOTIFY の wParam）。</summary>
    private const nuint IMN_OPENCANDIDATE = 0x0005;
    /// <summary>候補リストが変化した通知。これが飛んでくる＝アプリ側で候補を描く必要がある。</summary>
    private const nuint IMN_CHANGECANDIDATE = 0x0003;
    private const nuint IMN_CLOSECANDIDATE = 0x0004;

    private const uint NI_COMPOSITIONSTR = 0x0015;

    // 変換モード。ALPHANUMERIC(0) のままだと IME を開いてもローマ字が素通りする。
    private const uint IME_CMODE_NATIVE = 0x0001;
    private const uint IME_CMODE_FULLSHAPE = 0x0008;
    private const uint IME_CMODE_ROMAN = 0x0010;
    private const uint IME_SMODE_NONE = 0x0000;
    private const uint CPS_CANCEL = 0x0004;

    private const int VK_BACK = 0x08;
    private const int VK_RETURN = 0x0D;
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_END = 0x23;
    private const int VK_HOME = 0x24;
    private const int VK_LEFT = 0x25;
    private const int VK_UP = 0x26;
    private const int VK_RIGHT = 0x27;
    private const int VK_DOWN = 0x28;
    private const int VK_DELETE = 0x2E;
    private const int VK_A = 0x41;
    private const int VK_C = 0x43;
    private const int VK_V = 0x56;
    private const int VK_X = 0x58;
    private const nuint VK_PROCESSKEY = 0xE5;
    // IME 操作キー。半角/全角キーはキーボードや状況で VK_KANJI / VK_OEM_AUTO / VK_OEM_ENLW の
    // いずれかで飛んでくるので全部拾う。
    private const nuint VK_KANA = 0x15;
    private const nuint VK_KANJI = 0x19;
    private const nuint VK_CONVERT = 0x1C;
    private const nuint VK_NONCONVERT = 0x1D;
    private const nuint VK_OEM_COPY = 0xF2;
    private const nuint VK_OEM_AUTO = 0xF3;
    private const nuint VK_OEM_ENLW = 0xF4;
    private const nuint VK_OEM_BACKTAB = 0xF5;

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CompositionForm
    {
        public uint Style;
        public Point CurrentPosition;
        public Rect Area;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CandidateForm
    {
        public uint Index;
        public uint Style;
        public Point CurrentPosition;
        public Rect Area;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProc(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint window, SubclassProc proc, nuint id, nuint data);
    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint window, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static extern nint DefWindowProc(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("imm32.dll")]
    private static extern nint ImmGetContext(nint window);
    [DllImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImmReleaseContext(nint window, nint context);
    [DllImport("imm32.dll")]
    private static extern nint ImmAssociateContext(nint window, nint context);
    [DllImport("imm32.dll")]
    private static extern nint ImmCreateContext();
    [DllImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImmDestroyContext(nint context);
    [DllImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImmSetOpenStatus(nint context, [MarshalAs(UnmanagedType.Bool)] bool open);
    [DllImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImmSetConversionStatus(nint context, uint conversion, uint sentence);
    [DllImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImmNotifyIME(nint context, uint action, uint index, uint value);
    [DllImport("imm32.dll", CharSet = CharSet.Unicode)]
    private static extern int ImmGetCompositionStringW(nint context, uint index, byte[]? buffer, uint length);
    [DllImport("imm32.dll")]
    private static extern int ImmGetCandidateListW(nint context, uint index, nint list, uint length);
    [DllImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImmSetCompositionWindow(nint context, ref CompositionForm form);
    [DllImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImmSetCandidateWindow(nint context, ref CandidateForm form);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out Rect rect);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, nint processId);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();
    [DllImport("user32.dll")]
    private static extern nint GetClipboardData(uint format);
    [DllImport("user32.dll")]
    private static extern nint SetClipboardData(uint format, nint memory);
    [DllImport("kernel32.dll")]
    private static extern nint GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll")]
    private static extern nint GlobalFree(nint memory);
    [DllImport("kernel32.dll")]
    private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(nint memory);
}
