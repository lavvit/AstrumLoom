namespace AstrumLoom.Extend;

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
        var str = GetString(name);
        return str != null && double.TryParse(str, out var result) ? result : defaultValue;
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
    public LayoutUtil.Point GetPoint(string key, double defaultx = 0, double defaulty = 0, char separator = ',')
    {
        double x = GetDouble(key + "X", defaultx);
        double y = GetDouble(key + "Y", defaulty);
        double[] value = GetDoubleArray(key, separator);
        return value.Length < 2 ? new((int)x, (int)y) : new((int)value[0], (int)value[1]);
    }
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
    public void Load(string[] lines, char separator = DefaultSeparator, string commentPrefix = CommentPrefix, string path = "")
    {
        FilePath = path;
        Items.Clear();
        string? lastComment = null;
        foreach (var line in lines)
        {
            if (line.StartsWith("//") || string.IsNullOrWhiteSpace(line))
                continue;
            if (line.StartsWith(commentPrefix))
            {
                if (lastComment != null)
                    lastComment += "\n" + line;
                else lastComment = line;
                continue;
            }
            var parts = line.Split(separator, 2);
            if (parts.Length != 2)
                continue;
            var key = parts[0].Trim();
            var value = parts[1].Trim();
            Items.Add(new(key, value, lastComment ?? ""));
        }
    }

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

public struct ConfItem(string name, string value, string comment = "")
{
    public string Name { get; set; } = name;
    public string Value { get; set; } = value;
    public string Comment { get; set; } = comment;
    public readonly T GetValue<T>() => (T)Convert.ChangeType(Value, typeof(T));

    public override readonly string ToString()
        => $"{Name}={Value}" + (string.IsNullOrEmpty(Comment) ? "" : $"\n{Comment}");
}
