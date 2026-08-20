namespace AstrumLoom;

/// <summary>
/// ゲーム起動時に AstrumCore.Boot へ渡す設定一式。ウィンドウ・タイミング・リソース・デバッグ関連の
/// 既定値をまとめて持つ。ここに無い項目は各ゲーム側で個別に扱う。
/// </summary>
public sealed class GameConfig
{
    // --- Window ---
    public string Title { get; set; } = "AstrumLoom Game";
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public double Scale { get; set; } = 1.0;   // 論理解像度に対する拡大率
    public bool Resizable { get; set; } = true;
    public bool RunInBackground { get; set; } = true; // 非アクティブでも動かすか
    public bool Fullscreen { get; set; } = false;
    public bool ShowMouse { get; set; } = true;

    // --- Timing / Performance ---
    public int TargetFps { get; set; } = 60;
    public bool VSync { get; set; } = false;
    public bool UseMultiThreadUpdate { get; set; } = false;
    public int SleepDurationMs { get; set; } = 1000 * 60 * 10; // 長時間放置でスリープするまで

    /// <summary>
    /// 更新を固定ステップで回すか。true にすると <see cref="IGame.Update"/> は常に
    /// 1/<see cref="FixedUpdateHz"/> 秒の deltaTime で呼ばれ、描画レートから独立します。
    /// 記録・再生やセルフテストの再現性はこれが前提です。
    /// </summary>
    public bool FixedUpdate { get; set; } = false;
    /// <summary>固定ステップ更新の周波数 (Hz)。</summary>
    public double FixedUpdateHz { get; set; } = 60.0;
    /// <summary>1 回のループで許す最大キャッチアップ回数。処理落ちの巻き戻り爆発を防ぎます。</summary>
    public int MaxCatchUpSteps { get; set; } = 5;

    /// <summary>
    /// 1 ループにつき必ず 1 論理フレームだけ進めるモード。実時間を一切見ないので、
    /// 同じ入力からは必ず同じ結果が出ます。記録・再生とセルフテストで使います。
    /// <see cref="FixedUpdate"/> と併用します。
    /// </summary>
    public bool LockStep { get; set; } = false;

    /// <summary>乱数シード。null なら毎回ランダム。</summary>
    public int? Seed { get; set; }

    // --- Resources ---
    public bool AsyncResourceLoad { get; set; } = true;
    public string ContentRoot { get; set; } = AppContext.BaseDirectory ?? ".";

    // --- System / Input ---
    public bool EnableDragDrop { get; set; } = true;

    // --- Debug / Logging ---
    public bool EnableLogging { get; set; } = true;
    public bool ShowFpsOverlay { get; set; } = false;
    /// <summary>F1〜F5 のデバッグホットキーを有効にするか。</summary>
    public bool EnableDebugHotkeys { get; set; } = true;
    /// <summary>スクリーンショット・ログ・記録の出力先ディレクトリ。</summary>
    public string DebugOutputDir { get; set; } = "debugout";

    // --- 使用するバックエンド ---
    public GraphicsBackendKind GraphicsBackend { get; set; } = GraphicsBackendKind.DxLib;
}
