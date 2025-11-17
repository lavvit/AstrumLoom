using System.Diagnostics;

namespace AstrumLoom;


public class Log
{
    public static List<LogEntry> LogMessages = [];

    public static void Write(string message, LogLevel level = LogLevel.Info, bool timestamp = false)
    {
        var logEntry = new LogEntry(message, level)
        {
            Timestamped = timestamp
        };
        Console.WriteLine(logEntry.ToFileString());
        if (logEntry.Level != LogLevel.Info) Trace.WriteLine(logEntry.ToFileString());
        LogMessages.Add(logEntry);
    }
    public static void Write(string message, bool timestamp) => Write(message, LogLevel.Info, timestamp);
    public static void Write(Exception ex) => Error($"{ex.GetType()}: {ex.Message}\n{ex.StackTrace}");
    public static void Warning(string message, bool timestamp = false) => Write(message, LogLevel.Warning, timestamp);
    public static void Error(string message) => Write(message, LogLevel.Error, true);
    public static void Debug(string message, bool timestamp = false) => Write(message, LogLevel.Debug, timestamp);
    public static void EmptyLine() => Write("");


    public static void Clear() => LogMessages.Clear();
    public static void Save(string filePath)
    {
        if (LogMessages.Count == 0)
        {
            Write("No log messages to save.");
            return;
        }
        if (!Directory.Exists(Path.GetDirectoryName(filePath)))
            Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? "");
        if (string.IsNullOrEmpty(filePath))
        {
            filePath = $"Log_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
        }
        try
        {
            Text.Save([.. LogMessages.Where(l => l.Level != LogLevel.Debug).Select(l => l.ToString())], filePath);
        }
        catch (Exception ex)
        {
            Write($"Failed to save log: {ex.Message}", LogLevel.Error);
        }
    }

    public static void Print()
    {
        foreach (var log in LogMessages)
        {
            Console.WriteLine(log.ToString());
        }
    }

    public static bool IncludeInfo = true;
    public static int MaxLogCount = 30;
    public static void Draw(IGamePlatform platform)
    {
        var g = platform.Graphics;
        int x = 10, y = 10;
        var loglist = LogMessages
            .Where(l => (DateTime.Now - l.Timestamp).TotalSeconds < 10)
#if !DEBUG
            .Where(l => l.Level != LogLevel.Debug)
#endif
            .Where(l => IncludeInfo || l.Level != LogLevel.Info)
            .ToList();

        // MaxLogCount が設定されていれば、最新の MaxLogCount 件のみに絞る
        if (MaxLogCount > 0 && loglist.Count > MaxLogCount)
        {
            int skip = Math.Max(0, loglist.Count - MaxLogCount);
            loglist = [.. loglist.Skip(skip)];
        }
        if (loglist.Count == 0) return;

        (int width, int height) = g.MeasureText(string.Join("\n", loglist).Trim());
        int size = 16;
        g.Box(0, 0, x + width + 10, y + height + 10, Color.Black, opacity: 0.5);
        for (int i = 0; i < loglist.Count; i++)
        {
            var log = loglist[i];
            g.Text(x, y + i * size, log, log.Color);
        }
    }
}

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
