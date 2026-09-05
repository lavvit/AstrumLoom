using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

using Raylib_cs;

using static AstrumLoom.LayoutUtil;
using static AstrumLoom.RayLib.RayLibGraphics;
using static Raylib_cs.Raylib;

using RColor = Raylib_cs.Color;

namespace AstrumLoom.RayLib;

/// <summary>
/// IMovie の raylib 実装。raylib 自体に動画デコード機能が無いので、ffmpeg を子プロセスとして起動し、
/// 生の RGBA フレームをパイプで受け取って 1 枚のテクスチャへ毎フレーム転送する（docs\MOVIE.md）。
///
/// スレッドの分担:
///   準備スレッド   … ffprobe で情報取得 → 音声を WAV へ抽出 → デコーダ起動
///   デコードスレッド … ffmpeg の標準出力から W*H*4 バイトずつ読んでキューへ積む
///   メインスレッド … Pump() で時計を進め、表示すべきフレームを UpdateTexture で反映する
/// ネイティブ（raylib）API を触るのは Pump()/Draw()/Dispose() の内側だけで、いずれもメインスレッド限定。
/// </summary>
public sealed class RayLibMovie : IMovie
{
    /// <summary>デコード済みフレームを何本まで先読みするか。1080p RGBA で 1 本 8MB あるので欲張らない。</summary>
    private const int QueueCapacity = 8;

    /// <summary>fps が取れなかった動画に使う既定フレームレート。</summary>
    private const double FallbackFps = 30.0;

    private readonly object _sync = new();
    private readonly ConcurrentQueue<Frame> _queue = new();
    private readonly ConcurrentBag<byte[]> _pool = [];
    private readonly CancellationTokenSource _cts = new();

    private Thread? _prepareThread;
    private Thread? _decodeThread;
    private Process? _decoder;

    private string _workDir = "";
    private RayLibSound? _audio;
    private bool _hasAudio;

    private Texture2D _native;
    private int _frameBytes;
    private double _fps = FallbackFps;

    // 再生時計。音声があれば音声の再生位置を正とし、無ければ Stopwatch を時計に使う。
    private readonly Stopwatch _clock = new();
    private double _clockOffsetMs;
    private double _timeMs;

    // デコーダが今どこから読み始めているか（シーク基準）。フレーム番号→表示時刻の変換に使う。
    private double _decodeBaseMs;
    private volatile bool _decodeDone;
    // デコーダを起動し直すたびに増やす世代番号。前の世代のデコードスレッドが後から
    // 「読み終わった」と書き込んで、シーク直後の空のキューを終端と誤認させないために使う。
    private int _decodeGeneration;

    private int _shownFrames;
    private bool _played;
    private bool _disposed;
    private bool _pendingTextureUnload;

    // -1 = 失敗, 0 = 準備中, 1 = 準備完了
    private int _state = 0;

    public RayLibMovie(string path)
    {
        Path = path ?? "";

        if (!File.Exists(Path))
        {
            Log.Warning($"Movie: not found: {Path}");
            Volatile.Write(ref _state, -1);
            return;
        }
        if (!FFmpegTool.Available)
        {
            FFmpegTool.WarnMissing();
            Volatile.Write(ref _state, -1);
            return;
        }

        _prepareThread = new Thread(Prepare)
        {
            IsBackground = true,
            Name = "AstrumLoom.Movie.Prepare",
        };
        _prepareThread.Start();
    }

    ~RayLibMovie() => Dispose();

    public string Path { get; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>尺（ミリ秒）。取れないコンテナでは 0。</summary>
    public int Length { get; private set; }

    public bool IsReady => Volatile.Read(ref _state) == 1;
    public bool IsFailed => Volatile.Read(ref _state) == -1;
    public bool Loaded => Volatile.Read(ref _state) != 0;
    public bool Enable => IsReady && !_disposed && Width > 0 && Height > 0;

    /// <summary>これまでに画面へ出したフレーム数。セルフテストから「映像が進んでいるか」を見るために公開している。</summary>
    public int ShownFrames => _shownFrames;

    private static bool IsMainThread
        => Environment.CurrentManagedThreadId == AstrumCore.MainThreadId;

    #region 準備（ffprobe → 音声抽出 → デコーダ起動）

    /// <summary>準備スレッドの本体。動画情報の取得・音声の WAV 抽出・デコーダの起動をまとめて行う。</summary>
    private void Prepare()
    {
        var watch = Stopwatch.StartNew();
        try
        {
            if (!Probe())
            {
                Volatile.Write(ref _state, -1);
                return;
            }

            string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AstrumLoom", "movie");
            SweepStaleWorkDirs(root);
            _workDir = System.IO.Path.Combine(root, Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(_workDir);

            double probeMs = watch.Elapsed.TotalMilliseconds;
            if (_hasAudio) ExtractAudio();
            double audioMs = watch.Elapsed.TotalMilliseconds;

            _frameBytes = Width * Height * 4;
            StartDecoder(0);

            Volatile.Write(ref _state, 1);
            // 準備にどれだけかかったかは体感に直結するので、内訳を残しておく。
            Log.Debug($"Movie: ready in {watch.Elapsed.TotalMilliseconds:0}ms "
                + $"(probe {probeMs:0}ms / audio {audioMs - probeMs:0}ms / "
                + $"decoder {watch.Elapsed.TotalMilliseconds - audioMs:0}ms)");
        }
        catch (Exception ex)
        {
            Log.Error($"Movie: prepare failed: {ex.Message}");
            Volatile.Write(ref _state, -1);
        }
    }

    /// <summary>ffprobe で 幅・高さ・fps・尺・音声トラックの有無を取得する。映像が無ければ false。</summary>
    private bool Probe()
    {
        string? video = FFmpegTool.RunProbe([
            "-v", "error",
            "-select_streams", "v:0",
            "-show_entries", "stream=width,height,avg_frame_rate,r_frame_rate:format=duration",
            "-of", "default=noprint_wrappers=1",
            Path,
        ]);
        if (string.IsNullOrWhiteSpace(video))
        {
            Log.Error($"Movie: ffprobe から情報を取得できませんでした: {Path}");
            return false;
        }

        var info = ParseEntries(video);
        Width = (int)(GetNumber(info, "width") ?? 0);
        Height = (int)(GetNumber(info, "height") ?? 0);
        if (Width <= 0 || Height <= 0)
        {
            Log.Error($"Movie: 映像トラックがありません: {Path}");
            return false;
        }

        double fps = ParseRational(info.GetValueOrDefault("avg_frame_rate"));
        if (fps <= 0) fps = ParseRational(info.GetValueOrDefault("r_frame_rate"));
        _fps = fps > 0 ? fps : FallbackFps;

        double? duration = GetNumber(info, "duration");
        Length = duration is > 0 ? (int)Math.Round(duration.Value * 1000.0) : 0;

        string? audio = FFmpegTool.RunProbe([
            "-v", "error",
            "-select_streams", "a:0",
            "-show_entries", "stream=codec_type",
            "-of", "default=noprint_wrappers=1",
            Path,
        ]);
        _hasAudio = audio?.Contains("audio", StringComparison.OrdinalIgnoreCase) == true;

        Log.Debug($"Movie: {System.IO.Path.GetFileName(Path)} {Width}x{Height} {_fps:0.##}fps "
            + $"{Length}ms audio={_hasAudio}");
        return true;
    }

    /// <summary>音声トラックを WAV(PCM16) へ書き出し、RayLibSound として読み込む。失敗しても映像だけで続行する。</summary>
    private void ExtractAudio()
    {
        string wav = System.IO.Path.Combine(_workDir, "audio.wav");
        var psi = FFmpegTool.StartInfo(FFmpegTool.FFmpegPath!, [
            "-hide_banner", "-loglevel", "error", "-nostdin",
            "-i", Path,
            "-vn", "-acodec", "pcm_s16le", "-ar", "44100", "-ac", "2",
            "-y", wav,
        ]);
        try
        {
            using var proc = Process.Start(psi!);
            if (proc == null) return;
            _ = proc.StandardOutput.ReadToEnd();
            string err = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            if (proc.ExitCode != 0 || !File.Exists(wav))
            {
                Log.Warning($"Movie: 音声の抽出に失敗しました（映像のみ再生します）: {err.Trim()}");
                _hasAudio = false;
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"Movie: 音声の抽出に失敗しました（映像のみ再生します）: {ex.Message}");
            _hasAudio = false;
            return;
        }

        if (_cts.IsCancellationRequested) return;

        // RayLibSound はどのスレッドから作ってもよい（ネイティブ生成はメインスレッドの Pump へ回される）。
        var sound = new RayLibSound(wav, streaming: true);
        lock (_sync)
        {
            _audio = sound;
        }
        ApplyAudioState();
    }

    /// <summary>今の音量・パン・速度・ループ設定を音声側へ反映する。</summary>
    private void ApplyAudioState()
    {
        var audio = _audio;
        if (audio == null || !audio.Enable) return;
        audio.Volume = _volume;
        audio.Pan = _pan;
        audio.Speed = _speed;
        // ループは動画側（Pump）で先頭へシークして作るので、音声側のループは使わない。
        audio.Loop = false;
        _audioStateApplied = true;
    }
    private bool _audioStateApplied;

    #endregion

    #region デコード

    /// <summary>デコード済みの 1 フレーム。Data はプールから借りたバッファで、表示後に返却する。</summary>
    private sealed class Frame
    {
        public byte[] Data = [];
        public int Index;
    }

    /// <summary>
    /// 指定位置から ffmpeg を起動し直し、生 RGBA フレームを読むスレッドを立てる。
    /// 可変フレームレートの動画も fps フィルタで CFR に均してから受け取るので、フレーム番号だけで表示時刻が決まる。
    /// </summary>
    private void StartDecoder(double startMs)
    {
        StopDecoder();

        _decodeBaseMs = Math.Max(0, startMs);
        _decodeDone = false;
        int generation = Interlocked.Increment(ref _decodeGeneration);

        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin" };
        if (_decodeBaseMs > 0)
        {
            // 入力側 -ss はキーフレーム単位の高速シーク。ミリ秒の厳密さより開始の速さを優先する。
            args.Add("-ss");
            args.Add((_decodeBaseMs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture));
        }
        args.AddRange([
            "-i", Path,
            "-an",
            "-vf", $"fps={_fps.ToString("0.######", CultureInfo.InvariantCulture)},format=rgba",
            "-f", "rawvideo", "-pix_fmt", "rgba",
            "-",
        ]);

        var psi = FFmpegTool.StartInfo(FFmpegTool.FFmpegPath!, args);
        var proc = Process.Start(psi!) ?? throw new InvalidOperationException("ffmpeg を起動できませんでした。");

        lock (_sync)
        {
            _decoder = proc;
        }

        // 標準エラーを読み捨てないとパイプが詰まって ffmpeg が止まる。
        var drain = new Thread(() =>
        {
            try
            {
                string err = proc.StandardError.ReadToEnd();
                if (!string.IsNullOrWhiteSpace(err) && !_cts.IsCancellationRequested)
                    Log.Debug($"Movie: ffmpeg: {err.Trim()}");
            }
            catch { }
        })
        { IsBackground = true, Name = "AstrumLoom.Movie.Stderr" };
        drain.Start();

        _decodeThread = new Thread(() => DecodeLoop(proc, generation))
        {
            IsBackground = true,
            Name = "AstrumLoom.Movie.Decode",
        };
        _decodeThread.Start();
    }

    /// <summary>ffmpeg の標準出力から 1 フレーム分ちょうどを読み続け、キューへ積む。キューが埋まっている間は待つ。</summary>
    private void DecodeLoop(Process proc, int generation)
    {
        var token = _cts.Token;
        int index = 0;
        var stream = proc.StandardOutput.BaseStream;

        try
        {
            while (!token.IsCancellationRequested)
            {
                // 先読みしすぎないよう、キューが満ちている間は読まない（ffmpeg 側もパイプで自然に止まる）。
                while (_queue.Count >= QueueCapacity && !token.IsCancellationRequested)
                {
                    Thread.Sleep(2);
                }
                if (token.IsCancellationRequested) break;

                if (!_pool.TryTake(out byte[]? buffer) || buffer.Length != _frameBytes)
                    buffer = new byte[_frameBytes];

                if (!ReadExactly(stream, buffer, _frameBytes)) break; // 末尾まで読んだ
                _queue.Enqueue(new Frame { Data = buffer, Index = index++ });
            }
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested) Log.Debug($"Movie: decode stopped: {ex.Message}");
        }
        finally
        {
            // 自分が最新の世代のときだけ終端を報告する。古い世代の後始末が
            // シーク直後の再生を終端と誤認させると、そのままループ＝先頭へ巻き戻ってしまう。
            if (Volatile.Read(ref _decodeGeneration) == generation) _decodeDone = true;
        }
    }

    /// <summary>ストリームから count バイトちょうど読む。途中で終端に達したら false（＝最後の半端なフレームは捨てる）。</summary>
    private static bool ReadExactly(Stream stream, byte[] buffer, int count)
    {
        int read = 0;
        while (read < count)
        {
            int n = stream.Read(buffer, read, count - read);
            if (n <= 0) return false;
            read += n;
        }
        return true;
    }

    /// <summary>デコーダのプロセスとスレッドを止め、キューを空にする。シークとループのやり直しで使う。</summary>
    private void StopDecoder()
    {
        Process? proc;
        Thread? thread;
        lock (_sync)
        {
            proc = _decoder;
            thread = _decodeThread;
            _decoder = null;
            _decodeThread = null;
        }

        if (proc != null)
        {
            try { if (!proc.HasExited) proc.Kill(true); } catch { }
            try { proc.WaitForExit(1000); } catch { }
            try { proc.Dispose(); } catch { }
        }
        // デコードスレッドは Read で止まっているので、プロセスを殺した後に短く待てば必ず抜ける。
        if (thread != null && thread != Thread.CurrentThread)
        {
            try { thread.Join(1000); } catch { }
        }

        while (_queue.TryDequeue(out var frame)) Recycle(frame);
    }

    /// <summary>使い終わったフレームのバッファをプールへ返す（1080p で 1 本 8MB あり、毎フレーム確保すると GC が持たない）。</summary>
    private void Recycle(Frame frame)
    {
        if (frame.Data.Length == _frameBytes && _pool.Count < QueueCapacity * 2)
            _pool.Add(frame.Data);
    }

    /// <summary>フレーム番号を表示時刻（ミリ秒）へ直す。シークした場合はその位置が起点になる。</summary>
    private double FrameTime(int index) => _decodeBaseMs + index * 1000.0 / _fps;

    #endregion

    #region 更新

    /// <summary>
    /// 毎フレームメインスレッドから呼ぶ。テクスチャの生成、音声の更新、再生時計の前進、
    /// デコード済みフレームの取り込み、終端／ループ処理を行う。
    /// </summary>
    public void Pump()
    {
        if (_disposed) return;
        if (!IsMainThread) return; // raylib API はメインスレッド専用

        if (_pendingTextureUnload)
        {
            UnloadNative();
            return;
        }
        if (!IsReady) return;

        EnsureTexture();
        UpdateAudio();
        AdvanceClock();
        PullFrames();
        CheckEnd();
    }

    /// <summary>幅・高さが分かった時点で、書き換え用のテクスチャを 1 枚だけ作る。</summary>
    private void EnsureTexture()
    {
        if (_native.Id != 0 || Width <= 0 || Height <= 0) return;
        var img = GenImageColor(Width, Height, RColor.Blank);
        _native = LoadTextureFromImage(img);
        UnloadImage(img);
    }

    /// <summary>音声を毎フレーム更新する。抽出直後は Enable になった時点で音量等の設定を流し込む。</summary>
    private void UpdateAudio()
    {
        var audio = _audio;
        if (audio == null) return;

        audio.Pump();
        if (!_audioStateApplied && audio.Enable) ApplyAudioState();
        if (_played) audio.Update();
    }

    /// <summary>再生時刻を進める。音声が鳴っていればその再生位置を正とし、無ければ Stopwatch を時計に使う。</summary>
    private void AdvanceClock()
    {
        if (!_played) return;

        var audio = _audio;
        if (audio != null && audio.Enable && audio.IsPlaying)
        {
            _timeMs = audio.Time;
            // 音声が時計になったら Stopwatch 側の基準も合わせておく（音声が終わった後に飛ばないように）。
            _clockOffsetMs = _timeMs;
            _clock.Restart();
            return;
        }

        if (_clock.IsRunning)
        {
            _timeMs = _clockOffsetMs + _clock.Elapsed.TotalMilliseconds * _speed;
        }
    }

    /// <summary>
    /// 表示時刻が来ているフレームをキューから取り出し、最後の 1 枚だけをテクスチャへ転送する。
    /// 描画が間に合っていないときは途中のフレームを捨てる（音と時計に合わせるためのコマ落ち）。
    /// </summary>
    private void PullFrames()
    {
        if (_native.Id == 0) return;

        double half = 500.0 / _fps;
        Frame? latest = null;

        while (_queue.TryPeek(out var head))
        {
            // まだ 1 枚も出していないときは、時刻に関係なく先頭を出して静止画として見せる。
            bool due = _shownFrames == 0 || FrameTime(head.Index) <= _timeMs + half;
            if (!due) break;
            if (!_queue.TryDequeue(out var frame)) break;

            if (latest != null) Recycle(latest);
            latest = frame;
            if (_shownFrames == 0) break;
        }

        if (latest == null) return;

        UpdateTexture<byte>(_native, latest.Data);
        _shownFrames++;
        Recycle(latest);
    }

    /// <summary>終端に達したかを判定し、Loop なら先頭へ戻し、そうでなければ停止する。</summary>
    private void CheckEnd()
    {
        if (!_played) return;

        // デコードが最後まで終わっていて、積んだフレームも出し切ったら終端。
        // シーク直後はキューが空でデコーダも動き出したばかりなので、新しい位置の絵を
        // 1 枚でも出すまでは終端と見なさない。
        bool drained = _decodeDone && _queue.IsEmpty && _shownFrames > 0;
        bool overrun = Length > 0 && _timeMs >= Length - 1;
        if (!drained && !overrun) return;

        if (Loop)
        {
            SeekTo(0, restartAudio: true);
        }
        else
        {
            Stop();
        }
    }

    #endregion

    #region 再生操作

    private double _volume = 1.0;
    private double _pan;
    private double _speed = 1.0;

    /// <summary>再生位置（ミリ秒）。代入するとシークする。</summary>
    public double Time
    {
        get => _timeMs;
        set => SeekTo(value, restartAudio: false);
    }

    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0.0, 1.0);
            var audio = _audio;
            if (audio != null && audio.Enable) audio.Volume = _volume;
        }
    }

    public double Pan
    {
        get => _pan;
        set
        {
            _pan = Math.Clamp(value, -1.0, 1.0);
            var audio = _audio;
            if (audio != null && audio.Enable) audio.Pan = _pan;
        }
    }

    /// <summary>raylib 側は速度とピッチが同じ操作なので、DxLib 実装と同様に Speed と同一視する。</summary>
    public double Pitch
    {
        get => Speed;
        set => Speed = value;
    }

    public double Speed
    {
        get => _speed;
        set
        {
            _speed = value <= 0 ? 1.0 : value;
            // Stopwatch 側の基準を今の時刻で取り直さないと、速度変更前の経過時間まで新しい倍率で伸び縮みする。
            _clockOffsetMs = _timeMs;
            if (_clock.IsRunning) _clock.Restart();
            var audio = _audio;
            if (audio != null && audio.Enable) audio.Speed = _speed;
        }
    }

    public bool IsPlaying => _played;
    public bool Loop { get; set; }

    /// <summary>先頭から再生を開始する。準備が済んでいなければ何もしない。</summary>
    public void Play()
    {
        if (!Enable) return;
        if (_played) return;

        _played = true;
        _clockOffsetMs = _timeMs;
        _clock.Restart();

        var audio = _audio;
        if (audio != null && audio.Enable)
        {
            audio.Play();
            if (_timeMs > 0) audio.Time = _timeMs;
        }
    }

    /// <summary>再生を止める。表示中のフレームはそのまま残す（静止画として見える）。</summary>
    public void Stop()
    {
        if (!_played) return;
        _played = false;
        if (_clock.IsRunning) _clock.Stop();
        _clockOffsetMs = _timeMs;
        _audio?.Stop();
    }

    /// <summary>未再生なら再生を始め、再生中なら何もしない（毎フレーム呼ぶ用途）。</summary>
    public void PlayStream()
    {
        if (!Enable) return;
        if (!_played) Play();
    }

    /// <summary>指定ミリ秒へシークする。ffmpeg を開き直すので、連続で呼ぶ用途には向かない。</summary>
    private void SeekTo(double targetMs, bool restartAudio)
    {
        if (!Enable) return;
        if (double.IsNaN(targetMs) || double.IsInfinity(targetMs)) return;

        double clamped = Math.Clamp(targetMs, 0, Length > 0 ? Length : targetMs);
        _timeMs = clamped;
        _clockOffsetMs = clamped;
        if (_played) _clock.Restart();
        else if (_clock.IsRunning) _clock.Reset();

        try
        {
            StartDecoder(clamped);
        }
        catch (Exception ex)
        {
            Log.Error($"Movie: seek failed: {ex.Message}");
            Volatile.Write(ref _state, -1);
            return;
        }
        // シーク直後は「まだ 1 枚も出していない」扱いに戻し、新しい位置の絵を即座に出す。
        _shownFrames = 0;

        var audio = _audio;
        if (audio != null && audio.Enable)
        {
            if (restartAudio && _played)
            {
                audio.Stop();
                audio.Play();
            }
            audio.Time = clamped;
        }
    }

    #endregion

    #region 描画

    /// <summary>
    /// DrawOptions（切り出し矩形・基準点・拡縮・回転・反転・色・不透明度・ブレンド）を反映して描画する。
    /// 変換は RayLibTexture.Draw と同じ。動画は RenderTexture 由来ではないので UV の上下反転補正は要らない。
    /// </summary>
    public void Draw(double x, double y, DrawOptions option)
    {
        if (!Enable || _native.Id == 0) return;

        var use = option;
        SetOptions(use);

        (double width, double height) = use.Rectangle.HasValue
            ? (use.Rectangle.Value.Width, use.Rectangle.Value.Height)
            : (Width, Height);

        var point = use.Position ?? (GetAnchorOffset(use.Point, width, height) * -1);
        double opacity = Math.Clamp(use.Opacity, 0.0, 1.0);
        var color = use.Color ?? Color.White;
        float defscale = (float)Drawing.DefaultScale;
        float fx = (float)(x * defscale);
        float fy = (float)(y * defscale);
        (double w, double h) = use.Scale;
        double angle = use.Angle;
        int tx = use.Flip.X ? -1 : 1;
        int ty = use.Flip.Y ? -1 : 1;

        var origin = new System.Numerics.Vector2(
            (float)(point.X * Math.Abs(w)),
            (float)(point.Y * Math.Abs(h)));

        var rect = use.Rectangle ?? new(0, 0, Width, Height);
        var srcRect = new Rectangle(
            (float)rect.X, (float)rect.Y,
            (float)rect.Width * tx,
            (float)rect.Height * ty);

        var dstRect = new Rectangle(fx, fy,
            (float)(rect.Width * Math.Abs(w)),
            (float)(rect.Height * Math.Abs(h)));

        DrawTexturePro(_native, srcRect, dstRect, origin,
            360 * (float)angle, ToRayColor(color, opacity));

        ResetOptions(use);
    }

    #endregion

    #region 破棄

    /// <summary>
    /// 子プロセス・スレッド・音声・一時ディレクトリを片付ける。テクスチャの解放はメインスレッドでしかできないので、
    /// 別スレッドから呼ばれた場合は AstrumCore へ回して次のメインスレッド処理で解放する。
    /// </summary>
    public void Dispose()
    {
        // メインスレッド以外からの Dispose でテクスチャ解放だけ持ち越している場合、
        // AstrumCore が改めてメインスレッドで呼び直してくれるので、ここで解放だけ済ませる。
        if (_disposed)
        {
            if (_pendingTextureUnload) UnloadNative();
            return;
        }
        _disposed = true;

        try { _cts.Cancel(); } catch { }
        StopDecoder();
        try { _prepareThread?.Join(2000); } catch { }

        _audio?.Dispose();
        _audio = null;

        UnloadNative();

        // 音声の WAV は raylib 側の Music が掴んでおり、その解放はメインスレッドまで
        // 遅延することがある。ここで消せるとは限らないので、消えるまで裏で粘る。
        CleanupWorkDir(_workDir);

        try { _cts.Dispose(); } catch { }
        _pool.Clear();
        GC.SuppressFinalize(this);
    }


    /// <summary>
    /// 作業フォルダを削除する。音声 WAV の解放がメインスレッド待ちになっていることがあるので、
    /// すぐに消えなくても諦めず、裏で数秒ぶんリトライする。
    /// </summary>
    private static void CleanupWorkDir(string dir)
    {
        if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir)) return;

        var thread = new Thread(() =>
        {
            for (int i = 0; i < 40; i++)
            {
                try
                {
                    if (!System.IO.Directory.Exists(dir)) return;
                    System.IO.Directory.Delete(dir, true);
                    return;
                }
                catch { Thread.Sleep(250); }
            }
            Log.Debug($"Movie: 一時フォルダを消せませんでした: {dir}");
        })
        { IsBackground = true, Name = "AstrumLoom.Movie.Cleanup" };
        thread.Start();
    }

    /// <summary>
    /// 過去の実行が残した作業フォルダを掃除する。強制終了でプロセスが落ちると Dispose が走らず
    /// フレーム・音声が temp に残るため、次に動画を開いたときについでに片付ける。
    /// 同時に動いている別インスタンスの作業中フォルダを消さないよう、10 分以上前のものだけを対象にする
    /// （再生中の音声 WAV は掴まれていて消せないので、消し損ねても実害は無い）。
    /// </summary>
    private static void SweepStaleWorkDirs(string root)
    {
        try
        {
            if (!System.IO.Directory.Exists(root)) return;
            var limit = DateTime.Now - TimeSpan.FromMinutes(10);
            foreach (string dir in System.IO.Directory.GetDirectories(root))
            {
                try
                {
                    if (System.IO.Directory.GetLastWriteTime(dir) > limit) continue;
                    System.IO.Directory.Delete(dir, true);
                }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>テクスチャのネイティブ解放。メインスレッド以外なら AstrumCore へ委譲する。</summary>
    private void UnloadNative()
    {
        if (_native.Id == 0) { _pendingTextureUnload = false; return; }

        if (!IsWindowReady())
        {
            _native = default;
            _pendingTextureUnload = false;
            return;
        }
        if (!IsMainThread)
        {
            _pendingTextureUnload = true;
            AstrumCore.RequestDispose(this);
            return;
        }

        try { UnloadTexture(_native); }
        catch { Log.Error($"Failed to unload movie texture: {Path}"); }
        _native = default;
        _pendingTextureUnload = false;
    }

    #endregion

    #region ffprobe 出力の解析

    /// <summary>ffprobe の `key=value` 形式の出力を辞書へ直す。</summary>
    private static Dictionary<string, string> ParseEntries(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim();
            if (value is "N/A" or "") continue;
            map[key] = value;
        }
        return map;
    }

    private static double? GetNumber(Dictionary<string, string> map, string key)
        => map.TryGetValue(key, out string? v)
            && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
            ? d : null;

    /// <summary>"30000/1001" のような有理数表記を double へ直す。"0/0" や壊れた値は 0 を返す。</summary>
    private static double ParseRational(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        int slash = text.IndexOf('/');
        if (slash < 0)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double single)
                ? single : 0;
        }
        if (!double.TryParse(text[..slash], NumberStyles.Float, CultureInfo.InvariantCulture, out double num)
            || !double.TryParse(text[(slash + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out double den)
            || den <= 0)
        {
            return 0;
        }
        return num / den;
    }

    #endregion
}
