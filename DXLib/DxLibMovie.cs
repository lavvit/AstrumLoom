using static AstrumLoom.DXLib.DxLibGraphics;
using static AstrumLoom.LayoutUtil;
using static DxLibDLL.DX;

namespace AstrumLoom.DXLib;

/// <summary>
/// DxLibバックエンドでの動画実装。DxLibは動画も「グラフィックハンドル」1つで音声・映像両方を扱うため、
/// ISoundとITextureを両方満たすIMovieをこのクラス単体で実装している（AsyncLoadableBaseは使わず、
/// 独自の_asyncStateで簡易的な非同期ロード管理を行う）。
/// </summary>
internal sealed class DxLibMovie : IMovie
{
    public string Path { get; private set; } = "";
    public int Handle { get; private set; } = -1;
    public int Width { get; private set; } = 0;
    public int Height { get; private set; } = 0;

    /// <summary>再生時間(ミリ秒)。取れない場合は 0。</summary>
    public int Length { get; private set; } = 0;

    public DxLibMovie(string path)
    {
        Path = path;
        Load();
    }

    /// <summary>動画ハンドルを解放する。メインスレッド以外から呼ばれた場合はAstrumCore.RequestDisposeへ回し、実際の破棄は次のメインスレッド処理まで遅延する。</summary>
    public void Dispose()
    {
        if (!IsMainThread)
        {
            AstrumCore.RequestDispose(this);
            return;
        }
        if (Handle > 0)
        {
            DeleteGraph(Handle);
        }
        Handle = -1;
        Volatile.Write(ref _asyncState, -1);
    }

    #region 読み込み

    /// <summary>
    /// LoadGraphで動画をロードする。呼び出しがメインスレッドでない場合はネイティブAPIを叩かず
    /// _deferred=trueにして即returnし、実際のロードは次にPumpがメインスレッドから呼ばれたときに行う。
    /// </summary>
    public void Load()
    {
        if (!File.Exists(Path))
        {
            Log.Debug($"Movie: not found: {Path}");
            Volatile.Write(ref _asyncState, -1);
            Handle = -1;
            return;
        }

        // メインスレッドでのみ DxLib を触る
        if (!IsMainThread)
        {
            _deferred = true;
            Volatile.Write(ref _asyncState, 0); // Loading
            return;
        }

        int handle = LoadGraph(Path);
        if (handle < 0)
        {
            Log.Debug($"Movie: Load failed: {Path}");
            Volatile.Write(ref _asyncState, -1);
            Handle = -1;
            return;
        }

        Handle = handle;
        _startTicks = Environment.TickCount64;

        if (GetGraphSize(Handle, out int w, out int h) != 0)
        {
            w = h = 0;
        }
        Width = w;
        Height = h;

        // 長さが取れるなら計算しておく（取れないフォーマットもある）:contentReference[oaicite:1]{index=1}
        int totalFrames = GetMovieTotalFrameToGraph(Handle);
        long frameTimeUs = GetOneFrameTimeMovieToGraph(Handle);
        if (totalFrames > 0 && frameTimeUs > 0)
        {
            // µ秒 → ms
            Length = (int)(totalFrames * frameTimeUs / 1000L);
        }

        Volatile.Write(ref _asyncState,
            (CheckHandleASyncLoad(Handle) == 0) ? 1 : 0);
    }

    // 0 = Loading, 1 = Ready, -1 = Failed
    private int _asyncState = -1;
    public bool IsReady => Volatile.Read(ref _asyncState) == 1;
    public bool IsFailed => Volatile.Read(ref _asyncState) == -1;
    public bool Loaded
    {
        get
        {
            Pump(); // 念のため
            return Volatile.Read(ref _asyncState) != 0;
        }
    }

    public bool Enable => Loaded && Handle > 0;

    private static bool IsMainThread
        => Environment.CurrentManagedThreadId == AstrumCore.MainThreadId;

    private bool _deferred;
    private long _startTicks;
    private const int DefaultTimeoutMs = 15000;
    public int TimeoutMs { get; set; } = DefaultTimeoutMs;

    /// <summary>メインスレッドから毎フレーム呼ぶ想定の更新処理。保留中ロードの開始、非同期ロード完了待ち、タイムアウト検知、ループ再生の再始動を行う。</summary>
    public void Pump()
    {
        if (!IsMainThread) return;

        if (Handle > 0 && Width + Height == 0)
        {
            if (GetGraphSize(Handle, out int w, out int h) != 0)
            {
                w = h = 0;
            }
            Width = w;
            Height = h;
        }

        // 保留中ならメインスレッドでロード開始
        if (_deferred)
        {
            _deferred = false;
            Load();
            return;
        }

        // 非同期ロードの完了待ち
        if (Handle > 0 && Volatile.Read(ref _asyncState) == 0)
        {
            if (CheckHandleASyncLoad(Handle) == 0)
            {
                Volatile.Write(ref _asyncState, 1); // Ready
                return;
            }

            long elapsed = Environment.TickCount64 - _startTicks;
            if (TimeoutMs > 0 && elapsed >= TimeoutMs)
            {
                Log.Debug($"Movie: Load timeout: {Path}");
                Dispose();
                return;
            }
        }

        // ループ処理（再生が止まっていて、以前は再生中だった & Loop=true）
        if (Handle > 0 && Loop && IsReady)
        {
            int state = GetMovieStateToGraph(Handle); // 0:停止 / 1:再生中 / -1:エラー:contentReference[oaicite:2]{index=2}
            if (state == 0 && _wasPlaying)
            {
                SeekMovieToGraph(Handle, 0);
                PlayMovieToGraph(Handle);
            }
            _wasPlaying = state == 1;
        }
    }

    private bool _wasPlaying;

    #endregion

    #region 再生プロパティ

    /// <summary>再生位置(ミリ秒)。setはLengthが分かっている場合のみ範囲外シークを防ぐためclampする。</summary>
    public double Time
    {
        get
        {
            if (Handle <= 0 || !IsReady) return 0;
            int t = TellMovieToGraph(Handle); // ms:contentReference[oaicite:3]{index=3}
            return t < 0 ? 0 : t;
        }
        set
        {
            if (Handle <= 0 || !IsReady) return;
            int ms = (int)Math.Max(0, value);
            // 範囲外を指定するとフリーズする事例があるので注意（Length がわかるなら clamp 推奨）:contentReference[oaicite:4]{index=4}
            if (Length > 0 && ms > Length) ms = Length;
            SeekMovieToGraph(Handle, ms);
        }
    }

    // 0.0〜1.0 の正規化値を、SetMovieVolumeToGraphが要求する0〜10000スケールへ変換する。
    public double Volume
    {
        get;
        set
        {
            field = Math.Clamp(value, 0.0, 1.0);
            if (Handle <= 0) return;
            int vol = (int)(field * 10000.0); // 0〜10000:contentReference[oaicite:5]{index=5}
            SetMovieVolumeToGraph(vol, Handle);
        }
    } = 1.0;

    public bool IsPlaying
        => Handle > 0 && GetMovieStateToGraph(Handle) == 1;

    public double Pan
    {
        get => 0.0;
        set { /* DxLib では未対応 */ }
    }
    public double Pitch
    {
        get => Speed;
        set => Speed = value;
    }

    public double Speed
    {
        get;
        set
        {
            if (Handle <= 0) return;
            field = Math.Max(0.0, value);
            SetPlaySpeedRateMovieToGraph(Handle, field);
        }
    } = 1.0;

    public bool Loop { get; set; }

    private bool _played = false;
    public void Play()
    {
        if (Handle <= 0 || !Loaded) return;
        PlayMovieToGraph(Handle);
        _played = true;
    }

    public void Stop()
    {
        if (Handle <= 0) return;
        PauseMovieToGraph(Handle);
        //SeekMovieToGraph(Handle, 0);
        _played = false;
    }

    public void PlayStream()
    {
        if (!Enable) return;
        if (_played)
        {
            return;
        }
        Play();
    }

    #endregion

    #region 描画

    /// <summary>DrawOptionsをDxLibの回転描画API(DrawRotaGraph3F/DrawRectRotaGraph3F)の引数へ変換して描画する。DxLibTexture.Drawと同様の変換ロジック。</summary>
    public void Draw(double x, double y, DrawOptions option)
    {
        if (!Enable) return;

        var use = option;
        SetOptions(use);

        (double width, double height) = use.Rectangle.HasValue
            ? (use.Rectangle.Value.Width, use.Rectangle.Value.Height)
            : (Width, Height);

        var point = use.Position ?? Point(use.Point, use.Rectangle);
        point = new(Math.Abs(point.X), Math.Abs(point.Y));

        float defscale = (float)Drawing.DefaultScale;
        float fx = (float)(x * defscale);
        float fy = (float)(y * defscale);

        (double w, double h) = use.Scale;
        w *= defscale;
        h *= defscale;

        double angle = use.Angle * 2 * Math.PI;
        (int tx, int ty) = (use.Flip.X ? 1 : 0, use.Flip.Y ? 1 : 0);

        if (use.Rectangle.HasValue)
        {
            var rect = use.Rectangle.Value;
            DrawRectRotaGraph3F(
                fx, fy,
                (int)rect.X, (int)rect.Y,
                (int)rect.Width, (int)rect.Height,
                (float)point.X, (float)point.Y,
                (float)w, (float)h,
                angle, Handle, TRUE, tx, ty);
        }
        else
        {
            DrawRotaGraph3F(
                fx, fy,
                (float)point.X, (float)point.Y,
                (float)w, (float)h,
                angle, Handle, TRUE, tx, ty);
        }

        ResetOptions(use);
    }

    /// <summary>ReferencePoint（基準点の種類）を、動画左上原点からの座標オフセットへ変換する。DxLibの回転中心APIに渡すための下準備。</summary>
    private Point Point(ReferencePoint point, Rect? rectangle = null)
    {
        if (!rectangle.HasValue) rectangle = new(0, 0, Width, Height);
        return point switch
        {
            ReferencePoint.TopCenter => new(rectangle.Value.Width / 2, 0),
            ReferencePoint.TopRight => new(rectangle.Value.Width, 0),
            ReferencePoint.CenterLeft => new(0, rectangle.Value.Height / 2),
            ReferencePoint.Center => new(rectangle.Value.Width / 2, rectangle.Value.Height / 2),
            ReferencePoint.CenterRight => new(rectangle.Value.Width, rectangle.Value.Height / 2),
            ReferencePoint.BottomLeft => new(0, rectangle.Value.Height),
            ReferencePoint.BottomCenter => new(rectangle.Value.Width / 2, rectangle.Value.Height),
            ReferencePoint.BottomRight => new(rectangle.Value.Width, rectangle.Value.Height),
            _ => new(0, 0),
        };
    }

    #endregion
}
