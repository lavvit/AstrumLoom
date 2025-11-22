using System.Numerics;

using Raylib_cs;

using static Raylib_cs.Raylib;

using RayBlend = Raylib_cs.BlendMode;
using RColor = Raylib_cs.Color;

namespace AstrumLoom.RayLib;

// ================================
//  IGraphics 実装
// ================================

internal sealed class RayLibGraphics : IGraphics
{
    public RayLibGraphics() => DefaultFont = CreateFont(new FontSpec("", 24));

    private (int w, int h) _size;
    public LayoutUtil.Size Size
    {
        get
        {
            if (_size.w <= 0 || _size.h <= 0)
            {
                _size.w = GetScreenWidth();
                _size.h = GetScreenHeight();
            }
            return new LayoutUtil.Size(_size.w, _size.h);
        }
    }
    public ITexture LoadTexture(string path)
    {
        // 失敗時は Raylib が自前のエラーを出すので、ここではそのまま投げる
        var tex = Raylib.LoadTexture(path);
        return new RayLibTexture(tex);
    }

    public void BeginFrame() => BeginDrawing();

    public void Clear(Color color)
    {
        var rc = ToRayColor(color);
        ClearBackground(rc);
    }

    public void EndFrame() => EndDrawing();

    public void Blackout(double opacity = 1.0, Color? color = null)
    {
        var c = color ?? Color.Black;
        DrawRectangle(0, 0, GetScreenWidth(), GetScreenHeight(), ToRayColor(c, opacity));
    }

    // Updated: use DrawOptions to match IGraphics
    public void Line(double x, double y, double dx, double dy,
        DrawOptions options)
    {
        int thickness = Math.Max(1, options.Thickness);
        double opacity = Math.Clamp(options.Opacity, 0.0, 1.0);
        var col = ToRayColor(options.Color ?? Color.White, opacity);
        var a = new Vector2((float)x, (float)y);
        var b = new Vector2((float)(x + dx), (float)(y + dy));
        DrawLineEx(a, b, thickness, col);
    }

    public void Box(double x, double y, double width, double height,
        DrawOptions options)
    {
        int thickness = Math.Max(1, options.Thickness);
        double opacity = Math.Clamp(options.Opacity, 0.0, 1.0);
        var col = ToRayColor(options.Color ?? Color.White, opacity);
        var rect = new Raylib_cs.Rectangle((float)x, (float)y, (float)width, (float)height);

        if (options.Fill) DrawRectangleRec(rect, col);
        else DrawRectangleLinesEx(rect, thickness, col);
    }

    public void Circle(double x, double y, double radius,
        DrawOptions options, int segments = 64)
    {
        int thickness = Math.Max(1, options.Thickness);
        double opacity = Math.Clamp(options.Opacity, 0.0, 1.0);
        var col = ToRayColor(options.Color ?? Color.White, opacity);
        if (options.Fill) DrawCircleV(new Vector2((float)x, (float)y), (float)radius, col);
        else DrawCircleLines((int)Math.Round(x), (int)Math.Round(y), (float)radius, col);
    }

    public void Oval(double x, double y, double rx, double ry,
        DrawOptions options, int segments = 64)
    {
        int thickness = Math.Max(1, options.Thickness);
        double opacity = Math.Clamp(options.Opacity, 0.0, 1.0);
        var col = ToRayColor(options.Color ?? Color.White, opacity);
        if (options.Fill) DrawEllipse((int)x, (int)y, (int)rx, (int)ry, col);
        else DrawEllipseLines((int)x, (int)y, (int)rx, (int)ry, col);
    }

    public void Triangle(double x1, double y1, double x2, double y2, double x3, double y3,
        DrawOptions options)
    {
        int thickness = Math.Max(1, options.Thickness);
        double opacity = Math.Clamp(options.Opacity, 0.0, 1.0);
        var col = ToRayColor(options.Color ?? Color.White, opacity);

        var p1 = new Vector2((float)x1, (float)y1);
        var p2 = new Vector2((float)x2, (float)y2);
        var p3 = new Vector2((float)x3, (float)y3);

        // 重心を計算
        float cx = (p1.X + p2.X + p3.X) / 3f;
        float cy = (p1.Y + p2.Y + p3.Y) / 3f;

        // 各点の角度（重心基準）
        double Angle(Vector2 p) => Math.Atan2((double)p.Y - cy, (double)p.X - cx);

        var pts = new[] { p1, p2, p3 };
        // 降順にソートすると時計回りになる
        Array.Sort(pts, (a, b) => Angle(b).CompareTo(Angle(a)));

        if (options.Fill)
        {
            DrawTriangle(pts[0], pts[1], pts[2], col);
        }

        // 枠線は常に描画（おまけ）
        DrawLineEx(pts[0], pts[1], thickness, col);
        DrawLineEx(pts[1], pts[2], thickness, col);
        DrawLineEx(pts[2], pts[0], thickness, col);
    }

    public void Text(double x, double y, string text,
        int fontSize,
        DrawOptions options)
    {
        if (string.IsNullOrEmpty(text)) return;

        double opacity = Math.Clamp(options.Opacity, 0.0, 1.0);
        var fg = ToRayColor(options.Color ?? Color.White, opacity);

        var size = MeasureTextInternal(text, fontSize);
        var anchorOffset = LayoutUtil.GetAnchorOffset(options.Point, size.X, size.Y);
        var pnt = new LayoutUtil.Point(x, y) - anchorOffset;
        var pos = new Vector2((float)pnt.X, (float)pnt.Y);

        // Raylib の DrawTextEx を使う
        DrawTextEx(GetFontDefault(), text, pos, fontSize, 1f, fg);
    }

    public (int Width, int Height) MeasureText(string text, int fontSize = 20)
    {
        var v = MeasureTextInternal(text, fontSize);
        return ((int)v.X, (int)v.Y);
    }

    private static Vector2 MeasureTextInternal(string text, int fontSize)
        => MeasureTextEx(GetFontDefault(), text ?? "", fontSize, 1f);

    public IFont DefaultFont { get; }
    public IFont CreateFont(FontSpec spec)
        => new RayLibFont(spec);

    internal static RayBlend GetBlendMode(BlendMode mode) => mode switch
    {
        BlendMode.None => RayBlend.Alpha,
        BlendMode.Add => RayBlend.Additive,
        BlendMode.Subtract => RayBlend.SubtractColors,
        BlendMode.Multiply => RayBlend.Multiplied,
        BlendMode.Reverse => RayBlend.Custom,
        _ => RayBlend.Alpha,
    };

    // Color helper
    internal static RColor ToRayColor(Color c, double opacity = 1.0)
    {
        int a = (int)Math.Clamp(Math.Round(c.A * opacity), 0, 255);
        return new RColor(c.R, c.G, c.B, (byte)a);
    }

    internal static void SetOptions(DrawOptions options)
    {
        double opacity = Math.Clamp(options.Opacity, 0.0, 1.0);
        var color = options.Color ?? Color.White;
        opacity *= color.A / 255.0;

        if (options.Blend != BlendMode.None)
            BeginBlendMode(GetBlendMode(options.Blend));
    }
    internal static void ResetOptions(DrawOptions options)
    {
        double opacity = Math.Clamp(options.Opacity, 0.0, 1.0);
        var color = options.Color ?? Color.White;
        if (options.Blend != BlendMode.None)
            EndBlendMode();
    }
}