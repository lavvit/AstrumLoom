using System.Diagnostics;

using AstrumLoom;

namespace Sandbox;

// 図形描画テスト用のオシャレなシーン
internal sealed class FancyShapesScene : Scene
{
    private readonly int _width;
    private readonly int _height;
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private readonly Random _rand = new(12345);

    public FancyShapesScene(int width, int height)
    {
        _width = width;
        _height = height;
    }

    public override void Draw()
    {
        // 背景に回転グラデーション
        double t = _sw.Elapsed.TotalSeconds;
        var bg = new Gradation(new[]
        {
            (0.00f, Color.FromHSB(220, 0.20, 0.12)),
            (0.35f, Color.FromHSB(250, 0.35, 0.18)),
            (0.65f, Color.FromHSB(290, 0.40, 0.20)),
            (1.00f, Color.FromHSB(330, 0.50, 0.22)),
        });
        Drawing.Gradation(0, 0, _width, _height, bg, colorSpace: Gradation.ColorSpace.OKLCH);
        // rotate: (t * 12) % 360,

        // タイトル
        Drawing.Text(_width / 2.0, 32, "AstrumLoom Shapes", Color.White, ReferencePoint.TopCenter, edgecolor: Color.Black);
        Drawing.Text(_width / 2.0, 68, "- simple, clean and animated -", new Color(220, 225, 235), ReferencePoint.TopCenter);
        Drawing.DefaultFont.DrawEdge(_width / 2.0, 92, "日本語のテキスト", new Color(180, 185, 195), ReferencePoint.TopCenter);
        Drawing.Line(_width / 2.0 - 100, 120, 200, 0, new Color(255, 255, 255, 100), thickness: 1);

        // 左: ラインとクロス
        double leftX = 120;
        for (int i = 0; i < 8; i++)
        {
            double ang = i / 8.0 * Math.PI * 2 + t * 0.6;
            double len = 70 + 40 * Math.Sin(t * 1.7 + i);
            var col = ColorEx.From(new Rainbow(i * 45));
            Drawing.Line(leftX, 160, Math.Cos(ang) * len, Math.Sin(ang) * len, col, thickness: 2);
        }
        Drawing.Cross(leftX, 160, 14, new Color(255, 255, 255, 220), thickness: 3);

        // 中央: サークル/オーバルのリング
        double cx = _width / 2.0, cy = _height / 2.0 + 20;
        for (int i = 0; i < 5; i++)
        {
            double r = 60 + i * 22 + 6 * Math.Sin(t * 2 + i);
            var col = Color.Lerp(new Color(255, 255, 255, 16), new Color(255, 255, 255, 140), (float)(0.2 + i * 0.15));
            Drawing.Circle(cx, cy, r, col, thickness: 2);
        }
        // 少し歪んだ楕円のレイヤ
        for (int i = 0; i < 3; i++)
        {
            double a = 120 + i * 36 + 8 * Math.Sin(t * 1.3 + i);
            double b = 70 + i * 28 + 5 * Math.Sin(t * 1.6 + i * 0.7);
            double rot = t * 25 + i * 30;
            var ring = new Gradation(new[]
            {
                (0.00f, new Color(255,255,255,16)),
                (0.50f, new Color(255,255,255,70)),
                (1.00f, new Color(255,255,255,16)),
            });
            // 疑似的に線で楕円リングを構成
            int seg = 96;
            for (int s = 0; s < seg; s++)
            {
                double th1 = s * (Math.PI * 2 / seg);
                double th2 = (s + 1) * (Math.PI * 2 / seg);
                double x1 = cx + Math.Cos(th1 + rot * Math.PI / 180) * a;
                double y1 = cy + Math.Sin(th1 + rot * Math.PI / 180) * b;
                double x2 = cx + Math.Cos(th2 + rot * Math.PI / 180) * a;
                double y2 = cy + Math.Sin(th2 + rot * Math.PI / 180) * b;
                var c = ring.GetColor((float)s / (seg - 1), Gradation.ColorSpace.OKLab);
                Drawing.LineZ(x1, y1, x2, y2, c, thickness: 1);
            }
        }

        // 右: ボックスと三角形のタイル
        double rightX = _width - 280;
        for (int j = 0; j < 4; j++)
        {
            for (int i = 0; i < 3; i++)
            {
                double x = rightX + i * 76;
                double y = 120 + j * 76;
                var col = ColorEx.From(new Rainbow((float)((i + j * 0.5) * 60 + t * 60)));
                Drawing.Box(x, y, 60, 60, col.WithAlpha(40));
                Drawing.Box(x, y, 60, 60, Color.VisibleColor(col).WithAlpha(180), thickness: 2);

                // 三角（交互に塗り/線）
                double x1 = x + 8, y1 = y + 52;
                double x2 = x + 30, y2 = y + 12;
                double x3 = x + 52, y3 = y + 52;
                bool fill = (i + j) % 2 == 0;
                Drawing.Triangle(x1, y1, x2, y2, x3, y3, fill ? col.WithAlpha(120) : col, thickness: fill ? 0 : 2);
            }
        }

        // デモ: グラデパネル
        var panel = new Gradation(new[]
        {
            (0.00f, Color.FromHSB(45, 0.68, 0.98)),
            (0.50f, Color.FromHSB(355, 0.65, 0.98)),
            (1.00f, Color.FromHSB(210, 0.55, 0.98)),
        });
        double pw = Math.Min(520, _width - 80);
        double ph = 80;
        double px = (_width - pw) / 2.0;
        double py = _height - ph - 40;
        Drawing.Box(px - 6, py - 6, pw + 12, ph + 12, new Color(0, 0, 0, 60));
        Drawing.Gradation((int)px, (int)py, (int)pw, (int)ph, panel, colorSpace: Gradation.ColorSpace.OKLCH);
        // rotate: (t * 40) % 360,
        Drawing.Text(_width / 2.0, py + ph / 2.0, "OKLCH gradient", Color.Black, ReferencePoint.Center);

        // 端に目印
        DrawCornerGuides();
    }

    private void DrawCornerGuides()
    {
        int m = 14;
        var c = new Color(255, 255, 255, 180);
        // 左上
        Drawing.Line(m, m, 28, 0, c, thickness: 2);
        Drawing.Line(m, m, 0, 28, c, thickness: 2);
        // 右上
        Drawing.Line(_width - m, m, -28, 0, c, thickness: 2);
        Drawing.Line(_width - m, m, 0, 28, c, thickness: 2);
        // 左下
        Drawing.Line(m, _height - m, 28, 0, c, thickness: 2);
        Drawing.Line(m, _height - m, 0, -28, c, thickness: 2);
        // 右下
        Drawing.Line(_width - m, _height - m, -28, 0, c, thickness: 2);
        Drawing.Line(_width - m, _height - m, 0, -28, c, thickness: 2);
    }
}
