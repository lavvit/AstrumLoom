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
        }
    }
    public static void Write(string message, bool timestamp) => Write(message, LogLevel.Info, timestamp);
    public static void Write(Exception ex) => Error(ex);
    public static void Warning(string message, bool timestamp = false) => Write(message, LogLevel.Warning, timestamp);
    public static void Error(string message) => Write(message, LogLevel.Error, true);
    public static void Error(Exception ex, string message = "") => Write(message +
        $"{(!string.IsNullOrEmpty(message) ? "\n" : "")}{ex.GetType()}: {ex.Message}\n{ex.StackTrace}", LogLevel.Error, true);
    public static void Debug(string message, bool timestamp = false) => Write(message, LogLevel.Debug, timestamp);
    public static void EmptyLine() => Write("");

    public static void Clear()
    {
        lock (_sync) LogMessages.Clear();
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

    public static void Draw()
    {
        if (!DrawOnScreen) return;

        int x = 10, y = 10;
        var now = DateTime.Now;
        List<LogEntry> loglist;
        lock (_sync)
        {
            loglist = LogMessages
                .Where(l => (now - l.Timestamp).TotalSeconds < ScreenSeconds)
#if !DEBUG
                .Where(l => l.Level != LogLevel.Debug)
#endif
                .Where(l => IncludeInfo || l.Level != LogLevel.Info)
                .ToList();
        }

        // MaxLogCount が設定されていれば、最新の MaxLogCount 件のみに絞る
        if (MaxLogCount > 0 && loglist.Count > MaxLogCount)
        {
            int skip = Math.Max(0, loglist.Count - MaxLogCount);
            loglist = [.. loglist.Skip(skip)];
        }
        if (loglist.Count == 0) return;

        int size = Drawing.FontSize();
        string[] prefixes = [.. loglist.Select(l => l.Level switch
        {
            LogLevel.Warning => " ! ",
            LogLevel.Error =>   "!! ",
            _ => ""
        })];
        int width = Drawing.TextSize(string.Join("\n", loglist.Select((l, i) => prefixes[i] + l)).Trim()).width;
        int height = size * logCount(loglist);
        Drawing.Box(0, 0, x + width + 10, y + height + 10, Color.Black, opacity: 0.5);

        double pulse = 0.6 + 0.4 * Math.Sin(Environment.TickCount64 / 180.0);
        int h = 0;
        for (int i = 0; i < loglist.Count; i++)
        {
            var log = loglist[i];
            int lines = log.Message.Split('\n').Length;

            // Warning/Error は背景を敷いて目立たせる。Error はさらに脈打たせる。
            if (log.Level == LogLevel.Warning || log.Level == LogLevel.Error)
            {
                double bgOpacity = log.Level == LogLevel.Error ? 0.25 + 0.25 * pulse : 0.25;
                Drawing.Box(x - 4, y + h * size - 2, width + 8, size * lines, log.Color, opacity: bgOpacity);
            }

            Drawing.Text(x, y + h * size, prefixes[i] + log, log.Color);
            h += lines;
        }
    }

    private static int logCount(List<LogEntry> logs) => logs.Sum(l => l.Message.Split('\n').Length);
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
        _ => Color.White,
    };
}

public enum LogLevel
{
    Info,
    Warning,
    Error,
    Debug
}
