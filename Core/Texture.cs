using static AstrumLoom.LayoutUtil;

namespace AstrumLoom;

public interface ITexture : IResourse
{
    int Width { get; }
    int Height { get; }

    void Draw(double x, double y, DrawOptions option);
}

public static class TextureExtensions
{
    public static void Draw(this ITexture texture, double x = 0, double y = 0)
        => texture.Draw(x, y, new());
}
public class Texture : IDisposable
{
    private ITexture? _texture { get; set; } = null;
    public Texture() { }

    public Texture(Size size, Action method)
        => _texture = AstrumCore.Platform?.CreateTexture((int)size.Width, (int)size.Height, method);

    public Texture(string path)
        => _texture = AstrumCore.Platform?.LoadTexture(path);

    public ITexture Interface => _texture!;

    public void Draw(double x = 0, double y = 0)
        => _texture?.Draw(x, y, Option);
    public void Draw(double x, double y, DrawOption? option)
        => _texture?.Draw(x, y, Option.Temp(option));
    public void Draw(Point point, DrawOption? option = null) => _texture?.Draw(point.X, point.Y, Option);

    public void Draw(double x, double y,
        txScale? s = null,
        ReferencePoint? point = null,
        double? opacity = null,
        Color? color = null,
        BlendMode? blend = null)
    {
        var opt = new DrawOption()
        {
            Point = point,
            Color = color,
            Blend = blend,
            Opacity = opacity,
            Scale = s,
        };
        Draw(x, y, opt);
    }
    public struct txScale
    {
        public double X;
        public double Y;
        public static implicit operator txScale(double value)
            => new() { X = value, Y = value };
        public static implicit operator txScale((double x, double y) value)
            => new() { X = value.x, Y = value.y };
        public static implicit operator (double W, double H)(txScale value)
            => (value.X, value.Y);
    }

    public void Draw(double x, double y, Rect rectangle)
        => DrawRect(x, y, rectangle);
    public void DrawSize(double x, double y, Size size)
    {
        XYScale = (size.Width / Width, size.Height / Height);
        _texture?.Draw(x, y);
    }
    public void DrawRect(double x, double y, Rect rectangle)
    {
        Rect? before = Rectangle != null ? new Rect(Rectangle.Value.X, Rectangle.Value.Y, Rectangle.Value.Width, Rectangle.Value.Height) : null;
        Rectangle = rectangle;
        _texture?.Draw(x, y);
        Rectangle = before;
    }
    public void Grid(double x, double y)
    {
        var tplt = LayoutUtil.GetAnchorOffset(Point, Width * Scale, Height * Scale);
        x += tplt.X; y += tplt.Y;
        Drawing.Box(x, y, Width, Height, Color.DarkGray, 1);
        for (int i = 0; i < Width; i += 20)
        {
            Drawing.Line(x + i, y, 0, Height, Color.Gray);
        }
        for (int j = 0; j < Height; j += 20)
        {
            Drawing.Line(x, y + j, Width, 0, Color.Gray);
        }
    }

    public void Pump() => _texture?.Pump();

    ~Texture() => Dispose();

    public void Dispose()
    {
        AstrumCore.RequestDispose(_texture!);
        GC.SuppressFinalize(this);
    }

    public override string ToString()
    {
        string name = Enable && string.IsNullOrEmpty(Path) ? "Manual draw" : System.IO.Path.GetFileName(Path);
        return !Loaded
            ? "Loading Texture\n" + name
            : !Enable
            ? "Disabled Texture\n" + name
            : $"Texture : {name}\n" +
               $"Size: {Width} x {Height}\n" +
               $"Opacity: {Opacity:F2}, Scale: {Scale:F2}\n" +
               $"Color: {Color}, BlendMode: {BlendMode}";
    }

    public string Path => _texture?.Path ?? "";
    public int Width => _texture?.Width ?? 0;
    public int Height => _texture?.Height ?? 0;

    public bool IsReady => _texture?.IsReady ?? false;
    public bool IsFailed => _texture?.IsFailed ?? false;
    public bool Loaded => _texture?.Loaded ?? false;
    public bool Enable => _texture?.Enable ?? false;

    #region DrawOptions Proxy
    private DrawOptions Option = new();
    public double Opacity
    {
        get => Option.Opacity;
        set => Option.Opacity = value;
    }
    public double Scale
    {
        get => Option.Scale.W;
        set => Option.Scale = (value, value);
    }
    public (double X, double Y)? XYScale
    {
        get => Option.Scale;
        set => Option.Scale = value != null ? value.Value : (1.0, 1.0);
    }
    public Point? Position
    {
        get => Option.Position;
        set => Option.Position = value;
    }
    public ReferencePoint Point
    {
        get => Option.Point;
        set => Option.Point = value;
    }
    public Rect? Rectangle
    {
        get => Option.Rectangle;
        set => Option.Rectangle = value;
    }
    public Color Color
    {
        get => Option.Color ?? Color.White;
        set => Option.Color = value;
    }
    public BlendMode BlendMode
    {
        get => Option.Blend;
        set => Option.Blend = value;
    }
    public double Angle
    {
        get => Option.Angle;
        set => Option.Angle = value;
    }
    public (bool X, bool Y) Flip
    {
        get => Option.Flip;
        set => Option.Flip = value;
    }
    #endregion

    public Size Size => new(Width, Height);
    public Size ScaledSize => new(
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

    public void Expand(double width, double height)
        => XYScale = (width / Width, height / Height);

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

    public void Draw(double x, double y, Rect rectangle, Point? point = null)
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
            EdgeColor = Option.EdgeColor,
            Font = Option.Font,
            Thickness = Option.Thickness,
        };
        _texture?.Draw(x, y, options);
    }
}

public class TextureCathe
{
    private Texture? Cathe;
    public Texture Get(Action method, Size size)
    {
        if (Cathe != null) return Cathe;
        Cathe = new Texture(size, method);
        return Cathe;
    }
}