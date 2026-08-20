using System.Globalization;

namespace AstrumLoom;

/// <summary>
/// テキストファイルからパラメータを読み、実行中の書き換えを自動で反映する調整用ストア。
/// <code>
/// double speed = Tune.Get("player.speed", 4.5);
/// </code>
/// 参照したキーは既定値つきで登録され、<see cref="Save"/> で雛形ファイルを書き出せます。
/// </summary>
public static class Tune
{
    private const double PollIntervalSeconds = 0.25;

    private static readonly object _sync = new();
    private static readonly Dictionary<string, string> _defaults = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>ファイル由来の値。読み込みのたびに丸ごと差し替えるので、読む側はロック不要。</summary>
    private static Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    private static string _path = "tuning.txt";
    private static DateTime _lastWrite = DateTime.MinValue;
    private static long _lastPollTicks;
    private static bool _missingLogged;

    /// <summary>tuning ファイルのパス。設定すると次の <see cref="Poll"/> で読み直します。</summary>
    public static string FilePath
    {
        get => _path;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            lock (_sync)
            {
                _path = value;
                _lastWrite = DateTime.MinValue;
                _lastPollTicks = 0;
                _missingLogged = false;
            }
        }
    }

    /// <summary>ファイルが読み込まれた回数。0 なら一度も読めていません。</summary>
    public static int LoadCount { get; private set; }

    /// <summary>現在有効なキーの数。</summary>
    public static int Count => _values.Count;

    /// <summary>参照されたことのある全キーと、その現在値。</summary>
    public static IReadOnlyDictionary<string, string> Snapshot()
    {
        lock (_sync)
        {
            var result = new Dictionary<string, string>(_defaults, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _values) result[kv.Key] = kv.Value;
            return result;
        }
    }

    #region 取得

    public static double Get(string key, double fallback)
        => TryRaw(key, fallback.ToString("R", CultureInfo.InvariantCulture), out string raw)
            && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
            ? v : fallback;

    public static float Get(string key, float fallback)
        => (float)Get(key, (double)fallback);

    public static int Get(string key, int fallback)
        => TryRaw(key, fallback.ToString(CultureInfo.InvariantCulture), out string raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
            ? v : fallback;

    public static bool Get(string key, bool fallback)
    {
        if (!TryRaw(key, fallback ? "true" : "false", out string raw)) return fallback;
        return raw.ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => fallback,
        };
    }

    public static string Get(string key, string fallback)
        => TryRaw(key, fallback, out string raw) ? raw : fallback;

    /// <summary>生の文字列を取り出しつつ、既定値を登録します。</summary>
    private static bool TryRaw(string key, string defaultText, out string raw)
    {
        raw = "";
        if (string.IsNullOrWhiteSpace(key)) return false;

        // 既定値の登録（初回のみ）。読み取りは lock の外で行いたいので分ける。
        if (!_defaults.ContainsKey(key))
        {
            lock (_sync) _defaults.TryAdd(key, defaultText);
        }

        // _values は差し替え専用なので、参照を1回掴めばロックなしで安全。
        return _values.TryGetValue(key, out raw!) && raw != null;
    }

    #endregion

    /// <summary>
    /// ファイルの更新を確認し、変わっていれば読み直します。ゲームループから毎フレーム呼んで構いません。
    /// </summary>
    public static void Poll(bool force = false)
    {
        long now = Environment.TickCount64;
        if (!force && now - _lastPollTicks < PollIntervalSeconds * 1000) return;
        _lastPollTicks = now;

        string path = _path;
        try
        {
            string full = Path.IsPathRooted(path) ? path : Path.Combine(AstrumCore.AppPath, path);
            if (!File.Exists(full))
            {
                if (!_missingLogged)
                {
                    _missingLogged = true;
                    Log.Debug($"tuning ファイルがありません: {AstrumCore.FilePath(full)}");
                }
                return;
            }
            _missingLogged = false;

            var stamp = File.GetLastWriteTimeUtc(full);
            if (!force && stamp == _lastWrite) return;
            _lastWrite = stamp;

            var parsed = Parse(File.ReadAllLines(full));
            lock (_sync)
            {
                _values = parsed;
                LoadCount++;
            }
            Log.Debug($"tuning 読み込み: {AstrumCore.FilePath(full)} ({parsed.Count} 件)");
        }
        catch (IOException)
        {
            // エディタが書き込み中で掴めないことがある。次の Poll で読めばよい。
            _lastWrite = DateTime.MinValue;
        }
        catch (Exception ex)
        {
            Log.Warning($"tuning の読み込みに失敗しました: {ex.Message}");
        }
    }

    private static Dictionary<string, string> Parse(IEnumerable<string> lines)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines)
        {
            string s = line.Trim();
            if (s.Length == 0 || s[0] is '#' or ';') continue;

            int sep = s.IndexOf('=');
            if (sep < 0) sep = s.IndexOf(':');
            if (sep <= 0) continue;

            string key = s[..sep].Trim();
            string value = s[(sep + 1)..].Trim();

            // 行末コメント（値が引用符で囲まれていない場合のみ）
            if (value.Length > 0 && value[0] != '"')
            {
                int hash = value.IndexOfAny(['#', ';']);
                if (hash >= 0) value = value[..hash].TrimEnd();
            }
            else if (value.Length >= 2 && value[0] == '"')
            {
                int close = value.IndexOf('"', 1);
                if (close > 0) value = value[1..close];
            }

            if (key.Length > 0) dict[key] = value;
        }
        return dict;
    }

    /// <summary>
    /// 参照されたことのある全キーを既定値つきで書き出します。ファイルが無いときの雛形作成用。
    /// </summary>
    public static bool Save(bool overwrite = false)
    {
        try
        {
            string path = _path;
            string full = Path.IsPathRooted(path) ? path : Path.Combine(AstrumCore.AppPath, path);
            if (File.Exists(full) && !overwrite) return false;

            Dictionary<string, string> merged;
            lock (_sync)
            {
                if (_defaults.Count == 0) return false;
                merged = new Dictionary<string, string>(_defaults, StringComparer.OrdinalIgnoreCase);
                foreach (var kv in _values) merged[kv.Key] = kv.Value;
            }

            string? dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var body = new List<string>
            {
                "# AstrumLoom tuning",
                "# このファイルを保存すると、実行中のゲームに即座に反映されます。",
                "# 書式: キー = 値   ( # 以降はコメント )",
                "",
            };
            foreach (var kv in merged.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                body.Add($"{kv.Key} = {kv.Value}");

            File.WriteAllLines(full, body);
            Log.Debug($"tuning 雛形を書き出しました: {AstrumCore.FilePath(full)}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning($"tuning の書き出しに失敗しました: {ex.Message}");
            return false;
        }
    }
}
