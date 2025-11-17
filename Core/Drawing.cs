namespace AstrumLoom;

public class Drawing
{
    private static IGraphics _g { get; set; } = null!;
    internal static void Initialize(IGraphics graphics)
    {
        _g = graphics;
    }

    public static void Line(double x1, double y1, double dx, double dy,
        Color? color = null,
        int thickness = 1,
        BlendMode blend = BlendMode.None, double opacity = 1)
        => _g.Line(x1, y1, dx, dy,
            new DrawOptions
            {
                Color = color,
                Thickness = thickness,
                Blend = blend,
                Opacity = opacity
            });
    public static void LineZ(double x1, double y1, double x2, double y2,
        Color? color = null,
        int thickness = 1,
        BlendMode blend = BlendMode.None, double opacity = 1)
        => Line(x1, y1, x2 - x1, y2 - y1, color, thickness, blend, opacity);


    public static void Box(double x, double y, double width, double height,
        Color? color = null,
        int thickness = 1,
        bool fill = false,
        BlendMode blend = BlendMode.None, double opacity = 1)
        => _g.Box(x, y, width, height,
            new DrawOptions
            {
                Color = color,
                Thickness = thickness,
                Fill = fill,
                Blend = blend,
                Opacity = opacity
            });
    public static void BoxZ(double x1, double y1, double x2, double y2,
        Color? color = null,
        int thickness = 1,
        bool fill = false,
        BlendMode blend = BlendMode.None, double opacity = 1)
        => Box(x1, y1, x2 - x1, y2 - y1, color, thickness, fill, blend, opacity);

    public static void Circle(double x, double y, double radius,
        Color? color = null,
        int thickness = 1,
        bool fill = false,
        BlendMode blend = BlendMode.None, double opacity = 1)
        => _g.Circle(x, y, radius,
            new DrawOptions
            {
                Color = color,
                Thickness = thickness,
                Fill = fill,
                Blend = blend,
                Opacity = opacity
            });
    public static void Oval(double x, double y, double rx, double ry,
        Color? color = null,
        int thickness = 1,
        bool fill = false,
        BlendMode blend = BlendMode.None, double opacity = 1)
        => _g.Oval(x, y, rx, ry,
            new DrawOptions
            {
                Color = color,
                Thickness = thickness,
                Fill = fill,
                Blend = blend,
                Opacity = opacity
            });
    public static void Triangle(double x1, double y1, double x2, double y2, double x3, double y3,
        Color? color = null,
        int thickness = 1,
        bool fill = false,
        BlendMode blend = BlendMode.None, double opacity = 1)
        => _g.Triangle(x1, y1, x2, y2, x3, y3,
            new DrawOptions
            {
                Color = color,
                Thickness = thickness,
                Fill = fill,
                Blend = blend,
                Opacity = opacity
            });

    public static void Cross(double x, double y, double size,
        Color? color = null,
        int thickness = 1,
        BlendMode blend = BlendMode.None, double opacity = 1)
    {
        Line(x - size / 2, y - size / 2, size, size,
            color, thickness, blend, opacity);
        Line(x - size / 2, y + size / 2, size, -size,
            color, thickness, blend, opacity);
    }

    public static void Blackout(double opacity = 1.0, Color? color = null)
        => _g.Blackout(opacity, color);

    public static (int width, int height) TextSize(string text)
        => _g.MeasureText(text);

    public static void Text(double x, double y, string text,
        Color? color = null,
        ReferencePoint point = ReferencePoint.TopLeft,
        BlendMode blend = BlendMode.None, double opacity = 1)
        => _g.Text(x, y, text, color ?? Color.White, point, blend, opacity);
}