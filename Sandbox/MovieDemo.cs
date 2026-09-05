using System.Collections.Concurrent;

using AstrumLoom;

namespace Sandbox;

/// <summary>
/// テーマ「映写室」。
///
/// Movie（動画）の振る舞いを 2 本のフィルムで確かめるシーン。Raylib バックエンドでは
/// ffmpeg を子プロセスで回して逐次デコードしている（docs\MOVIE.md）。
///
///   本編（movie_clock.mp4）  … 音つき 6 秒。再生・停止・シーク・ループ・速度・音量の確認用
///   予告（movie_silent.mp4） … 音なし 3 秒。音声トラックが無くても時計が進むことの確認用
///
/// 素材が無い / ffmpeg が無い環境でも落ちないこと自体が確認項目なので、
/// 読めなかった場合はその旨をカードに出して、シーンとしては普通に動き続ける。
///
/// 使う素材（tools\make-sandbox-assets.ps1 で生成）:
///   movie_clock.mp4  … 640x360 30fps 6 秒、440Hz のサイン波つき
///   movie_silent.mp4 … 320x180 30fps 3 秒、音声トラック無し
/// </summary>
internal sealed class MovieDemoScene : Scene
{
    private Movie? _main;
    private Movie? _silent;

    /// <summary>
    /// 動画への指示はここに積み、Draw の先頭でまとめて実行する。
    /// SoundDemoScene と同じ理由: Update は更新スレッドで回るが、動画の再生操作は
    /// ネイティブ（テクスチャ転送・音声ストリーム）に触るのでメインスレッドでしか安全に呼べない。
    /// </summary>
    private readonly ConcurrentQueue<Action> _jobs = new();

    // 表示用に覚えておく設定値。実際の反映は _jobs 経由。
    private double _volume = 0.6;
    private double _speed = 1.0;
    private bool _loop = true;
    private bool _wantPlay = true;

    private double _time;              // シーンに入ってからの経過秒
    private int _seekCount;            // シークを何回要求したか（セルフテストの確認用）

    private const double PanelX = 872;
    private const double StageW = 640;
    private const double StageH = 360;

    public override void Enable()
    {
        base.Enable();

        // ロードは非同期。ここで待たされることはない（docs\MOVIE.md の N-1）。
        _main = new Movie("Assets/movie_clock.mp4") { Loop = true };
        _silent = new Movie("Assets/movie_silent.mp4") { Loop = true };

        _time = 0;
        _seekCount = 0;
        _wantPlay = true;
    }

    public override void Disable()
    {
        base.Disable();
        // 別のシーンへ移ったあとも音が鳴り続けないよう、必ず止めてから捨てる。
        _main?.Stop();
        _silent?.Stop();
        _main?.Dispose();
        _silent?.Dispose();
        _main = _silent = null;
        _jobs.Clear();
    }

    public override void Update()
    {
        _time += DemoUi.Delta;
        HandleKeys();
    }

    private void HandleKeys()
    {
        if (Key.Space.Push())
        {
            _wantPlay = !_wantPlay;
            _jobs.Enqueue(() =>
            {
                if (_wantPlay) { _main?.Play(); _silent?.Play(); }
                else { _main?.Stop(); _silent?.Stop(); }
            });
        }
        if (Key.L.Push())
        {
            _loop = !_loop;
            _jobs.Enqueue(() =>
            {
                if (_main != null) _main.Loop = _loop;
                if (_silent != null) _silent.Loop = _loop;
            });
        }
        if (Key.Left.Push()) Seek(-0.15);
        if (Key.Right.Push()) Seek(+0.15);

        double step = DemoUi.Delta * 0.6;
        if (Key.Up.Hold()) SetVolume(_volume + step);
        if (Key.Down.Hold()) SetVolume(_volume - step);
        if (Key.Comma.Hold()) SetSpeed(_speed - step);
        if (Key.Period.Hold()) SetSpeed(_speed + step);
    }

    /// <summary>再生位置を割合で動かす。シークは ffmpeg を開き直すので、連打しても 1 フレーム 1 回に収まるよう Draw で掃く。</summary>
    private void Seek(double delta)
    {
        _seekCount++;
        _jobs.Enqueue(() =>
        {
            if (_main is not { Enable: true } || _main.Length <= 0) return;
            _main.Progress = Math.Clamp(_main.Progress + delta, 0, 0.98);
        });
    }

    /// <summary>セルフテストから絶対位置でシークさせるための入口（0.0〜1.0）。</summary>
    public void SeekTo(double progress)
    {
        _seekCount++;
        _jobs.Enqueue(() =>
        {
            if (_main is not { Enable: true } || _main.Length <= 0) return;
            _main.Progress = Math.Clamp(progress, 0, 0.98);
        });
    }

    private void SetVolume(double v)
    {
        _volume = Math.Clamp(v, 0, 1);
        _jobs.Enqueue(() => { if (_main != null) _main.Volume = _volume; });
    }

    private void SetSpeed(double v)
    {
        _speed = Math.Clamp(v, 0.25, 2.0);
        _jobs.Enqueue(() =>
        {
            if (_main != null) _main.Speed = _speed;
            if (_silent != null) _silent.Speed = _speed;
        });
    }

    public override void Draw()
    {
        // ★ 動画を触るのはここだけ。Draw は必ずメインスレッドで走る。
        PumpMovies();

        DrawBackground();
        DrawStage();
        DrawTrailer();
        DrawHeader();
        DrawPanel();
    }

    /// <summary>積まれた指示を掃き、両方の動画を 1 フレーム分進める。</summary>
    private void PumpMovies()
    {
        while (_jobs.TryDequeue(out var job))
        {
            try { job(); }
            catch (Exception e) { Log.Error("動画の操作に失敗: " + e.Message); }
        }

        // Pump は「読み込みの完了待ち」も兼ねているので、再生していなくても毎フレーム呼ぶ。
        _main?.Pump();
        _silent?.Pump();

        // 準備ができ次第、勝手に流し始める（見本帳なので開いた瞬間から動いていてほしい）。
        if (!_wantPlay) return;
        if (_main is { Enable: true, IsPlaying: false })
        {
            _main.Volume = _volume;
            _main.Loop = _loop;
            _main.Play();
        }
        if (_silent is { Enable: true, IsPlaying: false })
        {
            _silent.Loop = _loop;
            _silent.Play();
        }
    }

    private void DrawBackground()
    {
        Drawing.Fill(new Color(10, 12, 22));
        // 映写機の光。動画の周りだけ少し明るくする。
        Drawing.Box(24, 78, StageW + 32, StageH + 32, new Color(24, 30, 52));
    }

    /// <summary>本編。読めていれば動画を、読めていなければ理由を出す。</summary>
    private void DrawStage()
    {
        double x = 40, y = 94;
        if (_main is { Enable: true })
        {
            // 動画の実サイズに関係なくステージへ収める。
            double scale = Math.Min(StageW / Math.Max(1, _main.Width), StageH / Math.Max(1, _main.Height));
            _main.Draw(x + StageW / 2, y + StageH / 2, new DrawOptions
            {
                Point = ReferencePoint.Center,
                Scale = (scale, scale),
            });
            Drawing.Box(x, y, StageW, StageH, new Color(90, 120, 180), thickness: 1);
            DrawProgressBar(x, y + StageH + 10, StageW, _main);
        }
        else
        {
            Drawing.Box(x, y, StageW, StageH, new Color(20, 24, 40, 230));
            Drawing.Box(x, y, StageW, StageH, new Color(90, 60, 60), thickness: 2);
            DemoUi.Notes(x + 16, y + 16, StageW - 32, new Color(226, 180, 180),
                _main == null ? "Movie を作れていません。"
                : _main.IsFailed ? "読み込みに失敗しました。"
                : "読み込み中…",
                "Assets\\movie_clock.mp4 が出力先に無いか、ffmpeg が入っていません。",
                "素材: tools\\make-sandbox-assets.ps1 -Force",
                "ffmpeg: winget install Gyan.FFmpeg",
                "（DxLib バックエンドでは DxLibMovie が使われます）");
        }
    }

    /// <summary>予告編（音なし）。右下に小さく出す。</summary>
    private void DrawTrailer()
    {
        double x = 40, y = 500;
        DemoUi.NoteFont.Draw(x, y - 18, "予告編（音声トラック無し / 320x180）", new Color(150, 168, 200));
        if (_silent is { Enable: true })
        {
            _silent.Draw(x, y, new DrawOptions { Point = ReferencePoint.TopLeft, Scale = (0.9, 0.9) });
            Drawing.Box(x, y, 320 * 0.9, 180 * 0.9, new Color(90, 120, 180), thickness: 1);
        }
        else
        {
            Drawing.Box(x, y, 320 * 0.9, 180 * 0.9, new Color(30, 34, 52, 230));
        }
    }

    /// <summary>再生位置のバー。つまみの位置が動くので、映像が止まっていても時計が進んでいるか分かる。</summary>
    private static void DrawProgressBar(double x, double y, double w, Movie movie)
    {
        Drawing.Box(x, y, w, 8, new Color(28, 34, 56));
        double p = Math.Clamp(movie.Progress, 0, 1);
        Drawing.Box(x, y, w * p, 8, new Color(96, 170, 240));
        Drawing.Box(x + w * p - 2, y - 3, 4, 14, new Color(220, 236, 255));
    }

    private void DrawHeader()
    {
        Drawing.Text(24, 18, "映写室 — Movie の見本帳", new Color(226, 236, 252));
        DemoUi.Note(24, 46, 820,
            "Space 再生/停止　L ループ　← → シーク　↑ ↓ 音量　, . 速度"
            + "（Raylib は ffmpeg で逐次デコード、DxLib は動画ハンドル）");
    }

    private void DrawPanel()
    {
        double w = AstrumCore.Width - PanelX - 24;
        DemoUi.Card(PanelX, 78, w, 300, "本編の状態", (x, y, cw) =>
        {
            double yy = y + 8;
            yy = DemoUi.Notes(x + 10, yy, cw - 20, new Color(198, 214, 240),
                $"Loaded  : {_main?.Loaded}",
                $"Enable  : {_main?.Enable}",
                $"IsFailed: {_main?.IsFailed}",
                $"size    : {_main?.Width}x{_main?.Height}",
                $"Length  : {_main?.Length} ms",
                $"Time    : {_main?.Time:0} ms",
                $"Progress: {_main?.Progress:0.000}",
                $"Playing : {_main?.IsPlaying}",
                $"Loop    : {_loop}",
                $"Volume  : {_volume:0.00}",
                $"Speed   : {_speed:0.00}",
                $"Seek 回数: {_seekCount}");
            _ = yy;
        });

        DemoUi.Card(PanelX, 392, w, 190, "予告編（音なし）", (x, y, cw) =>
        {
            DemoUi.Notes(x + 10, y + 8, cw - 20, new Color(198, 214, 240),
                $"Enable  : {_silent?.Enable}",
                $"size    : {_silent?.Width}x{_silent?.Height}",
                $"Length  : {_silent?.Length} ms",
                $"Time    : {_silent?.Time:0} ms",
                $"Playing : {_silent?.IsPlaying}",
                "音声が無い動画は内部の Stopwatch を時計に使う。");
        });
    }

    // ---- セルフテストから覗くための窓 -------------------------------------
    public bool MainReady => _main is { Enable: true };
    public bool MainFailed => _main?.IsFailed ?? false;
    public bool SilentReady => _silent is { Enable: true };
    public int MainWidth => _main?.Width ?? 0;
    public int MainHeight => _main?.Height ?? 0;
    public int MainLength => _main?.Length ?? 0;
    public double MainTime => _main?.Time ?? 0;
    public double MainProgress => _main?.Progress ?? 0;
    public bool MainPlaying => _main?.IsPlaying ?? false;
    public double SilentTime => _silent?.Time ?? 0;
    public int SilentLength => _silent?.Length ?? 0;
}
