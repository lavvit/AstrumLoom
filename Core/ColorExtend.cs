using static AstrumLoom.Color;

namespace AstrumLoom;

public static class ColorEx
{
    public static Color From(this Rainbow rainbow)
        => rainbow.Color.ToColor();

    /// <summary>
    /// HSB空間で補間する。
    /// - t: 0..1 の補間係数
    /// - shortest: true の場合は色相を最短回りで補間する（360度ラップを考慮）
    /// </summary>
    public static Color LerpHSB(Color a, Color b, float t, bool shortest = true)
    {
        // clamp t
        t = t < 0f ? 0f : (t > 1f ? 1f : t);

        var ha = a.ToHSB();
        var hb = b.ToHSB();

        double h1 = ha.Hue;
        double h2 = hb.Hue;
        double dh = h2 - h1;

        if (shortest)
        {
            if (dh > 180.0) dh -= 360.0;
            else if (dh < -180.0) dh += 360.0;
        }

        double h = h1 + dh * t;
        // normalize to [0,360)
        h = (h + 360.0) % 360.0;

        double s = ha.Saturation + (hb.Saturation - ha.Saturation) * t;
        double v = ha.Brightness + (hb.Brightness - ha.Brightness) * t;

        float alpha = (a.A + (b.A - a.A) * t) / 255f;

        return FromHSB(h, s, v, alpha);
    }

    #region oklab/oklch
    // ===== OKLab / OKLCH 変換＆知覚補間 =====
    private struct OKLab(float L, float a, float b)
    { public float L = L, a = a, b = b; }
    private struct OKLCH(float L, float C, float h)
    { public float L = L, C = C, h = h; }
    #region oklab
    // sRGB(0..255) -> Linear(0..1)
    private static float SrgbToLinear(float c)
        => (c <= 0.04045f) ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

    // Linear(0..1) -> sRGB(0..1)
    private static float LinearToSrgb(float c)
    {
        c = c < 0f ? 0f : (c > 1f ? 1f : c);
        return (c <= 0.0031308f) ? 12.92f * c : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;
    }

    // sRGB(0..255) -> OKLab
    private static OKLab RgbToOKLab(Color c)
    {
        // 1) to linear sRGB (0..1)
        float r = SrgbToLinear(c.R / 255f);
        float g = SrgbToLinear(c.G / 255f);
        float b = SrgbToLinear(c.B / 255f);

        // 2) linear sRGB -> LMS (OKLab)
        float l = 0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b;
        float m = 0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b;
        float s = 0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b;

        float l_ = MathF.Cbrt(l);
        float m_ = MathF.Cbrt(m);
        float s_ = MathF.Cbrt(s);

        float L = 0.2104542553f * l_ + 0.7936177850f * m_ - 0.0040720468f * s_;
        float A = 1.9779984951f * l_ - 2.4285922050f * m_ + 0.4505937099f * s_;
        float B = 0.0259040371f * l_ + 0.7827717662f * m_ - 0.8086757660f * s_;
        return new OKLab(L, A, B);
    }

    // OKLab -> sRGB(0..255)（単純クリップでガマット内に収める）
    private static Color OKLabToRgb(OKLab lab, byte alpha)
    {
        float l_ = lab.L + 0.3963377774f * lab.a + 0.2158037573f * lab.b;
        float m_ = lab.L - 0.1055613458f * lab.a - 0.0638541728f * lab.b;
        float s_ = lab.L - 0.0894841775f * lab.a - 1.2914855480f * lab.b;

        float l = l_ * l_ * l_;
        float m = m_ * m_ * m_;
        float s = s_ * s_ * s_;

        float r_lin = +4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s;
        float g_lin = -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s;
        float b_lin = -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s;

        int r = (int)MathF.Round(LinearToSrgb(r_lin) * 255f);
        int g = (int)MathF.Round(LinearToSrgb(g_lin) * 255f);
        int b = (int)MathF.Round(LinearToSrgb(b_lin) * 255f);
        return new Color(r, g, b, alpha);
    }

    private static OKLCH OKLabToOKLCH(OKLab lab)
    {
        float C = MathF.Sqrt(lab.a * lab.a + lab.b * lab.b);
        float h = MathF.Atan2(lab.b, lab.a) * (180f / MathF.PI);
        if (h < 0f) h += 360f;
        return new OKLCH(lab.L, C, h);
    }

    private static OKLab OKLCHToOKLab(OKLCH lch)
    {
        float a = lch.C * MathF.Cos(lch.h * MathF.PI / 180f);
        float b = lch.C * MathF.Sin(lch.h * MathF.PI / 180f);
        return new OKLab(lch.L, a, b);
    }

    public static Color LerpOKLab(Color a, Color b, float t)
    {
        t = t < 0f ? 0f : (t > 1f ? 1f : t);
        var la = RgbToOKLab(a);
        var lb = RgbToOKLab(b);
        float L = la.L + (lb.L - la.L) * t;
        float A = la.a + (lb.a - la.a) * t;
        float B = la.b + (lb.b - la.b) * t;
        var mid = new OKLab(L, A, B);
        byte alpha = (byte)MathF.Round(a.A + (b.A - a.A) * t);
        return OKLabToRgb(mid, alpha);
    }
    #endregion
    #region oklch
    public static Color LerpOKLCH(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        var la = OKLabToOKLCH(RgbToOKLab(a));
        var lb = OKLabToOKLCH(RgbToOKLab(b));

        // Hue 補間（角度）。longArc指定なら長い方の弧で回す
        (float h1, float h2) = OKLCHHue(a, b, HueRoute.Shortest);//Math.Round(difH, 0) < 0.5f

        float L = la.L + (lb.L - la.L) * t;
        float C = la.C + (lb.C - la.C) * t;
        float h = h1 + (h2 - h1) * t;
        // wrap
        h = (h + 360f) % 360f;

        var mid = OKLCHToOKLab(new OKLCH(L, C, h));
        byte alpha = (byte)MathF.Round(a.A + (b.A - a.A) * t);
        return OKLabToRgb(mid, alpha);
    }
    /// <summary>
    /// OKLCH（知覚的）で補間。赤→シアンを「紫側」で回したい時は throughMagenta=true。
    /// </summary>
    private static (float h1, float h2) OKLCHHue(Color a, Color b, HueRoute route)
    {
        var la = OKLabToOKLCH(RgbToOKLab(a));
        var lb = OKLabToOKLCH(RgbToOKLab(b));
        // Hue 補間（角度）。longArc指定なら長い方の弧で回す
        float h1 = la.h;
        float h2 = lb.h;

        float epsilonDeg = 0.5f;   // 180°近傍の“揺れ止め”幅

        float dh = h2 - h1;

        // 角度の正規化（-180..180]
        while (dh > 180f) dh -= 360f;
        while (dh <= -180f) dh += 360f;

        // 180度差の場合、長い方に回すことも(赤->シアンを紫側に回す)
        float ah = (float)new HSBColor(a).Hue;
        float bh = (float)new HSBColor(b).Hue;
        float difH = MathF.Abs(MathF.Abs(bh - ah) - 180f);

        switch (route)
        {
            case HueRoute.ViaMagenta:     // 必ず“長い弧”（紫側）へ
                {
                    float h3 = MathF.Abs(MathF.Max(h2 - h1, h1 - h2) - 180f);
                    float minH = MathF.Min(h2, h1);
                    if (minH < 60f && MathF.Abs(difH) < epsilonDeg)
                    {
                        // 短い方の弧なので、長い方に回す
                        if (h2 > h1)
                            h2 -= 360f;
                        else
                            h2 += 360f;
                    }
                }
                break;

            case HueRoute.ViaGreen:       // 必ず“長い弧”（緑側）へ
                dh = (dh > 0) ? dh + 360f : dh - 360f;
                break;

            case HueRoute.Shortest:
                if (h2 - h1 > 180f)
                    h2 -= 360f;
                if (h2 - h1 < -180f)
                    h2 += 360f;
                break;
        }
        /*if (throughMagenta)
        {
            float minH = MathF.Min(h2, h1);
            float centerH = (h1 + h2) / 2f;
            log += $"\n (HSB Δ180近似 detected, minH={minH:F2}, centerH={centerH:F2})";
            if (minH < 60f)
            {
                // 短い方の弧なので、長い方に回す
                if (h2 > h1)
                {
                    h2 -= 360f;
                    log += "\n (adjusted -)";
                }
                else
                {
                    h2 += 360f;
                    log += "\n (adjusted +)";
                }
            }
        }
        else
        {
        }*/
        return (h1, h2);
    }
    // 補助：-180..180 に正規化
    private static float Norm180(float a)
    {
        while (a > 180f) a -= 360f;
        while (a <= -180f) a += 360f;
        return a;
    }
    // 補助：0..1 のスムースステップ
    private static float SmoothStep(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
    public enum HueRoute { Shortest, ViaMagenta, ViaGreen }
    #endregion
    #endregion
}

public struct Rainbow(float potition, float white = 0, float black = 0)
{
    internal HSBColor Color = new(potition % 360, 1.0 - white, 1.0 - black);

    public Rainbow(Counter counter, float potition = 0, float white = 0, float black = 0, bool reverse = false)
        : this((potition +
              (reverse ? -360 * (float)counter.Progress + 360
              : 360 * (float)counter.Progress)) % 360, white, black)
    { }
}

public class Gradation
{
    private readonly ColorPoint[] Points;
    public Gradation((float pos, Color color)[] points)
    {
        Points = new ColorPoint[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            Points[i] = new ColorPoint(points[i].pos, points[i].color);
        }
    }
    public Gradation(Color[] colors)
    {
        Points = new ColorPoint[colors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            Points[i] = new ColorPoint((float)i / (colors.Length - 1), colors[i]);
        }
    }

    public Color GetColor(float position, ColorSpace colorSpace = ColorSpace.OKLab)
    {
        if (Points.Length == 0)
            throw new InvalidOperationException("No color points defined.");
        if (position <= Points[0].Position)
            return Points[0].Color;
        if (position >= Points[^1].Position)
            return Points[^1].Color;
        for (int i = 0; i < Points.Length - 1; i++)
        {
            if (position >= Points[i].Position && position <= Points[i + 1].Position)
            {
                float t = (position - Points[i].Position) / (Points[i + 1].Position - Points[i].Position);

                return colorSpace == ColorSpace.HSB
                    ? ColorEx.LerpHSB(Points[i].Color, Points[i + 1].Color, t, false)
                    : colorSpace == ColorSpace.OKLab
                    ? ColorEx.LerpOKLab(Points[i].Color, Points[i + 1].Color, t)
                    : colorSpace == ColorSpace.OKLCH
                    ? ColorEx.LerpOKLCH(Points[i].Color, Points[i + 1].Color, t)
                    : Lerp(Points[i].Color, Points[i + 1].Color, t);
            }
        }
        throw new InvalidOperationException("Position out of bounds.");
    }

    public override string ToString() => $"Grad.({string.Join("=>", Points.Select(p => p.ToString()))})";

    private struct ColorPoint(float position, Color color)
    {
        public float Position = position; // 0.0 to 1.0
        public Color Color = color;
        public override readonly string ToString() => $"[{Position:F2}:{Color}]";
    }

    public enum ColorSpace
    {
        RGB,
        HSB,
        OKLab,
        OKLCH,
    }

    private enum ColorBlend
    {
        None,
        Additive,
        Subtractive,
        Multiply,
    }
}
