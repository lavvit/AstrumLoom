namespace AstrumLoom.Extend;

/// <summary>
/// "キー=値" 形式のシンプルな設定ファイル（コメント行対応）を読み書きするための簡易パーサー。
/// Skin.ini など、決まったフォーマットの設定を持つゲーム素材の読み込みに使う。
/// </summary>
public class TextConf : IDisposable
{
    public string FilePath { get; private set; } = "";
    private List<ConfItem> Items { get; set; } = [];
    public Dictionary<string, string> ItemDictionary
        => Items.ToDictionary(i => i.Name, i => i.Value, StringComparer.OrdinalIgnoreCase);
    public int Count => Items.Count;

    public override string ToString() => $"{Path.GetFileName(FilePath)} : {Count} items" +
        (Items.Count > 0 ? "\n" + string.Join("\n", Items.Select(i => i.ToString())) : "");

    #region Get
    /// <summary>名前（大文字小文字を区別しない）で項目を検索します。無ければ null。</summary>
    public ConfItem? GetItem(string name)
    {
        foreach (var item in Items)
        {
            if (item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return item;
        }
        return null;
    }
    public bool HasItem(string name) => HasItem(name, out _);
    public bool HasItem(string name, out ConfItem? item)
    {
        item = GetItem(name);
        return item != null;
    }
    public string? GetString(string name)
    {
        var item = GetItem(name);
        return item?.Value;
    }
    public bool GetBool(string name, bool defaultValue = false) => GetInt(name, defaultValue ? 1 : 0) > 0;
    public int GetInt(string name, int defaultValue = 0) => (int)GetDouble(name, defaultValue);
    public double GetDouble(string name, double defaultValue = 0)
    {
        string? str = GetString(name);
        return str != null && double.TryParse(str, out double result) ? result : defaultValue;
    }
    public int[] GetIntArray(string key, char separator = ',', double[]? defaults = null)
        => [.. GetDoubleArray(key, separator, defaults).Select(v => (int)v)];
    public double[] GetDoubleArray(string key, char separator = ',', double[]? defaults = null)
    {
        string? value = GetString(key);
        if (string.IsNullOrEmpty(value)) return defaults ?? [];
        string[] parts = value.Split(separator);
        double[] result = new double[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (double.TryParse(parts[i].Trim(), out double v))
            {
                result[i] = v;
            }
        }
        return result;
    }
    public LayoutUtil.Point GetPoint(string key, LayoutUtil.Point? defaultValue, char separator = ',')
        => GetPoint(key, defaultValue?.X ?? 0, defaultValue?.Y ?? 0, separator);
    /// <summary>
    /// 座標を取得します。"KeyX"/"KeyY" の個別指定と "Key=x,y" のカンマ区切り指定の両方に対応し、
    /// カンマ区切りの値が見つかればそちらを優先します。
    /// </summary>
    public LayoutUtil.Point GetPoint(string key, double defaultx = 0, double defaulty = 0, char separator = ',')
    {
        double x = GetDouble(key + "X", defaultx);
        double y = GetDouble(key + "Y", defaulty);
        double[] value = GetDoubleArray(key, separator);
        return value.Length < 2 ? new((int)x, (int)y) : new((int)value[0], (int)value[1]);
    }
    /// <summary>
    /// 矩形を取得します。GetPoint 同様、"KeyX/KeyY/KeyWidth(orW)/KeyHeight(orH)" の個別指定と
    /// カンマ区切り指定の両方に対応します。
    /// </summary>
    public LayoutUtil.Rect GetRect(string key, char separator = ',', LayoutUtil.Rect? defaultValue = null)
    {
        double defaultx = defaultValue?.X ?? 0;
        double defaulty = defaultValue?.Y ?? 0;
        double defaultw = defaultValue?.Width ?? 0;
        double defaulth = defaultValue?.Height ?? 0;

        double x = GetDouble(key + "X", defaultx);
        double y = GetDouble(key + "Y", defaulty);
        double w = GetDouble(key + (HasItem(key + "Width") ? "Width" : "W"), defaultw);
        double h = GetDouble(key + (HasItem(key + "Height") ? "Height" : "H"), defaulth);

        double[] value = GetDoubleArray(key, separator);
        return value.Length < 4 ? new((int)x, (int)y, (int)w, (int)h)
            : new((int)value[0], (int)value[1], (int)value[2], (int)value[3]);
    }
    #endregion

    ~TextConf() { Dispose(); }
    public void Dispose()
    {
        Clear();
        GC.SuppressFinalize(this);
    }

    public TextConf() { }
    public TextConf(string path) => Load(path);

    public void Clear()
    {
        Items.Clear();
        FilePath = "";
    }

    private const char DefaultSeparator = '=';
    private const string CommentPrefix = "#";
    public void Load(string path, char separator = DefaultSeparator, string commentPrefix = CommentPrefix)
        => Load([.. Text.Read(path)], separator, commentPrefix, path);
    /// <summary>
    /// 行配列をパースします。"//" 始まりと空行は無視、commentPrefix 始まりの行はコメントとして直後の項目に紐付け、
    /// それ以外は separator で name/value に分割して項目として追加します。
    /// </summary>
    public void Load(string[] lines, char separator = DefaultSeparator, string commentPrefix = CommentPrefix, string path = "")
    {
        FilePath = path;
        Items.Clear();
        string? lastComment = null;
        foreach (string line in lines)
        {
            if (line.StartsWith("//") || string.IsNullOrWhiteSpace(line))
                continue;
            if (line.StartsWith(commentPrefix))
            {
                // 複数行のコメントは連結して次の項目にまとめて紐付ける
                if (lastComment != null)
                    lastComment += "\n" + line;
                else lastComment = line;
                continue;
            }
            string[] parts = line.Split(separator, 2);
            if (parts.Length != 2)
                continue;
            string key = parts[0].Trim();
            string value = parts[1].Trim();
            Items.Add(new(key, value, lastComment ?? ""));
            lastComment = null;
        }
    }

    /// <summary>保持している項目を "name=value"（コメントがあれば直後の行）の形式でファイルに書き出します。</summary>
    public void Save(string path, char separator = DefaultSeparator)
    {
        List<string> lines = [];
        foreach (var item in Items)
        {
            lines.Add($"{item.Name}{separator}{item.Value}");
            if (!string.IsNullOrEmpty(item.Comment))
                lines.Add(item.Comment);
        }
        Text.Save(path, lines);
    }
}

/// <summary>TextConf が保持する1件の設定項目（名前・値・直前のコメント）。</summary>
public struct ConfItem(string name, string value, string comment = "")
{
    public string Name { get; set; } = name;
    public string Value { get; set; } = value;
    public string Comment { get; set; } = comment;
    public readonly T GetValue<T>() => (T)Convert.ChangeType(Value, typeof(T));

    public override readonly string ToString()
        => $"{Name}={Value}" + (string.IsNullOrEmpty(Comment) ? "" : $"\n{Comment}");
}
