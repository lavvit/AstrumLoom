using Raylib_cs;

using static Raylib_cs.Raylib;

namespace AstrumLoom.RayLib;

internal sealed class RayLibFont : IFont
{
    public FontSpec Spec { get; }
    private readonly Font _font;
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

        // Raylib: size は "baseSize" として渡す
        int[] cps = EnumRange(0x20, 0xFFFF);
        _font = LoadFontEx(path, spec.Size, cps, cps.Length);
    }

    public (int width, int height) Measure(string text)
    {
        var size = MeasureTextEx(_font, text, Spec.Size, 0);
        if (size.X + size.Y == 0) size = new(MeasureText(text, Spec.Size), Spec.Size);
        return ((int)size.X, (int)size.Y);
    }

    public void Draw(IGraphics g, double x, double y, string text, DrawOptions options)
    {
        var (w, h) = Measure(text);
        var off =
            LayoutUtil.GetAnchorOffset(options.Point, w, h);
        int drawX = (int)(x + off.X);
        int drawY = (int)(y + off.Y);

        var color = options.Color ?? Color.White;
        double opacity = Math.Clamp(options.Opacity, 0.0, 1.0);
        byte a = (byte)(color.A * opacity);
        var c = new Raylib_cs.Color(color.R, color.G, color.B, a);

        DrawTextEx(_font, text, new System.Numerics.Vector2(drawX, drawY),
                   Spec.Size, 0, c);
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
