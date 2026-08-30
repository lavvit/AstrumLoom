using AstrumLoom;

using SkiaSharp;

namespace Sandbox;

/// <summary>
/// 技術検証その2: グラデーション＋縁取りの「装飾文字」を、
///   A) AstrumLoom標準（RayLibFont.DrawGrad、FreeType。毎フレーム焼き直す）
///   B) Skia + SkiaTextCache（内容が変わった時だけ焼き直し、以降はテクスチャを貼るだけ）
/// で並べて、実際のフレームコストを比較する。
///
/// [Space] で文字列を切り替える（キャッシュを意図的に無効化する）。
/// 何も押さなければ、Bは1回焼いたきり毎フレームのコストがほぼゼロになることを
/// HitStreak（再利用し続けたフレーム数）とAstrumCoreのFPS表示で確認できる。
/// </summary>
internal sealed class SkiaTextCompareDemoScene : Scene
{
    private static readonly string[] Titles =
    [
        "灼熱の乱れ打ち",
        "SkiaSharp -> raylib",
        "AstrumLoom Text Cache",
        "夏色ファンファーレ",
    ];
    private int _titleIndex = 0;

    private IFont? _nativeFont;
    private readonly SkiaTextCache _skiaCache = new();

    private double _nativeMsThisFrame = -1;
    private double _nativeMsAvg = -1;
    private readonly System.Diagnostics.Stopwatch _sw = new();

    private static readonly Gradation NativeGradation = new(
    [
        Color.FromHex("#ffe066"),
        Color.FromHex("#ff6b6b"),
    ]);

    public override void Enable()
    {
        base.Enable();
        // 縁取り(Edge=4)込みのフォント。AstrumLoom.Draw()は縁取りありだと
        // 8方向×Edge回、文字列全体を再描画する（RayLibFont.cs参照）。今回は
        // DrawGradを使うのでその力技そのものではないが、「毎フレーム描き直す」点は共通。
        _nativeFont = FontHandle.Create("Yu Gothic UI", 56, edge: 4, bold: true);
    }

    public override void Disable()
    {
        base.Disable();
        _nativeFont?.Dispose();
        _nativeFont = null;
        _skiaCache.Dispose();
    }

    public override void Update()
    {
        if (Key.Space.Push())
            _titleIndex = (_titleIndex + 1) % Titles.Length;
    }

    public override void Draw()
    {
        Drawing.Fill(new Color(9, 12, 24));
        Drawing.Text(20, 16, "装飾文字の描画コスト比較 / AstrumLoom標準 vs Skia+キャッシュ", Color.White,
            edgecolor: new Color(10, 14, 28));
        DemoUi.Note(20, 52, 1000,
            "[Space] 文字列を切り替える（右側のキャッシュを無効化させる）。左は毎フレーム焼き直し、右は変化時だけ焼き直す。",
            new Color(180, 198, 226));

        string title = Titles[_titleIndex];

        DrawNativeCard(60, 130, title);
        DrawSkiaCard(660, 130, title);

        DemoUi.Notes(60, 420, 1120, new Color(200, 210, 230),
            $"A) AstrumLoom標準 DrawGrad（毎フレーム焼き直し）: このフレーム {_nativeMsThisFrame:F3} ms" +
                (_nativeMsAvg >= 0 ? $"  /  直近平均 {_nativeMsAvg:F3} ms" : ""),
            $"B) Skia + SkiaTextCache: 直近の焼き直し {(_skiaCache.LastBakeMs >= 0 ? $"{_skiaCache.LastBakeMs:F2} ms" : "-")}" +
                $"  /  焼き直し回数 {_skiaCache.BakeCount}  /  キャッシュ再利用中のフレーム数 {_skiaCache.HitStreak}" +
                "（この値が増え続けている間、Bのフレームコストはテクスチャを貼るだけ＝ほぼ0）");
    }

    private void DrawNativeCard(double x, double y, string title)
    {
        Drawing.Text(x, y - 28, "A) AstrumLoom標準（FreeType, DrawGrad）", new Color(200, 210, 230));
        Drawing.Box(x - 4, y - 4, 560 + 8, 140 + 8, new Color(40, 46, 70));
        Drawing.Box(x, y, 560, 140, new Color(20, 22, 34));

        if (_nativeFont == null) return;

        // 実際にこのフレームで描くのに掛かった時間を計測する。DrawGradは呼ぶたびに
        // 白文字をオフスクリーンへ焼き直すので、この数字は原理上フレームごとにほぼ一定になるはず
        // （＝「変わらない文字列でも常に払い続けているコスト」）。
        _sw.Restart();
        _nativeFont.DrawGrad(x + 24, y + 40, title, NativeGradation,
            new DrawOptions { EdgeColor = new Color(40, 10, 10) });
        _sw.Stop();
        _nativeMsThisFrame = _sw.Elapsed.TotalMilliseconds;
        _nativeMsAvg = _nativeMsAvg < 0 ? _nativeMsThisFrame : _nativeMsAvg * 0.9 + _nativeMsThisFrame * 0.1;
    }

    private void DrawSkiaCard(double x, double y, string title)
    {
        Drawing.Text(x, y - 28, "B) Skia + SkiaTextCache（変化時のみ焼き直し）", new Color(200, 210, 230));
        Drawing.Box(x - 4, y - 4, 560 + 8, 140 + 8, new Color(40, 46, 70));
        Drawing.Box(x, y, 560, 140, new Color(20, 22, 34));

        // キーは「見た目を決める全部」を含める。ここではtitleだけだが、実運用ならフォント名/サイズ/色/
        // 縁取り設定なども混ぜる。キーが変わらない限りrenderは呼ばれない。
        var tex = _skiaCache.Get(title, () => RenderTitle(title));
        tex?.Draw(x + 24, y + 40);
    }

    /// <summary>SkiaSharp側の実装。Aと見た目を揃えるため、同じグラデーション色・同程度の縁取りにしてある。</summary>
    private static (int width, int height, byte[] rgba) RenderTitle(string text)
    {
        using var surface = SKSurface.Create(new SKImageInfo(560, 140, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        using var font = new SKFont(SKTypeface.FromFamilyName("Yu Gothic UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright), 44);

        // 縁取り: AstrumLoom側の「8方向オフセット再描画」ではなく、Stroke1本で済ませられる。
        using (var edge = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 7,
            Color = new SKColor(40, 10, 10),
        })
        {
            canvas.DrawText(text, 20, 76, font, edge);
        }

        using (var grad = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(20, 40), new SKPoint(500, 100),
                [new SKColor(255, 224, 102), new SKColor(255, 107, 107)],
                SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawText(text, 20, 76, font, grad);
        }

        using var image = surface.Snapshot();
        return SkiaTextCache.Extract(image);
    }
}
