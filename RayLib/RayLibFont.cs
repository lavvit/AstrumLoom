using System.Numerics;

using Raylib_cs;

using static AstrumLoom.LayoutUtil;
using static AstrumLoom.RayLib.RayLibGraphics;
using static Raylib_cs.Raylib;

namespace AstrumLoom.RayLib;

internal sealed class RayLibFont : IFont
{
    public FontSpec Spec { get; }
    private readonly Font _font;
    public bool Enable => _font.Texture.Id > 0;

    private int _edgeThickness = 0;
    private int _spacing = 0;
    public RayLibFont(FontSpec spec)
    {
        Spec = spec;

        string path = GetFont(spec.NameOrPath, spec);
        if (string.IsNullOrEmpty(path))
        {
            // 何も見つからなかったらデフォルトフォント
            _font = GetFontDefault();
            return;
        }
        int thickness = spec.Thickness > 1 ? spec.Thickness : spec.Bold ? 4 : 1;
        _edgeThickness = spec.Edge;
        _spacing = spec.Spacing;

        // Raylib: size は "baseSize" として渡す
        int[] cps = EnumRange(0x20, 0xFFFF);

        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            // TTF/OTF どちらも stb_truetype 経由でOK
            string ext = Path.GetExtension(path).ToLowerInvariant();

            if (ext == ".font")
            {
                byte[] bytes = File.ReadAllBytes(path);
                static string GuessFontHint(byte[] b)
                {
                    if (b.Length >= 4)
                    {
                        // 00 01 00 00 → TrueType
                        if (b[0] == 0x00 && b[1] == 0x01 && b[2] == 0x00 && b[3] == 0x00) return ".ttf";
                        // 'OTTO' → OpenType(CFF)
                        if (b[0] == (byte)'O' && b[1] == (byte)'T' && b[2] == (byte)'T' && b[3] == (byte)'O') return ".otf";
                        // 'ttcf' → TrueType Collection
                        if (b[0] == (byte)'t' && b[1] == (byte)'t' && b[2] == (byte)'c' && b[3] == (byte)'f') return ".ttc";
                    }
                    return ".ttf"; // わからなければ TTF とみなす
                }
                string hint = GuessFontHint(bytes);
                _font = Raylib.LoadFontFromMemory(hint, bytes, spec.Size, cps, cps.Length);
                Raylib.SetTextureFilter(_font.Texture, TextureFilter.Bilinear);
                return;
            }
            if (ext is ".ttf" or ".otf" or ".ttc" or ".otc")
            {
                _font = Raylib.LoadFontEx(path, spec.Size, cps, cps.Length);
                Raylib.SetTextureFilter(_font.Texture, TextureFilter.Bilinear);
            }
            else // 未対応拡張子 → 内蔵にフォールバック
                _font = Raylib.GetFontDefault();
        }
        else // パス無し → 内蔵フォント
            _font = Raylib.GetFontDefault();
    }

    public (int width, int height) Measure(string text)
    {
        var size = MeasureTextEx(_font, text, Spec.Size, 0);
        if (size.X + size.Y == 0) size = new(MeasureText(text, Spec.Size), Spec.Size);
        return ((int)size.X, (int)size.Y);
    }
    private static readonly Vector2[] EdgeDirs =
[
    new( 1,  0),
    new(-1,  0),
    new( 0,  1),
    new( 0, -1),/*
    new( 1,  1),
    new(-1,  1),
    new( 1, -1),
    new(-1, -1),*/
];
    public void Draw(double x, double y, string text, DrawOptions options)
    {
        if (!Enable)
        {
            Drawing.DefaultText(x, y, text);
            return;
        }
        SetOptions(options);

        int drawX = (int)x;
        int drawY = (int)y;
        if (options.Point != ReferencePoint.TopLeft)
        {
            var (w, h) = Measure(text);
            var off = GetAnchorOffset(options.Point, w, h);
            drawX = (int)(x + off.X);
            drawY = (int)(y + off.Y);
        }

        var color = options.Color ?? Color.White;
        double opacity = Math.Clamp(options.Opacity, 0.0, 1.0);
        var pos = new Point(drawX, drawY);

        // Edge（ふち）: オフセット描画
        if (_edgeThickness > 0)
        {
            // 8方向の単位ベクトル（円形に均一配置）
            int r = _edgeThickness;
            //for (int r = 1; r <= _edgeThickness; r++)
            {
                foreach (var v in EdgeDirs)
                {
                    // 端数でにじまないように整数へ
                    var p = new Point(MathF.Round((float)pos.X + v.X * r),
                    MathF.Round((float)pos.Y + v.Y * r));
                    DrawEx(text, p, options.EdgeColor ?? Color.VisibleColor(color), opacity);
                }
            }
        }
        DrawEx(text, pos, color, opacity);
        ResetOptions(options);
    }

    private void DrawEx(string s, Point pos, Color color, double opacity = 1, int spacing = 0)
    {
        var p = new Vector2((float)pos.X, (float)pos.Y);
        var c = ToRayColor(color, color.A / 255.0 * opacity);
        DrawTextEx(_font, s, p,
                   Spec.Size, spacing, c);
    }

    public void DrawEdge(double x, double y, string text, DrawOptions options)
    {
        if (!Enable || _edgeThickness <= 0)
        {
            return;
        }
        SetOptions(options);

        int drawX = (int)x;
        int drawY = (int)y;
        if (options.Point != ReferencePoint.TopLeft)
        {
            var (w, h) = Measure(text);
            var off = GetAnchorOffset(options.Point, w, h);
            drawX = (int)(x + off.X);
            drawY = (int)(y + off.Y);
        }
        var ec = options.EdgeColor ?? options.Color ?? Color.Black;
        double opacity = Math.Clamp(options.Opacity, 0.0, 1.0);
        var pos = new Point(drawX, drawY);
        // Edge（ふち）: オフセット描画
        int r = _edgeThickness;
        //for (int r = 1; r <= _edgeThickness; r++)
        {
            foreach (var v in EdgeDirs)
            {
                // 端数でにじまないように整数へ
                var p = new Point(
                    MathF.Round((float)pos.X + v.X * r),
                    MathF.Round((float)pos.Y + v.Y * r));
                DrawEx(text, p, ec, opacity);
            }
        }
        ResetOptions(options);
    }
    public void Dispose() => UnloadFont(_font);

    private static string GetFont(string? font, FontSpec spec)
    {
        if (string.IsNullOrEmpty(font)) return "";
        if (File.Exists(font)) return font;

        string? path = SystemFontResolver.Resolve(
                    spec.NameOrPath, spec.Bold, spec.Italic);
        if (string.IsNullOrEmpty(path))
            Log.Warning($"font: {spec.NameOrPath} is not found.");
        return path ?? "";
    }
    // よく使うコードポイント（ASCII + ひらがな + カタカナ + 一般句読点 + 数学記号）
    private static int[] CommonJpCodePoints()
    {
        // ASCII
        int[] ascii = EnumRange(0x20, 0x7E);

        // 一般句読点（General Punctuation）: U+2000..U+206F（† などを含む）
        int[] punct = EnumRange(0x2000, 0x206F);

        // 数学演算子（Mathematical Operators）: U+2200..U+22FF（∀ などを含む）
        int[] mathOps = EnumRange(0x2200, 0x22FF);

        // CJK 記号・句読点／全角記号など
        int[] cjkSym = EnumRange(0x3000, 0x303F);   // 、。・〜（　）他
        int[] fullwd = EnumRange(0xFF00, 0xFFEF);   // 全角英数・半角カナなど

        // ひらがな・カタカナ
        int[] hira = EnumRange(0x3040, 0x309F);
        int[] kata = EnumRange(0x30A0, 0x30FF);

        var baseSet = ascii
            .Concat(punct)
            .Concat(mathOps)
            .Concat(cjkSym)
            .Concat(fullwd)
            .Concat(hira)
            .Concat(kata);

        // 基本漢字（CJK Unified Ideographs）
        int[] cjkUni = EnumRange(0x4E00, 0x9FFF);

        // 互換漢字（量は控えめに。必要なら Extension-A なども追加可）
        int[] cjkCompat = EnumRange(0xF900, 0xFAFF);

        return [.. baseSet.Concat(cjkUni).Concat(cjkCompat).Distinct()];
    }
    private static int[] EnumRange(int start, int end) => [.. Enumerable.Range(start, end - start + 1)];
}
