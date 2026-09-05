using System.Diagnostics;

namespace AstrumLoom.RayLib;

/// <summary>
/// ffmpeg / ffprobe の実行ファイルを探して起動するためのヘルパ。
/// RayLib プロジェクトに NuGet 依存を増やさないため、ラッパーライブラリは使わず
/// 素の <see cref="Process"/> で呼ぶ（docs\MOVIE.md の N-6）。
/// </summary>
public static class FFmpegTool
{
    private static readonly object _lock = new();
    private static bool _searched;
    private static string? _ffmpeg;
    private static string? _ffprobe;
    private static bool _warned;

    /// <summary>
    /// ffmpeg.exe / ffprobe.exe が入ったフォルダを明示指定する。設定すると探索結果を捨てて次回に探し直す。
    /// </summary>
    public static string? Directory
    {
        get;
        set
        {
            lock (_lock)
            {
                field = value;
                _searched = false;
                _warned = false;
            }
        }
    }

    /// <summary>ffmpeg.exe のフルパス。見つからなければ null。</summary>
    public static string? FFmpegPath { get { Search(); return _ffmpeg; } }

    /// <summary>ffprobe.exe のフルパス。見つからなければ null。</summary>
    public static string? FFprobePath { get { Search(); return _ffprobe; } }

    /// <summary>ffmpeg と ffprobe の両方が使えるか。</summary>
    public static bool Available => FFmpegPath != null && FFprobePath != null;

    /// <summary>
    /// ffmpeg が無いことを 1 度だけログに出す。動画を何本も読むゲームでログが埋まらないようにするため、
    /// 2 回目以降は黙る。
    /// </summary>
    public static void WarnMissing()
    {
        lock (_lock)
        {
            if (_warned) return;
            _warned = true;
        }
        Log.Error("Movie: ffmpeg が見つかりません。`winget install Gyan.FFmpeg` で導入するか、"
            + "環境変数 ASTRUMLOOM_FFMPEG に ffmpeg.exe のパスを設定してください。");
    }

    /// <summary>docs\MOVIE.md 1.5 の順で ffmpeg/ffprobe を探す。結果はプロセス内でキャッシュする。</summary>
    private static void Search()
    {
        lock (_lock)
        {
            if (_searched) return;
            _searched = true;
            _ffmpeg = null;
            _ffprobe = null;

            foreach (string dir in Candidates())
            {
                string ffmpeg = System.IO.Path.Combine(dir, ExeName("ffmpeg"));
                string ffprobe = System.IO.Path.Combine(dir, ExeName("ffprobe"));
                if (File.Exists(ffmpeg) && File.Exists(ffprobe))
                {
                    _ffmpeg = ffmpeg;
                    _ffprobe = ffprobe;
                    Log.Debug($"Movie: ffmpeg found: {dir}");
                    return;
                }
            }
        }
    }

    private static string ExeName(string name)
        => OperatingSystem.IsWindows() ? name + ".exe" : name;

    /// <summary>探索候補のフォルダを順に返す。存在しないフォルダも混ざるが、呼び側で File.Exists するので害はない。</summary>
    private static IEnumerable<string> Candidates()
    {
        // 1. 明示指定
        if (!string.IsNullOrWhiteSpace(Directory))
        {
            yield return Directory!;
        }

        // 2. 環境変数（実行ファイルそのものを指していてもよい）
        string? env = Environment.GetEnvironmentVariable("ASTRUMLOOM_FFMPEG");
        if (!string.IsNullOrWhiteSpace(env))
        {
            yield return File.Exists(env)
                ? System.IO.Path.GetDirectoryName(env) ?? env
                : env;
        }

        // 3. 実行ファイルと同じ場所に置かれた同梱物
        string baseDir = AppContext.BaseDirectory;
        yield return System.IO.Path.Combine(baseDir, "ffmpeg", "bin");
        yield return System.IO.Path.Combine(baseDir, "ffmpeg");
        yield return System.IO.Path.Combine(baseDir, "tools", "ffmpeg", "bin");
        yield return baseDir;

        // 4. PATH
        string path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string dir in path.Split(System.IO.Path.PathSeparator))
        {
            if (!string.IsNullOrWhiteSpace(dir)) yield return dir.Trim('"');
        }

        // 5. winget 版（PATH へ通す前でも動くように）
        foreach (string dir in WinGetDirectories()) yield return dir;
    }

    /// <summary>winget で入れた Gyan.FFmpeg の bin フォルダを探す（PATH の反映は再ログインまで効かないことがあるため）。</summary>
    private static IEnumerable<string> WinGetDirectories()
    {
        if (!OperatingSystem.IsWindows()) yield break;

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string root = System.IO.Path.Combine(local, "Microsoft", "WinGet", "Packages");
        string[] found;
        try
        {
            if (!System.IO.Directory.Exists(root)) yield break;
            // Gyan.FFmpeg_.../ffmpeg-x.y.z-full_build/bin のように 1 段深いので再帰で拾う。
            found = System.IO.Directory.GetDirectories(root, "bin", SearchOption.AllDirectories);
        }
        catch { yield break; }

        foreach (string dir in found)
        {
            if (dir.Contains("FFmpeg", StringComparison.OrdinalIgnoreCase)) yield return dir;
        }
    }

    /// <summary>ffmpeg/ffprobe を起動するための ProcessStartInfo を作る（標準出力/標準エラーはリダイレクト済み）。</summary>
    public static ProcessStartInfo? StartInfo(string exePath, IEnumerable<string> args)
    {
        if (exePath == null) return null;
        var psi = new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    /// <summary>ffprobe を同期実行して標準出力を返す。失敗時は null。</summary>
    public static string? RunProbe(IEnumerable<string> args, int timeoutMs = 15000)
    {
        string? exe = FFprobePath;
        if (exe == null) return null;

        var psi = StartInfo(exe, args)!;
        try
        {
            using var proc = Process.Start(psi);
            if (proc == null) return null;
            string output = proc.StandardOutput.ReadToEnd();
            // 標準エラーを読み捨てないとバッファが詰まって ffprobe が止まる。
            _ = proc.StandardError.ReadToEnd();
            if (!proc.WaitForExit(timeoutMs))
            {
                try { proc.Kill(true); } catch { }
                return null;
            }
            return proc.ExitCode == 0 ? output : null;
        }
        catch (Exception ex)
        {
            Log.Debug($"Movie: ffprobe failed: {ex.Message}");
            return null;
        }
    }
}
