using AstrumLoom;

namespace Sandbox;

/// <summary>
/// テーマ「万華鏡工房」。
///
/// Drawing の図形プリミティブを 6 枚のカードに分けて見せる。派手な合成だけを見せると
/// 「なんとなく綺麗」で終わってしまうので、各カードは 1〜2 個の API に絞って
/// パラメータと絵を対応づける。演出はすべて DemoUi.Delta（経過秒）で進める
/// （[[astrumloom-library]] にある通り、フレーム数で進めると実機の無制限 FPS で暴走する）。
/// </summary>
internal sealed class ShapesDemoScene : Scene
{
    private double _time;
    private bool _spin = true;
    private int _thicknessIndex = 1;
    private static readonly int[] Thicknesses = [0, 3, 6, 12];

    public override void Enable()
    {
        base.Enable();
        _time = 0;
        _spin = true;
        _thicknessIndex = 1;
    }

    public override void Update()
    {
        double dt = DemoUi.Delta;
        if (_spin) _time += dt;

        if (Key.Space.Push()) _spin = !_spin;
        if (Key.T.Push()) _thicknessIndex = (_thicknessIndex + 1) % Thicknesses.Length;
    }

    public override void Draw()
    {
        DrawBackdrop();

        Drawing.Text(20, 16, "万華鏡工房 / 図形の見本帳", Color.White, edgecolor: new Color(10, 8, 20));
        DemoUi.Note(20, 52, 820,
            "[Space] 全体の回転を止める/動かす  [T] 円と楕円の枠の太さを切り替える",
            new Color(196, 186, 226));

        Card(0, 0, "1) Line / Cross", DrawLineCard);
        Card(1, 0, "2) Circle / Oval", DrawCircleCard);
        Card(2, 0, "3) Triangle / Polygon", DrawTriangleCard);
        Card(0, 1, "4) Box / Alpha", DrawBoxCard);
        Card(1, 1, "5) Gradation", DrawGradationCard);
        Card(2, 1, "6) 合成", DrawKaleidoscopeCard);
    }

    #region 背景とカードの枠

    private void DrawBackdrop()
    {
        var bg = new Gradation(
        [
            (0.00f, Color.FromHSB(260, 0.24, 0.10)),
            (0.45f, Color.FromHSB(290, 0.34, 0.15)),
            (1.00f, Color.FromHSB(320, 0.42, 0.18)),
        ]);
        Drawing.Gradation(0, 0, AstrumCore.Width, AstrumCore.Height, bg, colorSpace: Gradation.ColorSpace.OKLCH);
    }

    private const int CardW = 404;
    private const int CardH = 292;
    private const int CardTop = 84;
    private const int Pad = 10;
    private const double TextW = CardW - Pad * 2;

    private static void Card(int col, int row, string title, Action<double, double> body)
    {
        double x = 16 + col * (CardW + 12);
        double y = CardTop + row * (CardH + 10);
        DemoUi.Card(x, y, CardW, CardH, title, (bx, by, _) => body(bx, by));
    }

    #endregion

    #region 1) Line / Cross

    private void DrawLineCard(double x, double y)
    {
        double cx = x + CardW / 2.0, cy = y + 96;
        for (int i = 0; i < 12; i++)
        {
            double ang = i / 12.0 * Math.PI * 2 + _time * 0.5;
            double len = 60 + 26 * Math.Sin(_time * 1.3 + i);
            var col = ColorEx.From(new Rainbow(i * 30 + (float)(_time * 40)));
            Drawing.Line(cx, cy, Math.Cos(ang) * len, Math.Sin(ang) * len, col, thickness: 2);
        }
        Drawing.Cross(cx, cy, 14, new Color(255, 255, 255, 220), thickness: 3);

        double ny = y + 168;
        ny = DemoUi.Notes(x + Pad, ny, TextW, new Color(196, 212, 240),
            "Line(x, y, dx, dy) は始点と「差分」で引く。終点ではない。");
        DemoUi.Notes(x + Pad, ny, TextW, new Color(152, 170, 202),
            "Cross は十字。基準合わせの目印としてよく使う。");
    }

    #endregion

    #region 2) Circle / Oval

    private void DrawCircleCard(double x, double y)
    {
        double cx = x + CardW / 2.0, cy = y + 96;
        int thickness = Thicknesses[_thicknessIndex];

        for (int i = 0; i < 4; i++)
        {
            double r = 28 + i * 20 + 4 * Math.Sin(_time * 2 + i);
            var col = Color.FromHSB((_time * 40 + i * 30) % 360, 0.7, 1.0);
            if (thickness == 0)
                Drawing.Circle(cx, cy, r, col.WithAlpha(160));
            else
                Drawing.Circle(cx, cy, r, col, thickness: thickness);
        }
        Drawing.Oval(cx, cy + 74, 70, 24, new Color(255, 255, 255, 40));
        Drawing.Oval(cx, cy + 74, 70, 24, new Color(255, 255, 255, 200),
            thickness: thickness == 0 ? 0 : thickness);

        double ny = y + 210;
        ny = DemoUi.Notes(x + Pad, ny, TextW, new Color(196, 212, 240),
            $"Thickness = {thickness}（[T] で切替）");
        DemoUi.Notes(x + Pad, ny, TextW, new Color(212, 172, 112),
            "注: RayLib は枠線 Circle/Oval の太さを無視し、常に細線で描く。"
            + "DxLib では効く。--backend で見比べること。");
    }

    #endregion

    #region 3) Triangle / Polygon

    private void DrawTriangleCard(double x, double y)
    {
        double cx = x + CardW / 2.0, cy = y + 96;
        const int petals = 6;
        for (int i = 0; i < petals; i++)
        {
            double a0 = i / (double)petals * Math.PI * 2 + _time * 0.4;
            double a1 = (i + 1) / (double)petals * Math.PI * 2 + _time * 0.4;
            double r = 78;
            double x1 = cx, y1 = cy;
            double x2 = cx + Math.Cos(a0) * r, y2 = cy + Math.Sin(a0) * r;
            double x3 = cx + Math.Cos(a1) * r, y3 = cy + Math.Sin(a1) * r;
            var col = ColorEx.From(new Rainbow(i * 60 + (float)(_time * 50)));
            bool fill = i % 2 == 0;
            Drawing.Triangle(x1, y1, x2, y2, x3, y3, fill ? col.WithAlpha(150) : col, thickness: fill ? 0 : 2);
        }

        // 星形の輪郭を Polygon で。
        var points = new (double x, double y)[10];
        for (int i = 0; i < 10; i++)
        {
            double r = i % 2 == 0 ? 30 : 14;
            double a = i / 10.0 * Math.PI * 2 - _time * 0.6;
            points[i] = (cx + Math.Cos(a) * r, cy + Math.Sin(a) * r + 4);
        }
        Drawing.Polygon(points, new Color(255, 255, 255, 230), thickness: 2);

        double ny = y + 190;
        ny = DemoUi.Notes(x + Pad, ny, TextW, new Color(196, 212, 240),
            "花びらは Triangle（3 点指定）、星の輪郭は Polygon（点列）。");
        DemoUi.Notes(x + Pad, ny, TextW, new Color(152, 170, 202),
            "thickness:0 は塗りつぶし、それ以外は枠線だけになる。");
    }

    #endregion

    #region 4) Box / Alpha

    private void DrawBoxCard(double x, double y)
    {
        double gx = x + Pad, gy = y + 6, cell = 54;

        // 市松模様の下地。左半分は黒、右半分は白にして、同じ半透明色でも
        // 「下地が違うとどう見えるか」を並べて比べられるようにする。
        for (int j = 0; j < 3; j++)
        {
            for (int i = 0; i < 6; i++)
            {
                bool dark = i < 3;
                Drawing.Box(gx + i * cell, gy + j * cell, cell, cell, dark ? new Color(18, 18, 24) : new Color(230, 230, 236));
            }
        }

        for (int j = 0; j < 3; j++)
        {
            for (int i = 0; i < 6; i++)
            {
                double hue = (_time * 30 + (i * 3 + j) * 22) % 360;
                var col = Color.FromHSB(hue, 0.8, 1.0);
                int alpha = 60 + j * 70;
                double cx = gx + i * cell + cell / 2.0, cy = gy + j * cell + cell / 2.0;
                Drawing.Box(cx - 18, cy - 18, 36, 36, col.WithAlpha(alpha));
            }
        }

        double ny = gy + 3 * cell + 14;
        ny = DemoUi.Notes(x + Pad, ny, TextW, new Color(196, 212, 240),
            "同じ WithAlpha(60/130/200) を、黒地（左）と白地（右）に重ねている。");
        DemoUi.Notes(x + Pad, ny, TextW, new Color(152, 170, 202),
            "半透明の色は下地によって見え方が変わる、という当たり前だが忘れやすい確認。");
    }

    #endregion

    #region 5) Gradation

    private static readonly (string Label, Gradation.ColorSpace Space)[] Spaces =
    [
        ("RGB", Gradation.ColorSpace.RGB),
        ("OKLCH", Gradation.ColorSpace.OKLCH),
        ("OKLab", Gradation.ColorSpace.OKLab),
    ];

    private void DrawGradationCard(double x, double y)
    {
        // どの色空間も同じ 2 色（鮮やかな黄 → 鮮やかな青紫）を渡す。
        // RGB 補間は中間で彩度が抜けて灰色っぽくなり、OKLCH/OKLab は鮮やかなまま繋がる。
        var grad = new Gradation([(0.0f, Color.FromHSB(52, 0.85, 1.0)), (1.0f, Color.FromHSB(268, 0.75, 0.95))]);

        double gx = x + Pad, gw = TextW, gh = 56;
        double gy = y + 8;
        for (int i = 0; i < Spaces.Length; i++)
        {
            Drawing.Gradation((int)gx, (int)gy, (int)gw, (int)gh, grad, colorSpace: Spaces[i].Space);
            Drawing.Box(gx, gy, gw, gh, new Color(255, 255, 255, 60), thickness: 1);
            DemoUi.NoteFont.Draw(gx + gw / 2, gy + gh / 2 - 8, Spaces[i].Label,
                Color.Black, ReferencePoint.TopCenter);
            gy += gh + 8;
        }

        double ny = gy + 6;
        ny = DemoUi.Notes(x + Pad, ny, TextW, new Color(196, 212, 240),
            "同じ黄から青紫への 2 色を、3 通りの補間で繋いだもの。");
        DemoUi.Notes(x + Pad, ny, TextW, new Color(152, 170, 202),
            "RGB は中間で彩度が落ちて灰色がかる。OKLCH/OKLab は鮮やかさを保ったまま繋がる。");
    }

    #endregion

    #region 6) 合成

    private void DrawKaleidoscopeCard(double x, double y)
    {
        double cx = x + CardW / 2.0, cy = y + 130;
        const int seg = 72;
        for (int layer = 0; layer < 3; layer++)
        {
            double a = 70 + layer * 30;
            double b = 40 + layer * 22;
            double rot = _time * (18 + layer * 6);
            var ring = new Gradation(
            [
                (0.00f, new Color(255, 255, 255, 10)),
                (0.50f, Color.FromHSB((_time * 30 + layer * 90) % 360, 0.8, 1.0, 0.55f)),
                (1.00f, new Color(255, 255, 255, 10)),
            ]);
            for (int s = 0; s < seg; s++)
            {
                double th1 = s * (Math.PI * 2 / seg);
                double th2 = (s + 1) * (Math.PI * 2 / seg);
                double x1 = cx + Math.Cos(th1 + rot * Math.PI / 180) * a;
                double y1 = cy + Math.Sin(th1 + rot * Math.PI / 180) * b;
                double x2 = cx + Math.Cos(th2 + rot * Math.PI / 180) * a;
                double y2 = cy + Math.Sin(th2 + rot * Math.PI / 180) * b;
                var c = ring.GetColor((float)s / (seg - 1), Gradation.ColorSpace.OKLab);
                Drawing.LineZ(x1, y1, x2, y2, c, thickness: 2);
            }
        }
        Drawing.Cross(cx, cy, 8, new Color(255, 255, 255, 200), thickness: 2);

        DemoUi.Notes(x + Pad, y + 220, TextW, new Color(152, 170, 202),
            "ここまでのカードで使った API（Line / Gradation / 色空間）だけで組んだ飾り。");
    }

    #endregion

    // --- セルフテストから中を覗くための入口 ---------------------------------

    public bool Spinning => _spin;
    public int ThicknessIndex => _thicknessIndex;
}
