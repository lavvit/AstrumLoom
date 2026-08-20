using System.Collections.Concurrent;

using AstrumLoom;

namespace Sandbox;

/// <summary>
/// テーマ「入力コックピット」。
///
/// キーボード・マウス・テキスト入力・ゲームパッドを 6 枚のカードに分けて見せる。
/// どれも「押されているか」だけでなく、生の数値（座標・速度・フレーム数）をそのまま出す。
/// 入力系はバグが起きても絵は正常に見えることが多いので、数値まで出しておかないと
/// 見た目だけでは気づけない（docs\KNOWN-ISSUES.md の入力まわりの指摘がまさにそれ）。
/// </summary>
internal sealed class InputDemoScene : Scene
{
    private static readonly MouseButton[] MouseButtons = Enum.GetValues<MouseButton>();

    // 更新スレッド(Update)が書き、描画スレッド(Draw)が読む。素の List/Queue は
    // 書き込み中の列挙でクラッシュするので、スレッドセーフなキューと
    // 「新しいリストへ丸ごと差し替える」方式（参照の代入は原子的）で共有する。
    private readonly ConcurrentQueue<string> _eventLog = new();
    private IReadOnlyList<Key> _pressedKeys = [];
    private readonly TextInputOptions _textOptions = new() { MaxLength = 40 };

    private IFont? _kbFont;
    private string _textBuffer = string.Empty;
    private bool _textActive;
    private int _selectedPad;
    private double _mouseSpeedPeak;
    private double _mouseSpeedDecay;   // 秒単位の減衰係数（DemoUi.Delta で進める）
    private long _frame;               // イベントログの時刻表示用

    public override void Enable()
    {
        base.Enable();
        // FontHandle.Create に渡すフォント名は「入っていれば使う」程度の指定で、
        // 存在しないと静かに null や別サイズへフォールバックする。特に小さい号数だと
        // 「読み込みには成功したが文字が実質見えない」という中間状態になりうる
        // （RayLib で確認済み。DxLib では同じコードで問題なく見えていた）。
        // DemoUi.NoteFont は両バックエンドで見えることを他のシーンで確認済みなので、それを使う。
        _kbFont = DemoUi.NoteFont;
        _eventLog.Clear();
        _pressedKeys = [];
        _textBuffer = string.Empty;
        _textActive = false;
        _selectedPad = 0;
        _mouseSpeedPeak = 0;
        _mouseSpeedDecay = 0;
        _frame = 0;
    }

    public override void Update()
    {
        _frame++;
        CaptureKeyboardState();
        UpdateMouseMetrics();
        UpdateControllerState();
        UpdateTextInput();
    }

    private void CaptureKeyboardState()
    {
        // Draw 側が読んでいる最中の書き換えを避けるため、新しいリストを作ってから
        // フィールドへ丸ごと差し替える（Clear→Add で同じインスタンスを書き換えない）。
        var next = new List<Key>();
        foreach (var key in KeyInput.GetPressedKeys())
            next.Add(key);
        _pressedKeys = next;

        foreach (var key in KeyInput.GetAllKeys())
        {
            if (key.Push()) AddLog($"KeyDown {key}");
            else if (key.Left()) AddLog($"KeyUp {key}");
        }
    }

    private void UpdateMouseMetrics()
    {
        double speed = Mouse.Speed;
        // ピークは時間で減衰させる。フレーム数で減衰させると実機の無制限 FPS で一瞬で 0 になる。
        _mouseSpeedDecay = Math.Max(0, _mouseSpeedDecay - DemoUi.Delta * 240);
        _mouseSpeedPeak = Math.Max(speed, _mouseSpeedPeak - _mouseSpeedDecay * DemoUi.Delta);
        if (speed > _mouseSpeedPeak) { _mouseSpeedPeak = speed; _mouseSpeedDecay = speed; }

        if (Mouse.Wheel != 0)
            AddLog($"Wheel {(Mouse.Wheel > 0 ? "Up" : "Down")} (Total {Mouse.WheelTotal:0.##})");
    }

    private void UpdateControllerState()
    {
        int count = Pad.Count;
        if (count <= 0) { _selectedPad = 0; return; }

        if (Key.Left.Push())
        {
            _selectedPad = (_selectedPad - 1 + count) % count;
            AddLog($"Pad {_selectedPad} selected");
        }
        else if (Key.Right.Push())
        {
            _selectedPad = (_selectedPad + 1) % count;
            AddLog($"Pad {_selectedPad} selected");
        }
        if (_selectedPad >= count) _selectedPad = count - 1;

        var pad = Pad.GetJoyPad(_selectedPad);
        if (pad == null) return;

        if (Key.B.Push())
        {
            pad.Vibrate(0f, 0.35f, 400);
            AddLog($"Pad {_selectedPad} vibrate");
        }
        if (pad.NowPushedButton() is int button)
            AddLog($"Pad {_selectedPad} Button {button}");
    }

    private void UpdateTextInput()
    {
        if (!_textActive && Key.T.Push())
        {
            KeyInput.ActivateText(ref _textBuffer, _textOptions);
            _textActive = true;
            AddLog("Text input started");
        }
        if (_textActive && KeyInput.Enter(ref _textBuffer, out string? committed))
        {
            if (!string.IsNullOrEmpty(committed)) AddLog($"Entered \"{committed}\"");
            _textActive = false;
        }
        // Esc キャンセルは Enter() 経由では捕まらない（Finished ではなく Canceled で終わるため）。
        // KeyInput.Typing がセッション終了で false に落ちたら、確定/キャンセルどちらでも待機状態へ戻す。
        else if (_textActive && !KeyInput.Typing)
        {
            AddLog("Text input canceled");
            _textActive = false;
        }
    }

    public override void Draw()
    {
        Drawing.Fill(new Color(16, 18, 28));
        DrawGrid();

        Drawing.Text(20, 16, "入力コックピット / Input の見本帳", Color.White, edgecolor: new Color(6, 6, 12));
        DemoUi.Note(20, 52, 820,
            "[T] テキスト入力開始/確定  [Left/Right] パッド選択  [B] 振動  マウスを動かすと速度が動く",
            new Color(180, 198, 226));

        Card(0, 0, "1) Keyboard", DrawKeyboardCard);
        Card(1, 0, "2) Mouse", DrawMouseCard);
        Card(2, 0, "3) Text Input", DrawTextCard);
        Card(0, 1, "4) Gamepad", DrawGamepadCard);
        Card(1, 1, "5) Event Log", DrawLogCard);
        Card(2, 1, "6) 早見表", DrawCheatSheetCard);
    }

    private void DrawGrid()
    {
        var c = new Color(255, 255, 255, 6);
        for (int gx = 0; gx < AstrumCore.Width; gx += 40)
            Drawing.Line(gx, 0, 0, AstrumCore.Height, c);
        for (int gy = 0; gy < AstrumCore.Height; gy += 40)
            Drawing.Line(0, gy, AstrumCore.Width, 0, c);
    }

    #region カードの枠

    private const int CardW = 404;
    private const int CardH = 292;
    private const int CardTop = 84;
    private const int Pad_ = 10;
    private const double TextW = CardW - Pad_ * 2;

    private static void Card(int col, int row, string title, Action<double, double> body)
    {
        double x = 16 + col * (CardW + 12);
        double y = CardTop + row * (CardH + 10);
        DemoUi.Card(x, y, CardW, CardH, title, (bx, by, _) => body(bx, by));
    }

    #endregion

    #region 1) Keyboard

    private void DrawKeyboardCard(double x, double y)
    {
        var keys = _pressedKeys;   // 1 回だけ読んで固定する（このあと差し替わっても影響を受けない）。
        string pressed = keys.Count == 0 ? "なし" : string.Join(", ", keys.Take(6));
        double ny = y + 6;
        ny = DemoUi.Notes(x + Pad_, ny, TextW, new Color(196, 212, 240),
            $"押下中: {pressed}");
        ny = DemoUi.Notes(x + Pad_, ny, TextW, new Color(152, 170, 202),
            $"Shift {Flag(KeyInput.Shift)}  Ctrl {Flag(KeyInput.Ctrl)}  Alt {Flag(KeyInput.Alt)}  Typing {Flag(KeyInput.Typing)}");

        // 縮小した TKL 配列。押しているキーが KeyBoard.GetKeyColor で自動的に色づく。
        KeyBoard.Draw(x + Pad_, ny + 6, size: 7, KeyType.JPTKL, _kbFont);
    }

    #endregion

    #region 2) Mouse

    private void DrawMouseCard(double x, double y)
    {
        double ny = y + 6;
        ny = DemoUi.Notes(x + Pad_, ny, TextW, new Color(196, 212, 240),
            $"Position ({Mouse.X:0}, {Mouse.Y:0})",
            $"Speed {Mouse.Speed:0.0}   Peak {_mouseSpeedPeak:0.0}");
        ny = DemoUi.Notes(x + Pad_, ny, TextW, new Color(152, 170, 202),
            $"Wheel {Mouse.Wheel:+0.##;-0.##;0}（Total {Mouse.WheelTotal:0.##}）  TouchPad {Flag(Mouse.IsTouchPad)}");

        // 速度メーター。Peak との差でどれだけ減衰したかも見える。
        double mx = x + Pad_, mw = TextW, mh = 16, my = ny + 6;
        double frac = Math.Clamp(Mouse.Speed / 40.0, 0, 1);
        double peakFrac = Math.Clamp(_mouseSpeedPeak / 40.0, 0, 1);
        Drawing.Box(mx, my, mw, mh, new Color(30, 38, 62));
        Drawing.Box(mx, my, mw * frac, mh, new Color(120, 190, 240));
        Drawing.Line(mx + mw * peakFrac, my - 2, 0, mh + 4, new Color(255, 210, 120), thickness: 2);
        ny = my + mh + 18;

        foreach (var button in MouseButtons)
        {
            string state = Mouse.Push(button) ? "Push" : Mouse.Left(button) ? "Release" : Mouse.Hold(button) ? "Hold" : "-";
            var col = state == "-" ? new Color(120, 130, 150) : new Color(210, 226, 250);
            DemoUi.NoteFont.Draw(x + Pad_, ny, $"{button}: {state}", col);
            ny += DemoUi.LineHeight;
        }

        ny += 4;
        DemoUi.Notes(x + Pad_, ny, TextW, new Color(212, 172, 112),
            "注: IMouse だけ Buffer() が無く更新スレッドから直接読む。MT 構成では"
            + " 1 描画に更新が 2 回走るとホイールが二重にカウントされる。");
    }

    #endregion

    #region 3) Text Input

    private void DrawTextCard(double x, double y)
    {
        double ny = y + 6;
        ny = DemoUi.Notes(x + Pad_, ny, TextW, new Color(196, 212, 240),
            $"状態: {(_textActive ? "入力中" : "待機")}");

        double bx = x + Pad_, bw = TextW, bh = 34, by = ny + 6;
        Drawing.Box(bx, by, bw, bh, new Color(24, 30, 50));
        Drawing.Box(bx, by, bw, bh, _textActive ? new Color(120, 180, 230) : new Color(70, 84, 118), thickness: 2);
        string shown = string.IsNullOrEmpty(_textBuffer) ? (_textActive ? "" : "[T] で入力を開始") : _textBuffer;
        var textCol = string.IsNullOrEmpty(_textBuffer) && !_textActive ? new Color(120, 130, 150) : Color.White;
        if (_textActive)
            KeyInput.DrawText(bx + 8, by + bh / 2 - 8, textCol);
        else
            DemoUi.NoteFont.Draw(bx + 8, by + bh / 2 - 6, shown, textCol);

        ny = by + bh + 14;
        ny = DemoUi.Notes(x + Pad_, ny, TextW, new Color(152, 170, 202),
            $"最大 {_textOptions.MaxLength} 文字。Enter で確定してイベントログへ流れる。");

        DemoUi.Notes(x + Pad_, ny + 6, TextW, new Color(152, 200, 152),
            "Esc でもキャンセルできる（Typing ゲートを通さない RawGetKeyDown 経由）。");
    }

    #endregion

    #region 4) Gamepad

    private void DrawGamepadCard(double x, double y)
    {
        double ny = y + 6;
        ny = DemoUi.Notes(x + Pad_, ny, TextW, new Color(196, 212, 240),
            $"接続数: {Pad.Count}");

        if (Pad.Count == 0)
        {
            DemoUi.Notes(x + Pad_, ny + 6, TextW, new Color(152, 170, 202),
                "コントローラ未接続。接続すると一覧が出る。");
            DemoUi.Notes(x + Pad_, ny + 44, TextW, new Color(212, 172, 112),
                "注: IJoyPad.Index の基準がバックエンドで違う（RayLib は 0 始まり、"
                + "DxLib は 1 始まり）。保存した番号を復元するコードは要注意。");
            return;
        }

        string[] names = Pad.List ?? [];
        for (int i = 0; i < names.Length && i < 3; i++)
        {
            bool now = i == _selectedPad;
            string marker = now ? "> " : "  ";
            DemoUi.NoteFont.Draw(x + Pad_, ny, $"{marker}[{i}] {names[i]}",
                now ? Color.White : new Color(170, 186, 214));
            ny += DemoUi.LineHeight;
        }

        var pad = Pad.GetJoyPad(_selectedPad);
        if (pad == null)
        {
            DemoUi.Notes(x + Pad_, ny + 4, TextW, new Color(230, 150, 150), "選択中のパッドが取れない");
            return;
        }

        ny += 4;
        ny = DemoUi.Notes(x + Pad_, ny, TextW, new Color(180, 196, 224),
            $"Product {pad.Product}   Type {pad.Type}   Index {pad.Index}");

        string buttons = pad.Button.Select((v, i) => v != 0 ? i.ToString() : "")
            .Where(v => v.Length > 0).DefaultIfEmpty("なし").Aggregate((a, b) => $"{a}, {b}");
        ny = DemoUi.Notes(x + Pad_, ny, TextW, new Color(180, 196, 224), $"Buttons: {buttons}");

        string sticks = string.Join("  ", pad.Stick.Select((s, i) => $"S{i}({s.X:0.00},{s.Y:0.00})"));
        DemoUi.Notes(x + Pad_, ny, TextW, new Color(180, 196, 224), $"Sticks: {sticks}");
    }

    #endregion

    #region 5) Event Log

    private void DrawLogCard(double x, double y)
    {
        double ny = y + 4;
        // ConcurrentQueue.ToArray() は呼んだ瞬間のスナップショットを返すので、
        // 列挙中に Update スレッドが Enqueue/TryDequeue しても安全。
        string[] entries = _eventLog.ToArray();
        if (entries.Length == 0)
        {
            DemoUi.Notes(x + Pad_, ny, TextW, new Color(150, 168, 200), "まだイベントが無い。何かキーを押すとここに流れる。");
            return;
        }
        foreach (string entry in entries.Reverse())
        {
            DemoUi.NoteFont.Draw(x + Pad_, ny, entry, new Color(206, 220, 244));
            ny += DemoUi.LineHeight;
            if (ny > y + CardH - 34 - 10) break;
        }
    }

    #endregion

    #region 6) 早見表

    private void DrawCheatSheetCard(double x, double y)
    {
        double ny = y + 6;
        ny = DemoUi.Notes(x + Pad_, ny, TextW, new Color(196, 212, 240),
            "T: テキスト入力を開始/確定",
            "Left/Right: 選択中のゲームパッドを切替",
            "B: 選択中のパッドを振動",
            "数字キー 1-6: シーン切替（このシーンでは使わない）");
        ny += 4;
        DemoUi.Notes(x + Pad_, ny, TextW, new Color(152, 170, 202),
            "Key.Push/Hold/Left（離した）はどれも \"論理フレームで 1 回だけ\" 成立する。"
            + "Update を複数回呼ぶ経路があると多重検出しうる。");
    }

    #endregion

    private static string Flag(bool value) => value ? "ON" : "OFF";

    private void AddLog(string message)
    {
        const int maxEntries = 10;
        _eventLog.Enqueue($"f{_frame} {message}");
        while (_eventLog.Count > maxEntries) _eventLog.TryDequeue(out _);
    }

    // --- セルフテストから中を覗くための入口 ---------------------------------

    public bool TextActive => _textActive;
    public string TextBuffer => _textBuffer;
    public int LogCount => _eventLog.Count;
}
