using System.Runtime.InteropServices;

namespace AstrumLoom;

/// <summary>
/// コマンドライン引数から解釈した起動オプション。
/// null のプロパティは「指定なし（GameConfig の値をそのまま使う）」を意味します。
/// </summary>
public sealed class LaunchOptions
{
    // --- ウィンドウ / バックエンド ---
    public GraphicsBackendKind? Backend { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? Scale { get; set; }
    public bool? Fullscreen { get; set; }

    // --- タイミング ---
    public int? TargetFps { get; set; }
    public bool? VSync { get; set; }
    public bool? MultiThread { get; set; }
    public bool? FixedUpdate { get; set; }
    public double? FixedUpdateHz { get; set; }

    // --- 決定論 ---
    public int? Seed { get; set; }

    // --- 自動化 ---
    /// <summary>N 論理フレームごとにスクリーンショットを撮る。0 で無効。</summary>
    public int ShotEvery { get; set; }
    /// <summary>N 論理フレーム経過したら自動終了する。0 で無効。</summary>
    public long QuitAfterFrames { get; set; }
    /// <summary>N 秒経過したら自動終了する。0 で無効。</summary>
    public double QuitAfterSeconds { get; set; }
    /// <summary>登録済みのセルフテスト計画を自動走行する。</summary>
    public bool SelfTest { get; set; }
    /// <summary>入力を記録するファイルパス。</summary>
    public string? RecordPath { get; set; }
    /// <summary>入力を再生するファイルパス。</summary>
    public string? ReplayPath { get; set; }
    /// <summary>tuning ファイルのパス。既定は tuning.txt。</summary>
    public string? TuningPath { get; set; }
    /// <summary>スクリーンショットやログの出力先ディレクトリ。</summary>
    public string OutputDir { get; set; } = "debugout";

    // --- デバッグ表示 ---
    public bool? Overlay { get; set; }
    public bool? LogOverlay { get; set; }
    public bool? DebugHotkeys { get; set; }

    // --- その他 ---
    public bool ShowHelp { get; set; }
    public List<string> Errors { get; } = [];
    public List<string> Unknown { get; } = [];
    public string[] RawArgs { get; set; } = [];

    public bool HasError => Errors.Count > 0;

    /// <summary>再現性が要求されるモード（セルフテスト・記録・再生）かどうか。</summary>
    public bool DeterministicMode => SelfTest || RecordPath != null || ReplayPath != null;

    /// <summary>何らかの自動化フラグが付いているかどうか。</summary>
    public bool AutomationMode
        => DeterministicMode || ShotEvery > 0 || QuitAfterFrames > 0 || QuitAfterSeconds > 0;
}

/// <summary>
/// コマンドライン引数の解釈と <see cref="GameConfig"/> への反映を行います。
/// </summary>
public static class Startup
{
    public const string Usage = """
        AstrumLoom 共通オプション

          --backend <dxlib|raylib>   使用するグラフィックスバックエンド
          --width <px> --height <px> 論理解像度
          --scale <倍率>             ウィンドウ拡大率
          --fullscreen               フルスクリーンで起動
          --fps <数>                 目標 FPS (0 で無制限)
          --vsync / --no-vsync       垂直同期
          --mt / --no-mt             更新を別スレッドで回すか
          --fixed / --no-fixed       固定ステップ更新
          --hz <数>                  固定ステップの更新周波数 (既定 60)
          --seed <数>                乱数シード

        デバッグ・自動化

          --shot-every <N>           N 論理フレームごとにスクリーンショット
          --quit-after <N>           N 論理フレーム後に自動終了
          --quit-after-sec <秒>      指定秒後に自動終了
          --selftest                 登録済みテスト計画を自動走行して PASS/FAIL を出す
          --record <ファイル>        入力を記録する
          --replay <ファイル>        記録した入力を再生する
          --tuning <ファイル>        tuning ファイルのパス (既定 tuning.txt)
          --out <ディレクトリ>       スクショ・ログの出力先 (既定 debugout)
          --overlay / --no-overlay   デバッグオーバーレイの初期状態
          --no-log-overlay           画面左上のログ表示を消す（スクショを綺麗に撮る用）
          --no-hotkeys               F1〜F5 のデバッグホットキーを無効化
          -h, --help                 このヘルプを表示

        --selftest / --record / --replay を指定すると、再現性のため
        自動的に単一スレッド + 固定ステップ更新に切り替わります。
        """;

    /// <summary>コマンドライン引数を解釈します。例外は投げません。</summary>
    public static LaunchOptions Parse(string[]? args)
    {
        var o = new LaunchOptions { RawArgs = args ?? [] };
        if (args == null || args.Length == 0) return o;

        for (int i = 0; i < args.Length; i++)
        {
            string raw = args[i];
            if (string.IsNullOrWhiteSpace(raw)) continue;
            if (!raw.StartsWith('-')) { o.Unknown.Add(raw); continue; }

            string name = raw.TrimStart('-');
            string? inline = null;
            int eq = name.IndexOf('=');
            if (eq >= 0)
            {
                inline = name[(eq + 1)..];
                name = name[..eq];
            }
            name = name.ToLowerInvariant();

            // 次の引数を値として取り出す。inline (--key=value) があればそちらを優先。
            string? Value()
            {
                if (inline != null) return inline;
                if (i + 1 < args.Length && !IsFlagLike(args[i + 1]))
                    return args[++i];
                o.Errors.Add($"--{name} には値が必要です。");
                return null;
            }

            switch (name)
            {
                case "h" or "help" or "?":
                    o.ShowHelp = true;
                    break;

                case "backend":
                    {
                        string? v = Value();
                        if (v == null) break;
                        if (TryParseBackend(v, out var b)) o.Backend = b;
                        else o.Errors.Add($"バックエンド '{v}' は不明です。dxlib か raylib を指定してください。");
                        break;
                    }
                case "dxlib": o.Backend = GraphicsBackendKind.DxLib; break;
                case "raylib": o.Backend = GraphicsBackendKind.RayLib; break;

                case "width" or "w": o.Width = ParseInt(o, name, Value()); break;
                case "height" or "hgt": o.Height = ParseInt(o, name, Value()); break;
                case "scale": o.Scale = ParseDouble(o, name, Value()); break;
                case "fullscreen": o.Fullscreen = true; break;
                case "windowed" or "no-fullscreen": o.Fullscreen = false; break;

                case "fps": o.TargetFps = ParseInt(o, name, Value()); break;
                case "vsync": o.VSync = true; break;
                case "no-vsync": o.VSync = false; break;
                case "mt" or "multithread": o.MultiThread = true; break;
                case "no-mt" or "single-thread": o.MultiThread = false; break;
                case "fixed": o.FixedUpdate = true; break;
                case "no-fixed": o.FixedUpdate = false; break;
                case "hz": o.FixedUpdateHz = ParseDouble(o, name, Value()); break;
                case "seed": o.Seed = ParseInt(o, name, Value()); break;

                case "shot-every":
                    o.ShotEvery = ParseInt(o, name, Value()) ?? 0;
                    break;
                case "quit-after":
                    o.QuitAfterFrames = ParseLong(o, name, Value()) ?? 0;
                    break;
                case "quit-after-sec" or "quit-after-seconds":
                    o.QuitAfterSeconds = ParseDouble(o, name, Value()) ?? 0;
                    break;
                case "selftest" or "self-test": o.SelfTest = true; break;
                case "record": o.RecordPath = Value(); break;
                case "replay": o.ReplayPath = Value(); break;
                case "tuning": o.TuningPath = Value(); break;
                case "out" or "outdir":
                    {
                        string? v = Value();
                        if (!string.IsNullOrWhiteSpace(v)) o.OutputDir = v;
                        break;
                    }
                case "overlay": o.Overlay = true; break;
                case "no-overlay": o.Overlay = false; break;
                case "no-log-overlay": o.LogOverlay = false; break;
                case "log-overlay": o.LogOverlay = true; break;
                case "no-hotkeys": o.DebugHotkeys = false; break;
                case "hotkeys": o.DebugHotkeys = true; break;

                default:
                    o.Unknown.Add(raw);
                    break;
            }
        }

        if (o.ShotEvery < 0)
        {
            o.Errors.Add("--shot-every には 1 以上を指定してください。");
            o.ShotEvery = 0;
        }
        if (o.RecordPath != null && o.ReplayPath != null)
            o.Errors.Add("--record と --replay は同時に指定できません。");

        return o;
    }

    /// <summary>解釈したオプションを <see cref="GameConfig"/> に反映します。</summary>
    public static GameConfig Apply(this GameConfig config, LaunchOptions o)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(o);

        if (o.Backend.HasValue) config.GraphicsBackend = o.Backend.Value;
        if (o.Width is > 0) config.Width = o.Width.Value;
        if (o.Height is > 0) config.Height = o.Height.Value;
        if (o.Scale is > 0) config.Scale = o.Scale.Value;
        if (o.Fullscreen.HasValue) config.Fullscreen = o.Fullscreen.Value;

        if (o.TargetFps is >= 0) config.TargetFps = o.TargetFps.Value;
        if (o.VSync.HasValue) config.VSync = o.VSync.Value;
        if (o.MultiThread.HasValue) config.UseMultiThreadUpdate = o.MultiThread.Value;
        if (o.FixedUpdate.HasValue) config.FixedUpdate = o.FixedUpdate.Value;
        if (o.FixedUpdateHz is > 0) config.FixedUpdateHz = o.FixedUpdateHz.Value;
        if (o.Seed.HasValue) config.Seed = o.Seed.Value;
        if (o.Overlay.HasValue) config.ShowFpsOverlay = o.Overlay.Value;
        if (o.DebugHotkeys.HasValue) config.EnableDebugHotkeys = o.DebugHotkeys.Value;
        if (o.LogOverlay.HasValue) Log.DrawOnScreen = o.LogOverlay.Value;

        // 再現性が要る場合は、迷わず決定論寄りの設定に倒す。
        if (o.DeterministicMode)
        {
            if (config.UseMultiThreadUpdate && o.MultiThread != true)
            {
                Log.Debug("再現性のため更新スレッドを単一化しました (--mt で上書き可)。");
                config.UseMultiThreadUpdate = false;
            }
            if (!config.FixedUpdate && o.FixedUpdate != false)
            {
                Log.Debug($"再現性のため固定ステップ更新 ({config.FixedUpdateHz}Hz) に切り替えました。");
                config.FixedUpdate = true;
            }
            // 実時間に依存したキャッチアップが入ると再生がずれる。1 ループ 1 ステップに固定する。
            config.LockStep = true;
        }

        return config;
    }

    private static bool IsFlagLike(string s)
        => s.Length > 1 && s[0] == '-' && !char.IsDigit(s[1]) && s[1] != '.';

    public static bool TryParseBackend(string text, out GraphicsBackendKind kind)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "dx" or "dxlib" or "directx": kind = GraphicsBackendKind.DxLib; return true;
            case "ray" or "raylib": kind = GraphicsBackendKind.RayLib; return true;
            default: kind = GraphicsBackendKind.DxLib; return false;
        }
    }

    private static int? ParseInt(LaunchOptions o, string name, string? v)
    {
        if (v == null) return null;
        if (int.TryParse(v, out int r)) return r;
        o.Errors.Add($"--{name} の値 '{v}' を整数として読めません。");
        return null;
    }
    private static long? ParseLong(LaunchOptions o, string name, string? v)
    {
        if (v == null) return null;
        if (long.TryParse(v, out long r)) return r;
        o.Errors.Add($"--{name} の値 '{v}' を整数として読めません。");
        return null;
    }
    private static double? ParseDouble(LaunchOptions o, string name, string? v)
    {
        if (v == null) return null;
        if (double.TryParse(v, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double r)) return r;
        o.Errors.Add($"--{name} の値 '{v}' を数値として読めません。");
        return null;
    }
}

/// <summary>
/// WinExe から親のコンソールへ標準出力を戻すためのヘルパー。
/// PowerShell から起動したときにログが見えるようになります。
/// </summary>
public static class ConsoleBridge
{
    private const int AttachParentProcess = -1;
    private static bool _attached;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetConsoleWindow();

    /// <summary>親プロセスのコンソールに接続します。既に接続済み・非 Windows なら何もしません。</summary>
    public static bool Attach()
    {
        if (_attached) return true;
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            if (GetConsoleWindow() != nint.Zero)
            {
                _attached = true;
                return true;
            }
            if (!AttachConsole(AttachParentProcess)) return false;

            // AttachConsole の後は標準出力を開き直さないと書き込めない。
            // 既定の StreamWriter は BOM なし UTF-8 固定なので、出力先に合わせて選ぶ。
            //   ・パイプ／ファイルへリダイレクト → UTF-8（ツール側で読む前提）
            //   ・本物のコンソール → そのコンソールのコードページ
            //     （ここで Console.OutputEncoding を書き換えると、親シェルの
            //       コードページまで巻き添えで変わってしまうので触らない）
            var utf8 = new System.Text.UTF8Encoding(false);
            var outEnc = Console.IsOutputRedirected ? utf8 : SafeConsoleEncoding(utf8);
            var errEnc = Console.IsErrorRedirected ? utf8 : SafeConsoleEncoding(utf8);

            var stdout = new StreamWriter(Console.OpenStandardOutput(), outEnc) { AutoFlush = true };
            var stderr = new StreamWriter(Console.OpenStandardError(), errEnc) { AutoFlush = true };
            Console.SetOut(stdout);
            Console.SetError(stderr);
            _attached = true;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static System.Text.Encoding SafeConsoleEncoding(System.Text.Encoding fallback)
    {
        try { return Console.OutputEncoding; }
        catch { return fallback; }
    }
}
