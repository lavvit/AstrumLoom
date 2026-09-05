using System.Diagnostics;

namespace AstrumLoom;

/// <summary>アプリ全体のログ出力窓口。コンソール/Trace出力に加え、画面上への一定時間表示とファイル保存を提供する。</summary>
public class Log
{
    // LogMessages は更新スレッド・描画スレッド・バックグラウンドロードなど任意のスレッドから
    // Write される一方、Draw は描画スレッドが毎フレーム列挙する。ロック無しだと
    // 「Collection was modified」で Draw が例外を吐き、そのまま致命エラー画面に落ちる。
    // LogMessages 自体の型（公開 API）は変えず、このクラス内の読み書きを全部この lock で束ねる。
    private static readonly object _sync = new();
    public static List<LogEntry> LogMessages = [];

    /// <summary>
    /// LogMessages に保持しておく最大件数。これを超えたら古いものから捨てる。
    /// 上限が無いと、値を埋め込んだログ（座標・FPS等）を出し続けるゲームでメモリと
    /// Write/Draw の走査コストが際限なく増えていく。
    /// </summary>
    public static int MaxStoredCount = 2000;

    public static void Write(string message, LogLevel level = LogLevel.Info, bool timestamp = false)
    {
        var logEntry = new LogEntry(message, level)
        {
            Timestamped = timestamp
        };
        Console.WriteLine(logEntry.ToFileString());
        if (logEntry.Level != LogLevel.Info) Trace.WriteLine(logEntry.ToFileString());

        var now = DateTime.Now;
        lock (_sync)
        {
            // 直近1秒以内の同一メッセージは重複として弾く。新しいものほど末尾に積まれているので
            // 末尾から見て、1秒より古いものに当たった時点で以降は全部古い＝打ち切ってよい。
            bool duplicate = false;
            for (int i = LogMessages.Count - 1; i >= 0; i--)
            {
                if (now - LogMessages[i].Timestamp >= TimeSpan.FromSeconds(1)) break;
                if (LogMessages[i].Message == message) { duplicate = true; break; }
            }
            if (duplicate) return;

            LogMessages.Add(logEntry);
            if (LogMessages.Count > MaxStoredCount)
                LogMessages.RemoveRange(0, LogMessages.Count - MaxStoredCount);
            _version++;
        }
    }
    public static void Write(string message, bool timestamp) => Write(message, LogLevel.Info, timestamp);
    public static void Write(Exception ex) => Error(ex);
    public static void Warning(string message, bool timestamp = false) => Write(message, LogLevel.Warning, timestamp);
    public static void Error(string message) => Write(message, LogLevel.Error, true);
    public static void Error(Exception ex, string message = "") => Write(message +
        $"{(!string.IsNullOrEmpty(message) ? "\n" : "")}{ex.GetType()}: {ex.Message}\n{ex.StackTrace}", LogLevel.Error, true);
    public static void Debug(string message, bool timestamp = false) => Write(message, LogLevel.Debug, timestamp);
    /// <summary>セルフテストの結果行を記録します。画面には出さず貯めておき、完了時にまとめて表示します。</summary>
    public static void SelfTest(string message, bool timestamp = false) => Write(message, LogLevel.SelfTest, timestamp);
    public static void EmptyLine() => Write("");

    public static void Clear()
    {
        lock (_sync) { LogMessages.Clear(); _version++; }
    }

    public static void Save(string filePath)
    {
        List<LogEntry> snapshot;
        lock (_sync) snapshot = [.. LogMessages];

        if (snapshot.Count == 0)
        {
            Write("No log messages to save.");
            return;
        }
        try
        {
            // filePath が空のときの既定ファイル名生成は、ディレクトリ作成より必ず前に行う。
            // 後ろでやると filePath="" のまま Path.GetDirectoryName に渡ることになり、
            // 常にこちらの分岐より先に下の CreateDirectory("") で落ちて絶対に到達しない。
            if (string.IsNullOrEmpty(filePath))
                filePath = $"Log_{DateTime.Now:yyyyMMdd_HHmmss}.txt";

            // "Log.txt" のような相対ファイル名は GetDirectoryName が空文字列を返す。
            // それを素通しで CreateDirectory に渡すと ArgumentException になるためガードする。
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            Text.Save([.. snapshot.Where(l => l.Level != LogLevel.Debug).Select(l => l.ToString())], filePath);
        }
        catch (Exception ex)
        {
            Write($"Failed to save log: {ex.Message}", LogLevel.Error);
        }
    }

    public static void Print()
    {
        List<LogEntry> snapshot;
        lock (_sync) snapshot = [.. LogMessages];

        foreach (var log in snapshot)
        {
            Console.WriteLine(log.ToString());
        }
    }

    public static bool IncludeInfo = true;
    public static int MaxLogCount = 30;

    /// <summary>画面左上にログを流すか。スクリーンショットを綺麗に撮りたいときは false。</summary>
    public static bool DrawOnScreen = true;

    /// <summary>画面に出しておく秒数。</summary>
    public static double ScreenSeconds = 10;

    /// <summary>
    /// 画面表示に使うフォント。null なら描画バックエンドの組み込みフォント（RayLib なら GetFontDefault）で描く。
    /// ゲーム側の <see cref="Drawing.DefaultFont"/> とは切り離してあるので、装飾付きの重いフォントを
    /// 既定に据えていてもログの描画コストはそれに引きずられない。
    /// なお組み込みフォントは ASCII しか持たないので、日本語ログを読みたいときはここに日本語フォントを入れる。
    /// </summary>
    public static IFont? Font { get; set; }

    /// <summary><see cref="Font"/> が null のときに使う組み込みフォントのサイズ。</summary>
    public static int FontSize { get; set; } = 16;

    // ---- 表示用キャッシュ ----------------------------------------------------
    // Draw は毎フレーム呼ばれるが、ログの中身は滅多に変わらない。毎回 LINQ で絞り込み、
    // 文字列を連結して計測し直すと、それだけでフレーム時間を食う（特に装飾フォントの Measure）。
    // ログの更新（_version）と表示設定が変わったとき、あとは経過秒での間引きのために
    // 一定間隔でだけ組み直し、それ以外のフレームは組み上がった行をそのまま描く。
    private readonly record struct DrawLine(string Text, Color Color, LogLevel Level, int Lines);

    private static long _version;
    private static List<DrawLine> _lines = [];
    private static int _cachedWidth;
    private static int _cachedHeight;
    private static int _cachedLineHeight;
    private static long _cachedVersion = -1;
    private static long _cachedAt = long.MinValue;
    private static (bool info, int max, double seconds, IFont? font, int size) _cachedSetting;

    /// <summary>キャッシュを組み直す間隔（ミリ秒）。経過秒数での消去はこの粒度で反映される。</summary>
    public static int RebuildIntervalMs = 200;

    private static (int width, int height) MeasureText(string text)
        => Font != null ? Font.Measure(text) : Drawing.DefaultTextSize(text, FontSize);

    private static void DrawLineText(double x, double y, string text, Color color)
    {
        if (Font != null) Font.Draw(x, y, text, color);
        else Drawing.DefaultText(x, y, text, color, size: FontSize);
    }

    public static void Draw()
    {
        if (!DrawOnScreen) return;

        long tick = Environment.TickCount64;
        var setting = (IncludeInfo, MaxLogCount, ScreenSeconds, Font, FontSize);
        if (_cachedVersion != Volatile.Read(ref _version)
            || !_cachedSetting.Equals(setting)
            || tick - _cachedAt >= RebuildIntervalMs)
        {
            Rebuild(setting);
            _cachedAt = tick;
        }

        if (_lines.Count == 0) return;

        int x = 10, y = 10;
        int size = _cachedLineHeight;
        Drawing.Box(0, 0, x + _cachedWidth + 10, y + _cachedHeight + 10, Color.Black, opacity: 0.5);

        double pulse = 0.6 + 0.4 * Math.Sin(tick / 180.0);
        int h = 0;
        foreach (var line in _lines)
        {
            // Warning/Error は背景を敷いて目立たせる。Error はさらに脈打たせる。
            if (line.Level == LogLevel.Warning || line.Level == LogLevel.Error)
            {
                double bgOpacity = line.Level == LogLevel.Error ? 0.25 + 0.25 * pulse : 0.25;
                Drawing.Box(x - 4, y + h * size - 2, _cachedWidth + 8, size * line.Lines,
                    line.Level == LogLevel.Error ? Color.Red : Color.Yellow, opacity: bgOpacity);
            }

            DrawLineText(x, y + h * size, line.Text, line.Color);
            h += line.Lines;
        }
    }

    /// <summary>表示する行と、背景帯のサイズを組み直してキャッシュする。</summary>
    private static void Rebuild((bool info, int max, double seconds, IFont? font, int size) setting)
    {
        _cachedVersion = Volatile.Read(ref _version);
        _cachedSetting = setting;
        _lines.Clear();
        _cachedWidth = _cachedHeight = 0;

        var now = DateTime.Now;
        List<LogEntry> loglist;
        lock (_sync)
        {
            loglist = LogMessages
                // セルフテストの結果は貯めておいて完了時にまとめて表示するので、経過秒数での間引きは対象外にする。
                .Where(l => l.Level == LogLevel.SelfTest || (now - l.Timestamp).TotalSeconds < ScreenSeconds)
#if !DEBUG
                .Where(l => l.Level != LogLevel.Debug)
#endif
                .Where(l => IncludeInfo || l.Level != LogLevel.Info)
                .ToList();
        }

        // MaxLogCount が設定されていれば、最新の MaxLogCount 件のみに絞る
        if (MaxLogCount > 0 && loglist.Count > MaxLogCount)
            loglist.RemoveRange(0, loglist.Count - MaxLogCount);
        if (loglist.Count == 0) return;

        _cachedLineHeight = Math.Max(8, MeasureText("Ag").height);

        int total = 0;
        foreach (var log in loglist)
        {
            string prefix = log.Level switch
            {
                LogLevel.Warning => " ! ",
                LogLevel.Error => "!! ",
                _ => ""
            };
            string text = prefix + log;
            int lines = 1;
            for (int i = 0; i < log.Message.Length; i++)
                if (log.Message[i] == '\n') lines++;

            _lines.Add(new DrawLine(text, log.Color, log.Level, lines));
            _cachedWidth = Math.Max(_cachedWidth, MeasureText(text).width);
            total += lines;
        }
        _cachedHeight = _cachedLineHeight * total;
    }
}

/// <summary>1件分のログ情報。表示用/保存用の文字列整形と、レベルに応じた表示色を持つ。</summary>
public class LogEntry(string message, LogLevel level)
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public LogLevel Level { get; set; } = level;
    public string Message { get; set; } = message;

    public bool Timestamped { get; set; } = true;

    public string ToFileString() => string.IsNullOrEmpty(Message)
            ? ""
            : $"{(Timestamped ? $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] " : "")}" +
            $"{(Level > LogLevel.Info ? $"[{Level}] " : "")}{Message}";

    public override string ToString() => string.IsNullOrEmpty(Message) ? "" : $"[{Timestamp:HH:mm:ss}] [{Level}] {Message}";

    public Color Color => Level switch
    {
        LogLevel.Info => Color.SkyBlue,
        LogLevel.Warning => Color.Yellow,
        LogLevel.Error => Color.Red,
        LogLevel.Debug => Color.Silver,
        LogLevel.SelfTest => Color.Lime,
        _ => Color.White,
    };
}

public enum LogLevel
{
    Info,
    Warning,
    Error,
    Debug,
    SelfTest
}
