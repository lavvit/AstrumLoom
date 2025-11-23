namespace AstrumLoom;

public interface IMouse
{
    /// <summary>マウスのX座標を取得します。</summary>
    double X { get; set; }
    /// <summary>マウスのY座標を取得します。</summary>
    double Y { get; set; }

    /// <summary>マウスホイールの回転量を取得します。</summary>
    double Wheel { get; }
    /// <summary>マウスホイールの総回転量を取得します。</summary>
    double WheelTotal { get; }

    void Init(bool visible);
    void Update();

    /// <summary>指定したボタンが押されたかどうかを取得します。</summary>
    bool Push(MouseButton button);
    /// <summary>指定したボタンを押しているかどうかを取得します。</summary>
    bool Hold(MouseButton button);
    /// <summary>指定したボタンが離されたかどうかを取得します。</summary>
    bool Left(MouseButton button);
}
public enum MouseState { None, Pressed, Held, Released }
public enum MouseButton { Left, Right, Middle }

public class Mouse
{
    public static IMouse MouseInstance { get; set; } = null!;

    public static void Init(IMouse mouse, bool visible)
    {
        MouseInstance = mouse;
        MouseInstance.Init(visible);
    }

    public static void Update()
    {
        MouseInstance.Update();
        if (AstrumCore.Active)
        {
            // 前回座標初期化
            double x = X;
            double y = Y;
            if (double.IsNaN(_prevX))
            {
                _prevX = x;
                _prevY = y;
            }
            _xdiff = x - _prevX;
            _ydiff = y - _prevY;

            // 次フレーム用記録
            _prevX = x;
            _prevY = y;

            if (Speed > 0)
            {
                Sleep.WakeUp();
            }
        }
    }
    public static double X => MouseInstance.X;
    public static double Y => MouseInstance.Y;
    public static double Wheel => MouseInstance.Wheel;
    public static double WheelTotal => MouseInstance.WheelTotal;

    public static bool IsTouchPad
    {
        get
        {
            // WheelTotal が小数を含むかどうかを判定する
            // NaN/Infinity は false とみなす
            double wt = WheelTotal;
            if (double.IsNaN(wt) || double.IsInfinity(wt))
                return false;
            // 整数部分との差の絶対値（小数部分）
            double frac = Math.Abs(wt - Math.Truncate(wt));
            const double eps = 1e-9;
            return frac > eps;
        }
    }
    private static double _xdiff, _ydiff;
    public static double Speed
    {
        get
        {
            // 移動量・スピード（将来拡張用）
            double dx = _xdiff;
            double dy = _ydiff;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    public static bool Push(MouseButton button) => MouseInstance.Push(button);
    public static bool Hold(MouseButton button) => MouseInstance.Hold(button);
    public static bool Left(MouseButton button) => MouseInstance.Left(button);

    // 前フレーム座標保持（動きに応じた角度計算用）
    private static double _prevX = double.NaN;
    private static double _prevY = double.NaN;

    /// <summary>
    /// イカしたマウス描画 + 棒（拡張カーソル）。
    /// 三角形カーソル + 影 + アクセント + 下方向へ伸びる棒。
    /// </summary>
    /// <param name="size">基本サイズ</param>
    /// <param name="color">メインカラー（nullなら白）</param>
    /// <param name="accent">アクセントカラー（nullならグレー）</param>
    public static void Draw(int size = 18, Color? color = null, Color? accent = null)
    {
        double x = X;
        double y = Y;

        // ホイールで微パルス + 押下で縮小
        double pulse = 1.0 + 0.05 * (Wheel != 0 ? Math.Sin(WheelTotal * 0.4) : 0)
            + 0.01 * Speed; // スピード依存も少し追加
        double s = size * pulse;
        if (Hold(MouseButton.Left) || Hold(MouseButton.Right) || Hold(MouseButton.Middle))
            s *= 0.9;

        // ローカル座標定義（基本カーソル）
        var tip = (X: 0.0, Y: 0.0);
        var baseL = (X: 0, Y: s);
        var baseR = (X: 0.77 * s, Y: 0.64 * s);
        var innerL = (X: 0, Y: s);
        var innerR = (X: 0.31 * s, Y: 0.64 * s);

        // 追加: 棒ローカル座標（細い矩形を2三角で）
        double stickW = 0.34 * s;
        var stickA = (X: 0.22 * s, Y: 0.89 * s);
        var stickB = (X: 0.54 * s, Y: 0.74 * s);
        var stickC = (X: 0.35 * s, Y: 1.20 * s);
        var stickD = (X: 0.69 * s, Y: 1.05 * s);

        (double X, double Y) P((double X, double Y) p)
            => (p.X + x, p.Y + y);

        // 押下サークル
        var pushColor = PushColor();
        if (pushColor.R > 0 || pushColor.G > 0 || pushColor.B > 0)
        {
            double circleSize = s * 0.2;
            Drawing.Circle(x, y, circleSize, pushColor, (int)Math.Max(1, size / 20.0));
        }

        // 回転後座標
        var rtTip = P(tip);
        var rtBaseL = P(baseL);
        var rtBaseR = P(baseR);
        var rtInnerL = P(innerL);
        var rtInnerR = P(innerR);
        var rtStickA = P(stickA);
        var rtStickB = P(stickB);
        var rtStickC = P(stickC);
        var rtStickD = P(stickD);

        // 影描画（三角カーソル影）
        double shadowOffset = size / 20.0;
        (double X, double Y) shTip = (rtTip.X + shadowOffset, rtTip.Y + shadowOffset);
        (double X, double Y) shBaseL = (rtBaseL.X + shadowOffset, rtBaseL.Y + shadowOffset);
        (double X, double Y) shBaseR = (rtBaseR.X + shadowOffset, rtBaseR.Y + shadowOffset);
        Drawing.Triangle(shTip.X, shTip.Y, shBaseL.X, shBaseL.Y, shBaseR.X, shBaseR.Y, new Color(0, 0, 0, 80));

        // 影: 棒
        (double X, double Y) shStickA = (rtStickA.X + shadowOffset, rtStickA.Y + shadowOffset);
        (double X, double Y) shStickB = (rtStickB.X + shadowOffset, rtStickB.Y + shadowOffset);
        (double X, double Y) shStickC = (rtStickC.X + shadowOffset, rtStickC.Y + shadowOffset);
        (double X, double Y) shStickD = (rtStickD.X + shadowOffset, rtStickD.Y + shadowOffset);
        Drawing.Triangle(shStickA.X, shStickA.Y, shStickB.X, shStickB.Y, shStickD.X, shStickD.Y, new Color(0, 0, 0, 70));
        Drawing.Triangle(shStickA.X, shStickA.Y, shStickD.X, shStickD.Y, shStickC.X, shStickC.Y, new Color(0, 0, 0, 70));

        var mainColor = color ?? Color.White;
        var accColor = accent ?? Color.Gray;
        // 棒本体カラー（やや淡いメイン寄り）
        var stickColor = ColorEx.LerpOKLab(mainColor, accColor, 0.4f);
        Drawing.Triangle(rtStickA.X, rtStickA.Y, rtStickB.X, rtStickB.Y, rtStickD.X, rtStickD.Y, stickColor);
        Drawing.Triangle(rtStickA.X, rtStickA.Y, rtStickD.X, rtStickD.Y, rtStickC.X, rtStickC.Y, stickColor);

        // 外形三角
        Drawing.Triangle(rtTip.X, rtTip.Y, rtBaseL.X, rtBaseL.Y, rtBaseR.X, rtBaseR.Y, mainColor);

        // アクセント
        var accColor2 = ColorEx.LerpOKLab(accColor, mainColor, 0.5f);
        Drawing.Triangle(rtInnerL.X, rtInnerL.Y, rtBaseR.X, rtBaseR.Y, rtInnerR.X, rtInnerR.Y, accColor);
        Drawing.Triangle(rtTip.X, rtTip.Y, rtInnerL.X, rtInnerL.Y, rtInnerR.X, rtInnerR.Y, accColor2);

        // 微アウトライン（三角）
        double outlineOffset = 0.8;
        (double X, double Y) olTip = (rtTip.X - outlineOffset, rtTip.Y - outlineOffset);
        (double X, double Y) olBaseL = (rtBaseL.X - outlineOffset, rtBaseL.Y - outlineOffset);
        (double X, double Y) olBaseR = (rtBaseR.X - outlineOffset, rtBaseR.Y - outlineOffset);
        Drawing.Triangle(olTip.X, olTip.Y, olBaseL.X, olBaseL.Y, olBaseR.X, olBaseR.Y, new Color(255, 255, 255, 40));

        // アウトライン（棒：軽い）
        (double X, double Y) olStickA = (rtStickA.X - outlineOffset * 0.5, rtStickA.Y - outlineOffset * 0.5);
        (double X, double Y) olStickB = (rtStickB.X - outlineOffset * 0.5, rtStickB.Y - outlineOffset * 0.5);
        (double X, double Y) olStickC = (rtStickC.X - outlineOffset * 0.5, rtStickC.Y - outlineOffset * 0.5);
        (double X, double Y) olStickD = (rtStickD.X - outlineOffset * 0.5, rtStickD.Y - outlineOffset * 0.5);
        Drawing.Triangle(olStickA.X, olStickA.Y, olStickB.X, olStickB.Y, olStickD.X, olStickD.Y, new Color(255, 255, 255, 25));
        Drawing.Triangle(olStickA.X, olStickA.Y, olStickD.X, olStickD.Y, olStickC.X, olStickC.Y, new Color(255, 255, 255, 25));

        // ホイール回転量表示（カーソル右）
        double hwX = x + s * 0.9;
        double hwY = y + s * 0.56;
        double hwW = s * 0.3;
        double hwH = s * 0.2;
        int[] hwlimit = [1, 2, 4, 8, 16];
        for (int i = 0; i < hwlimit.Length; i++)
        {
            if (Math.Abs(Wheel) >= hwlimit[i])
            {
                int vector = Wheel > 0 ? -1 : 1;
                var hwC = vector < 0 ? Color.Cyan : Color.HotPink;

                double hy = hwY + hwH * vector * i + s * 0.1;

                (double X, double Y) hwTip = (hwX, hy - hwH * -vector);
                (double X, double Y) hwBaseL = (hwX - hwW / 2, hy);
                (double X, double Y) hwBaseR = (hwX + hwW / 2, hy);
                Drawing.Triangle(hwTip.X, hwTip.Y, hwBaseL.X, hwBaseL.Y, hwBaseR.X, hwBaseR.Y, hwC);
            }
        }
        // 高速移動時のエフェクト（将来拡張用）
        if (Speed > 40.0)
        {
            int st = Speed > 200.0 ? 5 : Speed > 120 ? 3 : 1;
            Drawing.LineZ(0, y, AstrumCore.Width, y, new Color(Speed > 120 ? 255 : 0, 255, 0, 60), st);
            Drawing.LineZ(x, 0, x, AstrumCore.Height, new Color(Speed > 120 ? 255 : 0, 255, 0, 60), st);
            if (Speed > 200.0)
            {
                Drawing.LineZ(0, y, AstrumCore.Width, y, Color.White);
                Drawing.LineZ(x, 0, x, AstrumCore.Height, Color.White);
            }
        }
    }

    private static double LerpAngle(double a, double b, double t)
    {
        // 差を -PI..PI に正規化
        double diff = (b - a + Math.PI) % (2 * Math.PI) - Math.PI;
        return a + diff * t;
    }

    private static Color PushColor()
    {
        int r = 0, g = 0, b = 0;
        if (Hold(MouseButton.Left))
            r += 255;
        if (Hold(MouseButton.Right))
            g += 255;
        if (Hold(MouseButton.Middle))
            b += 255;
        return new Color(r, g, b);
    }
}