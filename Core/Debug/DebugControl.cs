namespace AstrumLoom;

/// <summary>
/// デバッグホットキーの受け付け方。ゲーム側が F1〜F6 を使いたいケースに対応するための切り替え。
/// </summary>
public enum DebugHotkeyMode
{
    /// <summary>既定。F1〜F6 を単独押しで受け付ける（従来どおり）。</summary>
    Direct,
    /// <summary>F1〜F6 は修飾キー（既定 Ctrl）併用時のみ受け付ける。ゲーム側は素の F キーを使える。</summary>
    Modifier,
    /// <summary>個別ホットキーは無効。<see cref="DebugControl.KeyMenu"/>（既定 Ctrl+F11）でメニューを開いてのみ操作する。</summary>
    MenuOnly,
    /// <summary>デバッグホットキー・メニューとも全て無効。</summary>
    Off,
}

/// <summary>デバッグホットキーに併用する修飾キー。複数指定した場合は全て押されている必要がある。</summary>
[Flags]
public enum DebugModifier
{
    None = 0,
    Ctrl = 1,
    Shift = 2,
    Alt = 4,
}

/// <summary>
/// 実機デバッグ用の共通操作（オーバーレイ・スクショ・スロー・一時停止・コマ送り）。
/// ゲーム側のキー処理より前に、フレームワークが毎フレーム <see cref="PollHotkeys"/> を呼びます。
/// </summary>
/// <remarks>
/// ホットキーは <see cref="KeyInput"/> ではなくプラットフォームの生入力を直接見ます。
/// そのため入力再生 (--replay) 中でも操作でき、記録された入力を汚しません。
/// </remarks>
public static class DebugControl
{
    /// <summary>ホットキーを受け付けるか。<see cref="GameConfig.EnableDebugHotkeys"/> から設定されます。</summary>
    /// <remarks>互換のため残しています。false のときは <see cref="Mode"/> に関わらず何もしません
    /// （<see cref="DebugHotkeyMode.Off"/> と同義扱い）。</remarks>
    public static bool Enabled { get; set; } = true;

    /// <summary>ホットキーの受け付け方。ゲーム側が F1〜F6 を使いたい場合は <see cref="DebugHotkeyMode.Modifier"/> や
    /// <see cref="DebugHotkeyMode.MenuOnly"/> に変更してください。<see cref="GameConfig.DebugHotkeyMode"/> から設定されます。</summary>
    public static DebugHotkeyMode Mode { get; set; } = DebugHotkeyMode.Direct;

    /// <summary><see cref="DebugHotkeyMode.Modifier"/> 時や、メニュー開閉（Modifier + <see cref="KeyMenu"/>）に使う修飾キー。
    /// <see cref="GameConfig.DebugHotkeyModifier"/> から設定されます。</summary>
    public static DebugModifier Modifier { get; set; } = DebugModifier.Ctrl;

    /// <summary>デバッグメニューの開閉キー。<see cref="Modifier"/> と併用します（既定 Ctrl+F11）。</summary>
    public static Key KeyMenu { get; set; } = Key.F11;

    /// <summary>デバッグオーバーレイを表示するか（F1）。</summary>
    public static bool ShowOverlay { get; set; }

    /// <summary>更新を止めているか（F4）。</summary>
    public static bool Paused { get; private set; }

    /// <summary>スロー倍率。1 で等速、2 なら半分の速さ（F3）。</summary>
    public static int SlowFactor { get; private set; } = 1;

    /// <summary>一時停止中に 1 フレームだけ進める要求（F5）。</summary>
    private static bool _stepRequested;

    /// <summary>スロー用の間引きカウンタ。</summary>
    private static int _slowCounter;

    /// <summary>直近のフレームで更新が実行されたか。オーバーレイ表示用。</summary>
    public static bool LastTickRan { get; private set; } = true;

    // --- ホットキー割り当て（差し替え可能） ---
    public static Key KeyOverlay { get; set; } = Key.F1;
    public static Key KeyScreenshot { get; set; } = Key.F2;
    public static Key KeySlow { get; set; } = Key.F3;
    public static Key KeyPause { get; set; } = Key.F4;
    public static Key KeyStep { get; set; } = Key.F5;
    public static Key KeyTuning { get; set; } = Key.F6;

    /// <summary>スロー倍率の巡回順。</summary>
    private static readonly int[] SlowSteps = [1, 2, 4, 8];

    /// <summary>更新を止めます。</summary>
    public static void Pause() => Paused = true;
    /// <summary>更新を再開し、コマ送り要求も取り消します。</summary>
    public static void Resume() { Paused = false; _stepRequested = false; }
    /// <summary>一時停止のオン/オフを切り替えます。再開時はコマ送り要求も取り消します。</summary>
    public static void TogglePause() { Paused = !Paused; if (!Paused) _stepRequested = false; }
    /// <summary>一時停止中に 1 フレームだけ進めます。停止していなければ停止させます。</summary>
    public static void Step() { Paused = true; _stepRequested = true; }

    /// <summary>スロー倍率を設定します。切り替え時にカウンタをリセットしないと、直前の間引き状態を引きずってしまう。</summary>
    public static void SetSlow(int factor)
    {
        SlowFactor = Math.Max(1, factor);
        _slowCounter = 0;
    }

    /// <summary>デバッグ操作を初期状態に戻します。</summary>
    public static void Reset()
    {
        Paused = false;
        _stepRequested = false;
        SlowFactor = 1;
        _slowCounter = 0;
        LastTickRan = true;
    }

    /// <summary>ホットキーを処理します。更新スレッドの、ゲーム更新より前から呼ばれます。</summary>
    internal static void PollHotkeys()
    {
        if (!Enabled || Mode == DebugHotkeyMode.Off) return;
        var input = AstrumCore.Platform?.Input;
        if (input == null) return;

        bool modifierHeld = IsModifierHeld(input);

        // メニュー開閉は Modifier + KeyMenu（既定 Ctrl+F1）。モードに関わらず常に受け付ける。
        if (modifierHeld && input.GetKeyDown(KeyMenu))
        {
            DebugMenu.Toggle();
            return;
        }

        if (DebugMenu.IsOpen)
        {
            DebugMenu.Poll(input);
            return;
        }

        if (Mode == DebugHotkeyMode.MenuOnly) return;

        // Direct モードでも、修飾キーを押している間は「メニュー用のジェスチャーの途中」とみなし
        // 個別ホットキー（素の F1〜F6）は処理しない。誤爆防止。
        if (modifierHeld && Mode == DebugHotkeyMode.Direct) return;
        // Modifier モードでは、修飾キーを押していなければ個別ホットキーは無効
        // （＝ゲーム側が素の F1〜F6 をそのまま使える）。
        if (Mode == DebugHotkeyMode.Modifier && !modifierHeld) return;

        if (input.GetKeyDown(KeyOverlay))
        {
            ShowOverlay = !ShowOverlay;
            Log.Debug($"オーバーレイ: {(ShowOverlay ? "表示" : "非表示")}");
        }
        if (input.GetKeyDown(KeyScreenshot))
            Snapshot.Request("manual");

        if (input.GetKeyDown(KeySlow))
        {
            int index = Array.IndexOf(SlowSteps, SlowFactor);
            SetSlow(SlowSteps[(index + 1) % SlowSteps.Length]);
            Log.Debug($"スロー: 1/{SlowFactor}");
        }
        if (input.GetKeyDown(KeyPause))
        {
            TogglePause();
            Log.Debug(Paused ? "一時停止" : "再開");
        }
        if (input.GetKeyDown(KeyStep))
            Step();

        if (input.GetKeyDown(KeyTuning))
        {
            Tune.Poll(force: true);
            if (Tune.LoadCount == 0) Tune.Save();
        }
    }

    /// <summary>
    /// <see cref="Modifier"/> に含まれる修飾キーが（複数指定なら全て）押されているかを返します。
    /// 入力再生を汚さないよう、<see cref="KeyInput"/>（Typing ゲート付き）ではなく
    /// プラットフォームの生入力を直接見ます。
    /// </summary>
    private static bool IsModifierHeld(IInput input)
    {
        if (Modifier == DebugModifier.None) return false;
        bool held = true;
        if ((Modifier & DebugModifier.Ctrl) != 0)
            held &= input.GetKey(Key.LCtrl) || input.GetKey(Key.RCtrl);
        if ((Modifier & DebugModifier.Shift) != 0)
            held &= input.GetKey(Key.LShift) || input.GetKey(Key.RShift);
        if ((Modifier & DebugModifier.Alt) != 0)
            held &= input.GetKey(Key.LAlt) || input.GetKey(Key.RAlt);
        return held;
    }

    /// <summary>
    /// このフレームでゲーム更新を実行してよいかを返します。副作用があるので 1 フレームに 1 回だけ呼びます。
    /// </summary>
    internal static bool ShouldRunUpdate()
    {
        if (Paused)
        {
            if (!_stepRequested) return LastTickRan = false;
            _stepRequested = false;
            _slowCounter = 0;
            return LastTickRan = true;
        }

        if (SlowFactor <= 1) return LastTickRan = true;

        _slowCounter++;
        if (_slowCounter < SlowFactor) return LastTickRan = false;
        _slowCounter = 0;
        return LastTickRan = true;
    }

    /// <summary>オーバーレイに出す 1 行の状態表示。通常時は空文字。</summary>
    public static string StatusText
    {
        get
        {
            if (Paused) return _stepRequested ? "STEP" : "PAUSED";
            return SlowFactor > 1 ? $"SLOW 1/{SlowFactor}" : "";
        }
    }
}
