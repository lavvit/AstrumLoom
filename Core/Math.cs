namespace AstrumLoom;

public static class MathExtend
{
    /// <summary>最大公約数を返します。</summary>
    public static int GCD(int a, int b)
    {
        while (b != 0) { int t = a % b; a = b; b = t; }
        return Math.Abs(a);
    }
    /// <summary>最小公倍数を返します。</summary>
    public static int LCM(int a, int b)
    {
        // If either is zero, LCM is defined as 0 (avoid division by zero)
        if (a == 0 || b == 0) return 0;
        int g = GCD(a, b);
        if (g == 0) return 0;
        // Use division first to reduce overflow risk
        return Math.Abs(a / g * b);
    }

    /// <summary>複数の整数の最小公倍数を返します。</summary>
    public static int LCM(IEnumerable<int> numbers)
    {
        int lcm = 1;
        foreach (int n in numbers)
        {
            // If any number is zero, overall LCM is zero
            if (n == 0) return 0;
            lcm = LCM(lcm, n);
        }
        return lcm;
    }
    /// <summary>有理数の最小公倍数を返します。</summary>
    public static int LCM(Rational a, Rational b)
    {
        // 分母のLCMで整数化
        int L = LCM(a.Den, b.Den);
        if (L == 0) return 1;
        int i1 = checked(a.Num * (L / a.Den));
        int i2 = checked(b.Num * (L / b.Den));
        if (i1 == 0 || i2 == 0) return 1;

        int m = LCM(Math.Abs(i1), Math.Abs(i2));
        if (m <= 0) return 1; // どちらか0や異常時のフォールバック
        return m;
    }

    public static bool PrimeCheck(int n)
    {
        if (n <= 1) return false;
        if (n <= 3) return true;
        if (n % 2 == 0 || n % 3 == 0) return false;
        for (int i = 5; i * i <= n; i += 6)
        {
            if (n % i == 0 || n % (i + 2) == 0)
                return false;
        }
        return true;
    }
}

// ---- 有理数表現 ----
public readonly struct Rational
{
    // 分子・分母は互いに素、分母は正
    public int Num { get; }
    // 分母は0禁止（0なら1にする）
    public int Den { get; }

    public override string ToString() => $"{Num}/{Den}";

    public double Value => (double)Num / Den;

    public Rational(int num, int den)
    {
        if (den == 0) { Den = 1; Num = num; return; }
        if (den < 0) { num = -num; den = -den; }
        int g = MathExtend.GCD(Math.Abs(num), den);
        if (g == 0) g = 1;
        Num = num / g;
        Den = den / g;
    }

    // 連分数で double → 分数近似
    public static Rational FromDouble(double x, int maxDen = 1_000_000, double eps = 1e-12)
    {
        if (double.IsNaN(x) || double.IsInfinity(x))
            return new Rational(0, 1);
        int sign = x < 0 ? -1 : 1;
        x = Math.Abs(x);

        int n0 = 0, d0 = 1;
        int n1 = 1, d1 = 0;

        double a = Math.Floor(x);
        double frac = x;
        while (true)
        {
            a = Math.Floor(frac);
            try
            {
                // オーバーフローするかも
                int n2 = checked((int)a * n1 + n0);
                int d2 = checked((int)a * d1 + d0);

                if (d2 > maxDen)
                {
                    // 前のほうが安全
                    return new Rational(sign * n1, d1);
                }

                double approx = (double)n2 / d2;
                if (Math.Abs(approx - x) <= Math.Max(eps, eps * x))
                    return new Rational(sign * n2, d2);

                // 次へ
                n0 = n1; d0 = d1;
                n1 = n2; d1 = d2;

                double diff = frac - a;
                if (diff < eps) // ほぼ整数
                    return new Rational(sign * n1, d1);
                frac = 1.0 / diff;
            }
            catch (OverflowException)
            {
                // オーバーフローしたら前のを返す
                return new Rational(sign * n1, d1);
            }
        }
    }
}

public class Easing
{
    public static double Ease(Counter counter, double min = 0, double max = 1,
        EEasing type = 0, EInOut inout = 0, double backrate = 4) => counter == null ? max : Ease(counter, (int)counter.End, min, max, type, inout, backrate);
    public static double Ease(Counter counter, int end, double min = 0, double max = 1,
        EEasing type = 0, EInOut inout = 0, double backrate = 4) => counter == null ? max : Ease(counter, 0, end, min, max, type, inout, backrate);
    public static double Ease(Counter counter, int start, int end, double min = 0, double max = 1,
        EEasing type = 0, EInOut inout = 0, double backrate = 4) => counter == null
            ? max
            : counter.Value < start
            ? min
            : counter.Value > end ? max : Ease(counter.Value - start, end - start, min, max, type, inout, backrate);
    public static double Ease(double t, EEasing type = 0, EInOut inout = 0, double min = 0, double max = 1, double backrate = 4)
        => Ease(t, 1, min, max, type, inout, backrate);
    public static double Ease(double t, double totaltime, double min = 0, double max = 1,
        EEasing type = 0, EInOut inout = 0, double backrate = 4)
    {
        if (inout == EInOut.OutIn)
        {
            return t < totaltime / 2
                ? Ease(t * 2, totaltime, min, (min + max) / 2, type, EInOut.Out, backrate)
                : Ease((t - totaltime / 2) * 2, totaltime, (min + max) / 2, max, type, EInOut.In, backrate);
        }
        if (min > max) return min - Ease(t, totaltime, 0, min - max, type, inout, backrate);
        int io = (int)inout;
        return type switch
        {
            EEasing.Sine => Sine(t, totaltime, min, max, io),
            EEasing.Quad => Quad(t, totaltime, min, max, io),
            EEasing.Cubic => Cubic(t, totaltime, min, max, io),
            EEasing.Quart => Quart(t, totaltime, min, max, io),
            EEasing.Quint => Quint(t, totaltime, min, max, io),
            EEasing.Exp => Exp(t, totaltime, min, max, io),
            EEasing.Circ => Circ(t, totaltime, min, max, io),
            EEasing.Back => Back(t, totaltime, min, max, io, backrate),
            EEasing.Elastic => Elastic(t, totaltime, min, max, io),
            EEasing.Bounce => Bounce(t, totaltime, min, max, io),
            _ => Linear(t, totaltime, min, max),
        };
    }

    // type 0:In 1:Out 2:InOut
    public static double Quad(double t, double totaltime, double min, double max, int type)
    {
        max -= min;
        if (max == 0) return min;
        switch (type)
        {
            case 0:
                t /= totaltime;
                return max * t * t + min;
            case 1:
                t /= totaltime;
                return -max * t * (t - 2) + min;
            case 2:
            default:
                t /= totaltime / 2;
                if (t < 1) return max / 2 * t * t + min;

                t--;
                return -max / 2 * (t * (t - 2) - 1) + min;
        }
    }

    public static double Cubic(double t, double totaltime, double min, double max, int type)
    {
        max -= min;
        if (max == 0) return min;
        switch (type)
        {
            case 0:
                t /= totaltime;
                return max * t * t * t + min;
            case 1:
                t = t / totaltime - 1;
                return max * (t * t * t + 1) + min;
            case 2:
            default:
                t /= totaltime / 2;
                if (t < 1) return max / 2 * t * t * t + min;

                t -= 2;
                return max / 2 * (t * t * t + 2) + min;
        }
    }

    public static double Quart(double t, double totaltime, double min, double max, int type)
    {
        max -= min;
        if (max == 0) return min;
        switch (type)
        {
            case 0:
                t /= totaltime;
                return max * t * t * t * t + min;
            case 1:
                t = t / totaltime - 1;
                return -max * (t * t * t * t - 1) + min;
            case 2:
            default:
                t /= totaltime / 2;
                if (t < 1) return max / 2 * t * t * t * t + min;

                t -= 2;
                return -max / 2 * (t * t * t * t - 2) + min;
        }
    }

    public static double Quint(double t, double totaltime, double min, double max, int type)
    {
        max -= min;
        if (max == 0) return min;
        switch (type)
        {
            case 0:
                t /= totaltime;
                return max * t * t * t * t * t + min;
            case 1:
                t = t / totaltime - 1;
                return max * (t * t * t * t * t + 1) + min;
            case 2:
            default:
                t /= totaltime / 2;
                if (t < 1) return max / 2 * t * t * t * t * t + min;

                t -= 2;
                return max / 2 * (t * t * t * t * t + 2) + min;
        }
    }

    public static double Sine(double t, double totaltime, double min, double max, int type)
    {
        max -= min;
        return max == 0
            ? min
            : type switch
            {
                0 => -max * Math.Cos(t * (Math.PI * 90 / 180) / totaltime) + max + min,
                1 => max * Math.Sin(t * (Math.PI * 90 / 180) / totaltime) + min,
                _ => -max / 2 * (Math.Cos(t * Math.PI / totaltime) - 1) + min,
            };
    }

    public static double Exp(double t, double totaltime, double min, double max, int type)
    {
        switch (type)
        {
            case 0:
                max -= min;
                if (max == 0) return min;
                return t == 0.0 ? min : max * Math.Pow(2, 10 * (t / totaltime - 1)) + min;
            case 1:
                max -= min;
                if (max == 0) return min;
                return t == totaltime ? max + min : max * (-Math.Pow(2, -10 * t / totaltime) + 1) + min;
            case 2:
            default:
                if (t == 0.0f) return min;
                if (t == totaltime) return max;
                max -= min;
                if (max == 0) return min;
                t /= totaltime / 2;

                if (t < 1) return max / 2 * Math.Pow(2, 10 * (t - 1)) + min;

                t--;
                return max / 2 * (-Math.Pow(2, -10 * t) + 2) + min;
        }
    }

    public static double Circ(double t, double totaltime, double min, double max, int type)
    {
        max -= min;
        if (max == 0) return min;
        switch (type)
        {
            case 0:
                t /= totaltime;
                return -max * (Math.Sqrt(1 - t * t) - 1) + min;
            case 1:
                t = t / totaltime - 1;
                return max * Math.Sqrt(1 - t * t) + min;
            case 2:
            default:
                t /= totaltime / 2;
                if (t < 1) return -max / 2 * (Math.Sqrt(1 - t * t) - 1) + min;

                t -= 2;
                return max / 2 * (Math.Sqrt(1 - t * t) + 1) + min;
        }
    }

    public static double Elastic(double t, double totaltime, double min, double max, int type)
    {
        max -= min;
        if (max == 0) return min;
        double s = 1.70158f;
        double p = totaltime * 0.3f;
        double a = max;
        switch (type)
        {
            case 0:
                t /= totaltime;
                if (t == 0) return min;
                if (t == 1) return min + max;

                if (a < Math.Abs(max))
                {
                    a = max;
                    s = p / 4;
                }
                else
                {
                    s = p / (2 * Math.PI) * Math.Asin(max / a);
                }

                t--;
                return -(a * Math.Pow(2, 10 * t) * Math.Sin((t * totaltime - s) * (2 * Math.PI) / p)) + min;
            case 1:
                t /= totaltime;
                if (t == 0) return min;
                if (t == 1) return min + max;

                if (a < Math.Abs(max))
                {
                    a = max;
                    s = p / 4;
                }
                else
                {
                    s = p / (2 * Math.PI) * Math.Asin(max / a);
                }

                double n = a * Math.Pow(2, -10 * t) * Math.Sin((t * totaltime - s) * (2 * Math.PI) / p) + max + min;
                return !double.IsNaN(n) ? n : 0;
            case 2:
            default:
                t /= totaltime / 2;
                p *= 1.5f;

                if (t == 0) return min;
                if (t == 2) return min + max;

                if (a < Math.Abs(max))
                {
                    a = max;
                    s = p / 4;
                }
                else
                {
                    s = p / (2 * Math.PI) * Math.Asin(max / a);
                }

                if (t < 1)
                {
                    return -0.5f * (a * Math.Pow(2, 10 * (t -= 1)) * Math.Sin((t * totaltime - s) * (2 * Math.PI) / p)) + min;
                }

                t--;
                return a * Math.Pow(2, -10 * t) * Math.Sin((t * totaltime - s) * (2 * Math.PI) / p) * 0.5f + max + min;
        }
    }

    public static double Back(double t, double totaltime, double min, double max, int type, double s = 1.0)
    {
        max -= min;
        if (max == 0) return min;
        switch (type)
        {
            case 0:
                t /= totaltime;
                double m = max * t * t * ((s + 1) * t - s) + min;
                return max * t * t * ((s + 1) * t - s) + min;
            case 1:
                t = t / totaltime - 1;
                return max * (t * t * ((s + 1) * t + s) + 1) + min;
            case 2:
            default:
                s *= 1.525f;
                t /= totaltime / 2;
                if (t < 1) return max / 2 * (t * t * ((s + 1) * t - s)) + min;

                t -= 2;
                return max / 2 * (t * t * ((s + 1) * t + s) + 2) + min;
        }
    }

    public static double Bounce(double t, double totaltime, double min, double max, int type) => type switch
    {
        0 => BounceIn(t, totaltime, min, max),
        1 => BounceOut(t, totaltime, min, max),
        _ => t < totaltime / 2
                                ? BounceIn(t * 2, totaltime, 0, max - min) * 0.5f + min
                                : BounceOut(t * 2 - totaltime, totaltime, 0, max - min) * 0.5f + min + (max - min) * 0.5f,
    };
    public static double BounceIn(double t, double totaltime, double min, double max)
    {
        max -= min;
        return max == 0 ? min : max - BounceOut(totaltime - t, totaltime, 0, max) + min;
    }

    public static double BounceOut(double t, double totaltime, double min, double max)
    {
        max -= min;
        if (max == 0) return min;
        t /= totaltime;

        if (t < 1.0f / 2.75f)
        {
            return max * (7.5625f * t * t) + min;
        }
        else if (t < 2.0f / 2.75f)
        {
            t -= 1.5f / 2.75f;
            return max * (7.5625f * t * t + 0.75f) + min;
        }
        else if (t < 2.5f / 2.75f)
        {
            t -= 2.25f / 2.75f;
            return max * (7.5625f * t * t + 0.9375f) + min;
        }
        else
        {
            t -= 2.625f / 2.75f;
            return max * (7.5625f * t * t + 0.984375f) + min;
        }
    }

    public static double Linear(double t, double totaltime, double min, double max) => (max - min) * t / totaltime + min;
}

public enum EEasing
{
    Linear,
    Sine,
    Quad,
    Cubic,
    Quart,
    Quint,
    Exp,
    Circ,
    Back,
    Elastic,
    Bounce,
}

public enum EInOut
{
    In,
    Out,
    InOut,
    OutIn
}