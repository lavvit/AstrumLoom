using AstrumLoom;

using static AstrumLoom.LayoutUtil;

namespace Sandbox;

/// <summary>
/// テーマ「星図アトラス」。
///
/// Texture の振る舞いを 6 枚のカードに分けて見せる。素材を眺めるためではなく、
/// 設定を変えた瞬間に何が変わるかを目で追えることを狙って作ってある。
///
/// 使う素材（tools\make-sandbox-assets.ps1 で生成）:
///   star_atlas.png … 96px 四方 × 8 コマ。切り出し（Rectangle）とコマ送りの確認用
///   compass.png    … 上下左右と四隅が全部違う方位盤。反転・回転・基準点の確認用
///   nebula.png     … アルファ付きの光。ブレンドモードの確認用
///   ring.png       … 切れ目のある環。色づけと不透明度の確認用
/// </summary>
internal sealed class TextureDemoScene : Scene
{
    // 素材はカードごとに別インスタンスで持つ。
    // Texture は Color/Scale/Angle 等の描画オプションを「インスタンスの状態」として抱えるので、
    // 1 枚を使い回すとカード間で設定が漏れる。分けておくと各カードが独立して読める。
    private Texture? _atlasBig;    // 切り出し
    private Texture? _atlasStrip;  // コマ一覧
    private Texture? _compassAnchor;
    private Texture? _compassSpin;
    private Texture? _nebula;
    private Texture? _ring;

    /// <summary>焼き込みテクスチャ。メインスレッドでしか作れないので Draw の中で遅延生成する。</summary>
    private Texture? _baked;
    private int _bakeSeed = 1;
    private bool _bakeRequested = true;

    // 操作状態
    private int _frame;              // 表示中のコマ
    private bool _autoFrame = true;  // コマ送りを自動で進めるか
    private int _anchor;             // ReferencePoint の選択
    private bool _flipX, _flipY;
    private bool _spin = true;

    private double _time;            // シーンに入ってからの経過秒。DemoUi.Delta を積む。
    private double _frameTimer;      // コマ送りの残り時間

    private const int Cell = 96;         // star_atlas.png の 1 コマ
    private const int Frames = 8;
    private const double FrameSeconds = 0.12;   // コマ送りの間隔

    private static readonly ReferencePoint[] Anchors =
    [
        ReferencePoint.TopLeft, ReferencePoint.TopCenter, ReferencePoint.TopRight,
        ReferencePoint.CenterLeft, ReferencePoint.Center, ReferencePoint.CenterRight,
        ReferencePoint.BottomLeft, ReferencePoint.BottomCenter, ReferencePoint.BottomRight,
    ];

    private static readonly BlendMode[] Blends =
    [
        BlendMode.None, BlendMode.Add, BlendMode.Screen,
        BlendMode.Multiply, BlendMode.Subtract, BlendMode.Reverse,
    ];

    public override void Enable()
    {
        base.Enable();

        // ファイルからのロードは別スレッドから呼んでも安全（各バックエンドが
        // メインスレッドへ渡し直す）。シーン切り替えは更新スレッドから起きるので、これは重要。
        _atlasBig = new Texture("Assets/star_atlas.png");
        _atlasStrip = new Texture("Assets/star_atlas.png");
        _compassAnchor = new Texture("Assets/compass.png");
        _compassSpin = new Texture("Assets/compass.png");
        _nebula = new Texture("Assets/nebula.png");
        _ring = new Texture("Assets/ring.png");

        _time = 0;
        _frameTimer = 0;
        _bakeRequested = true;
    }

    public override void Disable()
    {
        base.Disable();
        _atlasBig?.Dispose();
        _atlasStrip?.Dispose();
        _compassAnchor?.Dispose();
        _compassSpin?.Dispose();
        _nebula?.Dispose();
        _ring?.Dispose();
        _baked?.Dispose();
        _atlasBig = _atlasStrip = _compassAnchor = _compassSpin = _nebula = _ring = _baked = null;
    }

    public override void Update()
    {
        double dt = DemoUi.Delta;
        _time += dt;

        if (Key.Left.Push()) { _frame = (_frame + Frames - 1) % Frames; _autoFrame = false; }
        if (Key.Right.Push()) { _frame = (_frame + 1) % Frames; _autoFrame = false; }
        if (Key.Space.Push()) _autoFrame = !_autoFrame;

        // コマ送りは「何フレームごと」ではなく「何秒ごと」で数える。
        if (_autoFrame)
        {
            _frameTimer += dt;
            while (_frameTimer >= FrameSeconds)
            {
                _frameTimer -= FrameSeconds;
                _frame = (_frame + 1) % Frames;
            }
        }
        else
        {
            _frameTimer = 0;
        }

        if (Key.A.Push()) _anchor = (_anchor + 1) % Anchors.Length;
        if (Key.X.Push()) _flipX = !_flipX;
        if (Key.Y.Push()) _flipY = !_flipY;
        if (Key.C.Push()) _spin = !_spin;
        if (Key.R.Push()) { _bakeSeed++; _bakeRequested = true; }
    }

    public override void Draw()
    {
        DrawBackdrop();

        Drawing.Text(20, 16, "星図アトラス / Texture の見本帳", Color.White, edgecolor: new Color(10, 14, 28));
        DemoUi.Note(20, 52, 820,
            "[Left/Right] コマ  [Space] 自動送り  [A] 基準点  [X/Y] 反転  [C] 回転  [R] 焼き直し",
            new Color(180, 198, 226));

        // 3 列 × 2 段。カードの中は必ず「左上を (0,0) とみなした座標」で描く。
        Card(0, 0, "1) 切り出し / DrawRect", DrawClipCard);
        Card(1, 0, "2) 基準点 / ReferencePoint", DrawAnchorCard);
        Card(2, 0, "3) 回転と反転 / Angle・Flip", DrawSpinCard);
        Card(0, 1, "4) 重ね方 / BlendMode", DrawBlendCard);
        Card(1, 1, "5) 色と不透明度 / Color", DrawTintCard);
        Card(2, 1, "6) 焼き込みテクスチャ", DrawBakeCard);
    }

    #region 背景とカードの枠

    private void DrawBackdrop()
    {
        Drawing.Fill(new Color(9, 12, 24));

        // 星屑。乱数を持たずに済むよう、位置は添字から決める（毎フレーム同じ場所に出る）。
        for (int i = 0; i < 140; i++)
        {
            double x = (i * 977) % AstrumCore.Width;
            double y = (i * 613) % AstrumCore.Height;
            double tw = 0.35 + 0.65 * Math.Abs(Math.Sin(_time * 1.7 + i * 0.61));
            Drawing.Box(x, y, 2, 2, new Color(220, 230, 255, (int)(tw * 150)));
        }

        // 星雲を薄く敷いて、テーマの背景にする。
        if (_nebula is { Enable: true })
        {
            _nebula.ResetOption();
            _nebula.Point = ReferencePoint.Center;
            _nebula.BlendMode = BlendMode.Add;
            _nebula.Opacity = 0.22;
            _nebula.Scale = 3.2;
            _nebula.Draw(AstrumCore.Width * 0.78, AstrumCore.Height * 0.30);
            _nebula.Scale = 2.4;
            _nebula.Draw(AstrumCore.Width * 0.18, AstrumCore.Height * 0.78);
        }
    }

    private const int CardW = 404;
    private const int CardH = 292;
    private const int CardTop = 84;
    private const int Pad = 10;                 // カード内の左右余白
    private const double TextW = CardW - Pad * 2;
    private const double BodyH = CardH - 26;    // 見出しを除いた高さ

    private static void Card(int col, int row, string title, Action<double, double> body)
    {
        double x = 16 + col * (CardW + 12);
        double y = CardTop + row * (CardH + 10);
        DemoUi.Card(x, y, CardW, CardH, title, (bx, by, _) => body(bx, by));
    }

    #endregion

    #region 1)  切り出しとコマ送り

    private void DrawClipCard(double x, double y)
    {
        if (_atlasBig is not { Enable: true } || _atlasStrip is not { Enable: true })
        {
            DemoUi.Note(x + Pad, y + 10, TextW, "読み込み中...");
            return;
        }

        // 1 コマだけを切り出して大きく描く。
        // DrawRect は Rectangle を一時的に差し替えて元へ戻すので、呼び出しの前後で状態が汚れない。
        _atlasBig.ResetOption();
        _atlasBig.Point = ReferencePoint.Center;
        _atlasBig.Scale = 1.4;
        _atlasBig.BlendMode = BlendMode.Add;
        _atlasBig.DrawRect(x + CardW / 2.0, y + 76, new Rect(_frame * Cell, 0, Cell, Cell));

        // 全コマを小さく並べ、今どこを切り出しているかを枠で示す。
        double sw = 40;
        double sx = x + (CardW - sw * Frames) / 2.0;
        double sy = y + 152;
        for (int i = 0; i < Frames; i++)
        {
            _atlasStrip.ResetOption();
            _atlasStrip.XYScale = (sw / Cell, sw / Cell);
            _atlasStrip.BlendMode = BlendMode.Add;
            _atlasStrip.Opacity = i == _frame ? 1.0 : 0.42;
            _atlasStrip.DrawRect(sx + i * sw, sy, new Rect(i * Cell, 0, Cell, Cell));
            if (i == _frame)
                Drawing.Box(sx + i * sw, sy, sw, sw, new Color(255, 215, 120), thickness: 2);
        }

        double ny = y + 194;
        ny = DemoUi.Notes(x + Pad, ny, TextW, new Color(196, 212, 240),
            $"コマ {_frame + 1}/{Frames}   Rectangle = ({_frame * Cell}, 0, {Cell}, {Cell})");
        ny = DemoUi.Notes(x + Pad, ny, TextW, new Color(152, 170, 202),
            $"自動送り: {(_autoFrame ? $"ON（{FrameSeconds:0.00} 秒ごと）" : "OFF")}",
            "1 枚の画像から矩形を切り替えるだけでコマ送りになる。");
        DemoUi.Notes(x + Pad, ny, TextW, new Color(120, 138, 172),
            $"素材の実寸 {_atlasBig.Width} x {_atlasBig.Height}");
    }

    #endregion

    #region 2)  基準点

    private void DrawAnchorCard(double x, double y)
    {
        if (_compassAnchor is not { Enable: true })
        {
            DemoUi.Note(x + Pad, y + 10, TextW, "読み込み中...");
            return;
        }

        double ax = x + CardW / 2.0;
        double ay = y + 88;

        // 基準点の効果は「同じ座標を渡したのに絵の位置が変わる」ことでしか分からないので、
        // 渡している座標そのものを十字で描いておく。
        _compassAnchor.ResetOption();
        _compassAnchor.Scale = 0.52;
        _compassAnchor.Point = Anchors[_anchor];
        _compassAnchor.Draw(ax, ay);

        Drawing.Line(ax - 150, ay, 300, 0, new Color(255, 90, 90, 110));
        Drawing.Line(ax, ay - 78, 0, 156, new Color(255, 90, 90, 110));
        Drawing.Cross(ax, ay, 8, new Color(255, 120, 120), thickness: 2);

        double ny = y + 182;
        ny = DemoUi.Notes(x + Pad, ny, TextW, new Color(196, 212, 240),
            $"ReferencePoint.{Anchors[_anchor]}");
        ny = DemoUi.Notes(x + Pad, ny, TextW, new Color(152, 170, 202),
            "赤い十字が Draw に渡した座標。画像がその周りのどこへ置かれるかだけが変わる。");
        DemoUi.Notes(x + Pad, ny, TextW, new Color(120, 138, 172),
            "[A] で 9 通りを巡回");
    }

    #endregion

    #region 3)  回転と反転

    private void DrawSpinCard(double x, double y)
    {
        if (_compassSpin is not { Enable: true })
        {
            DemoUi.Note(x + Pad, y + 10, TextW, "読み込み中...");
            return;
        }

        double cx = x + CardW / 2.0;
        double cy = y + 88;
        // Texture.Angle の単位は「回転数」。度でもラジアンでもない。
        // DxLib 側は angle * 2π をラジアンとして、RayLib 側は angle * 360 を度として渡している。
        double degree = _spin ? (_time * 72) % 360 : 0;   // 5 秒で 1 周
        double turn = degree / 360.0;

        // 元の向きを薄く先に敷いて、比較できるようにする。
        _compassSpin.ResetOption();
        _compassSpin.Point = ReferencePoint.Center;
        _compassSpin.Scale = 0.52;
        _compassSpin.Opacity = 0.16;
        _compassSpin.Draw(cx, cy);

        // 回転の基準は Point（ここでは中心）。Center 以外にすると軸がずれる。
        _compassSpin.Opacity = 1.0;
        _compassSpin.Angle = turn;
        _compassSpin.Flip = (_flipX, _flipY);
        _compassSpin.Draw(cx, cy);

        double ny = y + 182;
        ny = DemoUi.Notes(x + Pad, ny, TextW, new Color(196, 212, 240),
            $"Angle = {turn:0.000} 回転（{degree:F0}°）  {(_spin ? "回転中" : "停止")}",
            $"Flip = (X:{(_flipX ? "反転" : "素")}, Y:{(_flipY ? "反転" : "素")})");
        ny = DemoUi.Notes(x + Pad, ny, TextW, new Color(152, 170, 202),
            "赤い長針が上、白い短針が下、黄の中針が右。左には針が無い。");
        DemoUi.Notes(x + Pad, ny, TextW, new Color(120, 138, 172),
            "薄く重なっているのが回転も反転もしていない元の向き。");
    }

    #endregion

    #region 4)  ブレンドモード

    private void DrawBlendCard(double x, double y)
    {
        if (_nebula is not { Enable: true })
        {
            DemoUi.Note(x + Pad, y + 10, TextW, "読み込み中...");
            return;
        }

        // 下地を作る。Multiply / Subtract は「下に何かある」ときだけ違いが出るので、
        // 暗い帯と明るい帯の両方をまたぐように敷く。
        double gx = x + Pad, gy = y + 6, gw = TextW, gh = 150;
        for (int i = 0; i < 6; i++)
        {
            int v = 40 + i * 38;
            Drawing.Box(gx + gw / 6.0 * i, gy, gw / 6.0 + 1, gh, new Color(v, (int)(v * 0.8), (int)(v * 1.15)));
        }
        Drawing.Box(gx, gy, gw, gh, new Color(90, 116, 170), thickness: 1);

        double cellW = gw / 3.0;
        double cellH = gh / 2.0;
        for (int i = 0; i < Blends.Length; i++)
        {
            double bx = gx + cellW * (i % 3) + cellW / 2.0;
            double by = gy + cellH * (i / 3) + cellH / 2.0 - 6;

            _nebula.ResetOption();
            _nebula.Point = ReferencePoint.Center;
            _nebula.Scale = 0.26;
            _nebula.BlendMode = Blends[i];
            _nebula.Draw(bx, by);

            DemoUi.NoteFont.Draw(bx, gy + cellH * (i / 3) + cellH - 16, Blends[i].ToString(),
                Color.White, ReferencePoint.TopCenter);
        }

        double ny = y + 162;
        ny = DemoUi.Notes(x + Pad, ny, TextW, new Color(152, 170, 202),
            "下地は左ほど暗く右ほど明るい 6 段の帯。",
            "Add と Screen は下地を明るくし、Multiply と Subtract は暗くする。",
            "None は素材のアルファだけで重ねる、いちばん素直な合成。");
        DemoUi.Notes(x + Pad, ny + 4, TextW, new Color(212, 172, 112),
            "注: 同じ BlendMode でも DxLib と RayLib で結果は一致しない。特に RayLib の "
            + "Subtract と Reverse は素材の矩形ごと暗く塗る。--backend で見比べること。");
    }

    #endregion

    #region 5)  色と不透明度

    private void DrawTintCard(double x, double y)
    {
        if (_ring is not { Enable: true })
        {
            DemoUi.Note(x + Pad, y + 10, TextW, "読み込み中...");
            return;
        }

        // Color は「素材に掛け算する色」。白なら素材そのまま。
        double hue = (_time * 54) % 360;
        var tint = Color.FromHSB(hue, 0.75, 1.0);

        _ring.ResetOption();
        _ring.Point = ReferencePoint.Center;
        _ring.Scale = 0.34;
        _ring.Color = tint;
        _ring.BlendMode = BlendMode.Add;
        _ring.Angle = ((-_time * 48) % 360) / 360.0;   // 単位は回転数（3)  を参照）
        _ring.Draw(x + CardW / 2.0, y + 58);

        // 不透明度の段階見本。数値と見た目を並べておくと、疑ったときに確かめられる。
        double[] steps = [0.15, 0.35, 0.6, 1.0];
        double cell = 66;
        double sx = x + (CardW - cell * steps.Length) / 2.0;
        double sy = y + 116;
        for (int i = 0; i < steps.Length; i++)
        {
            Drawing.Box(sx + i * cell + 3, sy, cell - 6, cell - 6, new Color(40, 48, 74));
            _ring.ResetOption();
            _ring.Point = ReferencePoint.Center;
            _ring.XYScale = ((cell - 14) / _ring.Width, (cell - 14) / _ring.Height);
            _ring.Color = tint;
            _ring.Opacity = steps[i];
            _ring.Draw(sx + i * cell + cell / 2.0, sy + (cell - 6) / 2.0);
            DemoUi.NoteFont.Draw(sx + i * cell + cell / 2.0, sy + cell - 2,
                $"{steps[i]:0.00}", new Color(190, 205, 230), ReferencePoint.TopCenter);
        }

        double ny = y + 202;
        ny = DemoUi.Notes(x + Pad, ny, TextW, new Color(196, 212, 240),
            $"Color = HSB({hue:F0}, 0.75, 1.00) を素材に掛けている。");
        DemoUi.Notes(x + Pad, ny, TextW, new Color(152, 170, 202),
            "上の環は BlendMode.Add。色を変えると発光の色が変わる。",
            "下段は BlendMode.None のまま Opacity だけを変えたもの。");
    }

    #endregion

    #region 6)  焼き込みテクスチャ

    private void DrawBakeCard(double x, double y)
    {
        // new Texture(size, action) はレンダーターゲットを作るので、メインスレッドからしか呼べない。
        // シーンの Enable() は更新スレッドから呼ばれることがある（Scene 差し替えの呼び元が Update だとそうなる）。
        // だから生成はここ、つまり必ずメインスレッドで回る Draw の中で行う。
        if (_bakeRequested)
        {
            _bakeRequested = false;
            _baked?.Dispose();
            int seed = _bakeSeed;
            _baked = new Texture(new Size(300, 150), () => BakeStarChart(seed));
        }

        double ny;
        if (_baked is { Enable: true })
        {
            _baked.ResetOption();
            _baked.Point = ReferencePoint.TopCenter;
            _baked.Draw(x + CardW / 2.0, y + 10);
            Drawing.Box(x + (CardW - 300) / 2.0, y + 10, 300, 150, new Color(90, 116, 170), thickness: 1);
            ny = DemoUi.Notes(x + Pad, y + 168, TextW, new Color(196, 212, 240),
                $"焼き込み済み {_baked.Width} x {_baked.Height}（seed {_bakeSeed}）");
        }
        else
        {
            ny = DemoUi.Notes(x + Pad, y + 40, TextW, new Color(235, 130, 130),
                "焼き込みに失敗した。メインスレッド以外から作った可能性がある。");
        }

        ny = DemoUi.Notes(x + Pad, ny, TextW, new Color(152, 170, 202),
            "図形と文字を 1 枚のテクスチャへ描き込んで固定してある。",
            "毎フレーム描き直す代わりに 1 回焼けば、以降は 1 ドローコールで済む。");
        DemoUi.Notes(x + Pad, ny, TextW, new Color(120, 138, 172),
            "[R] で別の seed に焼き直す。旧テクスチャは Dispose してから作る。");
    }

    /// <summary>焼き込みテクスチャの中身。呼ばれるのはレンダーターゲットへの描画中だけ。</summary>
    private static void BakeStarChart(int seed)
    {
        Drawing.Fill(new Color(14, 20, 40));

        var rand = new Random(seed);
        // 星と、それを結ぶ星座線。
        var pts = new (double x, double y)[9];
        for (int i = 0; i < pts.Length; i++)
            pts[i] = (18 + rand.NextDouble() * 264, 24 + rand.NextDouble() * 106);

        for (int i = 1; i < pts.Length; i++)
            Drawing.LineZ(pts[i - 1].x, pts[i - 1].y, pts[i].x, pts[i].y, new Color(90, 130, 200, 160));

        foreach (var (px, py) in pts)
        {
            Drawing.Circle(px, py, 4, new Color(255, 245, 210));
            Drawing.Circle(px, py, 7, new Color(150, 190, 255, 90));
        }

        DemoUi.NoteFont.Draw(6, 4, $"CHART #{seed:000}", new Color(120, 160, 230));
        DemoUi.NoteFont.Draw(294, 132, "baked", new Color(90, 120, 180), ReferencePoint.TopRight);
    }

    #endregion

    // --- セルフテストから中を覗くための入口 ---------------------------------

    /// <summary>素材が 4 枚とも読めているか。</summary>
    public bool AssetsReady =>
        _atlasBig is { Enable: true } && _compassAnchor is { Enable: true } &&
        _nebula is { Enable: true } && _ring is { Enable: true };

    /// <summary>焼き込みテクスチャが実際に作れたか。</summary>
    public bool BakedReady => _baked is { Enable: true };

    public int CurrentFrame => _frame;
    public int AnchorIndex => _anchor;
    public bool FlipX => _flipX;
}
