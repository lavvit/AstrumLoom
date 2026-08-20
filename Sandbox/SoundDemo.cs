using System.Collections.Concurrent;

using AstrumLoom;

namespace Sandbox;

/// <summary>
/// テーマ「灯台の夜」。
///
/// Sound のプロパティを、そのまま情景に結びつけて確かめるシーン。
///   Pan    … 船が画面のどちら側にいるか
///   Volume … 船がどれだけ遠いか
///   Speed  … 船が近づいてくるか遠ざかるか（ドップラー）
///   Loop / Time / Progress … 潮の伴奏（BGM）の再生位置
///
/// 音は耳でしか確かめられないので、右側に生の数値をそのまま出している。
/// 「鳴っているつもりで鳴っていない」を見抜けるよう、Loaded / Enable / IsFailed も並べる。
///
/// 使う素材（tools\make-sandbox-assets.ps1 で生成）:
///   bgm_tide.wav … 8 秒ちょうどで継ぎ目なくループする伴奏（ステレオ）
///   se_horn.wav  … 霧笛。低く長いので Pan と Speed の変化を耳で追える
///   se_bell.wav  … 鐘。減衰が速く、非整数倍音なので Speed 変化が分かりやすい
/// </summary>
internal sealed class SoundDemoScene : Scene
{
    private Sound? _bgm;
    private Sound? _horn;
    private Sound? _bell;

    /// <summary>
    /// 音への指示はここに積み、Draw の先頭でまとめて実行する。
    ///
    /// 理由: Update は UseMultiThreadUpdate=true だと専用の更新スレッドで回る。
    /// 一方 raylib の UpdateMusicStream / PlaySound はメインスレッド専用 API で、
    /// Draw だけが必ずメインスレッドで走る。入力の判定は Update に置いたまま、
    /// バックエンドを叩く瞬間だけメインスレッドへ寄せるための受け渡し。
    /// （AstrumCore.RequestToMainThread は 1 フレームに 1 件しか掃けないので、
    ///   1 フレームに複数の音を出したいここでは使わない。）
    /// </summary>
    private readonly ConcurrentQueue<Action> _soundJobs = new();

    // --- 情景の状態 ---------------------------------------------------------
    private double _time;              // シーンに入ってからの経過秒
    private double _shipX = 0.62;      // 0..1 の航路上の位置
    private double _shipDir = 1;       // 進行方向
    private bool _autoSail = true;
    private bool _autoBell = true;
    private double _beam;              // 灯台の光の角度（回転数 0..1）
    private double _lastBeamToShip = 1;
    private double _bellCooldown;      // 鐘の最短間隔。これが無いと 1 掃きで何度も鳴る

    // --- 音の設定 -----------------------------------------------------------
    private double _bgmVolume = 0.55;
    private double _bgmSpeed = 1.0;
    private bool _bgmWanted = true;
    private bool _loop = true;

    // 直近に霧笛へ渡した値。表示用に覚えておく。
    private double _lastHornPan, _lastHornVol, _lastHornSpeed;
    private int _hornCount, _bellCount;

    private const double SeaY = 300;   // 水平線
    private const double PanelX = 866;

    public override void Enable()
    {
        base.Enable();

        // stream: true は「曲として流す」指定。RayLib は Music として持ち、DxLib は今のところ
        // 引数を見ずに常に LoadSoundMem する（docs\KNOWN-ISSUES.md の
        // 「LoadSound の streaming 引数が完全に無視される」）。長い曲でも動くが、
        // DxLib 側では全部メモリに載る点だけ意識しておく。
        _bgm = new Sound("Assets/bgm_tide.wav", stream: true);
        _horn = new Sound("Assets/se_horn.wav");
        _bell = new Sound("Assets/se_bell.wav");

        _time = 0;
        _bellCooldown = 0;
        _hornCount = _bellCount = 0;
        _bgmWanted = true;
    }

    public override void Disable()
    {
        base.Disable();
        // 別のシーンへ移ったあとも鳴り続けないよう、必ず止めてから捨てる。
        _bgm?.Stop();
        _horn?.Stop();
        _bell?.Stop();
        _bgm?.Dispose();
        _horn?.Dispose();
        _bell?.Dispose();
        _bgm = _horn = _bell = null;
        _soundJobs.Clear();
    }

    public override void Update()
    {
        // 演出はすべて経過秒で進める。フレーム数で進めると、無制限 FPS の実機で
        // 灯台が高速回転し、鐘が毎秒何千回も鳴る（--selftest では 60Hz 固定なので気づけない）。
        double dt = DemoUi.Delta;
        _time += dt;
        _beam = (_beam + dt * 0.22) % 1.0;      // 約 4.5 秒で 1 周
        _bellCooldown = Math.Max(0, _bellCooldown - dt);

        UpdateShip(dt);
        HandleKeys();
        UpdateAutoBell();
    }

    private void UpdateShip(double dt)
    {
        if (Key.A.Hold()) { _shipX -= 0.45 * dt; _autoSail = false; }
        if (Key.D.Hold()) { _shipX += 0.45 * dt; _autoSail = false; }
        if (Key.M.Push()) _autoSail = !_autoSail;

        if (_autoSail)
        {
            _shipX += 0.14 * dt * _shipDir;      // 端から端まで約 7 秒
            if (_shipX > 1) { _shipX = 1; _shipDir = -1; }
            if (_shipX < 0) { _shipX = 0; _shipDir = 1; }
        }
        else
        {
            _shipX = Math.Clamp(_shipX, 0, 1);
        }
    }

    private void HandleKeys()
    {
        if (Key.P.Push())
        {
            _bgmWanted = !_bgmWanted;
            if (!_bgmWanted) _soundJobs.Enqueue(() => _bgm?.Stop());
        }
        if (Key.L.Push())
        {
            _loop = !_loop;
            _soundJobs.Enqueue(() => { if (_bgm != null) _bgm.Loop = _loop; });
        }
        // 押しっぱなしの量も経過秒で決める（1 秒押して 0.5 変わる）。
        double step = DemoUi.Delta * 0.5;
        if (Key.Up.Hold()) SetBgmVolume(_bgmVolume + step);
        if (Key.Down.Hold()) SetBgmVolume(_bgmVolume - step);
        if (Key.Comma.Hold()) SetBgmSpeed(_bgmSpeed - step);
        if (Key.Period.Hold()) SetBgmSpeed(_bgmSpeed + step);
        if (Key.Left.Push()) SeekBgm(-0.08);
        if (Key.Right.Push()) SeekBgm(+0.08);

        if (Key.Space.Push()) FireHorn();
        if (Key.B.Push()) FireBell(1.0);
        if (Key.N.Push()) _autoBell = !_autoBell;
    }

    private void SetBgmVolume(double v)
    {
        _bgmVolume = Math.Clamp(v, 0, 1);
        _soundJobs.Enqueue(() => { if (_bgm != null) _bgm.Volume = _bgmVolume; });
    }

    private void SetBgmSpeed(double v)
    {
        _bgmSpeed = Math.Clamp(v, 0.5, 2.0);
        _soundJobs.Enqueue(() => { if (_bgm != null) _bgm.Speed = _bgmSpeed; });
    }

    private void SeekBgm(double delta)
        => _soundJobs.Enqueue(() =>
        {
            if (_bgm is not { Enable: true } || _bgm.Length <= 0) return;
            _bgm.Progress = Math.Clamp(_bgm.Progress + delta, 0, 0.99);
        });

    /// <summary>灯台の光が船を掃いた瞬間に鐘を鳴らす。「光と音が揃う」ので遅延が体感できる。</summary>
    private void UpdateAutoBell()
    {
        // 灯室から船へ向かう角度と、今の光の角度の差。
        // 座標は DrawLighthouse の灯室（by - 105）と必ず揃えること。ずれると
        // 「光が当たっていないのに鳴る」ようになり、しかも絵だけ見ても気づけない。
        double lx = 210, ly = SeaY + 24 - 105;
        double sx = ShipScreenX, sy = SeaY + 42;
        double toShip = (Math.Atan2(sy - ly, sx - lx) / (Math.PI * 2) + 1) % 1;
        double diff = Math.Abs(((_beam - toShip) + 1.5) % 1.0 - 0.5);

        // 差が縮んでから広がりに転じた瞬間＝いちばん近づいた瞬間。
        // クールダウンが無いと、1 回の掃きで条件が何度も成立して鳴りっぱなしになる。
        if (_autoBell && _bellCooldown <= 0 && diff > _lastBeamToShip && _lastBeamToShip < 0.02)
        {
            _bellCooldown = 0.5;
            // 遠い船ほど低い鐘にする（速度で音程が変わることの確認も兼ねる）。
            FireBell(1.15 - 0.35 * Distance);
        }
        _lastBeamToShip = diff;
    }

    private void FireHorn()
    {
        double pan = Pan;
        double vol = Math.Clamp(1.0 - Distance * 0.85, 0.08, 1.0);
        // ドップラー：こちらへ寄ってくるなら速く、離れていくなら遅く。
        double toward = (0.5 - _shipX) * _shipDir > 0 ? 1 : -1;
        double speed = Math.Clamp(1.0 + toward * 0.06 * (_autoSail ? 1 : 0), 0.5, 2.0);

        _lastHornPan = pan; _lastHornVol = vol; _lastHornSpeed = speed;
        _hornCount++;
        _soundJobs.Enqueue(() =>
        {
            if (_horn is not { Enable: true }) return;
            // 順番が大事。Play より先に設定しておかないと、1 回目だけ前の設定で鳴る。
            _horn.Volume = vol;
            _horn.Pan = pan;
            _horn.Speed = speed;
            _horn.Play();
        });
    }

    private void FireBell(double speed)
    {
        double pan = Pan * 0.5;
        _bellCount++;
        _soundJobs.Enqueue(() =>
        {
            if (_bell is not { Enable: true }) return;
            _bell.Volume = 0.7;
            _bell.Pan = pan;
            _bell.Speed = Math.Clamp(speed, 0.5, 2.0);
            _bell.Play();
        });
    }

    // 船の位置から決まる値。
    private double ShipScreenX => 60 + _shipX * (PanelX - 120);
    /// <summary>聴き手（画面中央の岸）から見た左右。-1 が左、+1 が右。</summary>
    private double Pan => Math.Clamp((_shipX - 0.5) * 2.0, -1, 1);
    /// <summary>0 が目の前、1 がいちばん遠い。</summary>
    private double Distance => Math.Abs(_shipX - 0.5) * 2.0;

    public override void Draw()
    {
        // ★ 音を触るのはここだけ。Draw は必ずメインスレッドで走る。
        PumpSound();

        DrawSky();
        DrawSea();
        DrawLighthouse();
        DrawShip();
        DrawHeader();
        DrawPanel();
    }

    /// <summary>積まれた音の指示を掃き、BGM のストリームを 1 フレーム分進める。</summary>
    private void PumpSound()
    {
        while (_soundJobs.TryDequeue(out var job))
        {
            try { job(); }
            catch (Exception e) { Log.Error("音の操作に失敗: " + e.Message); }
        }

        if (_bgm == null) return;

        if (_bgmWanted)
        {
            // PlayStream は「まだなら鳴らす、鳴っているなら状態を進める」入口。
            // ループの再開もこの中の Update が担っているので、Loop=true でも
            // Play() を 1 回呼んだだけでは 2 周目が来ない。毎フレーム呼ぶのが正解。
            _bgm.Loop = _loop;
            _bgm.Volume = _bgmVolume;
            _bgm.PlayStream();
        }
    }

    #region 情景

    private void DrawSky()
    {
        var sky = new Gradation(
        [
            (0.00f, new Color(6, 8, 22)),
            (0.55f, new Color(14, 22, 52)),
            (1.00f, new Color(32, 40, 74)),
        ]);
        Drawing.Gradation(0, 0, AstrumCore.Width, (int)SeaY, sky);

        for (int i = 0; i < 90; i++)
        {
            double x = (i * 811) % AstrumCore.Width;
            double y = (i * 397) % (SeaY - 30);
            double tw = 0.3 + 0.7 * Math.Abs(Math.Sin(_time * 1.5 + i * 0.83));
            Drawing.Box(x, y, 2, 2, new Color(235, 240, 255, (int)(tw * 170)));
        }

        // 月
        Drawing.Circle(1130, 78, 34, new Color(245, 240, 215));
        Drawing.Circle(1130, 78, 46, new Color(245, 240, 215, 40));
    }

    private void DrawSea()
    {
        Drawing.Box(0, SeaY, AstrumCore.Width, AstrumCore.Height - SeaY, new Color(9, 16, 34));

        // 波。BGM の再生位置に合わせてうねらせると、音と絵が同じ時計で動いていることが分かる。
        double phase = (_bgm?.Progress ?? 0) * Math.PI * 4;
        for (int row = 0; row < 18; row++)
        {
            double y = SeaY + 10 + row * row * 1.05;
            if (y > AstrumCore.Height - 30) break;
            int alpha = (int)(58 - row * 2.6);
            if (alpha < 8) alpha = 8;
            // 手前の行ほど長く、まばらに。全部の桁を埋めると帯になって波に見えない。
            for (int seg = 0; seg < 20; seg++)
            {
                double wave = Math.Sin(phase + row * 0.9 + seg * 1.7);
                if (wave < 0.15) continue;
                double x = seg * (AstrumCore.Width / 20.0) + wave * 9;
                double w = (6 + row * 2.2) * wave;
                Drawing.Box(x, y, w, 2, new Color(130, 180, 230, alpha));
            }
        }
    }

    private void DrawLighthouse()
    {
        double bx = 210, by = SeaY + 24;

        // 岩と塔
        Drawing.Polygon([(bx - 66, SeaY + 96), (bx - 30, SeaY + 30), (bx + 30, SeaY + 30), (bx + 70, SeaY + 96)],
            new Color(26, 30, 46));
        for (int i = 0; i < 6; i++)
        {
            double t = i / 6.0;
            var c = i % 2 == 0 ? new Color(226, 230, 240) : new Color(198, 74, 74);
            Drawing.Polygon(
            [
                (bx - 26 + t * 8, by + 30 - t * 130),
                (bx + 26 - t * 8, by + 30 - t * 130),
                (bx + 26 - (t + 1 / 6.0) * 8, by + 30 - (t + 1 / 6.0) * 130),
                (bx - 26 + (t + 1 / 6.0) * 8, by + 30 - (t + 1 / 6.0) * 130),
            ], c);
        }
        Drawing.Box(bx - 22, by - 118, 44, 26, new Color(250, 232, 170));
        Drawing.Box(bx - 26, by - 126, 52, 10, new Color(40, 46, 66));

        // 光の帯。灯室（塔のてっぺん）から出す。回転数（0..1）で持っているので、
        // 描くときだけラジアンへ直す。加算で重ねないと夜の海に埋もれて見えない。
        double lampY = by - 105;
        double rad = _beam * Math.PI * 2;
        const double len = 760;
        // 広がりの違う三角形を 3 枚重ねて、中心が明るい帯にする。
        // 細い線を並べる方式だと RayLib で 1 本ずつ見えて金網のようになった。
        foreach (var (spread, alpha) in new[] { (0.075, 14), (0.040, 18), (0.015, 26) })
        {
            Drawing.Polygon(
            [
                (bx, lampY),
                (bx + Math.Cos(rad - spread) * len, lampY + Math.Sin(rad - spread) * len),
                (bx + Math.Cos(rad + spread) * len, lampY + Math.Sin(rad + spread) * len),
            ], new Color(255, 240, 190, alpha), blend: BlendMode.Add);
        }
        Drawing.Circle(bx, lampY, 9, new Color(255, 245, 200, 200), blend: BlendMode.Add);
    }

    private void DrawShip()
    {
        double sx = ShipScreenX;
        double sy = SeaY + 42;
        double bob = Math.Sin(_time * 2.4) * 3;

        // 船体
        Drawing.Polygon(
        [
            (sx - 34, sy + bob), (sx + 34, sy + bob),
            (sx + 24, sy + 14 + bob), (sx - 24, sy + 14 + bob),
        ], new Color(48, 58, 86));
        Drawing.Box(sx - 12, sy - 18 + bob, 24, 18, new Color(70, 84, 118));
        Drawing.Line(sx, sy - 18 + bob, 0, -22, new Color(120, 138, 170), thickness: 2);
        // 舷灯：左が赤、右が緑。Pan の符号と見比べられる。
        Drawing.Circle(sx - 30, sy + 4 + bob, 3, new Color(230, 80, 80));
        Drawing.Circle(sx + 30, sy + 4 + bob, 3, new Color(90, 220, 120));

        // 反射
        for (int i = 1; i < 8; i++)
            Drawing.Box(sx - 20 + Math.Sin(_time * 2.0 + i * 1.2) * 6, sy + 18 + i * 5,
                40 - i * 3, 2, new Color(150, 190, 230, 60 - i * 6));

        // 聴き手の位置（画面中央の岸）と、そこから船までの距離を明示する。
        double lx = PanelX / 2.0;
        double ly = AstrumCore.Height - 74;   // 画面下端 26px はシーン切り替えのメニューバー
        Drawing.Cross(lx, ly, 10, new Color(255, 220, 140), thickness: 2);
        DemoUi.NoteFont.Draw(lx, ly + 12, "聴き手（Pan と音量の基準）", new Color(255, 220, 140), ReferencePoint.TopCenter);
        Drawing.LineZ(lx, ly, sx, sy + 14 + bob, new Color(255, 220, 140, 70));
    }

    private void DrawHeader()
    {
        Drawing.Text(20, 16, "灯台の夜 / Sound の見本帳", Color.White, edgecolor: new Color(6, 8, 20));
        DemoUi.Notes(20, 50, PanelX - 40, new Color(180, 198, 226),
            "[A/D] 船を動かす  [M] 自動航行  [Space] 霧笛  [B] 鐘  [N] 光と連動して鐘を鳴らす",
            "[P] BGM 再生/停止  [L] ループ  [Up/Down] 音量  [Left/Right] シーク  [,.] 速度");
    }

    #endregion

    #region 右の計器盤

    private void DrawPanel()
    {
        double x = PanelX, y = 14, w = AstrumCore.Width - PanelX - 14;
        double h = AstrumCore.Height - 28 - 26;   // 下端 26px はメニューバー
        Drawing.Box(x, y, w, h, new Color(12, 16, 32, 225));
        Drawing.Box(x, y, w, h, new Color(70, 96, 150), thickness: 2);

        double cy = y + 10;
        cy = DrawBgmBlock(x + 12, cy, w - 24);
        cy = DrawSeBlock(x + 12, cy + 8, w - 24, "霧笛  se_horn.wav", _horn,
            $"直近に渡した値  Pan {_lastHornPan:+0.00;-0.00; 0.00}   Volume {_lastHornVol:0.00}   Speed {_lastHornSpeed:0.00}",
            $"鳴らした回数 {_hornCount}");
        cy = DrawSeBlock(x + 12, cy + 8, w - 24, "鐘  se_bell.wav", _bell,
            $"灯台の光と連動: {(_autoBell ? "ON" : "OFF")}（遠い船ほど低い音になる）",
            $"鳴らした回数 {_bellCount}");

        DrawFooter(x + 12, cy + 10, w - 24);
    }

    private double DrawBgmBlock(double x, double y, double w)
    {
        DemoUi.NoteFont.Draw(x, y, "潮の伴奏  bgm_tide.wav", new Color(220, 232, 250));
        y += 20;

        if (_bgm == null)
        {
            DemoUi.Note(x, y, w, "未生成", new Color(235, 130, 130));
            return y + 20;
        }

        y = DrawState(x, y, w, _bgm);

        // 再生位置のバー。ループの継ぎ目で 0 に戻るのが見える。
        double p = Math.Clamp(_bgm.Progress, 0, 1);
        Drawing.Box(x, y, w, 16, new Color(30, 38, 62));
        Drawing.Box(x, y, w * p, 16, new Color(90, 180, 230));
        Drawing.Box(x, y, w, 16, new Color(90, 116, 170), thickness: 1);
        DemoUi.NoteFont.Draw(x + w / 2, y + 1, $"{_bgm.Time / 1000.0:0.00} / {_bgm.Length / 1000.0:0.00} 秒",
            Color.White, ReferencePoint.TopCenter);
        y += 22;

        y = DemoUi.Notes(x, y, w,
            _bgmWanted == _bgm.Playing ? new Color(160, 200, 170) : new Color(235, 190, 120),
            $"再生の要求: {(_bgmWanted ? "あり" : "なし")}   実際に再生中: {(_bgm.Playing ? "はい" : "いいえ")}");
        y = DemoUi.Notes(x, y, w, new Color(180, 196, 224),
            $"Loop {_loop}   Volume {_bgmVolume:0.00}   Speed {_bgmSpeed:0.00}");
        return y + 2;
    }

    private double DrawSeBlock(double x, double y, double w, string title, Sound? sound, string line1, string line2)
    {
        Drawing.Line(x, y, w, 0, new Color(60, 80, 124));
        y += 6;
        DemoUi.NoteFont.Draw(x, y, title, new Color(220, 232, 250));
        y += 20;

        if (sound == null)
        {
            DemoUi.Note(x, y, w, "未生成", new Color(235, 130, 130));
            return y + 20;
        }

        y = DrawState(x, y, w, sound);
        y = DemoUi.Notes(x, y, w, new Color(180, 196, 224), line1);
        y = DemoUi.Notes(x, y, w, new Color(140, 158, 190), line2);
        return y;
    }

    /// <summary>読み込み状態と長さ。ここが赤いときは、以降の数値を信じても意味がない。</summary>
    private static double DrawState(double x, double y, double w, Sound s)
    {
        Color c = s.IsFailed ? new Color(240, 120, 120)
                : s.Enable ? new Color(150, 210, 170)
                : new Color(230, 200, 130);
        string state = s.IsFailed ? "読み込み失敗" : s.Enable ? "使用可" : "読み込み中";
        return DemoUi.Notes(x, y, w, c, $"{state}   長さ {s.Length} ms");
    }

    private static void DrawFooter(double x, double y, double w)
    {
        Drawing.Line(x, y, w, 0, new Color(60, 80, 124));
        y += 6;
        var c = new Color(146, 164, 196);
        y = DemoUi.Notes(x, y, w, c,
            "・Pitch は Speed の別名。どちらのバックエンドも再生周波数を変えるので、速さと音程は必ず連動する。");
        y = DemoUi.Notes(x, y + 4, w, c,
            "・同じ Sound を続けて Play すると重ならず頭から鳴り直す。重ねたいときは Sound を複数持つ。");
        DemoUi.Notes(x, y + 4, w, c,
            "・Loop=true でも Play() だけでは 2 周目が来ない。毎フレーム PlayStream() を呼ぶのが正しい使い方。");
    }

    #endregion

    // --- セルフテストから中を覗くための入口 ---------------------------------

    /// <summary>3 つとも読めているか。</summary>
    public bool SoundsReady =>
        _bgm is { Enable: true } && _horn is { Enable: true } && _bell is { Enable: true };

    /// <summary>読み込みに失敗したものがあるか。</summary>
    public bool AnyFailed =>
        (_bgm?.IsFailed ?? false) || (_horn?.IsFailed ?? false) || (_bell?.IsFailed ?? false);

    public bool BgmPlaying => _bgm?.Playing ?? false;
    public double BgmProgress => _bgm?.Progress ?? 0;
    public int BgmLength => _bgm?.Length ?? 0;
    public int HornCount => _hornCount;
    public int BellCount => _bellCount;
    public double ShipPan => Pan;
}
