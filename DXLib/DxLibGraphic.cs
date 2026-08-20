using static DxLibDLL.DX;

namespace AstrumLoom.DXLib;

/// <summary>DxLibバックエンドでのIGraphics実装。図形・テキスト描画API群をDxLibのAA付き関数(DrawLineAA等)へ委譲する。</summary>
internal sealed class DxLibGraphics : IGraphics
{
    public DxLibGraphics() =>
        // ここではとりあえず「Default」の 12px ぐらいを作っておく
        DefaultFont = CreateFont(new FontSpec("", 12));

    private (int w, int h) _size = (-1, -1);
    /// <summary>ウィンドウサイズ。初回アクセス時のみGetWindowSizeで取得しキャッシュする（ウィンドウサイズは実行中に変わらない前提）。</summary>
    public LayoutUtil.Size Size
    {
        get
        {
            if (_size.w < 0 || _size.h < 0)
            {
                GetWindowSize(out int w, out int h);
                _size = (w, h);
            }
            return new LayoutUtil.Size(_size.w, _size.h);
        }
    }

    public ITexture LoadTexture(string path) => new DxLibTexture(path);

    public void BeginFrame()
    {
        // 今は特に何もしない（必要ならここで状態リセット）
    }

    /// <summary>裏画面(DX_SCREEN_BACK)を指定色でクリアする。</summary>
    public void Clear(Color color)
    {
        SetDrawScreen(DX_SCREEN_BACK);
        // AstrumLoom.Color → System.Drawing.Color にキャストできるのでそれを使う
        var c = (System.Drawing.Color)color;
        SetBackgroundColor(c.R, c.G, c.B);
        ClearDrawScreen();
    }

    /// <summary>裏画面の描画結果をScreenFlipで表画面へ切り替える（ダブルバッファのフリップ）。</summary>
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

    // Line/Box/Circle/Oval/Triangle は共通のパターン：色とThickness/Opacity/BlendをDxLibのブレンドモードへ設定し、
    // DrawXxxAA系のアンチエイリアス付き描画関数を呼んでから、ブレンド設定を必ず既定(None/255)に戻す。
    // 戻し忘れると以降の描画全てに前回のブレンドが残ってしまうため、各メソッドの最後で必ずリセットしている。
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
    /// <summary>DxLibの既定フォントでテキストを描画する（IFont経由ではなくSetFontSize等のグローバル状態を直接操作する簡易版）。フォントサイズはEnsureFontSizeで変更が必要な時だけ切り替える。</summary>
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
    /// <summary>DxLibのSetFontSizeはグローバル状態を書き換えるコストがあるため、直前と同じサイズなら呼び出しを省略する。</summary>
    private void EnsureFontSize(int fontSize)
    {
        if (_currentFontSize == fontSize) return; // 変わらないなら何もしない
        SetFontSize(fontSize);
        _currentFontSize = fontSize;
    }

    public IFont DefaultFont { get; }
    public IFont CreateFont(FontSpec spec)
        => new DxLibFont(spec);

    /// <summary>拡張子からDxLibの保存形式(BMP/JPEG/DDS/既定でPNG)を判定し、裏画面の内容をファイルへ保存する。</summary>
    public bool SaveScreenshot(string path)
    {
        // SaveDrawScreen は「現在の描画対象」を保存するので、裏画面に戻してから撮る。
        // BeginFrame と EndFrame の間から呼ばれる前提。
        GetWindowSize(out int w, out int h);
        if (w <= 0 || h <= 0) return false;

        int previous = GetDrawScreen();
        SetDrawScreen(DX_SCREEN_BACK);
        try
        {
            int type = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".bmp" => DX_IMAGESAVETYPE_BMP,
                ".jpg" or ".jpeg" => DX_IMAGESAVETYPE_JPEG,
                ".dds" => DX_IMAGESAVETYPE_DDS,
                _ => DX_IMAGESAVETYPE_PNG,
            };
            return SaveDrawScreen(0, 0, w, h, path, type) == 0;
        }
        finally
        {
            if (previous != DX_SCREEN_BACK) SetDrawScreen(previous);
        }
    }

    /// <summary>
    /// 共通のBlendMode列挙をDxLibのDX_BLENDMODE_*定数へ変換する。
    /// Screen(src+dst-src*dst)はSPINE_SCREENがそのままの式を持つのでそれを使う（RayLib側と同じ結果になる）。
    /// Reverse(src-dst想定)はDxLibにOpenGLのような合成係数のカスタム指定がないため、
    /// SUB(dst-src)の逆方向として一番近いSUB2を割り当てる（厳密な一致はRayLib側と保証されない）。
    /// Multiplyは MUL(アルファ無視で矩形ごと黒潰れ) / SRCCOLOR(同様に黒潰れ) を実機で試した結果、
    /// アルファを正しく考慮して安全なのはMULAだけだった。ただしRayLibのMultiplied（GL_DST_COLOR基準）
    /// より効果が弱く出る（DxLibに合成係数を自由指定するAPIが無く、これ以上は追い込めない）。
    /// </summary>
    internal static int GetBlendMode(BlendMode mode) => mode switch
    {
        BlendMode.None => DX_BLENDMODE_ALPHA,
        BlendMode.Add => DX_BLENDMODE_ADD,
        BlendMode.Subtract => DX_BLENDMODE_SUB,
        BlendMode.Multiply => DX_BLENDMODE_MULA,
        BlendMode.Screen => DX_BLENDMODE_SPINE_SCREEN,
        BlendMode.Reverse => DX_BLENDMODE_SUB2,
        _ => DX_BLENDMODE_NOBLEND,
    };

    // ToDxColor は MultiBeat のやつをそのまま持ってきてOK
    internal static int ToDxColor(Color col)
        => (int)GetColor(col.R, col.G, col.B);

    /// <summary>
    /// DxLibTexture/DxLibMovieの描画前に呼ぶ共通のブレンド設定。ブレンドが既定(None)かつOpacity=1.0のときは
    /// SetDrawBlendModeの呼び出し自体を省略する（コスト削減）。色がWhite以外ならSetDrawBrightで乗算色として反映する。
    /// </summary>
    internal static void SetColorBlend(BlendMode blend, double opacity, Color col)
    {
        if (blend > BlendMode.None || opacity < 1.0)
        {
            double op = Math.Clamp(opacity, 0.0, 1.0);
            SetDrawBlendMode(GetBlendMode(blend), (int)(255.0 * op));
        }
        if (col != Color.White)
            SetDrawBright(col.R, col.G, col.B);
    }
    /// <summary>DrawOptionsからSetColorBlendを呼ぶ薄いラッパー。色のアルファ値もOpacityへ掛け合わせる。</summary>
    internal static void SetOptions(DrawOptions option)
    {
        double opacity = Math.Clamp(option.Opacity, 0.0, 1.0);
        var color = option.Color ?? Color.White;
        opacity *= color.A / 255.0;

        SetColorBlend(option.Blend, opacity, color);
    }
    /// <summary>SetColorBlendで変更したDxLibのブレンド/明度設定を既定値へ戻す。SetColorBlendと対で呼ぶ必要がある。</summary>
    internal static void ResetColorBlend(BlendMode blend, double opacity, Color col)
    {
        if (blend > BlendMode.None || opacity < 1.0)
            SetDrawBlendMode(DX_BLENDMODE_ALPHA, 255);
        if (col != Color.White)
            SetDrawBright(255, 255, 255);
    }
    internal static void ResetOptions(DrawOptions option)
    {
        var color = option.Color ?? Color.White;
        ResetColorBlend(option.Blend, option.Opacity, color);
    }
}