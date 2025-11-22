using System.Drawing.Text;

using static AstrumLoom.DXLib.DxLibGraphics;
using static AstrumLoom.LayoutUtil;
using static DxLibDLL.DX;

namespace AstrumLoom.DXLib;

internal sealed class DxLibFont : IFont
{
    public FontSpec Spec { get; }
    private readonly int _handle;
    private readonly int _edgehandle = -1;
    public bool Enable => _handle > 0;

    private const int _defaulttype = DX_FONTTYPE_ANTIALIASING_8X8;
    private int _edgeThickness = 0;
    private int _spacing = 0;
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

    public (int width, int height) Measure(string text)
    {
        if (!Enable)
            return Drawing.DefaultTextSize(text);
        GetDrawStringSizeToHandle(
            out int w, out int h,
            out _, text, text.Length, _handle
        );
        return (w, h);
    }

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
                fy += h / 2;
            }
        }
        //SetColorBlend(options.Blend, options.Opacity, useColor);

        SetFontOnlyDrawType(0);
        SetFontSpaceToHandle(_spacing, _handle);
        DrawStringToHandle(drawX, drawY, text, c, _handle);
        //ResetOptions(options);
    }
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
        DrawStringToHandle(drawX - _edgeThickness, drawY - _edgeThickness, text, ec, _edgehandle, ec);
        SetFontOnlyDrawType(0);
        ResetOptions(options);
    }

    public void Dispose()
    {
        if (_handle != -1)
        {
            DeleteFontToHandle(_handle);
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

    private static string GetFont(string? font)
    {
        string name = GetFontName();
        if (font == null) return name;

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

            return font;
        }
        else
        {
            // パスでなければそのままフォント名を返す
            return font;
        }
    }
}
