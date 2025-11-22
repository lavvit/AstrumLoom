using static AstrumLoom.LayoutUtil;

namespace AstrumLoom;

public interface IGraphics
{
    void BeginFrame();
    void Clear(Color color);

    void EndFrame();

    // --- 追加: 図形 ---
    void Blackout(double opacity = 1.0, Color? color = null);

    void Line(
        double x, double y, double dx, double dy, DrawOptions option);

    void Box(
        double x, double y, double width, double height,
        DrawOptions option);

    void Circle(
        double x, double y, double radius,
        DrawOptions option, int segments = 64);

    void Oval(
        double x, double y, double radiusX, double radiusY,
        DrawOptions option, int segments = 64);

    void Triangle(
        double x1, double y1,
        double x2, double y2,
        double x3, double y3,
        DrawOptions option);

    // --- 追加: テキスト ---
    void Text(
        double x, double y, string text, int size,
        DrawOptions option);

    (int Width, int Height) MeasureText(
        string text, int size);

    // フォント生成
    IFont CreateFont(FontSpec spec);

    // おまけ：デフォルトフォント（ゲームごとに 1 個用意）
    IFont DefaultFont { get; }
}

public enum BlendMode
{
    None = 0,
    Add = 1,
    Subtract = 2,
    Multiply = 3,
    Reverse = 4,
    Screen = 5,
}

public enum ReferencePoint
{
    TopLeft,
    TopCenter,
    TopRight,
    CenterLeft,
    Center,
    CenterRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
}

public struct DrawOptions
{
    public DrawOptions() { }
    public Color? Color { get; set; } = null;
    public double Opacity { get; set; } = 1.0;
    public readonly bool Fill => Thickness <= 0;
    public int Thickness { get; set; } = 0;
    public BlendMode Blend { get; set; } = BlendMode.None;
    public ReferencePoint Point { get; set; } = ReferencePoint.TopLeft;

    // テクスチャ用
    public (double W, double H) Scale { get; set; } = (1.0, 1.0);
    public double Angle { get; set; } = 0.0;
    public (bool X, bool Y) Flip { get; set; } = (false, false);

    public Point? Position { get; set; } = null;
    public Rect? Rectangle { get; set; } = null;
}

public static class GraphicsExtensions
{
    public static void Line(
        this IGraphics g,
        double x1, double y1, double dx, double dy,
        Color? color = null,
        int thickness = 1,
        BlendMode blend = BlendMode.None, double opacity = 1)
        => g.Line(x1, y1, dx, dy, new DrawOptions
        {
            Color = color,
            Thickness = thickness,
            Blend = blend,
            Opacity = opacity
        });

    public static void LineZ(
    this IGraphics g,
    double x1, double y1, double x2, double y2,
    Color? color = null,
    int thickness = 1,
    BlendMode blend = BlendMode.None, double opacity = 1)
    => Line(g, x1, y1, x2 - x1, y2 - y1, color, thickness, blend, opacity);

    public static void Box(
        this IGraphics g,
        double x, double y, double width, double height,
        Color? color = null,
        int thickness = 0,
        BlendMode blend = BlendMode.None, double opacity = 1)
        => g.Box(x, y, width, height, new DrawOptions
        {
            Color = color,
            Thickness = thickness,
            Blend = blend,
            Opacity = opacity
        });

    public static void BoxZ(
        this IGraphics g,
        double x1, double y1, double x2, double y2,
        Color? color = null,
        int thickness = 0,
        BlendMode blend = BlendMode.None, double opacity = 1)
        => Box(g, x1, y1, x2 - x1, y2 - y1, color, thickness, blend, opacity);

    public static void Circle(
    this IGraphics g,
    double x, double y, double radius,
    Color? color = null,
    int thickness = 0,
    BlendMode blend = BlendMode.None,
    double opacity = 1,
    int segments = 64)
    => g.Circle(x, y, radius, new DrawOptions
    {
        Color = color,
        Thickness = thickness,
        Blend = blend,
        Opacity = opacity
    }, segments);

    public static void Oval(
        this IGraphics g,
        double x, double y, double rx, double ry,
        Color? color = null,
        int thickness = 0,
        BlendMode blend = BlendMode.None,
        double opacity = 1,
        int segments = 64)
        => g.Oval(x, y, rx, ry, new DrawOptions
        {
            Color = color,
            Thickness = thickness,
            Blend = blend,
            Opacity = opacity
        }, segments);

    public static void Cross(
        this IGraphics g,
        double x, double y, double size, DrawOptions options)
    {
        g.Box(x - size / 2, y - (options.Thickness - 1) / 2.0, size, options.Thickness, options);
        g.Box(x - (options.Thickness - 1) / 2.0, y - size / 2.0, options.Thickness, size, options);
    }

    public static void Cross(
    this IGraphics g,
    double x, double y,
    double size = 20,
    Color? color = null,
    int thickness = 1,
    BlendMode blend = BlendMode.None,
    double opacity = 1)
    => g.Cross(x, y, size, new DrawOptions
    {
        Color = color,
        Thickness = thickness,
        Blend = blend,
        Opacity = opacity
    });

    public static void Text(
    this IGraphics g,
    double x, double y,
    object text,
    Color? color = null,
    ReferencePoint point = ReferencePoint.TopLeft,
    BlendMode blend = BlendMode.None,
    double opacity = 1)
    => g.DefaultFont.Draw(g, x, y, text.ToString() ?? "", new DrawOptions
    {
        Color = color,
        Point = point,
        Blend = blend,
        Opacity = opacity
    });

    public static (int Width, int Height) MeasureText(
        this IGraphics g,
        object text)
        => g.DefaultFont.Measure(text.ToString() ?? "");
}
public static class LayoutUtil
{
    public static Point GetAnchorOffset(
        ReferencePoint point, double w, double h)
        => point switch
        {
            ReferencePoint.TopLeft => new Point(),
            ReferencePoint.TopCenter => new Point(-w / 2, 0),
            ReferencePoint.TopRight => new Point(-w, 0),
            ReferencePoint.CenterLeft => new Point(0, -h / 2),
            ReferencePoint.Center => new Point(-w / 2, -h / 2),
            ReferencePoint.CenterRight => new Point(-w, -h / 2),
            ReferencePoint.BottomLeft => new Point(0, -h),
            ReferencePoint.BottomCenter => new Point(-w / 2, -h),
            ReferencePoint.BottomRight => new Point(-w, -h),
            _ => new Point()
        };
    public struct Point
    {
        public double X;
        public double Y;
        public Point() { X = 0; Y = 0; }
        public Point(double x, double y)
        {
            X = x;
            Y = y;
        }
        public Point(System.Drawing.PointF p)
        {
            X = p.X;
            Y = p.Y;
        }
        public static Point operator +(Point a, Point b) => new(a.X + b.X, a.Y + b.Y);
        public static Point operator -(Point a, Point b) => new(a.X - b.X, a.Y - b.Y);
        public static Point operator *(Point a, double b) => new(a.X * b, a.Y * b);
        public static Point operator /(Point a, double b) => new(a.X / b, a.Y / b);
        public static bool operator ==(Point a, Point b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(Point a, Point b) => !(a == b);
        public override readonly bool Equals(object? obj) => obj is Point p && this == p;

        public override string ToString() => $"({X}, {Y})";
        public readonly double Length() => Math.Sqrt(X * X + Y * Y);
        public Point Normalize()
        {
            double len = Length();
            return new Point(X / len, Y / len);
        }
        public (double x, double y) ToTuple() => (X, Y);

        public override int GetHashCode() => HashCode.Combine(X, Y);
    }

    public struct Size
    {
        public double Width;
        public double Height;
        public Size() { Width = 0; Height = 0; }
        public Size(double w, double h)
        {
            Width = w;
            Height = h;
        }
        public Size(System.Drawing.SizeF s)
        {
            Width = s.Width;
            Height = s.Height;
        }
        public static Size operator +(Size a, Size b) => new(a.Width + b.Width, a.Height + b.Height);
        public static Size operator -(Size a, Size b) => new(a.Width - b.Width, a.Height - b.Height);
        public static Size operator *(Size a, double b) => new(a.Width * b, a.Height * b);
        public static Size operator /(Size a, double b) => new(a.Width / b, a.Height / b);
        public static bool operator ==(Size a, Size b) => a.Width == b.Width && a.Height == b.Height;
        public static bool operator !=(Size a, Size b) => !(a == b);
        public override readonly bool Equals(object? obj) => obj is Size s && this == s;
        public override readonly string ToString() => $"({Width}, {Height})";
        public override int GetHashCode() => HashCode.Combine(Width, Height);
    }

    public struct Rect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
        public Rect()
        {
            X = 0; Y = 0; Width = 0; Height = 0;
        }
        public Rect(Point p, Size s)
        {
            X = p.X; Y = p.Y; Width = s.Width; Height = s.Height;
        }
        public Rect(double w, double h)
        {
            X = 0; Y = 0; Width = w; Height = h;
        }
        public Rect(double x, double y, double w, double h)
        {
            X = x; Y = y; Width = w; Height = h;
        }
        public Rect(System.Drawing.RectangleF r)
        {
            X = r.X; Y = r.Y; Width = r.Width; Height = r.Height;
        }
        public static Rect operator +(Rect a, Point b)
            => new(a.X + b.X, a.Y + b.Y, a.Width, a.Height);
        public static Rect operator -(Rect a, Point b)
            => new(a.X - b.X, a.Y - b.Y, a.Width, a.Height);
        public static bool operator ==(Rect a, Rect b)
            => a.X == b.X && a.Y == b.Y && a.Width == b.Width && a.Height == b.Height;
        public static bool operator !=(Rect a, Rect b) => !(a == b);
        public override readonly bool Equals(object? obj) => obj is Rect r && this == r;
        public override readonly string ToString() => $"({X}, {Y}, {Width}, {Height})";
        public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);
    }
}
