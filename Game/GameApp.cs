using AstrumLoom.DXLib;
using AstrumLoom.RayLib;

namespace AstrumLoom;

/// <summary>
/// ゲームの起動口。バックエンドの選択・コマンドライン引数の解釈・例外の受け止めを引き受けます。
/// ゲーム側は <c>Main</c> でこれを 1 回呼ぶだけで済みます。
/// <code>
/// [STAThread]
/// static int Main(string[] args) => GameApp.Run(args, new GameConfig
/// {
///     Title = "MyGame",
///     Width = 1280,
///     Height = 720,
/// }, () =&gt; new TitleScene());
/// </code>
/// バックエンドの型名がゲーム側に出てこないのが要点です。切り替えは
/// <see cref="GameConfig.GraphicsBackend"/> か <c>--backend raylib</c> で行います。
/// </summary>
public static class GameApp
{
    /// <summary>終了コード: 正常終了。</summary>
    public const int ExitOk = 0;
    /// <summary>終了コード: セルフテスト失敗、または実行中の致命的エラー。</summary>
    public const int ExitFailure = 1;
    /// <summary>終了コード: 引数が不正。</summary>
    public const int ExitBadUsage = 2;

    /// <summary>設定に応じたプラットフォームを生成します。</summary>
    public static IGamePlatform CreatePlatform(GameConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return config.GraphicsBackend switch
        {
            GraphicsBackendKind.DxLib => new DxLibPlatform(config),
            GraphicsBackendKind.RayLib => new RayLibPlatform(config),
            _ => throw new NotSupportedException(
                $"バックエンド {config.GraphicsBackend} には対応していません。"),
        };
    }

    /// <summary>ゲームを起動します。戻り値はプロセスの終了コードです。</summary>
    /// <param name="args">コマンドライン引数。</param>
    /// <param name="config">ゲームごとの設定。引数の内容で上書きされます。</param>
    /// <param name="sceneFactory">最初のシーンを作る処理。プラットフォーム生成後に呼ばれます。</param>
    public static int Run(string[]? args, GameConfig config, Func<Scene> sceneFactory)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(sceneFactory);

        // WinExe でも PowerShell から起動したときにログが見えるようにする。
        ConsoleBridge.Attach();

        var options = Startup.Parse(args);

        if (options.ShowHelp)
        {
            Console.WriteLine($"{config.Title}\n");
            Console.WriteLine(Startup.Usage);
            return ExitOk;
        }

        foreach (string unknown in options.Unknown)
            Console.Error.WriteLine($"警告: 不明な引数 '{unknown}' を無視しました。");

        if (options.HasError)
        {
            foreach (string error in options.Errors)
                Console.Error.WriteLine($"エラー: {error}");
            Console.Error.WriteLine("\n--help で使い方を表示します。");
            return ExitBadUsage;
        }

        config.Apply(options);

        IGamePlatform? platform = null;
        try
        {
            platform = CreatePlatform(config);
            AstrumCore.Boot(config, platform, sceneFactory(), options);
        }
        catch (Exception ex)
        {
            // ウィンドウが出る前に落ちると画面には何も出ないので、必ずコンソールへ出す。
            Console.Error.WriteLine($"起動に失敗しました: {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            Log.Error(ex, "起動に失敗しました");
            TryDispose(platform);
            return ExitFailure;
        }

        // 実行中に致命的エラーを踏んでいたら失敗として返す。
        if (AstrumCore.HasFatalError) return ExitFailure;

        // セルフテストは Environment.ExitCode に結果を入れる。
        return Environment.ExitCode;
    }

    /// <summary>シーンのインスタンスを直接渡す版。</summary>
    public static int Run(string[]? args, GameConfig config, Scene scene)
        => Run(args, config, () => scene);

    private static void TryDispose(IGamePlatform? platform)
    {
        try { platform?.Dispose(); }
        catch { /* 起動失敗時の後始末なので、ここでの例外は握りつぶす */ }
    }
}
