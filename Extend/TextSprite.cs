namespace AstrumLoom.Extend;

public class TextSprite : IDisposable
{
    public string Text { get; set; } = "";
    public IFont? Font { get; set; } = null;

    private Texture? _texture = null;
    private bool _dirty;
    private int _width;
    private int _height;

    public TextSprite(IFont? font = null, Color? color = null,
        ReferencePoint? point = null, Color? edgeColor = null, BlendMode? blend = null, double? opacity = null)
        : this("", font, color, point, edgeColor, blend, opacity) { }
    public TextSprite(string text, IFont? font = null, Color? color = null,
        ReferencePoint? point = null, Color? edgeColor = null, BlendMode? blend = null, double? opacity = null)
    {
        Text = text;
        Font = font;
        if (color.HasValue) Color = color.Value;
        if (point.HasValue) Point = point.Value;
        if (edgeColor.HasValue) EdgeColor = edgeColor.Value;
        if (blend.HasValue) Blend = blend.Value;
        if (opacity.HasValue) Opacity = opacity.Value;
        RecreateRenderTextureIfNeeded();
        _dirty = true;
    }
    public TextSprite(string text, IFont? font, DecorateText.DecorateOption decorate,
        ReferencePoint? point = null, Color? edgeColor = null, BlendMode? blend = null, double? opacity = null)
    {
        Text = text;
        Font = font;
        DecoOption = decorate;
        if (point.HasValue) Point = point.Value;
        if (edgeColor.HasValue) EdgeColor = edgeColor.Value;
        if (blend.HasValue) Blend = blend.Value;
        if (opacity.HasValue) Opacity = opacity.Value;
        RecreateRenderTextureIfNeeded();
        _dirty = true;
    }

    private void Initialize()
    {
        RecreateRenderTextureIfNeeded();
        _dirty = true;
    }

    public void SetText(string text)
    {
        if (Text == text) return;
        Dispose();
        Text = text;
        _dirty = true;
    }

    public void Draw(string text, double x, double y)
    {
        SetText(text);
        Draw(x, y);
    }
    public void Draw(double x, double y)
    {
        UpdateTextureIfNeeded();
        _texture?.Point = Point;
        _texture?.BlendMode = Blend;
        _texture?.Opacity = Opacity;
        _texture?.Draw(x, y);
    }

    public void Dispose()
    {
        _texture?.Dispose();
        _texture = null;
        GC.SuppressFinalize(this);
    }
    private void RecreateRenderTextureIfNeeded()
    {
        // テキストの想定サイズ
        var (width, height) = Font?.Measure(Text) ?? (0, 0);
        int w = (int)MathF.Ceiling(width);
        int h = (int)MathF.Ceiling(height);

        if (w <= 0) w = 1;
        if (h <= 0) h = 1;

        // サイズが変わったときだけ作り直す
        if (_texture == null || !_texture.Enable || w != _width || h != _height)
        {
            Dispose();

            _width = w;
            _height = h;
            _dirty = true;
        }
    }

    private void UpdateTextureIfNeeded()
    {
        if (!_dirty) return;

        RecreateRenderTextureIfNeeded();

        // サイズを再計算
        LayoutUtil.Size size = new(_width, _height);

        // レンダーテクスチャに描画
        _texture = new Texture(new LayoutUtil.Size(_width, _height), () =>
        {
            Drawing.Fill(Color.Transparent);
            if (DecoOption != null)
                Font?.Draw(0, 0, Text, DecoOption, edgecolor: EdgeColor);
            else
                Font?.Draw(0, 0, Text, Color, edgecolor: EdgeColor);
        });

        _dirty = false;
    }
    public Color Color { get; set; } = Color.White;
    public Color? EdgeColor { get; set; } = null;
    public DecorateText.DecorateOption? DecoOption { get; set; } = null;
    public ReferencePoint Point { get; set; } = ReferencePoint.TopLeft;
    public BlendMode Blend { get; set; } = BlendMode.None;
    public double Opacity { get; set; } = 1.0;
}
public static class TextSprites
{
    // 直接動かすためのstaticメソッド
    #region キャッシュ管理
    private static Dictionary<string, TextSprite> _cache = [];
    private static Dictionary<string, bool> _used = [];
    private static TextSprite Get(string text, IFont font, Color color)
    {
        string key = GetCacheKey(text, font, color);
        if (!_cache.TryGetValue(key, out var sprite))
        {
            sprite = new TextSprite(text, font, color);
            _cache[key] = sprite;
        }
        _used[key] = true;
        AstrumCore.AddExtendAction($"TextSprite_CleanupCache", DisposeUnused, inEndStart: true);
        return sprite;
    }
    private static TextSprite Get(string text, IFont font, DecorateText.DecorateOption decorate)
    {
        string key = GetCacheKey(text, font, decorate);
        if (!_cache.TryGetValue(key, out var sprite))
        {
            sprite = new TextSprite(text, font, decorate);
            _cache[key] = sprite;
        }
        _used[key] = true;
        AstrumCore.AddExtendAction($"TextSprite_CleanupCache", DisposeUnused, inEndStart: true);
        return sprite;
    }
    private static void DisposeUnused()
    {
        string[] targetkeys = [.. _used.Where(u => !u.Value).Select(u => u.Key)];
        foreach (string key in targetkeys)
        {
            if (_cache.TryGetValue(key, out var sprite))
            {
                sprite.Dispose();
                _cache.Remove(key);
            }
            _used.Remove(key);
        }
        foreach (string? k in _used.Keys.ToList())
        {
            _used[k] = false;
        }
    }
    #endregion

    public static void Draw(string text, double x, double y)
        => Draw(null, text, x, y, Color.White);
    public static void Draw(IFont? font, object? text, double x, double y, Color? color = null,
        ReferencePoint point = ReferencePoint.TopLeft, Color? edgeColor = null, BlendMode blend = BlendMode.None, double opacity = 1)
    {
        string str = text?.ToString() ?? "";
        var col = color ?? Color.White;
        var fnt = font ?? Drawing.DefaultFont;
        var sprite = Get(str, fnt, col);
        sprite.Font = fnt;
        sprite.Color = col;
        sprite.Point = point;
        if (edgeColor.HasValue) sprite.EdgeColor = edgeColor.Value;
        sprite.Blend = blend;
        sprite.Opacity = opacity;
        sprite.SetText(str);
        sprite.Draw(x, y);
    }
    public static void Draw(IFont? font, object? text, double x, double y, DecorateText.DecorateOption decorate,
        ReferencePoint point = ReferencePoint.TopLeft, Color? edgeColor = null,
        BlendMode blend = BlendMode.None, double opacity = 1)
        => DrawDeco(font, text, x, y, decorate, point, edgeColor, blend, opacity);
    public static void DrawDeco(IFont? font, object? text, double x, double y, DecorateText.DecorateOption decorate,
        ReferencePoint point = ReferencePoint.TopLeft, Color? edgeColor = null,
        BlendMode blend = BlendMode.None, double opacity = 1)
    {
        string str = text?.ToString() ?? "";
        var fnt = font ?? Drawing.DefaultFont;
        var sprite = Get(str, fnt, decorate);
        sprite.Font = fnt;
        sprite.DecoOption = decorate;
        sprite.Point = point;
        if (edgeColor.HasValue) sprite.EdgeColor = edgeColor.Value;
        sprite.Blend = blend;
        sprite.Opacity = opacity;
        sprite.SetText(str);
        sprite.Draw(x, y);
    }

    private static string GetCacheKey(string text, IFont font, Color color)
    {
        string fontKey = font.GetHashCode().ToString();
        string colorKey = $"{color.R}_{color.G}_{color.B}_{color.A}";
        return $"{text}__{fontKey}__{colorKey}";
    }
    private static string GetCacheKey(string text, IFont font, DecorateText.DecorateOption decorate)
    {
        string fontKey = font.GetHashCode().ToString();
        string decoKey = decorate.GetHashCode().ToString();
        return $"{text}__{fontKey}__{decoKey}";
    }
}