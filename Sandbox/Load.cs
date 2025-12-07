using AstrumLoom;
using AstrumLoom.Extend;

namespace Sandbox;

internal class LoadCheckScene : Scene
{
    private IFont? _font;
    private Texture? _tex;
    private readonly int _samples = 1000; // 描画負荷用のサンプル数
    private bool _showTexture = false; // テクスチャ表示
    private bool _showFont = false;    // フォント表示
    private bool _showShape = false;     // Box表示
    private bool _regenerate = false; // テクスチャ再生成フラグ

    public override void Enable()
    {
        // 計測用のフォントとテクスチャを準備
        _font = FontHandle.Create("ＤＦ太丸ゴシック体 Pro-5", 20, edge: 1);
        _tex = new Texture("Assets/font.png");
        Drawing.DefaultFont = _font!;
    }

    public override void Update()
    {
        // 表示切り替え: T=Texture, F=Font, B=Box
        if (Key.T.Push()) _showTexture = !_showTexture;
        if (Key.F.Push()) _showFont = !_showFont;
        if (Key.B.Push()) _showShape = !_showShape;
        if (Key.R.Push()) _regenerate = true;

        // 1フレーム単位のプロファイル開始
        Profiler.BeginLoop();
    }

    public override void Draw()
    {
        Drawing.Fill(Color.LightGray);

        // テクスチャ描画計測
        if (_showTexture)
        {
            Profiler.BeginSection("TextureDraw");
            if (_tex != null)
            {
                for (int i = 0; i < _samples; i++)
                {
                    int w = 60, h = 20;
                    int x = 20 + w * (i / 30);
                    int y = 80 + h * (i % 30);
                    _tex.DrawSize(x, y, new LayoutUtil.Size(w, h));
                }
            }
            Profiler.EndSection("TextureDraw");
        }

        // フォント描画計測
        if (_showFont)
        {
            Profiler.BeginSection("FontDraw");
            for (int i = 0; i < _samples; i++)
            {
                int x = 20 + 90 * (i / 100);
                int y = 80 + 10 * (i % 100);
                string text = $"Draw #{i}";
                //_font?.Draw(x, y, text);
                TextSprites.Draw(_font, text, x, y);
            }
            Profiler.EndSection("FontDraw");
        }

        // Box描画計測
        if (_showShape)
        {
            Profiler.BeginSection("ShapeDraw");
            for (int i = 0; i < _samples; i++)
            {
                int x = i * 17 % (AstrumCore.Width - 32);
                int y = i * 19 % (AstrumCore.Height - 32);
                int w = 16 + i % 64;
                int h = 16 + i % 64;
                // 塗りつぶしと枠線を交互に描画して負荷を測る
                if ((i & 1) == 0)
                    Drawing.Box(x, y, w, h, Color.DarkCyan);
                else
                    Drawing.Box(x, y, w, h, Color.Magenta, thickness: 2);
            }
            Profiler.EndSection("ShapeDraw");
        }

        // プロファイル終了して結果取得
        Profiler.EndLoop();
        var reports = Profiler.GetLastLoopReports();

        // レポート可視化（簡易バー描画）
        int rx = 20, ry = 60, rw = 400, rh = 24, gap = 6;
        // ヒント
        TextSprites.Draw(_font, $"[T]Texture: {(_showTexture ? "ON" : "OFF")}\n" +
            $"[F]Font: {(_showFont ? "ON" : "OFF")}\n" +
            $"[B]Box: {(_showShape ? "ON" : "OFF")}\n" +
            $"[R] Regenerate Texture", rx + rw + 20, ry, Color.AliceBlue);

        Gradation gradation = new([Color.Red, Color.Yellow, Color.Lime]);
        DecorateText.DecorateOption decorate = new(gradation);
        TextSprites.Draw(_font, "Render Load Profile:", rx, ry - rh - gap, decorate);

        foreach (var r in reports)
        {
            // バー背景
            Drawing.Box(rx, ry, rw, rh, Color.Gray);
            // 割合バー
            int bw = (int)(rw * Math.Clamp(r.Percent / 100.0, 0.0, 1.0));
            var bar = r.Name switch
            {
                "TextureDraw" => Color.SkyBlue,
                "FontDraw" => Color.Orange,
                "BoxDraw" => Color.Lime,
                "<unaccounted>" => Color.DarkRed,
                "<total>" => Color.Green,
                _ => Color.White
            };
            Drawing.Box(rx, ry, bw, rh, bar);
            // テキスト
            ShapeText.Draw(rx + 6, ry + 7, $"{r.Name}: {r.Milliseconds:F2} ms ({r.Percent:F1}%)", size: 10, color: Color.VisibleColor(bar));
            ry += rh + gap;
        }

        // テクスチャ再生成
        if (_regenerate)
        {
            _regenerate = false;
            _tex = new Texture(new LayoutUtil.Size(90, 30), () =>
            {
                Drawing.Fill(Color.Blue);
                Drawing.Text(0, 0, DateTime.Now.ToString("HHmmss"), Color.Yellow);
            });
            Log.Write("Texture regenerated.");
        }
    }
}
