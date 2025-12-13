using System.Reflection;

namespace AstrumLoom;

internal class BaseProgram : IGame
{
    private Scene _scene => Scene.NowScene;
    public BaseProgram() { }
    public void Initialize() { }
    public void Update(float deltaTime)
    {
        _scene?.Update();

        bool drop = AstrumCore.IsDroppable;
        AstrumCore.Platform.SetDragDrop(drop);
        if (!drop) return;
        string[] files = AstrumCore.Platform.DropFiles;
        if (files.Length > 0)
            foreach (string f in files)
                _scene?.Drag(f);
    }
    public void Draw()
    {
        _scene?.Draw();
        _scene?.Debug();
    }
}
public class AstrumCore
{
    private static string _Version => "0.1.0-alpha";
    public static int MainThreadId { get; internal set; }
    public static IGamePlatform Platform { get; internal set; } = null!;
    public static IGraphics? Graphic => Platform?.Graphics;

    public static GameConfig WindowConfig { get; private set; } = null!;
    public static int Width => (int)Platform.Graphics.Size.Width;
    public static int Height => (int)Platform.Graphics.Size.Height;

    public static void Boot(GameConfig config, IGamePlatform platform, Scene scene)
    {
        MainThreadId = Environment.CurrentManagedThreadId;
        Platform = platform;
        WindowConfig = config;
        var game = new BaseProgram();

        using var host = new GameHost(config, platform, game);
        Scene.Set(scene);
        host.Run();
    }
    public static void End() => Platform.Close();
    public static FpsCounter DrawFPS = new();
    public static FpsCounter UpdateFPS = new();
    private static FatalErrorInfo? _fatalError;
    public static FatalErrorInfo? FatalError => Interlocked.CompareExchange(ref _fatalError, null, null);
    public static bool HasFatalError => FatalError != null;

    internal static void ReportFatalError(string phase, Exception exception)
    {
        var info = new FatalErrorInfo(phase, exception);
        if (Interlocked.CompareExchange(ref _fatalError, info, null) == null)
        {
            Log.EmptyLine();
            Log.Error($"{phase} にてエラーが発生しました。ごめんねなの！" +
                $"\n{exception.GetType()}: {exception.Message}" +
                $"\n発生時刻: {info.Timestamp:yyyy-MM-dd HH:mm:ss}" +
                $"\nスタックトレース: \n{info.StackTrace}");
        }
    }

    #region ドラッグ＆ドロップ
    // ドラッグ＆ドロップを受け付けるかどうかを、一時的に有効化するためのカウンタとヘルパー
    private static int _dropCounter = 0;
    /// <summary>
    /// 現在ドラッグ＆ドロップを受け付ける状態かどうか
    /// </summary>
    public static bool IsDroppable => _dropCounter > 0;

    /// <summary>
    /// usingで囲んだ間だけドラッグ＆ドロップを受け付けるスコープを返します。
    /// 例: using (DXLib.Droppable()) { /* Update/Draw内で処理する */ }
    /// </summary>
    public static void Droppable() => Interlocked.Increment(ref _dropCounter);

    internal static void InitDrop() => _dropCounter = 0;
    #endregion

    #region アプリケーションパス関連
    public static string AppPath => AppContext.BaseDirectory ?? "C:\\";
    public static string FilePath(string path) => Path.GetRelativePath(AppPath, Path.GetFullPath(path));
    /// <summary>アプリケーションディレクトリおよびそのサブディレクトリ内でファイル名を検索し、
    /// 必要に応じて指定フォルダで絞り込んで、最も適した一致の相対パスを返します。該当がなければフォールバックのパスを返します。</summary>
    /// <remarks>複数の一致が見つかった場合は、アプリケーションルートに最も近い（階層が浅い）ディレクトリにあるファイルを選びます。
    /// 例外は投げず、エラーや一致なしの場合は引数に基づくフォールバックパスを返します。本メソッドはスレッドセーフです。</remarks>
    /// <param name="path">検索するファイルパスまたはファイル名。既存のファイルパスが渡された場合はそのまま返します。
    /// ファイル名のみが渡された場合はアプリケーションディレクトリ内を検索します。</param>
    /// <param name="searchfolder">検索を特定のサブディレクトリに限定するためのフォルダ名配列（省略可）。
    /// 指定されたフォルダに存在するファイルのみを候補とします。null または空配列の場合はすべてのサブディレクトリを検索します。</param>
    /// <returns>検索条件に最も合致するファイルの相対パス。該当がない場合は引数に基づくフォールバックパスを返します。</returns>
    public static string SearchPath(string path, string[]? searchfolder = null)
    {
        try
        {
            // 1. 既存パスチェック
            if (File.Exists(path))
                return FilePath(path);

            // ファイル名を取得（パスで渡された場合にも対応）
            string fileName = Path.GetFileName(path);
            if (string.IsNullOrEmpty(fileName))
                // 無効なパスだった場合はそのまま返す
                return path;

            // 2. AppPath配下から同名ファイルを検索
            string appPathFull = Path.GetFullPath(AppPath);
            var foundFiles = Directory.GetFiles(appPathFull, fileName, SearchOption.AllDirectories).ToList();

            // 3. 見つからなければフォールバックパスを返す
            if (foundFiles.Count == 0)
                return FilePath(Path.Combine(AppPath, path));

            // 4. searchfolderが指定されている場合はフィルタリング
            if (searchfolder != null && searchfolder.Length > 0)
            {

                var loweredSearch = new HashSet<string>(searchfolder.Select(s => Path.Combine(AppPath, s)), StringComparer.OrdinalIgnoreCase);
                var filtered = foundFiles.Where(f =>
                {
                    string dir = Path.GetDirectoryName(f) ?? "";
                    return loweredSearch.Contains(dir);
                }).ToList();

                if (filtered.Count > 0)
                    foundFiles = filtered;
                // 指定にマッチするものが無ければ
                else return FilePath(Path.Combine(AppPath, path));
            }

            // 5. 最も浅いものを選択（AppPathからの相対パスのセグメント数で判定）
            string best = foundFiles
                .OrderBy(f =>
                {
                    string dir = Path.GetDirectoryName(f) ?? "";
                    string rel = Path.GetRelativePath(appPathFull, dir);
                    return string.IsNullOrEmpty(rel) || rel == "."
                        ? 0
                        : rel.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries).Length;
                })
                .First();

            return FilePath(best);
        }
        catch
        {
            // 失敗時は元の引数を基にしたパスを返す（例外はログなど外側で処理される想定）
            return FilePath(Path.Combine(AppPath, path));
        }
    }
    #endregion

    public static double FPS => Platform.SystemFPS ?? DrawFPS.GetFPS();
    internal static (double draw, double update) NowFPSValue
        => (DrawFPS.GetFPS(0.2), UpdateFPS.GetFPS(0.2));
    public static string NowFPS
    {
        get
        {
            (double d, double u) = NowFPSValue;
            return MultiThreading ?
                $"Draw: {d:0.#}\nUpdate: {u:0.#}" :
                $"FPS: {d:0.#}";
        }
    }

    public static string Version
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v == null ? _Version : $"{v.Major}.{v.Minor}.{v.Build} / {_Version}";
        }
    }

    public static bool Active => Platform.IsActive;

    public static bool InitCompleted
    { get; internal set; }

    public static double WindowScale => WindowConfig.Scale;
    public static string Title => WindowConfig.Title;
    public static bool VSync
    {
        get => WindowConfig.VSync;
        set
        {
            WindowConfig.VSync = value;
            // DXLib など、プラットフォーム側で VSync を制御する場合は反映させる
            Platform.SetVSync(value);
        }
    }
    public static bool MultiThreading => WindowConfig.UseMultiThreadUpdate;

    #region メインスレッド破棄キュー
    // 他スレッドからメインスレッドに依存するリソースの破棄を依頼するためのキュー
    private static readonly System.Collections.Concurrent.ConcurrentQueue<IDisposable> _disposeQueue = new();

    /// <summary>
    /// メインスレッドでの破棄が必要なリソースを登録します。
    /// 例: Texture/Sound など、プラットフォームがメインスレッド要求を持つもの。
    /// </summary>
    public static void RequestDispose(IDisposable disposable)
    {
        if (disposable == null) return;
        _disposeQueue.Enqueue(disposable);
    }

    /// <summary>
    /// メインスレッドで保留中の破棄を処理します。ゲームループ内から呼び出されます。
    /// </summary>
    public static void ProcessPendingDisposals()
    {
        // メインスレッドでのみ処理
        if (Environment.CurrentManagedThreadId != MainThreadId) return;

        while (_disposeQueue.TryDequeue(out var d))
        {
            try { d.Dispose(); }
            catch { /* 破棄失敗は握りつぶす（ログは各プラットフォーム側で対応）*/ }
            finally { }
        }
    }
    #endregion

    public static void AddExtendAction(string key, Action action, bool inEndStart = true)
        => GameRunner.AddExtendAction(key, action, inEndStart);
}

public class Sleep
{
    private static long SleepDuration => AstrumCore.WindowConfig.SleepDurationMs;
    private static bool _vsync => AstrumCore.VSync;
    private static long _lastWakeTime = 0;
    public static bool Sleeping { get; private set; } = false;
    public static void Update()
    {
        long ms = SleepDuration;
        // minute分以上スリープしている場合も垂直同期
        long last = _lastWakeTime;
        long now = Environment.TickCount64;
        if (now - last > ms)
        {
            if (!_vsync)
            {
                if (!Sleeping)
                {
                    Sleeping = true;
                }
                AstrumCore.Platform.SetVSync(true);
                return;
            }
        }
        else
        {
            if (!_vsync && Sleeping)
                Sleeping = false;
        }
        AstrumCore.Platform.SetVSync(_vsync);
    }
    public static void WakeUp()
    {
        if (!AstrumCore.Active) return;
        _lastWakeTime = Environment.TickCount64;
    }
}

public sealed class FatalErrorInfo
{
    public FatalErrorInfo(string phase, Exception exception)
    {
        Phase = phase;
        ExceptionType = exception.GetType().Name;
        Message = exception.Message;
        StackTrace = FormatStackTrace(exception.StackTrace);
        Timestamp = DateTime.Now;
        Details = FormatStackTrace(exception.ToString())
            .Split(['\r', '\n']);
    }

    public string Phase { get; }
    public string ExceptionType { get; }
    public string Message { get; }
    public string StackTrace { get; }
    public DateTime Timestamp { get; }
    public string[] Details { get; }

    private static string FormatStackTrace(string? stackTrace)
    {
        if (string.IsNullOrWhiteSpace(stackTrace))
            return string.Empty;

        const string separator = " in ";
        string[] lines = stackTrace.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var formatted = new List<string>(lines.Length * 2);

        foreach (string rawLine in lines)
        {
            if (string.IsNullOrEmpty(rawLine))
            {
                formatted.Add(string.Empty);
                continue;
            }

            int index = rawLine.IndexOf(separator, StringComparison.Ordinal);
            if (index < 0)
            {
                formatted.Add(rawLine);
                continue;
            }

            string before = rawLine[..index];
            string after = NormalizeStackTraceLocation(rawLine[(index + separator.Length)..]);
            formatted.Add(before);

            int indentLength = before.Length - before.TrimStart().Length;
            string indent = indentLength > 0 ? before[..indentLength] : string.Empty;
            formatted.Add($"{indent}    in {after}");
        }

        return string.Join('\n', formatted);
    }

    private static string NormalizeStackTraceLocation(string location)
    {
        if (string.IsNullOrWhiteSpace(location))
            return string.Empty;

        const string lineMarker = ":line ";
        string trimmed = location.TrimStart();
        int lineIndex = trimmed.IndexOf(lineMarker, StringComparison.Ordinal);
        string lineSuffix = lineIndex >= 0 ? trimmed[lineIndex..] : string.Empty;
        string filePart = lineIndex >= 0 ? trimmed[..lineIndex] : trimmed;

        string normalized = filePart;
        try
        {
            if (!string.IsNullOrWhiteSpace(filePart))
                normalized = AstrumCore.FilePath(filePart.Trim());
        }
        catch
        {
            normalized = filePart;
        }

        return string.IsNullOrEmpty(lineSuffix)
            ? normalized
            : $"{normalized}{lineSuffix}";
    }
}