using static DxLibDLL.DX;

namespace AstrumLoom.DXLib;

internal sealed class DxLibGraphics : IGraphics
{
    public DxLibGraphics() =>
        // ここではとりあえず「Default」の 24px ぐらいを作っておく
        DefaultFont = CreateFont(new FontSpec("", 24));

    public ITexture LoadTexture(string path) => new DxLibTexture(path);

    public void BeginFrame()
    {
        // 今は特に何もしない（必要ならここで状態リセット）
    }

    public void Clear(Color color)
    {
        SetDrawScreen(DX_SCREEN_BACK);
        // AstrumLoom.Color → System.Drawing.Color にキャストできるのでそれを使う
        var c = (System.Drawing.Color)color;
        SetBackgroundColor(c.R, c.G, c.B);
        ClearDrawScreen();
    }

    public void EndFrame() => ScreenFlip();

    public void Blackout(double opacity = 1.0, Color? color = null)
    {
        GetWindowSize(out int w, out int h);
        Box(0, 0, w, h, new()
        {
            Color = color ?? Color.Black,
            Opacity = opacity
        });
    }

    public void Line(double x, double y, double dx, double dy,
        DrawOptions options)
    {
        var use = options.Color ?? Color.White;
        int c = ToDxColor(use);
        int thickness = Math.Max(1, options.Thickness);
        double opacity = Math.Clamp(options.Opacity * (use.A / 255.0), 0.0, 1.0);
        SetDrawBlendMode(GetBlendMode(options.Blend), (int)(255.0 * opacity));
        DrawLineAA((float)x, (float)y, (float)(x + dx), (float)(y + dy), (uint)c, thickness);
        SetDrawBlendMode((int)BlendMode.None, 255);
    }

    public void Box(double x, double y, double width, double height,
        DrawOptions options)
    {
        var use = options.Color ?? Color.White;
        int c = ToDxColor(use);
        int thickness = Math.Max(1, options.Thickness);
        double opacity = Math.Clamp(options.Opacity * (use.A / 255.0), 0.0, 1.0);
        SetDrawBlendMode(GetBlendMode(options.Blend), (int)(255.0 * opacity));
        DrawBoxAA((float)x, (float)y, (float)(x + width), (float)(y + height),
                  (uint)c, options.Fill ? TRUE : FALSE, thickness);
        SetDrawBlendMode((int)BlendMode.None, 255);
    }

    public void Circle(double x, double y, double radius,
        DrawOptions options, int segments = 64)
    {
        var use = options.Color ?? Color.White;
        int c = ToDxColor(use);
        int thickness = Math.Max(1, options.Thickness);
        double opacity = Math.Clamp(options.Opacity * (use.A / 255.0), 0.0, 1.0);
        SetDrawBlendMode(GetBlendMode(options.Blend), (int)(255.0 * opacity));
        DrawCircleAA((float)x, (float)y, (float)radius, segments,
                (uint)c, options.Fill ? TRUE : FALSE, thickness);
        SetDrawBlendMode((int)BlendMode.None, 255);
    }

    public void Oval(double x, double y, double rx, double ry,
        DrawOptions options, int segments = 64)
    {
        var use = options.Color ?? Color.White;
        int c = ToDxColor(use);
        int thickness = Math.Max(1, options.Thickness);
        double opacity = Math.Clamp(options.Opacity * (use.A / 255.0), 0.0, 1.0);
        SetDrawBlendMode(GetBlendMode(options.Blend), (int)(255.0 * opacity));
        DrawOvalAA((float)x, (float)y, (float)rx, (float)ry, segments,
            (uint)c, options.Fill ? TRUE : FALSE, thickness);
        SetDrawBlendMode((int)BlendMode.None, 255);
    }

    public void Triangle(double x1, double y1, double x2, double y2, double x3, double y3,
        DrawOptions options)
    {
        var use = options.Color ?? Color.White;
        int c = ToDxColor(use);
        int thickness = Math.Max(1, options.Thickness);
        double opacity = Math.Clamp(options.Opacity * (use.A / 255.0), 0.0, 1.0);
        SetDrawBlendMode(GetBlendMode(options.Blend), (int)(255.0 * opacity));
        DrawTriangleAA((float)x1, (float)y1, (float)x2, (float)y2, (float)x3, (float)y3,
                       (uint)c, options.Fill ? TRUE : FALSE, thickness);
        SetDrawBlendMode((int)BlendMode.None, 255);
    }

    // Text
    public void Text(double x, double y, string text, int fontSize,
        DrawOptions options)
    {
        var use = options.Color ?? Color.White;
        int c = ToDxColor(use);
        int thickness = Math.Max(1, options.Thickness);
        double opacity = Math.Clamp(options.Opacity * (use.A / 255.0), 0.0, 1.0);

        // まずフォントサイズだけ確定
        EnsureFontSize(fontSize);

        // サイズ計測（SetFontSize はもう中では呼ばない）
        var (w, h) = MeasureTextInternal(text);

        var offset = LayoutUtil.GetAnchorOffset(options.Point, w, h);
        float x1 = (float)(x - offset.X);
        float y1 = (float)(y - offset.Y);

        SetFontThickness(thickness);
        SetDrawBlendMode(GetBlendMode(options.Blend), (int)(255.0 * opacity));

        // 縁取りは「ずらし描き」で実装してもいいし、最初はナシでもOK
        DrawString((int)x1, (int)y1, text, (uint)c);

        SetDrawBlendMode((int)BlendMode.None, 255);

        SetFontThickness(1); // リセット
    }

    // ★フォントサイズ変更をしない内部版
    private static (int Width, int Height) MeasureTextInternal(string text)
    {
        GetDrawStringSize(out int w, out int h, out _, text, text.Length);
        return (w, h);
    }

    public (int Width, int Height) MeasureText(string text, int fontSize = 16)
    {
        EnsureFontSize(fontSize);
        return MeasureTextInternal(text);
    }

    private int _currentFontSize = -1;
    private void EnsureFontSize(int fontSize)
    {
        if (_currentFontSize == fontSize) return; // 変わらないなら何もしない
        SetFontSize(fontSize);
        _currentFontSize = fontSize;
    }

    public IFont DefaultFont { get; }
    public IFont CreateFont(FontSpec spec)
        => new DxLibFont(spec);

    internal static int GetBlendMode(BlendMode mode) => mode switch
    {
        BlendMode.None => DX_BLENDMODE_ALPHA,
        BlendMode.Add => DX_BLENDMODE_ADD,
        BlendMode.Subtract => DX_BLENDMODE_SUB,
        BlendMode.Multiply => DX_BLENDMODE_MUL,
        _ => DX_BLENDMODE_NOBLEND,
    };

    // ToDxColor は MultiBeat のやつをそのまま持ってきてOK
    internal static int ToDxColor(Color col)
        => (int)GetColor(col.R, col.G, col.B);
}