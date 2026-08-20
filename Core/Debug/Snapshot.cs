using System.Collections.Concurrent;

namespace AstrumLoom;

/// <summary>
/// スクリーンショットの撮影を要求し、描画スレッドで実際の保存を行う仕組み。
/// 要求はどのスレッドからでも出せますが、保存は必ず描画フレームの中で行われます。
/// </summary>
public static class Snapshot
{
    /// <summary>撮り溜まりすぎを防ぐための保留上限。</summary>
    private const int MaxPending = 8;

    /// <summary>要求されたスクショ 1 件。フレーム番号は「要求した時点」のものを持ち回る。</summary>
    private readonly record struct ShotRequest(string? Name, long Frame);

    private static readonly ConcurrentQueue<ShotRequest> _pending = new();

    /// <summary>保存先ディレクトリ。相対パスならアプリケーションディレクトリ基準。</summary>
    public static string Directory { get; set; } = "debugout";

    /// <summary>これまでに保存できた枚数。</summary>
    public static int Saved { get; private set; }

    /// <summary>直近に保存したファイルのパス。</summary>
    public static string? LastPath { get; private set; }

    /// <summary>保留中の要求数。</summary>
    public static int Pending => _pending.Count;

    /// <summary>
    /// スクリーンショットを 1 枚要求します。実際の保存は次の描画フレームで行われます。
    /// </summary>
    /// <param name="name">ファイル名に含める識別子。null なら連番のみ。</param>
    public static void Request(string? name = null)
    {
        if (_pending.Count >= MaxPending)
        {
            // 描画が追いつかないほど要求が溜まっている。取りこぼしを黙って捨てない。
            Log.Warning($"スクリーンショットの要求が溜まりすぎたため破棄しました ({name ?? "(無名)"})。");
            return;
        }
        // 保存は次の描画フレームなので、そのときには FrameCount が進んでいる。
        // ファイル名は「どのフレームを撮りたかったか」を指すべきなので、ここで固定する。
        _pending.Enqueue(new ShotRequest(name, AstrumCore.FrameCount));
    }

    /// <summary>保留中の要求を 1 件処理します。描画フレームの中（EndFrame の直前）から呼びます。</summary>
    internal static void Service(IGraphics? graphics)
    {
        if (graphics == null) return;
        if (!_pending.TryDequeue(out var request)) return;

        string? name = request.Name;
        string path = BuildPath(name, request.Frame);
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);

            if (graphics.SaveScreenshot(path))
            {
                Saved++;
                LastPath = path;
                Log.Debug($"スクリーンショット: {AstrumCore.FilePath(path)}");
            }
            else
            {
                Log.Warning($"このバックエンドはスクリーンショットに対応していません ({AstrumCore.Platform?.BackendKind}).");
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"スクリーンショットの保存に失敗しました: {ex.Message}");
        }
    }

    /// <summary>保存先のフルパスを組み立てます。</summary>
    /// <param name="name">ファイル名に含める識別子。</param>
    /// <param name="frame">ファイル名に入れる論理フレーム番号。null なら現在値。</param>
    public static string BuildPath(string? name, long? frame = null)
    {
        string dir = Directory;
        if (!Path.IsPathRooted(dir)) dir = Path.Combine(AstrumCore.AppPath, dir);

        long f = frame ?? AstrumCore.FrameCount;
        string safe = Sanitize(name);
        string file = string.IsNullOrEmpty(safe)
            ? $"shot_{f:D6}.png"
            : $"shot_{f:D6}_{safe}.png";
        return Path.Combine(dir, file);
    }

    /// <summary>ファイル名に使えない文字と空白をアンダースコアに置き換えます。</summary>
    private static string Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Trim()
            .Select(c => invalid.Contains(c) || c == ' ' ? '_' : c)
            .ToArray();
        return new string(chars);
    }
}
