using AstrumLoom;

namespace Sandbox;

/// <summary>
/// Texture / Sound の各種プロパティと描画・再生挙動を視覚的にテストするシーン。
/// </summary>
internal sealed class TextureDemoScene : Scene
{
    private Texture? _bgtex;
    private Texture? _tex;
    private Texture? _flash;
    private double _angle;          // 回転角度
    private double _time;           // 経過時間
    private readonly List<Texture> _clones = [];
    private bool _showGrid = true;
    private bool _paused;
    private ETexDrawState _drawState = ETexDrawState.BaseDraw;

    public override void Enable()
    {
        // アセット読み込み（Program と同じ配置想定）
        _tex = new Texture("Assets/test.png");
        _bgtex = new Texture("Assets/colorbg.jpg");
        _flash = new Texture("Assets/flash.png");
        // クローン作成（描画オプションの差異確認用）
        _clones.Clear();
        if (_tex != null)
        {
            for (int i = 0; i < 6; i++)
            {
                var t = _tex.Clone();
                _clones.Add(t);
            }
        }
    }

    public override void Disable()
    {
        base.Disable();
        _tex?.Dispose();
        _bgtex?.Dispose();
        _flash?.Dispose();
        foreach (var c in _clones) c.Dispose();
        _clones.Clear();
    }

    public override void Update()
    {
        if (Key.Esc.Push()) AstrumCore.End();
        if (Key.F1.Push()) _drawState = (ETexDrawState)(((int)_drawState + 1) % (int)ETexDrawState.Max);

        double dt = AstrumCore.Platform.UTime.DeltaTime;
        if (!_paused) _time += dt;

        switch (_drawState)
        {
            case ETexDrawState.BaseDraw:
                break;
            case ETexDrawState.BlendDraw:
                _flash?.Point = ReferencePoint.Center;
                _flash?.Scale = 3.0;
                _flash?.Opacity = 0.5 + 0.5 * Math.Sin(_time * 4.0);
                _flash?.BlendMode = BlendMode.Add;
                break;
        }

        /*

        if (Key.G.Push()) _showGrid = !_showGrid;
        if (Key.P.Push()) _paused = !_paused;

        // 回転角度更新
        _angle += dt * 90.0; // 90度/秒
        if (_angle >= 360.0) _angle -= 360.0;

        // メインテクスチャの動的パラメータ
        if (_tex != null)
        {
            _tex.Point = ReferencePoint.Center;
            _tex.Flip = (false, Math.Sin(_time * 2) > 0);

            _tex.Angle = _angle / 360.0;
            _tex.Scale = 1.0 + 0.15 * Math.Sin(_time * 2.0);
            // カラーと不透明度をサイン波で変化
            double s = (Math.Sin(_time * 1.4) + 1) * 0.5; // 0..1
            double s2 = (Math.Sin(_time * 0.8 + Math.PI / 2) + 1) * 0.5;
            _tex.Color = new Color((int)(40 + 215 * s), (int)(40 + 215 * s2), 220);
            _tex.Opacity = 0.6 + 0.4 * Math.Sin(_time * 3.2);
            // 小さな矩形切り抜きテスト（テクスチャ中央付近）
            int rw = Math.Max(16, (int)(_tex.Width * (0.3 + 0.2 * s)));
            int rh = Math.Max(16, (int)(_tex.Height * (0.3 + 0.2 * s2)));
            int rx = (_tex.Width - rw) / 2;
            int ry = (_tex.Height - rh) / 2;
            _tex.Rectangle = new LayoutUtil.Rect(rx, ry, rw, rh);
        }
        */
    }

    public override void Draw()
    {
        // 背景グラデ（簡易）
        Drawing.Fill(new Color(20, 24, 40));
        DrawBackdrop();

        if (_tex == null) return;

        /*


        double cx = AstrumCore.Width / 2.0;
        double cy = AstrumCore.Height / 2.0 - 40;

        // メイン表示（クロップ + 回転）
        _tex.Draw(cx, cy);
        Drawing.Cross(cx, cy, 36, Color.Orange, 2);

        // 参照点グリッド表示
        if (_showGrid) DrawReferencePointGrid(cx, cy + 240);

        // クローン達（オプション差異確認）
        double bx = 40;
        double by = 200;
        for (int i = 0; i < _clones.Count; i++)
        {
            var t = _clones[i];
            t.Point = (ReferencePoint)(i % 9);
            t.Rectangle = null; // クローンはフル表示
            t.Scale = 0.1 + i * 0.1;
            t.Opacity = 0.1 + i * 0.3;
            t.Angle = i * 0.25 + 0.1 * Math.Sin(_time * 1.2 + i);
            double ox = bx + i * 240;
            double oy = by + 80 * Math.Sin(_time * 1.2 + i);
            t.Draw(ox, oy);
            Drawing.Cross(ox, oy, 24, Color.Yellow, 2);
            Drawing.Text(ox, oy + 80, t.Point.ToString(), Color.White, point: ReferencePoint.Center);
        }*/

        switch (_drawState)
        {
            case ETexDrawState.BaseDraw:
                Draw_BaseDraw();
                break;
            case ETexDrawState.BlendDraw:
                Draw_BlendDraw();
                break;
        }

        // 情報パネル
        DrawInfoPanel();
    }

    private void Draw_BaseDraw()
    {
        // 基本描画（ブレンドなし）
        if (_tex != null)
        {
            _tex.BlendMode = BlendMode.None;
            _tex.Draw(100, 100);
        }
    }

    private void Draw_BlendDraw()
    {

        _bgtex?.Draw(0, 0, new LayoutUtil.Rect(0, 0, AstrumCore.Width, AstrumCore.Height));

        // ブレンド描画（通常のアルファブレンド）
        _flash?.Draw(600, 400);
    }


    private void DrawReferencePointGrid(double cx, double cy)
    {
        int cols = 3;
        double spacing = 140;
        int c = DateTime.Now.Second % 10;
        for (int i = 0; i < 9; i++)
        {
            double gx = cx + (i % cols - 1) * spacing;
            double gy = cy + (i / cols - 1) * spacing;
            if (i == c || c == 9)
            {
                _tex?.Point = (ReferencePoint)i;
                _tex?.Scale = 0.25;
                _tex?.Angle = 0;
                _tex?.Rectangle = null;
                _tex?.Opacity = 0.35;
                _tex?.Color = Color.White;
                _tex?.Flip = (false, false);
                _tex?.Draw(gx, gy);
            }
            Drawing.Cross(gx, gy, 20, Color.Cyan, 2);
            Drawing.Text(gx, gy + 50, ((ReferencePoint)i).ToString(), Color.LightGray, point: ReferencePoint.Center);
        }
    }

    private void DrawInfoPanel()
    {
        double x = 20;
        double y = AstrumCore.Height - 140;
        Drawing.Box(x - 12, y - 12, 420, 130, new Color(0, 0, 0, 120));

        Drawing.Text(x, y + 44, $"Texture Opacity: {_tex?.Opacity:0.00} Scale: {_tex?.Scale:0.00} Angle: {_angle:0.0}", Color.White);
        Drawing.Text(x, y + 88, "G: Grid  P: Pause  ESC: Exit", Color.Gray);
    }

    private void DrawBackdrop()
    {
        // 簡易パーティクル風（時間と位置で色を変える）
        int count = 48;
        double t = _time;
        var rnd = new Random(1234); // 固定シードで安定描画
        for (int i = 0; i < count; i++)
        {
            double px = AstrumCore.Width * (i * 37 % 997 / 997.0);
            double py = AstrumCore.Height * (i * 91 % 991 / 991.0);
            double pulse = 0.4 + 0.6 * (0.5 + 0.5 * Math.Sin(t * 1.2 + i));
            int a = (int)(50 + 120 * pulse);
            var col = new Color(40 + (int)(pulse * 60), 60 + (int)(pulse * 100), 90 + (int)(pulse * 120), a);
            Drawing.Circle(px, py, 50 * pulse, col);
        }
    }
}

internal enum ETexDrawState
{
    BaseDraw,   // 基本描画
    BlendDraw,  // ブレンド描画

    Max,
}
