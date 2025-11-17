using System.Numerics;

using DrawingColor = System.Drawing.Color;

namespace AstrumLoom;

/// <summary>
/// 色(RGB)を表す構造体
/// (RGBA 0-255)
/// </summary>
public readonly struct Color : IEquatable<Color>
{
    private readonly byte r;
    private readonly byte g;
    private readonly byte b;
    private readonly byte a;

    public int R => r;
    public int G => g;
    public int B => b;
    public int A => a;

    public static implicit operator DrawingColor(Color c) => c.ToDrawingColor();
    public static implicit operator Color(DrawingColor c) => FromDrawing(c);

    public Color(int red, int green, int blue, int alpha = 255)
    {
        r = ClipToByte(red);
        g = ClipToByte(green);
        b = ClipToByte(blue);
        a = ClipToByte(alpha);
    }

    public Color(int red, int green, int blue, float alpha)
        : this(red, green, blue, (int)MathF.Round(alpha * 255f)) { }

    public Color(byte red, byte green, byte blue, byte alpha = 255)
    {
        r = red; g = green; b = blue; a = alpha;
    }

    public Color(uint argb)
    {
        // ARGB as 0xAARRGGBB or passed from System.Drawing.Color.ToArgb()
        int v = unchecked((int)argb);
        var dc = DrawingColor.FromArgb(v);
        r = dc.R; g = dc.G; b = dc.B; a = dc.A;
    }

    public Color(Vector3 v)
    {
        a = 255;
        r = ToByte(v.X); g = ToByte(v.Y); b = ToByte(v.Z);
    }

    public Color(Vector4 v)
    {
        r = ToByte(v.X); g = ToByte(v.Y); b = ToByte(v.Z); a = ToByte(v.W);
    }

    public Color(HSBColor color, float alpha = 1f)
    {
        double percent = color.Hue / 60.0;
        int max = (int)(255 * color.Brightness);
        int min = max - (int)(max * color.Saturation);
        int m = max - min;
        double d = percent - (int)percent;
        int R, G, B;
        switch ((int)percent % 6)
        {
            case 0:
            default:
                R = max;
                G = (int)(m * d) + min;
                B = min;
                break;
            case 1:
                R = (int)(m * (1.0 - d)) + min;
                G = max;
                B = min;
                break;
            case 2:
                R = min;
                G = max;
                B = (int)(m * d) + min;
                break;
            case 3:
                R = min;
                G = (int)(m * (1.0 - d)) + min;
                B = max;
                break;
            case 4:
                R = (int)(m * d) + min;
                G = min;
                B = max;
                break;
            case 5:
                R = max;
                G = min;
                B = (int)(m * (1.0 - d)) + min;
                break;
        }

        r = ClipToByte(R);
        g = ClipToByte(G);
        b = ClipToByte(B);
        a = ClipToByte((int)MathF.Round(alpha * 255f));

    }
    public Color(Rainbow color, float alpha = 1f)
        : this(color.Color, alpha) { }

    private static byte ToByte(float f) => ClipToByte((int)MathF.Round(f * 255f));
    private static byte ClipToByte(int v) => (byte)(v < 0 ? 0 : (v > 255 ? 255 : v));

    public DrawingColor ToDrawingColor() => DrawingColor.FromArgb(a, r, g, b);

    public Vector4 ToVector4() => new(r / 255f, g / 255f, b / 255f, a / 255f);

    // HSBColor and Rainbow left mostly unchanged
    public struct HSBColor
    {
        public double Hue, Saturation, Brightness;
        public HSBColor(double hue, double saturation, double brightness)
        {
            Hue = hue % 360;
            Saturation = saturation;
            Brightness = brightness;
        }
        public HSBColor(Color color)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;
            // Hue
            Hue = delta == 0 ? 0 : max == r ? 60 * ((g - b) / delta % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
            if (Hue < 0)
            {
                Hue += 360;
            }
            // Saturation
            Saturation = (max == 0) ? 0 : (delta / max);
            // Brightness
            Brightness = max;
        }
    }
    public HSBColor ToHSB()
    {
        float fr = R / 255f;
        float fg = G / 255f;
        float fb = B / 255f;
        float max = MathF.Max(fr, MathF.Max(fg, fb));
        float min = MathF.Min(fr, MathF.Min(fg, fb));
        float delta = max - min;
        float h = 0f;
        if (delta != 0f)
        {
            if (max == fr)
            {
                h = (fg - fb) / delta;
                if (h < 0f) h += 6f;
            }
            else
            {
                h = max == fg ? 2f + (fb - fr) / delta : 4f + (fr - fg) / delta;
            }
            h *= 60f;
        }
        float s = max == 0f ? 0f : delta / max;
        float b = max;
        return new HSBColor(h, s, b);
    }

    public static Color FromHSB(double hue, double saturation, double brightness, float alpha = 1f)
        => new(new HSBColor(hue, saturation, brightness), alpha);
    public static Color FromHSB(HSBColor color, float alpha = 1f) => new(color, alpha);
    public static Color FromRainbow(Rainbow color, float alpha = 1f) => new(color, alpha);

    /// <summary>
    /// Return a premultiplied color (RGB multiplied by alpha).
    /// </summary>
    public Color ToPremultiplied()
    {
        float af = a / 255f;
        return new Color(
            (int)MathF.Round(r * af),
            (int)MathF.Round(g * af),
            (int)MathF.Round(b * af),
            a
        );
    }

    public Color WithAlpha(int alpha) => new(r, g, b, alpha);
    public Color WithAlpha(float alpha) => new(r, g, b, (int)MathF.Round(alpha * 255f));

    public static Color FromDrawing(DrawingColor d) => new(d.R, d.G, d.B, d.A);
    public static Color FromArgb(int r, int g, int b, int a = 255) => new(r, g, b, a);
    public static Color FromHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) throw new ArgumentNullException(nameof(hex));
        hex = hex.Trim();
        if (hex.StartsWith('#')) hex = hex[1..];
        if (hex.Length == 6)
        {
            int rr = Convert.ToInt32(hex[..2], 16);
            int gg = Convert.ToInt32(hex.Substring(2, 2), 16);
            int bb = Convert.ToInt32(hex.Substring(4, 2), 16);
            return new Color(rr, gg, bb, 255);
        }
        if (hex.Length == 8)
        {
            int aa = Convert.ToInt32(hex[..2], 16);
            int rr = Convert.ToInt32(hex.Substring(2, 2), 16);
            int gg = Convert.ToInt32(hex.Substring(4, 2), 16);
            int bb = Convert.ToInt32(hex.Substring(6, 2), 16);
            return new Color(rr, gg, bb, aa);
        }
        throw new FormatException("Hex color must be 6 (RRGGBB) or 8 (AARRGGBB) digits");
    }

    public static bool TryParse(string? input, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(input)) return false;
        input = input.Trim();
        try
        {
            if (input.StartsWith('#'))
            {
                color = FromHex(input);
                return true;
            }

            if (int.TryParse(input, out int raw))
            {
                var dc = DrawingColor.FromArgb(raw);
                color = FromDrawing(dc);
                return true;
            }

            string[] parts = input.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3)
            {
                if (int.TryParse(parts[0], out int rr) && int.TryParse(parts[1], out int gg) && int.TryParse(parts[2], out int bb))
                {
                    int aa = 255;
                    if (parts.Length >= 4) int.TryParse(parts[3], out aa);
                    color = new Color(rr, gg, bb, aa);
                    return true;
                }
            }

            var named = DrawingColor.FromName(input);
            if (named.IsKnownColor || named.A != 0 || named.R != 0 || named.G != 0 || named.B != 0)
            {
                color = FromDrawing(named);
                return true;
            }
        }
        catch { }

        return false;
    }
    public static Color Parse(string input) => TryParse(input, out var color) ? color : White;
    public static Color Parse(int hex)
    {
        var dc = DrawingColor.FromArgb(hex);
        return FromDrawing(dc);
    }

    public static Color Lerp(Color a, Color b, float t)
    {
        t = t < 0f ? 0f : (t > 1f ? 1f : t);
        return new Color(
            (int)MathF.Round(a.R + (b.R - a.R) * t),
            (int)MathF.Round(a.G + (b.G - a.G) * t),
            (int)MathF.Round(a.B + (b.B - a.B) * t),
            (int)MathF.Round(a.A + (b.A - a.A) * t)
        );
    }

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
    private struct OKLab { public float L, a, b; public OKLab(float L, float a, float b) { this.L = L; this.a = a; this.b = b; } }
    private struct OKLCH { public float L, C, h; public OKLCH(float L, float C, float h) { this.L = L; this.C = C; this.h = h; } }
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

    // 背景色と被らない色
    public static Color VisibleColor(Color color)
    {
        // 黒か白かを選択
        // 明度を取得、逆を返す
        int brightness = (int)MathF.Round((color.R * 299 + color.G * 587 + color.B * 114) / 1000f);
        return brightness >= 128 ? Black : White;
    }
    public static Color Invert(Color color) => new(255 - color.R, 255 - color.G, 255 - color.B, color.A);

    public override string ToString() => $"R:{R} G:{G} B:{B} A:{A}";

    public bool Equals(Color other) => r == other.r && g == other.g && b == other.b && a == other.a;
    public override bool Equals(object? obj) => obj is Color c && Equals(c);
    public override int GetHashCode() => HashCode.Combine(r, g, b, a);
    public static bool operator ==(Color left, Color right) => left.Equals(right);
    public static bool operator !=(Color left, Color right) => !left.Equals(right);
    public static Color operator *(Color color, float factor) => new(
        (int)MathF.Round(color.R * factor),
        (int)MathF.Round(color.G * factor),
        (int)MathF.Round(color.B * factor),
        color.A
    );
    public static Color operator *(float factor, Color color) => color * factor;
    public static Color operator +(Color a, Color b) => new(
        a.R + b.R,
        a.G + b.G,
        a.B + b.B,
        a.A + b.A
    );
    public static Color operator -(Color a, Color b) => new(
        a.R - b.R,
        a.G - b.G,
        a.B - b.B,
        a.A - b.A
    );

    #region ColorDic
    // Common named colors (initialized from System.Drawing.Color for correctness)
    /// <summary>
    /// 透明 (R:0,G:0,B:0,A:0)
    /// </summary>
    public static Color Transparent { get; } = FromDrawing(DrawingColor.Transparent);
    /// <summary>
    /// ごく淡い青 (R:240,G:248,B:255,A:255)
    /// </summary>
    public static Color AliceBlue { get; } = FromDrawing(DrawingColor.AliceBlue);
    /// <summary>
    /// くすんだ白 (R:250,G:235,B:215,A:255)
    /// </summary>
    public static Color AntiqueWhite { get; } = FromDrawing(DrawingColor.AntiqueWhite);
    /// <summary>
    /// 青緑色 (R:0,G:255,B:255,A:255)
    /// </summary>
    public static Color Aqua { get; } = FromDrawing(DrawingColor.Aqua);
    /// <summary>
    /// 淡い青緑 (R:127,G:255,B:212,A:255)
    /// </summary>
    public static Color Aquamarine { get; } = FromDrawing(DrawingColor.Aquamarine);
    /// <summary>
    /// 青みの白 (R:240,G:255,B:255,A:255)
    /// </summary>
    public static Color Azure { get; } = FromDrawing(DrawingColor.Azure);
    /// <summary>
    /// ベージュ (R:245,G:245,B:220,A:255)
    /// </summary>
    public static Color Beige { get; } = FromDrawing(DrawingColor.Beige);
    /// <summary>
    /// 薄橙 (R:255,G:228,B:196,A:255)
    /// </summary>
    public static Color Bisque { get; } = FromDrawing(DrawingColor.Bisque);
    /// <summary>
    /// 黒 (R:0,G:0,B:0,A:255)
    /// </summary>
    public static Color Black { get; } = FromDrawing(DrawingColor.Black);
    /// <summary>
    /// 薄いアーモンド色 (R:255,G:235,B:205,A:255)
    /// </summary>
    public static Color BlanchedAlmond { get; } = FromDrawing(DrawingColor.BlanchedAlmond);
    /// <summary>
    /// 青色 (R:0,G:0,B:255,A:255)
    /// </summary>
    public static Color Blue { get; } = FromDrawing(DrawingColor.Blue);
    /// <summary>
    /// 青紫色 (R:138,G:43,B:226,A:255)
    /// </summary>
    public static Color BlueViolet { get; } = FromDrawing(DrawingColor.BlueViolet);
    /// <summary>
    /// 茶色 (R:165,G:42,B:42,A:255)
    /// </summary>
    public static Color Brown { get; } = FromDrawing(DrawingColor.Brown);
    /// <summary>
    /// 木肌色 (R:222,G:184,B:135,A:255)
    /// </summary>
    public static Color BurlyWood { get; } = FromDrawing(DrawingColor.BurlyWood);
    /// <summary>
    /// くすんだ青緑 (R:95,G:158,B:160,A:255)
    /// </summary>
    public static Color CadetBlue { get; } = FromDrawing(DrawingColor.CadetBlue);
    /// <summary>
    /// 黄緑色 (R:127,G:255,B:0,A:255)
    /// </summary>
    public static Color Chartreuse { get; } = FromDrawing(DrawingColor.Chartreuse);
    /// <summary>
    /// チョコレート色 (R:210,G:105,B:30,A:255)
    /// </summary>
    public static Color Chocolate { get; } = FromDrawing(DrawingColor.Chocolate);
    /// <summary>
    /// 珊瑚色 (R:255,G:127,B:80,A:255)
    /// </summary>
    public static Color Coral { get; } = FromDrawing(DrawingColor.Coral);
    /// <summary>
    /// 明るい青 (R:100,G:149,B:237,A:255)
    /// </summary>
    public static Color CornflowerBlue { get; } = FromDrawing(DrawingColor.CornflowerBlue);
    /// <summary>
    /// 淡いクリーム色 (R:255,G:248,B:220,A:255)
    /// </summary>
    public static Color Cornsilk { get; } = FromDrawing(DrawingColor.Cornsilk);
    /// <summary>
    /// 深い赤 (R:220,G:20,B:60,A:255)
    /// </summary>
    public static Color Crimson { get; } = FromDrawing(DrawingColor.Crimson);
    /// <summary>
    /// 青緑色 (R:0,G:255,B:255,A:255)
    /// </summary>
    public static Color Cyan { get; } = FromDrawing(DrawingColor.Cyan);
    /// <summary>
    /// 濃い青 (R:0,G:0,B:139,A:255)
    /// </summary>
    public static Color DarkBlue { get; } = FromDrawing(DrawingColor.DarkBlue);
    /// <summary>
    /// 濃い青緑 (R:0,G:139,B:139,A:255)
    /// </summary>
    public static Color DarkCyan { get; } = FromDrawing(DrawingColor.DarkCyan);
    /// <summary>
    /// 濃い黄金色 (R:184,G:134,B:11,A:255)
    /// </summary>
    public static Color DarkGoldenrod { get; } = FromDrawing(DrawingColor.DarkGoldenrod);
    /// <summary>
    /// 暗い灰色 (R:169,G:169,B:169,A:255)
    /// </summary>
    public static Color DarkGray { get; } = FromDrawing(DrawingColor.DarkGray);
    /// <summary>
    /// 濃い緑 (R:0,G:100,B:0,A:255)
    /// </summary>
    public static Color DarkGreen { get; } = FromDrawing(DrawingColor.DarkGreen);
    /// <summary>
    /// 濃いカーキ色 (R:189,G:183,B:107,A:255)
    /// </summary>
    public static Color DarkKhaki { get; } = FromDrawing(DrawingColor.DarkKhaki);
    /// <summary>
    /// 濃い赤紫 (R:139,G:0,B:139,A:255)
    /// </summary>
    public static Color DarkMagenta { get; } = FromDrawing(DrawingColor.DarkMagenta);
    /// <summary>
    /// 濃いオリーブ緑 (R:85,G:107,B:47,A:255)
    /// </summary>
    public static Color DarkOliveGreen { get; } = FromDrawing(DrawingColor.DarkOliveGreen);
    /// <summary>
    /// 濃い橙色 (R:255,G:140,B:0,A:255)
    /// </summary>
    public static Color DarkOrange { get; } = FromDrawing(DrawingColor.DarkOrange);
    /// <summary>
    /// 濃い蘭紫 (R:153,G:50,B:204,A:255)
    /// </summary>
    public static Color DarkOrchid { get; } = FromDrawing(DrawingColor.DarkOrchid);
    /// <summary>
    /// 濃い赤 (R:139,G:0,B:0,A:255)
    /// </summary>
    public static Color DarkRed { get; } = FromDrawing(DrawingColor.DarkRed);
    /// <summary>
    /// 濃いサーモン (R:233,G:150,B:122,A:255)
    /// </summary>
    public static Color DarkSalmon { get; } = FromDrawing(DrawingColor.DarkSalmon);
    /// <summary>
    /// くすんだ青緑 (R:143,G:188,B:139,A:255)
    /// </summary>
    public static Color DarkSeaGreen { get; } = FromDrawing(DrawingColor.DarkSeaGreen);
    /// <summary>
    /// 濃いスレートブルー (R:72,G:61,B:139,A:255)
    /// </summary>
    public static Color DarkSlateBlue { get; } = FromDrawing(DrawingColor.DarkSlateBlue);
    /// <summary>
    /// 濃いスレートグレー (R:47,G:79,B:79,A:255)
    /// </summary>
    public static Color DarkSlateGray { get; } = FromDrawing(DrawingColor.DarkSlateGray);
    /// <summary>
    /// 濃いターコイズ (R:0,G:206,B:209,A:255)
    /// </summary>
    public static Color DarkTurquoise { get; } = FromDrawing(DrawingColor.DarkTurquoise);
    /// <summary>
    /// 濃いすみれ色 (R:148,G:0,B:211,A:255)
    /// </summary>
    public static Color DarkViolet { get; } = FromDrawing(DrawingColor.DarkViolet);
    /// <summary>
    /// 鮮やかな桃色 (R:255,G:20,B:147,A:255)
    /// </summary>
    public static Color DeepPink { get; } = FromDrawing(DrawingColor.DeepPink);
    /// <summary>
    /// 鮮やかな空色 (R:0,G:191,B:255,A:255)
    /// </summary>
    public static Color DeepSkyBlue { get; } = FromDrawing(DrawingColor.DeepSkyBlue);
    /// <summary>
    /// やや暗い灰色 (R:105,G:105,B:105,A:255)
    /// </summary>
    public static Color DimGray { get; } = FromDrawing(DrawingColor.DimGray);
    /// <summary>
    /// 鮮やかな青 (R:30,G:144,B:255,A:255)
    /// </summary>
    public static Color DodgerBlue { get; } = FromDrawing(DrawingColor.DodgerBlue);
    /// <summary>
    /// レンガ色 (R:178,G:34,B:34,A:255)
    /// </summary>
    public static Color Firebrick { get; } = FromDrawing(DrawingColor.Firebrick);
    /// <summary>
    /// 黄みの白 (R:255,G:250,B:240,A:255)
    /// </summary>
    public static Color FloralWhite { get; } = FromDrawing(DrawingColor.FloralWhite);
    /// <summary>
    /// 深緑 (R:34,G:139,B:34,A:255)
    /// </summary>
    public static Color ForestGreen { get; } = FromDrawing(DrawingColor.ForestGreen);
    /// <summary>
    /// 鮮やかな赤紫 (R:255,G:0,B:255,A:255)
    /// </summary>
    public static Color Fuchsia { get; } = FromDrawing(DrawingColor.Fuchsia);
    /// <summary>
    /// ごく薄い灰色 (R:220,G:220,B:220,A:255)
    /// </summary>
    public static Color Gainsboro { get; } = FromDrawing(DrawingColor.Gainsboro);
    /// <summary>
    /// ごく淡い青みの白 (R:248,G:248,B:255,A:255)
    /// </summary>
    public static Color GhostWhite { get; } = FromDrawing(DrawingColor.GhostWhite);
    /// <summary>
    /// 金色 (R:255,G:215,B:0,A:255)
    /// </summary>
    public static Color Gold { get; } = FromDrawing(DrawingColor.Gold);
    /// <summary>
    /// 黄土がかった黄 (R:218,G:165,B:32,A:255)
    /// </summary>
    public static Color Goldenrod { get; } = FromDrawing(DrawingColor.Goldenrod);
    /// <summary>
    /// 灰色 (R:128,G:128,B:128,A:255)
    /// </summary>
    public static Color Gray { get; } = FromDrawing(DrawingColor.Gray);
    /// <summary>
    /// 緑色 (R:0,G:128,B:0,A:255)
    /// </summary>
    public static Color Green { get; } = FromDrawing(DrawingColor.Green);
    /// <summary>
    /// 緑みの黄色 (R:173,G:255,B:47,A:255)
    /// </summary>
    public static Color GreenYellow { get; } = FromDrawing(DrawingColor.GreenYellow);
    /// <summary>
    /// 淡い黄緑がかった白 (R:240,G:255,B:240,A:255)
    /// </summary>
    public static Color Honeydew { get; } = FromDrawing(DrawingColor.Honeydew);
    /// <summary>
    /// 鮮やかな桃色 (R:255,G:105,B:180,A:255)
    /// </summary>
    public static Color HotPink { get; } = FromDrawing(DrawingColor.HotPink);
    /// <summary>
    /// くすんだ赤 (R:205,G:92,B:92,A:255)
    /// </summary>
    public static Color IndianRed { get; } = FromDrawing(DrawingColor.IndianRed);
    /// <summary>
    /// 藍色 (R:75,G:0,B:130,A:255)
    /// </summary>
    public static Color Indigo { get; } = FromDrawing(DrawingColor.Indigo);
    /// <summary>
    /// 象牙色 (R:255,G:255,B:240,A:255)
    /// </summary>
    public static Color Ivory { get; } = FromDrawing(DrawingColor.Ivory);
    /// <summary>
    /// カーキ色 (R:240,G:230,B:140,A:255)
    /// </summary>
    public static Color Khaki { get; } = FromDrawing(DrawingColor.Khaki);
    /// <summary>
    /// 淡い紫 (R:230,G:230,B:250,A:255)
    /// </summary>
    public static Color Lavender { get; } = FromDrawing(DrawingColor.Lavender);
    /// <summary>
    /// 淡い桜色 (R:255,G:240,B:245,A:255)
    /// </summary>
    public static Color LavenderBlush { get; } = FromDrawing(DrawingColor.LavenderBlush);
    /// <summary>
    /// 芝生のような緑 (R:124,G:252,B:0,A:255)
    /// </summary>
    public static Color LawnGreen { get; } = FromDrawing(DrawingColor.LawnGreen);
    /// <summary>
    /// 淡いレモン色 (R:255,G:250,B:205,A:255)
    /// </summary>
    public static Color LemonChiffon { get; } = FromDrawing(DrawingColor.LemonChiffon);
    /// <summary>
    /// 淡い青 (R:173,G:216,B:230,A:255)
    /// </summary>
    public static Color LightBlue { get; } = FromDrawing(DrawingColor.LightBlue);
    /// <summary>
    /// 淡い珊瑚色 (R:240,G:128,B:128,A:255)
    /// </summary>
    public static Color LightCoral { get; } = FromDrawing(DrawingColor.LightCoral);
    /// <summary>
    /// 淡い青緑 (R:224,G:255,B:255,A:255)
    /// </summary>
    public static Color LightCyan { get; } = FromDrawing(DrawingColor.LightCyan);
    /// <summary>
    /// 淡い黄金色 (R:250,G:250,B:210,A:255)
    /// </summary>
    public static Color LightGoldenrodYellow { get; } = FromDrawing(DrawingColor.LightGoldenrodYellow);
    /// <summary>
    /// 明るい灰色 (R:211,G:211,B:211,A:255)
    /// </summary>
    public static Color LightGray { get; } = FromDrawing(DrawingColor.LightGray);
    /// <summary>
    /// 淡い緑 (R:144,G:238,B:144,A:255)
    /// </summary>
    public static Color LightGreen { get; } = FromDrawing(DrawingColor.LightGreen);
    /// <summary>
    /// 淡い桃色 (R:255,G:182,B:193,A:255)
    /// </summary>
    public static Color LightPink { get; } = FromDrawing(DrawingColor.LightPink);
    /// <summary>
    /// 淡いサーモン (R:255,G:160,B:122,A:255)
    /// </summary>
    public static Color LightSalmon { get; } = FromDrawing(DrawingColor.LightSalmon);
    /// <summary>
    /// 明るい青緑 (R:32,G:178,B:170,A:255)
    /// </summary>
    public static Color LightSeaGreen { get; } = FromDrawing(DrawingColor.LightSeaGreen);
    /// <summary>
    /// 淡い空色 (R:135,G:206,B:250,A:255)
    /// </summary>
    public static Color LightSkyBlue { get; } = FromDrawing(DrawingColor.LightSkyBlue);
    /// <summary>
    /// 淡いスレートグレー (R:119,G:136,B:153,A:255)
    /// </summary>
    public static Color LightSlateGray { get; } = FromDrawing(DrawingColor.LightSlateGray);
    /// <summary>
    /// 淡いスチールブルー (R:176,G:196,B:222,A:255)
    /// </summary>
    public static Color LightSteelBlue { get; } = FromDrawing(DrawingColor.LightSteelBlue);
    /// <summary>
    /// 淡い黄色 (R:255,G:255,B:224,A:255)
    /// </summary>
    public static Color LightYellow { get; } = FromDrawing(DrawingColor.LightYellow);
    /// <summary>
    /// 鮮やかな緑 (R:0,G:255,B:0,A:255)
    /// </summary>
    public static Color Lime { get; } = FromDrawing(DrawingColor.Lime);
    /// <summary>
    /// 明るい緑 (R:50,G:205,B:50,A:255)
    /// </summary>
    public static Color LimeGreen { get; } = FromDrawing(DrawingColor.LimeGreen);
    /// <summary>
    /// 生成り色 (R:250,G:240,B:230,A:255)
    /// </summary>
    public static Color Linen { get; } = FromDrawing(DrawingColor.Linen);
    /// <summary>
    /// 赤紫色 (R:255,G:0,B:255,A:255)
    /// </summary>
    public static Color Magenta { get; } = FromDrawing(DrawingColor.Magenta);
    /// <summary>
    /// えんじ色 (R:128,G:0,B:0,A:255)
    /// </summary>
    public static Color Maroon { get; } = FromDrawing(DrawingColor.Maroon);
    /// <summary>
    /// やや明るい青緑 (R:102,G:205,B:170,A:255)
    /// </summary>
    public static Color MediumAquamarine { get; } = FromDrawing(DrawingColor.MediumAquamarine);
    /// <summary>
    /// やや濃い青 (R:0,G:0,B:205,A:255)
    /// </summary>
    public static Color MediumBlue { get; } = FromDrawing(DrawingColor.MediumBlue);
    /// <summary>
    /// やや明るい紫 (R:186,G:85,B:211,A:255)
    /// </summary>
    public static Color MediumOrchid { get; } = FromDrawing(DrawingColor.MediumOrchid);
    /// <summary>
    /// やや明るい紫 (R:147,G:112,B:219,A:255)
    /// </summary>
    public static Color MediumPurple { get; } = FromDrawing(DrawingColor.MediumPurple);
    /// <summary>
    /// やや明るい青緑 (R:60,G:179,B:113,A:255)
    /// </summary>
    public static Color MediumSeaGreen { get; } = FromDrawing(DrawingColor.MediumSeaGreen);
    /// <summary>
    /// やや明るいスレートブルー (R:123,G:104,B:238,A:255)
    /// </summary>
    public static Color MediumSlateBlue { get; } = FromDrawing(DrawingColor.MediumSlateBlue);
    /// <summary>
    /// やや鮮やかな若草色 (R:0,G:250,B:154,A:255)
    /// </summary>
    public static Color MediumSpringGreen { get; } = FromDrawing(DrawingColor.MediumSpringGreen);
    /// <summary>
    /// やや明るいターコイズ (R:72,G:209,B:204,A:255)
    /// </summary>
    public static Color MediumTurquoise { get; } = FromDrawing(DrawingColor.MediumTurquoise);
    /// <summary>
    /// やや暗い赤紫 (R:199,G:21,B:133,A:255)
    /// </summary>
    public static Color MediumVioletRed { get; } = FromDrawing(DrawingColor.MediumVioletRed);
    /// <summary>
    /// 漆黒に近い青 (R:25,G:25,B:112,A:255)
    /// </summary>
    public static Color MidnightBlue { get; } = FromDrawing(DrawingColor.MidnightBlue);
    /// <summary>
    /// ごく淡い緑がかった白 (R:245,G:255,B:250,A:255)
    /// </summary>
    public static Color MintCream { get; } = FromDrawing(DrawingColor.MintCream);
    /// <summary>
    /// 淡いバラ色 (R:255,G:228,B:225,A:255)
    /// </summary>
    public static Color MistyRose { get; } = FromDrawing(DrawingColor.MistyRose);
    /// <summary>
    /// 黄みの肌色 (R:255,G:228,B:181,A:255)
    /// </summary>
    public static Color Moccasin { get; } = FromDrawing(DrawingColor.Moccasin);
    /// <summary>
    /// MonoGameオレンジ（鮮やかな橙） (R:231,G:60,B:0,A:255)
    /// </summary>
    public static Color MonoGameOrange { get; } = new Color(231, 60, 0);
    /// <summary>
    /// 黄みの肌色 (R:255,G:222,B:173,A:255)
    /// </summary>
    public static Color NavajoWhite { get; } = FromDrawing(DrawingColor.NavajoWhite);
    /// <summary>
    /// 濃紺 (R:0,G:0,B:128,A:255)
    /// </summary>
    public static Color Navy { get; } = FromDrawing(DrawingColor.Navy);
    /// <summary>
    /// 生成り白 (R:253,G:245,B:230,A:255)
    /// </summary>
    public static Color OldLace { get; } = FromDrawing(DrawingColor.OldLace);
    /// <summary>
    /// オリーブ色 (R:128,G:128,B:0,A:255)
    /// </summary>
    public static Color Olive { get; } = FromDrawing(DrawingColor.Olive);
    /// <summary>
    /// くすんだオリーブ色 (R:107,G:142,B:35,A:255)
    /// </summary>
    public static Color OliveDrab { get; } = FromDrawing(DrawingColor.OliveDrab);
    /// <summary>
    /// 橙色 (R:255,G:165,B:0,A:255)
    /// </summary>
    public static Color Orange { get; } = FromDrawing(DrawingColor.Orange);
    /// <summary>
    /// 赤みの強い橙 (R:255,G:69,B:0,A:255)
    /// </summary>
    public static Color OrangeRed { get; } = FromDrawing(DrawingColor.OrangeRed);
    /// <summary>
    /// 蘭紫（淡い紫） (R:218,G:112,B:214,A:255)
    /// </summary>
    public static Color Orchid { get; } = FromDrawing(DrawingColor.Orchid);
    /// <summary>
    /// 薄い黄金色 (R:238,G:232,B:170,A:255)
    /// </summary>
    public static Color PaleGoldenrod { get; } = FromDrawing(DrawingColor.PaleGoldenrod);
    /// <summary>
    /// 薄い緑 (R:152,G:251,B:152,A:255)
    /// </summary>
    public static Color PaleGreen { get; } = FromDrawing(DrawingColor.PaleGreen);
    /// <summary>
    /// 薄いターコイズ (R:175,G:238,B:238,A:255)
    /// </summary>
    public static Color PaleTurquoise { get; } = FromDrawing(DrawingColor.PaleTurquoise);
    /// <summary>
    /// 薄い赤紫 (R:219,G:112,B:147,A:255)
    /// </summary>
    public static Color PaleVioletRed { get; } = FromDrawing(DrawingColor.PaleVioletRed);
    /// <summary>
    /// 淡い肌色 (R:255,G:239,B:213,A:255)
    /// </summary>
    public static Color PapayaWhip { get; } = FromDrawing(DrawingColor.PapayaWhip);
    /// <summary>
    /// 桃色がかった肌色 (R:255,G:218,B:185,A:255)
    /// </summary>
    public static Color PeachPuff { get; } = FromDrawing(DrawingColor.PeachPuff);
    /// <summary>
    /// 黄土寄りの茶色 (R:205,G:133,B:63,A:255)
    /// </summary>
    public static Color Peru { get; } = FromDrawing(DrawingColor.Peru);
    /// <summary>
    /// 桃色 (R:255,G:192,B:203,A:255)
    /// </summary>
    public static Color Pink { get; } = FromDrawing(DrawingColor.Pink);
    /// <summary>
    /// 赤紫（プラム） (R:221,G:160,B:221,A:255)
    /// </summary>
    public static Color Plum { get; } = FromDrawing(DrawingColor.Plum);
    /// <summary>
    /// 淡い青 (R:176,G:224,B:230,A:255)
    /// </summary>
    public static Color PowderBlue { get; } = FromDrawing(DrawingColor.PowderBlue);
    /// <summary>
    /// 紫色 (R:128,G:0,B:128,A:255)
    /// </summary>
    public static Color Purple { get; } = FromDrawing(DrawingColor.Purple);
    /// <summary>
    /// 赤色 (R:255,G:0,B:0,A:255)
    /// </summary>
    public static Color Red { get; } = FromDrawing(DrawingColor.Red);
    /// <summary>
    /// ばら色がかった茶色 (R:188,G:143,B:143,A:255)
    /// </summary>
    public static Color RosyBrown { get; } = FromDrawing(DrawingColor.RosyBrown);
    /// <summary>
    /// 鮮やかな青 (R:65,G:105,B:225,A:255)
    /// </summary>
    public static Color RoyalBlue { get; } = FromDrawing(DrawingColor.RoyalBlue);
    /// <summary>
    /// 濃い茶色 (R:139,G:69,B:19,A:255)
    /// </summary>
    public static Color SaddleBrown { get; } = FromDrawing(DrawingColor.SaddleBrown);
    /// <summary>
    /// サーモンピンク (R:250,G:128,B:114,A:255)
    /// </summary>
    public static Color Salmon { get; } = FromDrawing(DrawingColor.Salmon);
    /// <summary>
    /// 砂色の茶 (R:244,G:164,B:96,A:255)
    /// </summary>
    public static Color SandyBrown { get; } = FromDrawing(DrawingColor.SandyBrown);
    /// <summary>
    /// 深い青緑 (R:46,G:139,B:87,A:255)
    /// </summary>
    public static Color SeaGreen { get; } = FromDrawing(DrawingColor.SeaGreen);
    /// <summary>
    /// 貝殻色 (R:255,G:245,B:238,A:255)
    /// </summary>
    public static Color SeaShell { get; } = FromDrawing(DrawingColor.SeaShell);
    /// <summary>
    /// 黄土色 (R:160,G:82,B:45,A:255)
    /// </summary>
    public static Color Sienna { get; } = FromDrawing(DrawingColor.Sienna);
    /// <summary>
    /// 銀色 (R:192,G:192,B:192,A:255)
    /// </summary>
    public static Color Silver { get; } = FromDrawing(DrawingColor.Silver);
    /// <summary>
    /// 空色 (R:135,G:206,B:235,A:255)
    /// </summary>
    public static Color SkyBlue { get; } = FromDrawing(DrawingColor.SkyBlue);
    /// <summary>
    /// 落ち着いた青紫 (R:106,G:90,B:205,A:255)
    /// </summary>
    public static Color SlateBlue { get; } = FromDrawing(DrawingColor.SlateBlue);
    /// <summary>
    /// 青みの灰色 (R:112,G:128,B:144,A:255)
    /// </summary>
    public static Color SlateGray { get; } = FromDrawing(DrawingColor.SlateGray);
    /// <summary>
    /// 雪色 (R:255,G:250,B:250,A:255)
    /// </summary>
    public static Color Snow { get; } = FromDrawing(DrawingColor.Snow);
    /// <summary>
    /// 若草色 (R:0,G:255,B:127,A:255)
    /// </summary>
    public static Color SpringGreen { get; } = FromDrawing(DrawingColor.SpringGreen);
    /// <summary>
    /// 灰みの青 (R:70,G:130,B:180,A:255)
    /// </summary>
    public static Color SteelBlue { get; } = FromDrawing(DrawingColor.SteelBlue);
    /// <summary>
    /// 黄褐色 (R:210,G:180,B:140,A:255)
    /// </summary>
    public static Color Tan { get; } = FromDrawing(DrawingColor.Tan);
    /// <summary>
    /// 深い青緑 (R:0,G:128,B:128,A:255)
    /// </summary>
    public static Color Teal { get; } = FromDrawing(DrawingColor.Teal);
    /// <summary>
    /// 薄い紫 (R:216,G:191,B:216,A:255)
    /// </summary>
    public static Color Thistle { get; } = FromDrawing(DrawingColor.Thistle);
    /// <summary>
    /// トマト色 (R:255,G:99,B:71,A:255)
    /// </summary>
    public static Color Tomato { get; } = FromDrawing(DrawingColor.Tomato);
    /// <summary>
    /// 青緑（ターコイズ） (R:64,G:224,B:208,A:255)
    /// </summary>
    public static Color Turquoise { get; } = FromDrawing(DrawingColor.Turquoise);
    /// <summary>
    /// すみれ色 (R:238,G:130,B:238,A:255)
    /// </summary>
    public static Color Violet { get; } = FromDrawing(DrawingColor.Violet);
    /// <summary>
    /// 小麦色 (R:245,G:222,B:179,A:255)
    /// </summary>
    public static Color Wheat { get; } = FromDrawing(DrawingColor.Wheat);
    /// <summary>
    /// 白 (R:255,G:255,B:255,A:255)
    /// </summary>
    public static Color White { get; } = FromDrawing(DrawingColor.White);
    /// <summary>
    /// スモークがかった白 (R:245,G:245,B:245,A:255)
    /// </summary>
    public static Color WhiteSmoke { get; } = FromDrawing(DrawingColor.WhiteSmoke);
    /// <summary>
    /// 黄色 (R:255,G:255,B:0,A:255)
    /// </summary>
    public static Color Yellow { get; } = FromDrawing(DrawingColor.Yellow);
    /// <summary>
    /// 黄緑色 (R:154,G:205,B:50,A:255)
    /// </summary>
    public static Color YellowGreen { get; } = FromDrawing(DrawingColor.YellowGreen);
    #endregion

    internal readonly string DebugDisplayString => $"{R}  {G}  {B}  {A}";
}
public struct Rainbow(float potition, float white = 0, float black = 0)
{
    internal Color.HSBColor Color = new(potition % 360, 1.0 - white, 1.0 - black);

    public Rainbow(double prog, float potition = 0, float white = 0, float black = 0, bool reverse = false)
        : this((potition +
              (reverse ? -360 * (float)prog + 360
              : 360 * (float)prog)) % 360, white, black)
    { }
}