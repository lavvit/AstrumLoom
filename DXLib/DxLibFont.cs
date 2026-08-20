using System.Drawing.Text;

using static AstrumLoom.DXLib.DxLibGraphics;
using static AstrumLoom.LayoutUtil;
using static DxLibDLL.DX;

namespace AstrumLoom.DXLib;

/// <summary>
/// DxLibバックエンドでのフォント実装。通常描画に加え、縁取り(_edgehandle)、行ごとに色を変える
/// グラデーション描画(DrawGrad)、文字型抜きにテクスチャを貼るDrawTextureまでを提供する。
/// グラデーション/テクスチャ描画は「白文字マスクをMakeScreenした一時画面に描いてから合成する」という
/// 共通の手法を使っており、その一時画面は_screenCacheで使い回している。
/// </summary>
internal sealed class DxLibFont : IFont
{
    public FontSpec Spec { get; }
    private readonly int _handle;
    private readonly int _edgehandle = -1;
    public bool Enable => _handle > 0;

    private const int _defaulttype = DX_FONTTYPE_ANTIALIASING_8X8;
    private int _edgeThickness = 0;
    private int _spacing = 0;
    /// <summary>フォント名/ファイルパスからCreateFontToHandleでフォントハンドルを作る。Edge&gt;0のときは縁取り専用のフォントハンドルも別途作成する。</summary>
    public DxLibFont(FontSpec spec)
    {
        Spec = spec;

        string name = GetFont(spec.NameOrPath);

        int thickness = spec.Thickness > 1 ? spec.Thickness : spec.Bold ? 4 : 1;
        _edgeThickness = spec.Edge;
        _spacing = spec.Spacing;

        _handle = CreateFontToHandle(
            name,
            spec.Size,
            thickness,
            _defaulttype, // or edge 付き
            -1, _edgeThickness, spec.Italic ? 1 : 0);
        if (_edgeThickness > 0)
        {
            _edgehandle = CreateFontToHandle(
                name,
                spec.Size,
                thickness,
                DX_FONTTYPE_ANTIALIASING_EDGE_8X8,
                -1, _edgeThickness, spec.Italic ? 1 : 0);
        }
    }

    /// <summary>フォントが未初期化ならDrawing.DefaultTextSizeにフォールバックし、そうでなければDxLibのGetDrawStringSizeToHandleで実測する。</summary>
    public (int width, int height) Measure(string text)
    {
        if (!Enable)
            return Drawing.DefaultTextSize(text);
        GetDrawStringSizeToHandle(
            out int w, out int h,
            out _, text, -1, _handle
        );
        return (w, h);
    }

    /// <summary>
    /// 通常のテキスト描画。縁取りハンドルがあれば先に1行ずつ縁取りを描いてから(SetFontOnlyDrawType(2))、
    /// 本体のフォントで文字を描く(SetFontOnlyDrawType(0))。改行を含むテキストは行ごとの高さを均等割りして
    /// 個別にDrawStringToHandleへ渡している（DxLibのDrawStringToHandleは複数行を一括では扱いにくいため）。
    /// </summary>
    public void Draw(double x, double y, string text, DrawOptions options)
    {
        if (!Enable)
        {
            Drawing.DefaultText(x, y, text);
            return;
        }
        //SetOptions(options);
        var (w, h) = Measure(text);
        var off = GetAnchorOffset(options.Point, w, h);
        int drawX = (int)(x + off.X);
        int drawY = (int)(y + off.Y);

        var useColor = options.Color ?? Color.White;
        uint c = (uint)ToDxColor(useColor); // らびぃが既に持ってる変換ヘルパー

        var useEdgeColor = options.EdgeColor ?? Color.VisibleColor(useColor);
        uint ec = (uint)ToDxColor(useEdgeColor);
        if (_edgehandle > 0)
        {
            //ResetColorBlend(options.Blend, useColor);
            //SetColorBlend(options.Blend, options.Opacity, useEdgeColor);
            int fy = 0;
            SetFontOnlyDrawType(2);
            SetFontSpaceToHandle(-_edgeThickness * 2 + _spacing, _edgehandle);
            int lineHeight = h / text.Split('\n').Length;
            foreach (string line in text.Split('\n'))
            {
                DrawStringToHandle(
                    x: drawX - _edgeThickness,
                    y: drawY + fy - _edgeThickness,
                    String: line,
                    Color: ec,
                    FontHandle: _edgehandle,
                    EdgeColor: ec,       // ★ 縁取りの色はこっち
                    VerticalFlag: 0
                );
                fy += lineHeight;
            }
        }
        //SetColorBlend(options.Blend, options.Opacity, useColor);

        SetFontOnlyDrawType(0);
        SetFontSpaceToHandle(_spacing, _handle);
        DrawStringToHandle(drawX, drawY, text, c, _handle);
        //ResetOptions(options);
    }
    /// <summary>縁取りのみを描画する（本体文字は描かない）。DrawGrad/DrawTextureが本体を別手法で合成する前に縁取りだけ先出しするために使う。</summary>
    public void DrawEdge(double x, double y, string text, DrawOptions options)
    {
        if (!Enable || _edgehandle < 0)
            return;
        SetOptions(options);
        var (w, h) = Measure(text);
        var off = GetAnchorOffset(options.Point, w, h);
        int drawX = (int)(x + off.X);
        int drawY = (int)(y + off.Y);

        var useColor = options.EdgeColor ?? options.Color ?? Color.Black;
        uint ec = (uint)ToDxColor(useColor);

        SetFontOnlyDrawType(2);
        SetFontSpaceToHandle(-_edgeThickness * 2 + _spacing, _edgehandle);
        int lineHeight = h / text.Split('\n').Length;
        int fy = 0;
        foreach (string line in text.Split('\n'))
        {
            DrawStringToHandle(
                x: drawX - _edgeThickness,
                y: drawY + fy - _edgeThickness,
                String: line,
                Color: ec,
                FontHandle: _edgehandle,
                EdgeColor: ec,       // ★ 縁取りの色はこっち
                VerticalFlag: 0
            );
            fy += lineHeight;
        }
        SetFontOnlyDrawType(0);
        ResetOptions(options);
    }

    /// <summary>フォントハンドルと縁取りハンドルを解放し、DrawGrad/DrawTexture用に貯めていた_screenCacheの一時画面も全て解放する。</summary>
    public void Dispose()
    {
        if (_handle != -1)
        {
            DeleteFontToHandle(_handle);
        }
        // 縁取り用に別途作っていた _edgehandle（35-43行目）を解放し忘れていた分。
        // 他の判定箇所（75行目の DrawEdge 呼び出し条件など）に合わせて > 0 で統一する。
        if (_edgehandle > 0)
        {
            DeleteFontToHandle(_edgehandle);
        }

        lock (_screenLock)
        {
            foreach (var s in _screenCache)
            {
                if (s.Handle > 0) DeleteGraph(s.Handle);
            }
            _screenCache.Clear();
        }
    }

    // 変換手順（詳細な擬似コード）
    // 1. GetFontName() で既定のフォント名を取得する。
    // 2. 引数 font が null なら既定のフォント名を返す。
    // 3. font がファイルパスを指している場合（File.Exists が true ）:
    //    a. 既存の処理と同様に AddFontFile(path) を呼んでフォントを登録する。
    //    b. SkiaSharp の SKTypeface.FromFile(path) を使ってフォントファイルを読み込む。
    //    c. 読み込みに成功したら SKTypeface.FamilyName を取得して font に代入する。
    //    d. 失敗または FamilyName が空なら既定のフォント名にフォールバックする。
    // 4. font がファイルでない（＝フォント名が直接渡されている）場合はそのまま返す。
    // 5. 例外はキャッチして既定フォント名を返す。
    // メモ: SkiaSharp を参照していることが前提。SKTypeface を使うので using SkiaSharp; がプロジェクトに必要。

    /// <summary>fontがファイルパスならDxLibへAddFontFileで登録しつつSkiaSharpでファミリー名を解決し、そうでなければフォント名としてそのまま返す。</summary>
    private static string GetFont(string? font)
    {
        string name = GetFontName() ?? "";
        // 空文字も未指定と同様に既定フォントへフォールバックする（空文字のままCreateFontToHandleへ渡すとGDIが
        // フェイス名なしマッチで任意のフォントを選び、日本語グリフを持たないものに当たると豆腐になる）。
        if (string.IsNullOrEmpty(font)) return name;

        // フォントがファイルパスかどうかを判定
        if (File.Exists(font))
        {
            // 既存の処理（DX側へ登録）
            AddFontFile(font);

            try
            {
                if (OperatingSystem.IsWindowsVersionAtLeast(6, 1))
                {
                    // Windows 環境ではフォントキャッシュの更新を試みる
                    using var pfc = new PrivateFontCollection();
                    pfc.AddFontFile(font);
                    var list = new List<string>();
                    foreach (var fam in pfc.Families) list.Add(fam.Name); // ← DXLibに渡す名前候補
                    font = list.FirstOrDefault(name);
                }
                font ??= name; // フォント名が取得できなかった場合はフォールバック
            }
            catch
            {
                // 例外が発生した場合はフォールバック
                font = name;
            }

            return font ?? "";
        }
        else
        {
            // パスでなければそのままフォント名を返す
            return font;
        }
    }

    #region Gradation Text
    /// <summary>
    /// 行方向にグラデーションがかかった文字を描画する。DxLibのDrawStringToHandleは単色描画しかできないため、
    /// (1)白文字マスクを一時画面に描き、(2)そのマスクを1行ずつSetDrawBrightで色を変えながらDrawRectGraphで
    /// 画面へ転写する、という手順で疑似的にグラデーションを実現している。
    /// </summary>
    public void DrawGrad(double x, double y, string text, Gradation gradation, DrawOptions options)
    {
        if (!Enable)
        {
            Drawing.DefaultText(x, y, text);
            return;
        }
        SetOptions(options);
        // サイズ＆アンカー
        var (w, h) = Measure(text);
        if (w <= 0 || h <= 0)
        {
            ResetOptions(options);
            return;
        }
        DrawEdge(x, y, text, options); // 縁取り先に描画

        var off = GetAnchorOffset(options.Point, w, h);
        int drawX = (int)(x + off.X);
        int drawY = (int)(y + off.Y);

        // 1. 白文字マスクを作る
        int mask = AcquireScreen(w, h);
        if (mask <= 0)
        {
            // 万が一失敗したら普通に単色
            Draw(x, y, text, options);
            ResetOptions(options);
            return;
        }

        int oldScreen = GetDrawScreen();
        SetDrawScreen(mask);
        ClearDrawScreen(); // 完全透明クリア

        // 白で文字だけ描く（既存のフォントハンドルを利用）
        uint white = GetColor(255, 255, 255);
        SetFontSpaceToHandle(_spacing, _handle);
        DrawStringToHandle(0, 0, text, white, _handle);

        // 元の描画先に戻す
        SetDrawScreen(oldScreen);

        // 2. 行ごとに色を変えて画面に貼る
        double opacity = Math.Clamp(options.Opacity, 0.0, 1.0);

        for (int row = 0; row < h; row++)
        {
            float t = h > 1 ? (float)row / (h - 1) : 0f;

            // OKLab グラデならこんな感じ
            var c = gradation.GetColor(t, gradation.UseColorSpace);

            // SetDrawBright は 0～255 指定なのでそのまま使える
            SetDrawBright(c.R, c.G, c.B);

            // マスクの１行だけを貼る
            DrawRectGraph(
                drawX, drawY + row,
                0, row,
                w, 1,
                mask,
                TRUE,   // トランス
                FALSE   // 反転なし
            );
        }

        // 明るさを元に戻す
        SetDrawBright(255, 255, 255);

        ReleaseScreen(mask, w, h);
        ResetOptions(options);
    }
    /// <summary>同サイズの一時画面(MakeScreen)を_screenCacheから再利用して取得する。無ければ新規作成する。DrawGrad/DrawTextureで毎回MakeScreenするコストを避けるためのキャッシュ。</summary>
    private int AcquireScreen(int width, int height)
    {
        lock (_screenLock)
        {
            for (int i = _screenCache.Count - 1; i >= 0; i--)
            {
                var s = _screenCache[i];
                if (s.Handle > 0 && s.Width == width && s.Height == height)
                {
                    _screenCache.RemoveAt(i);
                    return s.Handle;
                }
            }
        }

        // 見つからなければ新規作成
        int h = MakeScreen(width, height, TRUE);
        return h;
    }

    /// <summary>一時画面を_screenCacheへ返却する。キャッシュが上限(MaxScreenCache)に達していれば代わりに即DeleteGraphで解放する。</summary>
    private void ReleaseScreen(int handle, int width, int height)
    {
        if (handle <= 0) return;

        lock (_screenLock)
        {
            if (_screenCache.Count >= MaxScreenCache)
            {
                DeleteGraph(handle);
            }
            else
            {
                _screenCache.Add(new ScreenItem
                {
                    Handle = handle,
                    Width = width,
                    Height = height,
                });
            }
        }
    }

    // 追加：描画用の MakeScreen キャッシュ
    private readonly object _screenLock = new();
    private const int MaxScreenCache = 8;

    private struct ScreenItem
    {
        public int Handle;
        public int Width;
        public int Height;
    }

    private readonly List<ScreenItem> _screenCache = [];
    #endregion

    #region Texture Text
    /// <summary>
    /// 文字の形にテクスチャを貼り付けて描画する。手順は (1)白文字マスク、(2)テクスチャを敷き詰めた画面、
    /// (3)GraphBlendBltでRGBをテクスチャ側・AlphaをマスクのRから合成した画面、の3枚の一時画面(AcquireScreen)を
    /// 作ってから最終結果だけをDrawGraphで転写する。DrawGradと似た手法だがアルファ合成(GraphBlendBlt)を挟む点が異なる。
    /// </summary>
    public void DrawTexture(double x, double y, string text, ITexture[] texture, DrawOptions options)
    {
        if (!Enable)
        {
            Drawing.DefaultText(x, y, text);
            return;
        }
        SetOptions(options);

        var (w, h) = Measure(text);
        if (w <= 0 || h <= 0)
        {
            ResetOptions(options);
            return;
        }
        DrawEdge(x, y, text, options); // 縁取り先に描画

        var off = GetAnchorOffset(options.Point, w, h);
        int drawX = (int)(x + off.X);
        int drawY = (int)(y + off.Y);

        int mask = AcquireScreen(w, h);
        int texScreen = AcquireScreen(w, h);
        int outScreen = AcquireScreen(w, h);

        if (mask <= 0 || texScreen <= 0 || outScreen <= 0)
        {
            // どれか失敗したらフォールバック
            Draw(x, y, text, options);
            if (mask > 0) ReleaseScreen(mask, w, h);
            if (texScreen > 0) ReleaseScreen(texScreen, w, h);
            if (outScreen > 0) ReleaseScreen(outScreen, w, h);
            ResetOptions(options);
            return;
        }

        int oldScreen = GetDrawScreen();

        // 1. マスク (白文字)
        SetDrawScreen(mask);
        ClearDrawScreen();
        uint white = GetColor(255, 255, 255);
        DrawStringToHandle(0, 0, text, white, _handle);

        // 2. テクスチャ (塗りつぶし)
        SetDrawScreen(texScreen);
        ClearDrawScreen();
        // 好きな貼り方で textureHandle を texScreen に描画
        foreach (var textureHandle in texture)
        {
            int tex = textureHandle is DxLibTexture dxTex ? dxTex.Handle : -1;
            DrawExtendGraph(0, 0, w, h, tex, TRUE);
        }

        // 3. GraphBlend で mask をアルファとして合成
        SetDrawScreen(outScreen);
        ClearDrawScreen();

        // ここは擬似コード：DX_GRAPH_BLEND_MASK のパラメータ設定は
        // リファレンスを見ながら「RGB = texScreen, A = mask の R成分」
        // みたいな感じになるようにする
        GraphBlendBlt(
            texScreen,
            mask,
            outScreen,
            255,
            DX_GRAPH_BLEND_RGBA_SELECT_MIX,
            DX_RGBA_SELECT_SRC_R,    // R = tex.R
            DX_RGBA_SELECT_SRC_G,    // G = tex.G
            DX_RGBA_SELECT_SRC_B,    // B = tex.B
            DX_RGBA_SELECT_BLEND_A   // A = mask.A
        );

        // 描画先を元に戻す
        SetDrawScreen(oldScreen);

        // 4. outScreen を画面に描画
        DrawGraph(drawX, drawY, outScreen, TRUE);

        ReleaseScreen(mask, w, h);
        ReleaseScreen(texScreen, w, h);
        ReleaseScreen(outScreen, w, h);

        ResetOptions(options);
    }
    #endregion
}
