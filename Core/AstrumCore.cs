using System.Reflection;

namespace AstrumLoom;

internal class BaseProgram : IGame
{
    private readonly IGamePlatform _platform;
    private Scene _scene => Scene.NowScene;
    public BaseProgram(IGamePlatform platform) => _platform = platform;
    public void Initialize() { }
    public void Update(float deltaTime) => _scene?.Update();
    public void Draw() => _scene?.Draw();
}
public class AstrumCore
{
    private static string _Version => "0.1.0-alpha";
    public static int MainThreadId { get; internal set; }
    public static IGamePlatform Platform { get; internal set; } = null!;
    public static IGraphics Graphic => Platform.Graphics;

    public static GameConfig WindowConfig { get; private set; } = null!;
    public static int Width => (int)Platform.Graphics.Size.Width;
    public static int Height => (int)Platform.Graphics.Size.Height;

    public static void Boot(GameConfig config, IGamePlatform platform)
    {
        MainThreadId = Environment.CurrentManagedThreadId;
        Platform = platform;
        WindowConfig = config;
        var game = new BaseProgram(platform);

        using var host = new GameHost(config, platform, game);
        host.Run();
    }
    public static void End() => Platform.Close();
    public static FpsCounter DrawFPS = new();
    public static FpsCounter UpdateFPS = new();

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

    public static string NowFPS
    {
        get
        {
            double d = DrawFPS.GetFPS();
            double u = UpdateFPS.GetFPS();
            double ratio = d > u ? d / u : u / d;
            return ratio > 1.2 ? $"Draw: {d:0.0}\nUpdate: {u:0.0}" : $"FPS: {DrawFPS.GetFPS(0.3):0.0}";
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

    public static bool Active
    {
        get; internal set;
        //get => Platform.IsActive;
    } = true;

    public static bool InitCompleted
        { get; internal set; }

    public static double WindowScale => WindowConfig.Scale;
}