using System.Diagnostics;

using AstrumLoom;

using SkiaSharp;

namespace Sandbox;

/// <summary>
/// 技術検証: 「AstrumLoomの外の描画モジュール（ここではSkiaSharp）に絵を焼かせて、
/// 出来上がったピクセルだけ raylib のテクスチャとして取り込む」経路が成立するかの実験。
/// 特に「フォント（文字装飾）をSkiaに任せられるか」を主眼に、経路を2通り測って比較する。
///
/// AstrumLoom.Core / AstrumLoom.RayLib は SkiaSharp を一切知らない。知っているのは
/// <see cref="IGamePlatform.LoadTextureFromMemory"/>（PNG等エンコード済みバイト列）と
/// <see cref="IGamePlatform.LoadTextureFromPixels"/>（生RGBA32、エンコード無し）という
/// 2つの口だけ。このシーンはその使用例＝Skia側の実装。
///
/// 手順（共通）:
///   1) SkiaSharp の SKSurface にオフスクリーンで絵を描く
///      （グラデーション文字＋ドロップシャドウ＋本物のガウスぼかし。
///        raylib/AstrumLoomの手描き文字装飾—DrawGradの白文字焼き＋行ごと着色、
///        Draw()のedgeThicknessによる8方向×N回の文字列再描画—でやると面倒な部類）
///   2a) PNG経路: SKImage.Encode → new Texture(bytes, ".png") → LoadImageFromMemory
///   2b) 生ピクセル経路: SKBitmap.Bytes(RGBA32) → new Texture(w, h, bytes) → GenImageColor+UpdateTexture
///   3) あとは普通の Texture として毎フレーム描くだけ（両経路とも「焼くのは1回、使うのは毎フレーム」が前提）
///
/// [R] で再生成（色相とぼかし量を変えて焼き直す）。両経路の所要時間を並べて画面に出す。
/// 現バックエンドが raylib でなければ（DxLibでは両方とも未対応）その旨を表示する。
/// </summary>
internal sealed class SkiaDemoScene : Scene
{
    private const int CanvasW = 640;
    private const int CanvasH = 360;

    private Texture? _skiaTexture;
    private int _seed = 0;
    private double _renderMs = -1;    // SKSurfaceへ描くだけの時間（両経路共通）
    private double _pngPathMs = -1;   // ↑ + PNGエンコード + LoadTextureFromMemory
    private double _rawPathMs = -1;   // ↑(render) + RGBA32取り出し + LoadTextureFromPixels
    private string? _unsupportedMessage;

    public override void Enable()
    {
        base.Enable();
        _unsupportedMessage = null;
        // 1回目はSKTypeface解決やブラーのシェーダー/内部バッファ初期化が乗って重く出ることが多いので、
        // ウォームアップとして1回捨てる。表示・計測は定常状態（2回目）から。
        Regenerate();
        Regenerate();
    }

    public override void Disable()
    {
        base.Disable();
        _skiaTexture?.Dispose();
        _skiaTexture = null;
    }

    public override void Update()
    {
        if (Key.R.Push()) Regenerate();
    }

    /// <summary>SkiaSharpで同じ絵を1回描き、PNG経路と生ピクセル経路の両方の所要時間を計測する。画面に出すテクスチャは生ピクセル経路の結果を使う。</summary>
    private void Regenerate()
    {
        _seed++;
        try
        {
            var swRender = Stopwatch.StartNew();
            using var image = RenderWithSkia(_seed);
            swRender.Stop();
            _renderMs = swRender.Elapsed.TotalMilliseconds;

            // 経路A: PNGエンコードを経由（今の new Texture(byte[], ext) の使い方）
            var swPng = Stopwatch.StartNew();
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
            {
                byte[] png = data.ToArray();
                using var pngTexTiming = new Texture(png, ".png");
                _ = pngTexTiming; // 計測専用。画面には出さずすぐ破棄する。
            }
            swPng.Stop();
            _pngPathMs = _renderMs + swPng.Elapsed.TotalMilliseconds;

            // 経路B: エンコード無し。SKBitmapの生ピクセルをそのままGPUへ。
            var swRaw = Stopwatch.StartNew();
            using var bitmap = SKBitmap.FromImage(image);
            byte[] raw = bitmap.Bytes; // RGBA8888、premultiplied
            _skiaTexture?.Dispose();
            _skiaTexture = new Texture(bitmap.Width, bitmap.Height, raw);
            swRaw.Stop();
            _rawPathMs = _renderMs + swRaw.Elapsed.TotalMilliseconds;

            _unsupportedMessage = null;
        }
        catch (NotSupportedException ex)
        {
            // DxLibバックエンドではここに来る。実験の主旨はraylib側なので、エラーで落とさず案内に留める。
            _unsupportedMessage = ex.Message;
        }
    }

    /// <summary>SkiaSharp側の実装。AstrumLoomは戻り値のSKImageのピクセルしか使わないので、この中身は完全に独立している。</summary>
    private static SKImage RenderWithSkia(int seed)
    {
        using var surface = SKSurface.Create(new SKImageInfo(CanvasW, CanvasH, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(16, 18, 28, 255));

        // 背景: ぼかし付きの光る円（本物のガウスぼかし。raylibだけでやるならオフスクリーン多重合成が要る）
        float hue = (seed * 47) % 360;
        using (var glow = new SKPaint
        {
            IsAntialias = true,
            Color = SKColor.FromHsv(hue, 70, 100),
            ImageFilter = SKImageFilter.CreateBlur(24 + (seed % 5) * 6, 24 + (seed % 5) * 6),
        })
        {
            canvas.DrawCircle(CanvasW * 0.24f, CanvasH * 0.5f, 90, glow);
            canvas.DrawCircle(CanvasW * 0.82f, CanvasH * 0.7f, 60, glow);
        }

        // 本題: グラデーション文字＋ドロップシャドウ
        // AstrumLoomでは DrawGrad が「白文字をオフスクリーンに焼いてマスクにし行ごとに色を乗せる」という
        // 手作りの近似でやっている処理を、Skiaならシェーダー1個＋ImageFilter1個で済ませられる。
        using var font = new SKFont(SKTypeface.FromFamilyName("Yu Gothic UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright), 56);
        string text = "Skia -> raylib";

        using (var shadow = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(0, 0, 0, 160),
            ImageFilter = SKImageFilter.CreateBlur(6, 6),
        })
        {
            canvas.DrawText(text, 46, 176, font, shadow);
        }

        using (var grad = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(40, 120), new SKPoint(600, 200),
                [SKColor.FromHsv(hue, 85, 100), SKColor.FromHsv((hue + 80) % 360, 85, 100)],
                SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawText(text, 40, 170, font, grad);
        }

        using var sub = new SKFont(SKTypeface.FromFamilyName("Yu Gothic UI"), 18);
        using var subPaint = new SKPaint { IsAntialias = true, Color = new SKColor(200, 210, 230) };
        canvas.DrawText($"PNG経路 と 生ピクセル経路 を比較 (seed {seed})", 40, 220, sub, subPaint);

        return surface.Snapshot();
    }

    public override void Draw()
    {
        Drawing.Fill(new Color(9, 12, 24));
        Drawing.Text(20, 16, "SkiaSharp 焼き込みテクスチャ / フォント装飾の連携実験", Color.White,
            edgecolor: new Color(10, 14, 28));
        DemoUi.Note(20, 52, 900,
            "[R] 再生成。同じ絵をPNG経由(LoadTextureFromMemory)と生ピクセル(LoadTextureFromPixels)の両方で焼いて所要時間を比較する。",
            new Color(180, 198, 226));

        double x = (AstrumCore.Width - CanvasW) / 2.0;
        double y = 100;

        if (_unsupportedMessage != null)
        {
            Drawing.Box(x, y, CanvasW, CanvasH, new Color(40, 20, 20));
            DemoUi.Notes(x + 20, y + 20, CanvasW - 40, new Color(255, 160, 160),
                "このバックエンドでは未対応:", _unsupportedMessage,
                "--backend raylib を付けて起動し直してください。");
            return;
        }

        Drawing.Box(x - 4, y - 4, CanvasW + 8, CanvasH + 8, new Color(40, 46, 70));
        _skiaTexture?.Draw(x, y); // 表示しているのは生ピクセル経路の結果

        double resultY = y + CanvasH + 16;
        if (_renderMs >= 0)
        {
            DemoUi.Notes(x, resultY, CanvasW + 260, new Color(150, 220, 180),
                $"SKSurface描画のみ: {_renderMs:F2} ms",
                $"PNG経路合計（描画+エンコード+LoadTextureFromMemory）: {_pngPathMs:F2} ms",
                $"生ピクセル経路合計（描画+抽出+LoadTextureFromPixels）: {_rawPathMs:F2} ms  ← 画面に出ているのはこっち",
                $"エンコード/デコードぶんの差: {(_pngPathMs - _rawPathMs):F2} ms");
        }
        else
        {
            DemoUi.Note(x, resultY, CanvasW, "計測中...", new Color(150, 220, 180));
        }
    }
}
