using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Text;

using Raylib_cs;

using static Raylib_cs.Raylib;

using RColor = Raylib_cs.Color;

namespace AstrumLoom.RayLib;


// ================================
//  IGraphics 実装
// ================================

internal sealed class RayLibTexture : ITexture
{
    public Texture2D Native { get; }

    public int Width => Native.Width;
    public int Height => Native.Height;

    public RayLibTexture(Texture2D texture)
    {
        Native = texture;
    }
}

internal sealed class RayLibGraphics : IGraphics
{
    public ITexture LoadTexture(string path)
    {
        // 失敗時は Raylib が自前のエラーを出すので、ここではそのまま投げる
        Texture2D tex = Raylib.LoadTexture(path);
        return new RayLibTexture(tex);
    }

    public void UnloadTexture(ITexture texture)
    {
        if (texture is RayLibTexture t)
        {
            Raylib.UnloadTexture(t.Native);
        }
    }

    public void BeginFrame()
    {
        BeginDrawing();
    }

    public void Clear(Color color)
    {
        // AstrumLoom.Color → System.Drawing.Color → Raylib.Color
        var dc = (System.Drawing.Color)color;
        var rc = new RColor(dc.R, dc.G, dc.B, dc.A);
        ClearBackground(rc);
    }

    public void DrawTexture(
        ITexture texture,
        float x, float y,
        float scaleX = 1f,
        float scaleY = 1f,
        float rotationRad = 0f)
    {
        if (texture is not RayLibTexture tex) return;

        // まずは一番シンプルなパス（回転・拡大なし）
        bool noScale =
            Math.Abs(scaleX - 1f) < 0.0001f &&
            Math.Abs(scaleY - 1f) < 0.0001f;
        bool noRotate = Math.Abs(rotationRad) < 0.0001f;

        if (noScale && noRotate)
        {
            Raylib.DrawTexture(tex.Native, (int)x, (int)y, RColor.White);
            return;
        }

        // 拡大 or 回転あり → DrawTextureEx を使う（scale はとりあえず X を採用）
        float rotationDeg = rotationRad * 180f / (float)Math.PI;
        float scale = scaleX; // scaleY は必要ならあとで対応

        DrawTextureEx(
            tex.Native,
            new System.Numerics.Vector2(x, y),
            rotationDeg,
            scale,
            RColor.White);
    }

    public void EndFrame()
    {
        EndDrawing();
    }

    // Color helper
    private static RColor ToRay(Color c, double opacity = 1.0)
    {
        int a = (int)Math.Clamp(Math.Round(c.A * opacity), 0, 255);
        return new RColor(c.R, c.G, c.B, (byte)a);
    }

    public void Blackout(double opacity = 1.0, Color? color = null)
    {
        var c = color ?? Color.Black;
        DrawRectangle(0, 0, GetScreenWidth(), GetScreenHeight(), ToRay(c, opacity));
    }

    // Updated: use DrawOptions to match IGraphics
    public void Line(double x, double y, double dx, double dy,
        DrawOptions options)
    {
        var col = ToRay(options.Color ?? Color.White, options.Opacity);
        var a = new Vector2((float)x, (float)y);
        var b = new Vector2((float)(x + dx), (float)(y + dy));
        DrawLineEx(a, b, Math.Max(1, options.Thickness), col);
    }

    public void Box(double x, double y, double width, double height,
        DrawOptions options)
    {
        var col = ToRay(options.Color ?? Color.White, options.Opacity);
        var rect = new Raylib_cs.Rectangle((float)x, (float)y, (float)width, (float)height);

        if (options.Fill) DrawRectangleRec(rect, col);
        else DrawRectangleLinesEx(rect, Math.Max(1, options.Thickness), col);
    }

    public void Circle(double x, double y, double radius,
        DrawOptions options, int segments = 64)
    {
        var col = ToRay(options.Color ?? Color.White, options.Opacity);
        if (options.Fill) DrawCircleV(new Vector2((float)x, (float)y), (float)radius, col);
        else DrawCircleLines((int)Math.Round(x), (int)Math.Round(y), (float)radius, col);
    }

    public void Oval(double x, double y, double rx, double ry,
        DrawOptions options, int segments = 64)
    {
        var col = ToRay(options.Color ?? Color.White, options.Opacity);
        if (options.Fill) DrawEllipse((int)x, (int)y, (int)rx, (int)ry, col);
        else DrawEllipseLines((int)x, (int)y, (int)rx, (int)ry, col);
    }

    public void Triangle(double x1, double y1, double x2, double y2, double x3, double y3,
        DrawOptions options)
    {
        var col = ToRay(options.Color ?? Color.White, options.Opacity);
        if (options.Fill)
        {
            DrawTriangle(
                new Vector2((float)x1, (float)y1),
                new Vector2((float)x2, (float)y2),
                new Vector2((float)x3, (float)y3),
                col);
        }
        // 枠線はおまけ
        DrawLineEx(new((float)x1, (float)y1), new((float)x2, (float)y2), Math.Max(1, options.Thickness), col);
        DrawLineEx(new((float)x2, (float)y2), new((float)x3, (float)y3), Math.Max(1, options.Thickness), col);
        DrawLineEx(new((float)x3, (float)y3), new((float)x1, (float)y1), Math.Max(1, options.Thickness), col);
    }

    public void Cross(double x, double y, double size = 20, Color? color = null,
        double opacity = 1.0, int thickness = 1, BlendMode blend = BlendMode.None)
    {
        Box(x - size / 2, y - (thickness - 1) / 2.0, size, thickness, new DrawOptions
        {
            Color = color,
            Opacity = opacity,
            Fill = true,
            Thickness = thickness,
            Blend = blend
        });
        Box(x - (thickness - 1) / 2.0, y - size / 2.0, thickness, size, new DrawOptions
        {
            Color = color,
            Opacity = opacity,
            Fill = true,
            Thickness = thickness,
            Blend = blend
        });
    }

    public void Text(double x, double y, string text,
        int fontSize,
        DrawOptions options)
    {
        if (string.IsNullOrEmpty(text)) return;

        var fg = ToRay(options.Color ?? Color.White, options.Opacity);

        var size = MeasureTextInternal(text, fontSize);
        var anchorOffset = AnchorOffset(options.Point, size);
        var pos = new Vector2((float)x, (float)y) - anchorOffset;

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

    private static Vector2 AnchorOffset(ReferencePoint anchor, Vector2 size) => anchor switch
    {
        ReferencePoint.TopLeft => Vector2.Zero,
        ReferencePoint.TopCenter => new(size.X / 2f, 0),
        ReferencePoint.TopRight => new(size.X, 0),
        ReferencePoint.CenterLeft => new(0, size.Y / 2f),
        ReferencePoint.Center => new(size.X / 2f, size.Y / 2f),
        ReferencePoint.CenterRight => new(size.X, size.Y / 2f),
        ReferencePoint.BottomLeft => new(0, size.Y),
        ReferencePoint.BottomCenter => new(size.X / 2f, size.Y),
        ReferencePoint.BottomRight => new(size.X, size.Y),
        _ => Vector2.Zero
    };
}