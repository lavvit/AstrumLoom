using System.Numerics;

using static Raylib_cs.Raylib;

using RayBlend = Raylib_cs.BlendMode;
using RColor = Raylib_cs.Color;

namespace AstrumLoom.RayLib;

// ================================
//  IGraphics 実装
// ================================

/// <summary>
/// IGraphics の raylib 実装。プリミティブ描画・テキスト計測・スクリーンショットなど、
/// Core 側の描画APIを raylib の関数呼び出しへ変換する橋渡し役。
/// </summary>
internal sealed class RayLibGraphics : IGraphics
{
    public RayLibGraphics() => DefaultFont = CreateFont(new FontSpec("", 12));

    private (int w, int h) _size;
    /// <summary>現在の描画先サイズ。未取得時は raylib から実際のスクリーンサイズを取得してキャッシュする。</summary>
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

    public void BeginFrame() => BeginDrawing();

    public void Clear(Color color)
    {
        var rc = ToRayColor(color);
        ClearBackground(rc);
    }

    public void EndFrame() => EndDrawing();

    /// <summary>画面全体を指定色・不透明度で覆います（フェード演出などに使用）。</summary>
    public void Blackout(double opacity = 1.0, Color? color = null)
    {
        var c = color ?? Color.Black;
        DrawRectangle(0, 0, GetScreenWidth(), GetScreenHeight(), ToRayColor(c, opacity));
    }

    /// <summary>始点から相対座標(dx, dy)方向へ線分を描画します。</summary>
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

    /// <summary>矩形を描画します。options.Fill で塗りつぶし/枠線を切り替えます。</summary>
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

    /// <summary>円を描画します。segments は IGraphics のシグネチャ互換のためだけに存在し、raylibの円描画には使用しません。</summary>
    public void Circle(double x, double y, double radius,
        DrawOptions options, int segments = 64)
    {
        int thickness = Math.Max(1, options.Thickness);
        double opacity = Math.Clamp(options.Opacity, 0.0, 1.0);
        var col = ToRayColor(options.Color ?? Color.White, opacity);
        if (options.Fill) DrawCircleV(new Vector2((float)x, (float)y), (float)radius, col);
        else DrawCircleLines((int)Math.Round(x), (int)Math.Round(y), (float)radius, col);
    }

    /// <summary>楕円を描画します（rx, ryはそれぞれ横半径・縦半径）。</summary>
    public void Oval(double x, double y, double rx, double ry,
        DrawOptions options, int segments = 64)
    {
        int thickness = Math.Max(1, options.Thickness);
        double opacity = Math.Clamp(options.Opacity, 0.0, 1.0);
        var col = ToRayColor(options.Color ?? Color.White, opacity);
        if (options.Fill) DrawEllipse((int)x, (int)y, (int)rx, (int)ry, col);
        else DrawEllipseLines((int)x, (int)y, (int)rx, (int)ry, col);
    }

    /// <summary>
    /// 3点で三角形を描画します。raylibのDrawTriangleは頂点の巻き順(時計回り)が前提のため、
    /// 呼び出し側がどんな順で頂点を渡しても正しく描けるよう、重心からの角度でソートしてから描画します。
    /// </summary>
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

    /// <summary>
    /// テキストを描画します。options.Point（アンカー基準点）に応じて描画開始位置を
    /// テキストサイズから逆算し、指定座標がどの基準点に来るかを揃えます。
    /// </summary>
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

    /// <summary>デフォルトフォント・行間1pxでのテキストの描画サイズを計測します。</summary>
    public (int Width, int Height) MeasureText(string text, int fontSize = 20)
    {
        var v = MeasureTextInternal(text, fontSize);
        return ((int)v.X, (int)v.Y);
    }

    private static Vector2 MeasureTextInternal(string text, int fontSize)
        => MeasureTextEx(GetFontDefault(), text ?? "", fontSize, 1f);

    public IFont DefaultFont { get; }
    /// <summary>フォント指定からRayLibFontを生成します。</summary>
    public IFont CreateFont(FontSpec spec)
        => new RayLibFont(spec);

    /// <summary>現在のフレームバッファをファイルに保存します。</summary>
    public bool SaveScreenshot(string path)
    {
        // Raylib は描画命令をバッチに溜めて EndDrawing で初めてフレームバッファへ流す。
        // フレームの途中で呼ばれるここでは、先に流しておかないと
        // ClearBackground 直後の状態（＝真っ黒）を読んでしまう。
        Raylib_cs.Rlgl.DrawRenderBatchActive();

        // Raylib.TakeScreenshot は保存先が作業ディレクトリ基準になってしまうので、
        // 画像を取り出して自分でパスを指定して書き出す。
        var image = LoadImageFromScreen();
        try
        {
            return ExportImage(image, path);
        }
        finally
        {
            UnloadImage(image);
        }
    }

    /// <summary>
    /// Core側のBlendModeをraylibのBlendModeへ変換します。
    /// Screen/Reverseはraylibにプリセットが無いため、SetColorBlend側でrlSetBlendFactorsを使い
    /// OpenGLの合成係数を直接指定してからCustomモードで描画します。
    /// </summary>
    internal static RayBlend GetBlendMode(BlendMode mode) => mode switch
    {
        BlendMode.None => RayBlend.Alpha,
        BlendMode.Add => RayBlend.Additive,
        BlendMode.Subtract => RayBlend.SubtractColors,
        BlendMode.Multiply => RayBlend.Multiplied,
        BlendMode.Screen => RayBlend.Custom,
        BlendMode.Reverse => RayBlend.Custom,
        _ => RayBlend.Alpha,
    };

    // OpenGLの合成係数/式の定数（raylibにプリセットの無いBlendModeをrlSetBlendFactorsで組むために使う）
    private const int GL_ONE = 1;
    private const int GL_ONE_MINUS_SRC_COLOR = 0x0301;
    private const int GL_FUNC_ADD = 0x8006;
    private const int GL_FUNC_SUBTRACT = 0x800A;

    // Color helper
    /// <summary>Core側のColorをraylibのColorへ変換します。opacityはアルファ値に乗算されます。</summary>
    internal static RColor ToRayColor(Color c, double opacity = 1.0)
    {
        int a = (int)Math.Clamp(Math.Round(c.A * opacity), 0, 255);
        return new RColor(c.R, c.G, c.B, (byte)a);
    }

    /// <summary>ブレンドモードまたは不透明度が既定と異なる場合、raylibのブレンドモードを開始します。</summary>
    internal static void SetColorBlend(BlendMode blend, double opacity, Color col)
    {
        if (blend > BlendMode.None || opacity < 1.0)
        {
            // Screen: src + dst*(1-src) = src+dst-src*dst
            // Reverse: src - dst（Subtractの逆方向）
            switch (blend)
            {
                case BlendMode.Screen:
                    Raylib_cs.Rlgl.SetBlendFactors(GL_ONE, GL_ONE_MINUS_SRC_COLOR, GL_FUNC_ADD);
                    break;
                case BlendMode.Reverse:
                    Raylib_cs.Rlgl.SetBlendFactors(GL_ONE, GL_ONE, GL_FUNC_SUBTRACT);
                    break;
            }
            BeginBlendMode(GetBlendMode(blend));
        }
        //if (col != Color.White)
        //    SetDrawBright(col.R, col.G, col.B);
    }
    /// <summary>DrawOptionsの色アルファ・不透明度を合成し、ブレンドモードを適用します。描画前に呼び出します。</summary>
    internal static void SetOptions(DrawOptions option)
    {
        double opacity = Math.Clamp(option.Opacity, 0.0, 1.0);
        var color = option.Color ?? Color.White;
        opacity *= color.A / 255.0;

        SetColorBlend(option.Blend, opacity, color);
    }
    internal static void ResetColorBlend() => EndBlendMode();
    internal static void ResetOptions(DrawOptions option) => ResetColorBlend();
}