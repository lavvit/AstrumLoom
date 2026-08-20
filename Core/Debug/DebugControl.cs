namespace AstrumLoom;

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
    public static bool Enabled { get; set; } = true;

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

    public static void Pause() => Paused = true;
    public static void Resume() { Paused = false; _stepRequested = false; }
    public static void TogglePause() { Paused = !Paused; if (!Paused) _stepRequested = false; }
    /// <summary>一時停止中に 1 フレームだけ進めます。停止していなければ停止させます。</summary>
    public static void Step() { Paused = true; _stepRequested = true; }

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
        if (!Enabled) return;
        var input = AstrumCore.Platform?.Input;
        if (input == null) return;

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
