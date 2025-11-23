namespace AstrumLoom;

public interface ITexture
{
    string Path { get; }
    int Width { get; }
    int Height { get; }

    bool IsReady { get; }
    bool IsFailed { get; }
    bool Loaded { get; }
    bool Enable { get; }

    DrawOptions? Option { get; set; }

    void Draw(double x, double y, DrawOptions? options);
    void Pump();
    void Dispose();
}

public static class TextureExtensions
{
    public static void Draw(this ITexture texture, double x = 0, double y = 0)
        => texture.Draw(x, y, null);

    public static void Draw(this ITexture texture, double x, double y,
        Color color = default,
        BlendMode blend = BlendMode.None, double opacity = 1)
        => texture.Draw(x, y, new DrawOptions
        {
            Color = color,
            Opacity = opacity
        });
}
public class Texture : IDisposable
{
    private ITexture? _texture { get; set; } = null;
    private bool _disposed = false;
    public Texture() { }
    public Texture(string path)
        => _texture = AstrumCore.Platform?.LoadTexture(path);

    public void Draw(double x = 0, double y = 0) => _texture?.Draw(x, y);

    public void Pump() => _texture?.Pump();

    ~Texture() => Dispose(false);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _texture?.Dispose();
            }
            _texture = null;
            _disposed = true;
        }
    }

    public string Path => _texture?.Path ?? "";
    public int Width => _texture?.Width ?? 0;
    public int Height => _texture?.Height ?? 0;

    public bool IsReady => _texture?.IsReady ?? false;
    public bool IsFailed => _texture?.IsFailed ?? false;
    public bool Loaded => _texture?.Loaded ?? false;
    public bool Enable => _texture?.Enable ?? false;

    #region DrawOptions Proxy
    public double Opacity
    {
        get => _texture?.Option?.Opacity ?? 1.0;
        set
        {
            if (_texture?.Option != null)
            {
                var opt = _texture.Option.Value;
                opt.Opacity = value;
                _texture.Option = opt;
            }
        }
    }
    public double Scale
    {
        get => _texture?.Option?.Scale.W ?? 1.0;
        set
        {
            if (_texture?.Option != null)
            {
                var opt = _texture.Option.Value;
                opt.Scale = (value, value);
                _texture.Option = opt;
            }
        }
    }
    public (double X, double Y)? XYScale
    {
        get => _texture?.Option?.Scale;
        set
        {
            if (_texture?.Option != null && value != null)
            {
                var opt = _texture.Option.Value;
                opt.Scale = value.Value;
                _texture.Option = opt;
            }
            else if (_texture?.Option != null)
            {
                var opt = _texture.Option.Value;
                opt.Scale = (1.0, 1.0);
                _texture.Option = opt;
            }
        }
    }
    public LayoutUtil.Point? Position
    {
        get => _texture?.Option?.Position;
        set
        {
            if (_texture?.Option != null && value != null)
            {
                var opt = _texture.Option.Value;
                opt.Position = value.Value;
                _texture.Option = opt;
            }
            else if (_texture?.Option != null)
            {
                var opt = _texture.Option.Value;
                opt.Position = null;
                _texture.Option = opt;
            }
        }
    }
    public ReferencePoint Point
    {
        get => _texture?.Option?.Point ?? ReferencePoint.TopLeft;
        set
        {
            if (_texture?.Option != null)
            {
                var opt = _texture.Option.Value;
                opt.Point = value;
                _texture.Option = opt;
            }
        }
    }
    public LayoutUtil.Rect? Rectangle
    {
        get => _texture?.Option?.Rectangle;
        set
        {
            if (_texture?.Option != null && value != null)
            {
                var opt = _texture.Option.Value;
                opt.Rectangle = value;
                _texture.Option = opt;
            }
            else if (_texture?.Option != null)
            {
                var opt = _texture.Option.Value;
                opt.Rectangle = null;
                _texture.Option = opt;
            }
        }
    }
    public Color Color
    {
        get => _texture?.Option?.Color ?? Color.White;
        set
        {
            if (_texture?.Option != null)
            {
                var opt = _texture.Option.Value;
                opt.Color = value;
                _texture.Option = opt;
            }
        }
    }
    public BlendMode BlendMode
    {
        get => _texture?.Option?.Blend ?? BlendMode.None;
        set
        {
            if (_texture?.Option != null)
            {
                var opt = _texture.Option.Value;
                opt.Blend = value;
                _texture.Option = opt;
            }
        }
    }
    public double Angle
    {
        get => _texture?.Option?.Angle ?? 0.0;
        set
        {
            if (_texture?.Option != null)
            {
                var opt = _texture.Option.Value;
                opt.Angle = value;
                _texture.Option = opt;
            }
        }
    }
    public (bool X, bool Y) Flip
    {
        get => _texture?.Option?.Flip ?? (false, false);
        set
        {
            if (_texture?.Option != null)
            {
                var opt = _texture.Option.Value;
                opt.Flip = value;
                _texture.Option = opt;
            }
        }
    }
    #endregion

    public LayoutUtil.Size Size => new(Width, Height);
    public LayoutUtil.Size ScaledSize => new(
        (int)(Width * Scale * Drawing.DefaultScale),
        (int)(Height * Scale * Drawing.DefaultScale)
    );

    public void SetColor(Color color, Color? add = null)
    {
        Color = color;
        if (add != null)
        {
            //AddColor = add.Value;
        }
    }

    public Texture Clone()
    {
        var tex = new Texture(Path);
        tex.Import(Export());
        return tex;
    }

    public DrawOptions Export() => new()
    {
        Color = Color,
        //AddColor = AddColor,
        Blend = BlendMode,
        Opacity = Opacity,
        Scale = XYScale ?? (Scale, Scale),
        Angle = Angle,
        Position = Position,
        Point = Point,
        Rectangle = Rectangle,
        Flip = Flip,
    };
    public void Import(DrawOptions opt)
    {
        Color = opt.Color ?? Color.White;
        //AddColor = opt.AddColor;
        BlendMode = opt.Blend;
        Opacity = opt.Opacity;
        Angle = opt.Angle;
        if (opt.Scale.W == opt.Scale.H)
            Scale = opt.Scale.W;
        else XYScale = opt.Scale;
        Position = opt.Position;
        Point = opt.Point;
        Rectangle = opt.Rectangle;
        Flip = opt.Flip;
    }

    public void Draw(double x, double y, LayoutUtil.Rect rectangle, LayoutUtil.Point? point = null)
    {
        var options = new DrawOptions()
        {
            Color = Color,
            Blend = BlendMode,
            Opacity = Opacity,
            Scale = XYScale ?? (Scale, Scale),
            Angle = Angle,
            Position = point ?? Position,
            Point = Point,
            Rectangle = rectangle,
            Flip = Flip,
            EdgeColor = _texture?.Option?.EdgeColor,
            Font = _texture?.Option?.Font,
            Thickness = _texture?.Option?.Thickness ?? 0,
        };
        _texture?.Draw(x, y, options);
    }
}