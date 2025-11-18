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
        _font = LoadFontEx(path, spec.Size, null, 0);
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
}
