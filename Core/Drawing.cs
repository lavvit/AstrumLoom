using static AstrumLoom.Gradation;

namespace AstrumLoom;

/// <summary>
/// 図形・テキスト描画のstatic API。実描画は Graphics(IGraphics) に委譲し、
/// ここでは座標変換や省略可能引数の補完など呼び出しやすさのための薄いラッパーを提供する。
/// </summary>
public class Drawing
{
    // AstrumCore.Graphic は Platform?.Graphics で nullable（Boot() 前は Platform が未設定）。
    // ここを素通しすると NullReferenceException が呼び出し側の深いところで static に発生し、
    // 原因が「Boot をまだ呼んでいない」ことだと分かりにくい。戻り値は既存呼び出しを壊さないよう
    // 非 null の IGraphics のまま維持し、未初期化時だけ意味の分かる例外にする。
    internal static IGraphics Graphics => AstrumCore.Graphic
        ?? throw new InvalidOperationException("AstrumCore.Boot がまだ呼ばれていません。Drawing は Boot 完了後（Scene.Enable 以降）でのみ使用できます。");

    public static void Point(double x, double y,
        Color? color = null,
        int thickness = 1,
        BlendMode blend = BlendMode.None, double opacity = 1)
        => Circle(x, y, thickness / 2, color, 0, blend, opacity);
    public static void Line(double x1, double y1, double dx, double dy,
        Color? color = null,
        int thickness = 1,
        BlendMode blend = BlendMode.None, double opacity = 1)
    {
        if (dx == 0 && dy == 0)
            if (thickness > 1)
            { Point(x1, y1, color, thickness, blend, opacity); return; }
            else dy++;
        Graphics.Line(x1, y1, dx, dy,
            new DrawOptions
            {
                Color = color,
                Thickness = thickness,
                Blend = blend,
                Opacity = opacity
            });
    }
    public static void LineZ(double x1, double y1, double x2, double y2,
        Color? color = null,
        int thickness = 1,
        BlendMode blend = BlendMode.None, double opacity = 1)
        => Line(x1, y1, x2 - x1, y2 - y1, color, thickness, blend, opacity);
    public static void Line(LayoutUtil.Point p1, LayoutUtil.Point p2,
        Color? color = null,
        int thickness = 1,
        BlendMode blend = BlendMode.None, double opacity = 1)
    {
        var (x1, y1) = p1.ToTuple();
        var (x2, y2) = p2.ToTuple();
        Line(x1, y1, x2 - x1, y2 - y1, color, thickness, blend, opacity);
    }

    public static void Box(double x, double y, double width, double height,
        Color? color = null,
        int thickness = 0,
        BlendMode blend = BlendMode.None, double opacity = 1)
        => Graphics.Box(x, y, width, height,
            new DrawOptions
            {
                Color = color,
                Thickness = thickness,
                Blend = blend,
                Opacity = opacity
            });
    public static void BoxZ(double x1, double y1, double x2, double y2,
        Color? color = null,
        int thickness = 0,
        BlendMode blend = BlendMode.None, double opacity = 1)
        => Box(x1, y1, x2 - x1, y2 - y1, color, thickness, blend, opacity);
    public static void Box(LayoutUtil.Point p1, LayoutUtil.Point p2,
        Color? color = null,
        int thickness = 0,
        BlendMode blend = BlendMode.None, double opacity = 1)
    {
        var (x1, y1) = p1.ToTuple();
        var (x2, y2) = p2.ToTuple();
        Box(x1, y1, x2 - x1, y2 - y1, color, thickness, blend, opacity);
    }

    public static void Circle(double x, double y, double radius,
        Color? color = null,
        int thickness = 0,
        BlendMode blend = BlendMode.None, double opacity = 1)
        => Graphics.Circle(x, y, radius,
            new DrawOptions
            {
                Color = color,
                Thickness = thickness,
                Blend = blend,
                Opacity = opacity
            });
    public static void Circle(LayoutUtil.Point center, double radius,
        Color? color = null,
        int thickness = 1,
        BlendMode blend = BlendMode.None, double opacity = 1)
    {
        var (x, y) = center.ToTuple();
        Circle(x, y, radius, color, thickness, blend, opacity);
    }
    public static void Oval(double x, double y, double rx, double ry,
        Color? color = null,
        int thickness = 0,
        BlendMode blend = BlendMode.None, double opacity = 1)
        => Graphics.Oval(x, y, rx, ry,
            new DrawOptions
            {
                Color = color,
                Thickness = thickness,
                Blend = blend,
                Opacity = opacity
            });
    public static void Oval(LayoutUtil.Point center, double rx, double ry,
        Color? color = null,
        int thickness = 0,
        BlendMode blend = BlendMode.None, double opacity = 1)
    {
        var (x, y) = center.ToTuple();
        Oval(x, y, rx, ry, color, thickness, blend, opacity);
    }

    public static void Triangle(double x1, double y1, double x2, double y2, double x3, double y3,
        Color? color = null,
        int thickness = 0,
        BlendMode blend = BlendMode.None, double opacity = 1)
        => Graphics.Triangle(x1, y1, x2, y2, x3, y3,
            new DrawOptions
            {
                Color = color,
                Thickness = thickness,
                Blend = blend,
                Opacity = opacity
            });
    public static void Triangle(LayoutUtil.Point p1, LayoutUtil.Point p2, LayoutUtil.Point p3,
        Color? color = null,
        int thickness = 0,
        BlendMode blend = BlendMode.None, double opacity = 1)
    {
        var (x1, y1) = p1.ToTuple();
        var (x2, y2) = p2.ToTuple();
        var (x3, y3) = p3.ToTuple();
        Triangle(x1, y1, x2, y2, x3, y3, color, thickness, blend, opacity);
    }

    public static void Cross(double x, double y, double size,
        Color? color = null,
        int thickness = 1,
        BlendMode blend = BlendMode.None, double opacity = 1)
    {
        Line(x - size / 2, y, size, 0,
            color, thickness, blend, opacity);
        Line(x, y - size / 2, 0, size,
            color, thickness, blend, opacity);
    }

    public static void Blackout(double opacity = 1.0, Color? color = null)
        => Graphics.Blackout(opacity, color);
    public static void Fill(Color color)
        => Blackout(1.0, color);

    public static IFont DefaultFont
    {
        get => field ?? Graphics.DefaultFont;
        set;
    } = null;
    public static (int width, int height) TextSize(object text)
        => DefaultFont.Measure(text.ToString() ?? "");
    public static int FontSize()
        => TextSize("Aあ").height;

    public static (int width, int height) DefaultTextSize(object text, int size = 16)
    {
        var (w, h) = Graphics.MeasureText(text.ToString() ?? "", size);
        return (w, h);
    }
    public static void Text(double x, double y, object? text)
        => Text(x, y, text, Color.White);
    public static void Text(double x, double y, object? text,
        Color color,
        ReferencePoint point = ReferencePoint.TopLeft,
        Color? edgecolor = null,
        BlendMode blend = BlendMode.None, double opacity = 1)
        => DefaultFont.Draw(x, y, text?.ToString() ?? "", color, point, edgecolor, blend, opacity);
    public static void Text(double x, double y, object text,
        Color color,
        Color edgecolor,
        ReferencePoint point = ReferencePoint.TopLeft,
        BlendMode blend = BlendMode.None, double opacity = 1)
        => Text(x, y, text, color, point, edgecolor, blend, opacity);
    public static void DefaultText(double x, double y, object text,
        Color? color = null,
        ReferencePoint point = ReferencePoint.TopLeft,
        BlendMode blend = BlendMode.None, double opacity = 1)
        => Graphics.Text(x, y, text.ToString() ?? "", 16,
            new DrawOptions
            {
                Color = color,
                Blend = blend,
                Opacity = opacity,
                Point = point
            });

    public static void Polygon(IEnumerable<(double x, double y)> points,
        Color? color = null,
        int thickness = 0,
        BlendMode blend = BlendMode.None, double opacity = 1)
        => Polygon(points.Select(p => new LayoutUtil.Point(p.x, p.y)),
            color, thickness, blend, opacity);
    public static void Polygon(IEnumerable<LayoutUtil.Point> points,
        Color? color = null,
        int thickness = 0,
        BlendMode blend = BlendMode.None, double opacity = 1)
    {
        var pts = points.ToArray();
        for (int i = 0; i < pts.Length; i++)
        {
            var (x1, y1) = pts[i].ToTuple();
            var (x2, y2) = pts[(i + 1) % pts.Length].ToTuple();
            Line(x1, y1, x2 - x1, y2 - y1,
                color, thickness, blend, opacity);
        }
        if (thickness == 0)
        {
            // 簡易的に三角形分割で塗りつぶし
            var (x0, y0) = pts[0].ToTuple();
            for (int i = 1; i < pts.Length - 1; i++)
            {
                var (x1, y1) = pts[i].ToTuple();
                var (x2, y2) = pts[i + 1].ToTuple();
                Triangle(x0, y0, x1, y1, x2, y2,
                    color, thickness: 0, blend, opacity);
            }
        }
    }
    /// <summary>
    /// 矩形領域にグラデーションを描く。回転角が0/90/180/270度に近いときは列/行単位で塗る高速パスを使い、
    /// それ以外は各ピクセルを回転方向へ射影して位置を求める汎用パス（低速）を使う。
    /// </summary>
    public static void Gradation(int x, int y, int width, int height, Gradation grad, double rotate = 0, ColorSpace? colorSpace = null)
    {
        if (width <= 0 || height <= 0) return;

        // 回転を 0..360 に正規化
        double rmod = Math.Abs((rotate % 360 + 360) % 360);
        const double EPS = 1e-6;
        var cs = colorSpace ?? ColorSpace.RGB;
        // 0度/360度に近い（列単位の高速パス）
        if (Math.Abs(rmod) < EPS || Math.Abs(rmod - 360.0) < EPS)
        {
            if (width == 1)
            {
                var c = grad.GetColor(0.5f, cs);
                Box(x, y, 1, height, c);
                return;
            }
            for (int i = 0; i < width; i++)
            {
                float pos = (float)i / (width - 1);
                var color = grad.GetColor(pos, cs);
                Box(x + i, y, 1, height, color);
            }
            return;
        }

        // 180度に近い（列単位だが反転）
        if (Math.Abs(rmod - 180.0) < EPS)
        {
            if (width == 1)
            {
                var c = grad.GetColor(0.5f, cs);
                Box(x, y, 1, height, c);
                return;
            }
            for (int i = 0; i < width; i++)
            {
                float pos = 1f - (float)i / (width - 1);
                var color = grad.GetColor(pos, cs);
                Box(x + i, y, 1, height, color);
            }
            return;
        }

        // 90度または270度に近い（行単位の高速パス）
        if (Math.Abs(rmod - 90.0) < EPS || Math.Abs(rmod - 270.0) < EPS)
        {
            bool reversed = Math.Abs(rmod - 270.0) < EPS;
            if (height == 1)
            {
                var c = grad.GetColor(0.5f, cs);
                Box(x, y, width, 1, c);
                return;
            }
            for (int j = 0; j < height; j++)
            {
                float pos = (float)j / (height - 1);
                if (reversed) pos = 1f - pos;
                var color = grad.GetColor(pos, cs);
                Box(x, y + j, width, 1, color);
            }
            return;
        }

        // 汎用パス：各ピクセルを射影して正規化する（既存実装）
        double rad = rotate * Math.PI / 180.0;
        double dx = Math.Cos(rad);
        double dy = Math.Sin(rad);

        double p00 = 0.0;                       // (0,0)
        double p10 = width * dx;                // (width,0)
        double p01 = height * dy;               // (0,height)
        double p11 = width * dx + height * dy;  // (width,height)

        double minProj = Math.Min(Math.Min(p00, p10), Math.Min(p01, p11));
        double maxProj = Math.Max(Math.Max(p00, p10), Math.Max(p01, p11));
        double range = maxProj - minProj;
        if (Math.Abs(range) < 1e-9) range = 1.0; // ゼロ除算回避

        for (int j = 0; j < height; j++)
        {
            for (int i = 0; i < width; i++)
            {
                double px = i + 0.5;
                double py = j + 0.5;
                double proj = px * dx + py * dy;
                double t = (proj - minProj) / range;
                if (t < 0) t = 0;
                else if (t > 1) t = 1;
                var color = grad.GetColor((float)t, cs);
                Box(x + i, y + j, 1, 1, color);
            }
        }
    }

    public static Texture MakeTexture(Action drawAction, int width = 0, int height = 0)
    {
        // width/height が 0 のときは画面サイズいっぱいのレンダーターゲットにする、という
        // 引数の既定値(0)が示す意図を尊重する。
        int w = width > 0 ? width : AstrumCore.Width;
        int h = height > 0 ? height : AstrumCore.Height;
        // Texture(Size, Action) は IGamePlatform.CreateTexture を経由して drawAction を
        // レンダーターゲットへ実際に焼き込む（TextureCathe.Get と同じ経路）。
        // 以前はここが new("") のスタブで、drawAction も width/height も無視して
        // 空のテクスチャを返していた。
        return new Texture(new LayoutUtil.Size(w, h), drawAction);
    }

    public static double DefaultScale { get; set; } = 1.0;
}