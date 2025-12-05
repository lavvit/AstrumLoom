namespace AstrumLoom.Extend;

public static class DecorateText
{
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

    public class DecorateOption
    {
        public Gradation? Gradation { get; set; } = null;
        public Texture? Texture { get; set; } = null;

        public DecorateOption(Gradation gradation) => Gradation = gradation;
        public DecorateOption(Texture texture) => Texture = texture;
    }
}