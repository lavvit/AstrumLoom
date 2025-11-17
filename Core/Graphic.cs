namespace AstrumLoom;

public interface ITexture
{
    int Width { get; }
    int Height { get; }
}

public interface IGraphics
{
    ITexture LoadTexture(string path);
    void UnloadTexture(ITexture texture);

    void BeginFrame();
    void Clear(Color color);

    void DrawTexture(
        ITexture texture,
        float x, float y,
        float scaleX = 1f,
        float scaleY = 1f,
        float rotationRad = 0f);

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
}

public enum BlendMode
{
    None = 0,
    Alpha = 1,
    Add = 2,
    Multiply = 3,
    Screen = 4,
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
    public Color? Color { get; set; } = null;
    public double Opacity { get; set; } = 1.0;
    public bool Fill { get; set; } = true;
    public int Thickness { get; set; } = 1;
    public BlendMode Blend { get; set; } = BlendMode.None;
    public ReferencePoint Point { get; set; } = ReferencePoint.TopLeft;

    public DrawOptions() { }
}

public static class GraphicsExtensions
{
    public static void Line(
        this IGraphics g,
        double x1, double y1, double dx, double dy,
        Color? color = null,
        int thickness = 1,
        BlendMode blend = BlendMode.None)
        => g.Line(x1, y1, dx, dy, new DrawOptions
        {
            Color = color,
            Thickness = thickness,
            Blend = blend
        });

    public static void LineZ(
    this IGraphics g,
    double x1, double y1, double x2, double y2,
    Color? color = null,
    int thickness = 1,
    BlendMode blend = BlendMode.None)
    => g.Line(x1, y1, x2 - x1, y2 - y1, new DrawOptions
    {
        Color = color,
        Thickness = thickness,
        Blend = blend
    });

    public static void Box(
        this IGraphics g,
        double x, double y, double width, double height,
        Color? color = null,
        bool fill = true,
        int thickness = 1,
        BlendMode blend = BlendMode.None)
        => g.Box(x, y, width, height, new DrawOptions
        {
            Color = color,
            Fill = fill,
            Thickness = thickness,
            Blend = blend
        });

    public static void BoxZ(
        this IGraphics g,
        double x1, double y1, double x2, double y2,
        Color? color = null,
        bool fill = true,
        int thickness = 1,
        BlendMode blend = BlendMode.None)
        => g.Box(x1, y1, x2 - x1, y2 - y1, new DrawOptions
        {
            Color = color,
            Fill = fill,
            Thickness = thickness,
            Blend = blend
        });

    public static void Circle(
    this IGraphics g,
    double x, double y, double radius,
    Color? color = null,
    bool fill = true,
    int thickness = 1,
    BlendMode blend = BlendMode.None,
    int segments = 64)
    => g.Circle(x, y, radius, new DrawOptions
    {
        Color = color,
        Fill = fill,
        Thickness = thickness,
        Blend = blend
    }, segments);

    public static void Oval(
        this IGraphics g,
        double x, double y, double rx, double ry,
        Color? color = null,
        bool fill = true,
        int thickness = 1,
        BlendMode blend = BlendMode.None,
        int segments = 64)
        => g.Oval(x, y, rx, ry, new DrawOptions
        {
            Color = color,
            Fill = fill,
            Thickness = thickness,
            Blend = blend
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
    int thickness = 1)
    => g.Cross(x, y, size, new DrawOptions
    {
        Color = color,
        Thickness = thickness
    });


    public static void Text(
    this IGraphics g,
    double x, double y,
    string text,
    int size = 20,
    Color? color = null,
    ReferencePoint point = ReferencePoint.TopLeft)
    => g.Text(x, y, text, size, new DrawOptions
    {
        Color = color,
        Point = point
    });

}