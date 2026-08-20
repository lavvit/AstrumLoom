namespace AstrumLoom.Extend;

/// <summary>
/// IFont.Draw を拡張し、グラデーション塗り・テクスチャ塗りなど装飾つきのテキスト描画をまとめて呼び分ける。
/// </summary>
public static class DecorateText
{
    /// <summary>
    /// <paramref name="option"/> の内容（グラデーション／テクスチャ／通常）に応じて、対応する描画メソッドへ振り分けます。
    /// </summary>
    public static void Draw(this IFont font,
        double x, double y,
        object? text, DecorateOption? option,
        ReferencePoint point = ReferencePoint.TopLeft,
        Color? edgecolor = null,
        BlendMode blend = BlendMode.None, double opacity = 1)
    {
        string str = text?.ToString() ?? "";
        if (option?.Gradation != null)
        {
            font.DrawGrad(x, y, str, option.Gradation,
                new DrawOptions
                {
                    Point = point,
                    EdgeColor = edgecolor,
                    Blend = blend,
                    Opacity = opacity
                });
        }
        else if (option?.Texture != null)
        {
            font.DrawTexture(x, y, str, [option.Texture.Interface],
                new DrawOptions
                {
                    Point = point,
                    EdgeColor = edgecolor,
                    Blend = blend,
                    Opacity = opacity
                });
        }
        else
        {
            font.Draw(x, y, str,
                new DrawOptions
                {
                    Point = point,
                    EdgeColor = edgecolor,
                    Blend = blend,
                    Opacity = opacity
                });
        }
    }

    /// <summary>
    /// グラデーション塗りでテキストを描画する簡易ショートカット。font が null の場合は既定フォントを使う。
    /// </summary>
    public static void DrawGradient(this IFont? font,
        double x, double y,
        object? text, Gradation gradation,
        ReferencePoint point = ReferencePoint.TopLeft,
        Color? edgecolor = null,
        BlendMode blend = BlendMode.None, double opacity = 1)
        => (font ?? Drawing.DefaultFont).Draw(x, y, text, new DecorateOption(gradation),
            point, edgecolor, blend, opacity);

    /// <summary>
    /// テキスト装飾の指定。グラデーションかテクスチャのどちらか一方を保持する（通常描画時はどちらも null）。
    /// </summary>
    public class DecorateOption
    {
        public Gradation? Gradation { get; set; } = null;
        public Texture? Texture { get; set; } = null;

        public DecorateOption(Gradation gradation) => Gradation = gradation;
        public DecorateOption(Texture texture) => Texture = texture;
    }
}