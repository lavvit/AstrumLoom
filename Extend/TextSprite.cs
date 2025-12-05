namespace AstrumLoom.Extend;

public class TextSprite : IDisposable
{
    public string Text { get; set; } = "";
    public Color Color { get; set; } = Color.White;
    public IFont? Font { get; set; } = null;
    public DrawOptions Option { get; set; } = new DrawOptions();
    public DecorateText.DecorateOption? DecoOption { get; set; } = null;

    private Texture? _texture = null;
    private bool _dirty;
    private int _width;
    private int _height;

    public TextSprite(string text, IFont? font = null, Color? color = null)
    {
        Text = text;
        Font = font;
        if (color.HasValue) Color = color.Value;
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
                Font?.Draw(0, 0, Text, DecoOption);
            else
                Font?.Draw(0, 0, Text, Color);
        })
        {
            Flip = (false, false)
        };

        _dirty = false;
    }
}
