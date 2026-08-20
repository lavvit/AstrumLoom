namespace AstrumLoom.Extend;

/// <summary>
/// 毎フレームのテキスト描画をレンダーテクスチャ1枚にキャッシュし、テキストが変わったときだけ再描画するクラス。
/// フォント描画は比較的重いため、同じ文字列を毎フレーム描く場合の負荷軽減に使う。
/// </summary>
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
    public TextSprite(string text, IFont? font, Gradation gradation,
        ReferencePoint? point = null, Color? edgeColor = null, BlendMode? blend = null, double? opacity = null)
        : this(text, font, new DecorateText.DecorateOption(gradation), point, edgeColor, blend, opacity) { }
    public TextSprite(string text, IFont? font, Texture texture,
        ReferencePoint? point = null, Color? edgeColor = null, BlendMode? blend = null, double? opacity = null)
        : this(text, font, new DecorateText.DecorateOption(texture), point, edgeColor, blend, opacity) { }

    private void Initialize()
    {
        RecreateRenderTextureIfNeeded();
        _dirty = true;
    }

    /// <summary>表示テキストを変更します。内容が同じなら何もしません（キャッシュ再利用のため既存テクスチャは破棄して作り直す）。</summary>
    public void SetText(string text)
    {
        if (Text == text) return;
        Dispose();
        Text = text;
        _dirty = true;
    }

    /// <summary>テキストを更新してから描画する簡易版。</summary>
    public void Draw(string text, double x, double y)
    {
        SetText(text);
        Draw(x, y);
    }
    /// <summary>現在のテキストを描画します。内容が変わっていればレンダーテクスチャを再生成してから描画します。</summary>
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
    /// <summary>
    /// テキストの計測サイズが変わっていたら（初回含む）既存テクスチャを破棄し、次回描画時に再生成させます。
    /// </summary>
    private void RecreateRenderTextureIfNeeded()
    {
        // テキストの想定サイズ
        var (width, height) = Font?.Measure(Text) ?? (0, 0);
        int e = Font?.Spec.Edge ?? 0;
        int w = (int)MathF.Ceiling(width + e * 2);
        int h = (int)MathF.Ceiling(height + e * 2);

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

    /// <summary>_dirty フラグが立っていれば、レンダーテクスチャへ実際にテキストを描き込んで確定させます。</summary>
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
            int e = Font?.Spec.Edge ?? 0;
            if (DecoOption != null)
                Font?.Draw(e, e, Text, DecoOption, edgecolor: EdgeColor);
            else
                Font?.Draw(e, e, Text, Color, edgecolor: EdgeColor);
        });

        _dirty = false;
    }
    public Color Color { get; set; } = Color.White;
    private Color? _edgeColor = null;
    /// <summary>
    /// 縁取り色。変更時にキャッシュ済みテクスチャへ反映されるよう _dirty を立てる
    /// （EdgeColorはGetCacheKeyのキーに含まれないため、同一キャッシュキーのまま値だけ変わっても
    /// このsetterで検知しないと古い縁色テクスチャが描画され続けてしまう）。
    /// </summary>
    public Color? EdgeColor
    {
        get => _edgeColor;
        set
        {
            if (_edgeColor == value) return;
            _edgeColor = value;
            _dirty = true;
        }
    }
    public DecorateText.DecorateOption? DecoOption { get; set; } = null;
    public ReferencePoint Point { get; set; } = ReferencePoint.TopLeft;
    public BlendMode Blend { get; set; } = BlendMode.None;
    public double Opacity { get; set; } = 1.0;
}
/// <summary>
/// TextSprite をキー（テキスト・フォント・色/装飾）で自動キャッシュしながら、
/// Drawing.Text 感覚の static メソッドで直接呼び出せるようにするラッパー。
/// 使われなかったキャッシュは AddExtendAction 経由で毎フレーム終了後に自動破棄される。
/// </summary>
public static class TextSprites
{
    // 直接動かすためのstaticメソッド
    #region キャッシュ管理
    private static Dictionary<string, TextSprite> _cache = [];
    private static Dictionary<string, bool> _used = [];
    /// <summary>キーに対応する TextSprite をキャッシュから取得、無ければ生成します。使用済みマークも立てます。</summary>
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
    /// <summary>
    /// このフレームで使われなかった（_used が false の）キャッシュを破棄し、使用フラグを次フレーム用にリセットします。
    /// </summary>
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
    /// <summary>キャッシュされた TextSprite を使ってテキストを描画します（通常の単色描画版）。</summary>
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
    /// <summary>キャッシュされた TextSprite を使ってテキストを描画します（グラデーション／テクスチャ装飾版）。</summary>
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

    /// <summary>テキスト・フォント・色からキャッシュキーを組み立てます。</summary>
    private static string GetCacheKey(string text, IFont font, Color color)
    {
        string fontKey = font.GetHashCode().ToString();
        string colorKey = $"{color.R}_{color.G}_{color.B}_{color.A}";
        return $"{text}__{fontKey}__{colorKey}";
    }
    private static string GetCacheKey(string text, IFont font, DecorateText.DecorateOption decorate)
    {
        string fontKey = font.GetHashCode().ToString();
        // decorate(DecorateOptionラッパー)自体は呼び出しのたびにnewされるため、
        // その参照ハッシュをキーにすると絶対に一致しない。中身のGradation/Textureの参照ハッシュを使う。
        string gradKey = decorate.Gradation != null
            ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(decorate.Gradation).ToString() : "none";
        string texKey = decorate.Texture != null
            ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(decorate.Texture).ToString() : "none";
        return $"{text}__{fontKey}__{gradKey}_{texKey}";
    }
}