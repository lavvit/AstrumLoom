using System.Diagnostics;

using FFMpegCore;

namespace AstrumLoom.Extend;

/// <summary>
/// FFmpeg（FFMpegCore）を使って動画ファイルを連番PNGフレーム＋WAV音声に事前展開し、
/// IMovie として再生・描画できるようにするクラス。
/// 音声トラックがあれば <see cref="SoundExtend"/> の再生時刻を基準にフレームを選ぶが、
/// 音声が無い動画では内部の Stopwatch を時計代わりに使う。
/// </summary>
internal sealed class MovieExtend : IMovie, IDisposable
{
    private const int TextureCacheLimit = 6;
    private readonly string _workDir = string.Empty;
    private readonly string _framesDir = string.Empty;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _stateLock = new();
    private readonly object _cacheLock = new();
    private readonly Dictionary<int, ITexture> _textureCache = [];
    private readonly LinkedList<int> _recentFrames = new();

    private Task? _prepareTask;
    private SoundExtend? _sound;
    private string? _audioPath;
    private IReadOnlyList<string> _frameFiles = Array.Empty<string>();
    private double _frameDurationMs = 33.34;
    private double _timeMs;
    private double _volume = 1.0;
    private double _pan;
    private double _pitch = 1.0;
    private double _speed = 1.0;
    private Stopwatch _clock = new();
    private double _clockOffset;
    private volatile int _asyncState = -1; // -1=failed,0=loading,1=ready
    private bool _disposed;

    /// <summary>
    /// 動画ファイルのパスを受け取り、作業用一時ディレクトリを用意してフレーム抽出を非同期で開始します。
    /// ファイルが存在しない場合は即座に失敗状態になります。
    /// </summary>
    public MovieExtend(string path)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        if (!File.Exists(Path))
        {
            Log.Warning($"MovieExtend: file not found: {Path}");
            _asyncState = -1;
            return;
        }

        string baseDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AstrumLoom", "movie");
        Directory.CreateDirectory(baseDir);
        _workDir = System.IO.Path.Combine(baseDir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
        _framesDir = System.IO.Path.Combine(_workDir, "frames");
        Directory.CreateDirectory(_framesDir);

        _asyncState = 0;
        _prepareTask = PrepareAsync(_cts.Token);
    }

    public string Path { get; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Length { get; private set; }
    public DrawOptions? Option { get; set; }

    public bool IsReady => Volatile.Read(ref _asyncState) == 1;
    public bool IsFailed => Volatile.Read(ref _asyncState) == -1;
    public bool Loaded => Volatile.Read(ref _asyncState) != 0;
    public bool Enable => IsReady && _frameFiles.Count > 0;

    public double Time
    {
        get => Volatile.Read(ref _timeMs);
        set => Seek(value);
    }

    public double Volume
    {
        get => _sound?.Volume ?? _volume;
        set
        {
            _volume = Math.Clamp(value, 0.0, 1.0);
            _sound?.Volume = _volume;
        }
    }

    public double Pan
    {
        get => _sound?.Pan ?? _pan;
        set
        {
            _pan = Math.Clamp(value, -1.0, 1.0);
            _sound?.Pan = _pan;
        }
    }

    public double Pitch
    {
        get => _sound?.Pitch ?? _pitch;
        set
        {
            _pitch = value;
            _sound?.Pitch = _pitch;
        }
    }

    public double Speed
    {
        get => _sound?.Speed ?? _speed;
        set
        {
            _speed = value <= 0 ? 1.0 : value;
            _sound?.Speed = _speed;
        }
    }

    public bool IsPlaying { get; private set; }

    public bool Loop
    {
        get; set
        {
            field = value;
            _sound?.Loop = value;
        }
    }

    /// <summary>
    /// 再生を開始します。音声がある場合はサウンド側の再生に委ね、無い場合は Stopwatch を起点として時間を進めます。
    /// </summary>
    public void Play()
    {
        if (!Enable) return;
        if (IsPlaying) return;

        if (_sound != null)
        {
            if (!_sound.Enable)
            {
                // サウンドの非同期ロードがまだなら一度 Pump して進める
                _sound.Pump();
                if (!_sound.Enable) return;
            }
            _sound.Play();
        }
        else
        {
            _clockOffset = _timeMs;
            _clock.Restart();
        }

        IsPlaying = true;
    }

    public void Stop()
    {
        if (!IsPlaying) return;
        _sound?.Stop();
        if (_clock.IsRunning) _clock.Reset();
        IsPlaying = false;
    }

    public void PlayStream() => Play();

    /// <summary>
    /// 毎フレーム呼び出し、再生時刻の更新・ループ／終端処理・準備タスクの監視を行います。
    /// </summary>
    public void Pump()
    {
        if (_disposed) return;
        _sound?.Pump();

        if (!Enable)
        {
            MonitorPrepareTask();
            return;
        }

        if (IsPlaying)
        {
            if (_sound != null && _sound.Enable)
            {
                // 音声があるときはサウンドの再生位置を正として同期する
                _timeMs = _sound.Time;
            }
            else if (_clock.IsRunning)
            {
                _timeMs = _clockOffset + _clock.Elapsed.TotalMilliseconds * _speed;
            }

            if (Length > 0 && _timeMs >= Length - 1)
            {
                if (Loop)
                {
                    Seek(0);
                    if (_sound != null)
                    {
                        if (!_sound.IsPlaying) _sound.Play();
                    }
                    else
                    {
                        _clockOffset = 0;
                        _clock.Restart();
                    }
                }
                else
                {
                    Stop();
                }
            }
        }
    }

    public void Draw(double x, double y, DrawOptions option)
    {
        if (!Enable) return;
        var tex = GetTextureForCurrentFrame();
        tex?.Draw(x, y, option);
    }

    /// <summary>
    /// キャッシュ済みテクスチャ・音声・準備タスクを破棄し、抽出したフレーム一式が入った作業ディレクトリを削除します。
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _cts.Cancel(); } catch { }
        try { _prepareTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }

        lock (_cacheLock)
        {
            foreach (var (_, texture) in _textureCache.ToArray())
            {
                try { texture.Dispose(); } catch { }
            }
            _textureCache.Clear();
            _recentFrames.Clear();
        }

        _sound?.Dispose();
        _sound = null;

        try
        {
            if (!string.IsNullOrEmpty(_workDir) && Directory.Exists(_workDir))
            {
                Directory.Delete(_workDir, true);
            }
        }
        catch { }

        _cts.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// バックグラウンドで動画を解析し、フレーム・音声を抽出して再生可能状態にする。
    /// キャンセルまたは失敗時は _asyncState を -1(失敗)にする。
    /// </summary>
    private async Task PrepareAsync(CancellationToken token)
    {
        try
        {
            var analysis = await FFProbe.AnalyseAsync(Path).ConfigureAwait(false);
            Width = analysis.PrimaryVideoStream?.Width ?? Width;
            Height = analysis.PrimaryVideoStream?.Height ?? Height;
            Length = analysis.Duration != TimeSpan.Zero
                ? (int)Math.Round(analysis.Duration.TotalMilliseconds)
                : Length;

            double fps = GetFrameRate(analysis.PrimaryVideoStream);
            if (fps < 1) fps = 30.0;
            _frameDurationMs = 1000.0 / fps;

            await ExtractFramesAsync(token).ConfigureAwait(false);
            if (_frameFiles.Count == 0)
            {
                throw new InvalidOperationException("FFmpeg 情報: フレームを抽出できませんでした。");
            }

            if (Length <= 0)
            {
                Length = (int)Math.Round(_frameFiles.Count * _frameDurationMs);
            }

            if (analysis.PrimaryAudioStream != null)
            {
                await ExtractAudioAsync(token).ConfigureAwait(false);
            }

            ApplyAudioState();
            Volatile.Write(ref _asyncState, 1);
        }
        catch (OperationCanceledException)
        {
            Volatile.Write(ref _asyncState, -1);
        }
        catch (Exception ex)
        {
            Log.Error($"MovieExtend: {ex.Message}");
            Volatile.Write(ref _asyncState, -1);
        }
    }

    /// <summary>FFmpeg で動画を連番PNG（RGBA・可変フレームレート維持）に書き出す。</summary>
    private async Task ExtractFramesAsync(CancellationToken token)
    {
        string pattern = System.IO.Path.Combine(_framesDir, "frame_%08d.png");
        var args = FFMpegArguments
            .FromFileInput(Path)
            .OutputToFile(pattern, overwrite: true, options => options.WithCustomArgument("-an")
                    .WithCustomArgument("-vcodec png")
                    .WithCustomArgument("-pix_fmt rgba")
                    .WithCustomArgument("-vsync 0")
                    .ForceFormat("image2"));

        await args.ProcessAsynchronously(true).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();

        string[] files = Directory.GetFiles(_framesDir, "frame_*.png", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.Ordinal);
        _frameFiles = files;
    }

    /// <summary>FFmpeg で音声トラックを WAV（PCM16）に書き出し、SoundExtend として読み込む。</summary>
    private async Task ExtractAudioAsync(CancellationToken token)
    {
        _audioPath = System.IO.Path.Combine(_workDir, "audio.wav");
        var args = FFMpegArguments
            .FromFileInput(Path)
            .OutputToFile(_audioPath, overwrite: true, options => options.WithCustomArgument("-vn")
                    .WithCustomArgument("-acodec pcm_s16le")
                    .ForceFormat("wav"));

        await args.ProcessAsynchronously(true).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        _sound = new SoundExtend(_audioPath, loop: Loop, prescan: true);
    }

    /// <summary>抽出直後のサウンドに、これまで保持していた音量・パン・ピッチ・速度・ループ設定を反映する。</summary>
    private void ApplyAudioState()
    {
        if (_sound == null) return;
        _sound.Volume = _volume;
        _sound.Pan = _pan;
        _sound.Pitch = _pitch;
        _sound.Speed = _speed;
        _sound.Loop = Loop;
    }

    /// <summary>再生位置を指定ミリ秒へ移動する。音声があれば音声側もシークし、無ければ内部クロックの基準をずらす。</summary>
    private void Seek(double targetMs)
    {
        if (double.IsNaN(targetMs) || double.IsInfinity(targetMs)) return;
        lock (_stateLock)
        {
            _timeMs = Math.Clamp(targetMs, 0, Length > 0 ? Length : targetMs);
            if (_sound != null && _sound.Enable)
            {
                _sound.Time = _timeMs;
            }
            else if (_clock.IsRunning)
            {
                _clockOffset = _timeMs;
                _clock.Restart();
            }
        }
    }

    /// <summary>
    /// 現在の再生時刻に対応するフレームのテクスチャを取得する。LRUキャッシュに無ければ読み込んで追加する。
    /// </summary>
    private ITexture? GetTextureForCurrentFrame()
    {
        if (_frameFiles.Count == 0) return null;
        int idx = GetFrameIndex();

        lock (_cacheLock)
        {
            if (_textureCache.TryGetValue(idx, out var cached))
            {
                TouchFrame(idx);
                cached.Pump();
                return cached;
            }

            string path = _frameFiles[idx];
            if (!File.Exists(path)) return null;

            var texture = AstrumCore.Platform.LoadTexture(path);
            texture.Pump();
            _textureCache[idx] = texture;
            _recentFrames.AddFirst(idx);
            TrimCache(); // キャッシュ上限を超えた分は古いものから破棄
            return texture;
        }
    }

    /// <summary>現在時刻を _frameDurationMs で割ってフレーム番号に変換する（範囲外は端にクランプ）。</summary>
    private int GetFrameIndex()
    {
        if (_frameFiles.Count == 0 || _frameDurationMs <= 0) return 0;
        double frame = _timeMs / _frameDurationMs;
        int idx = (int)Math.Floor(frame);
        if (idx < 0) idx = 0;
        if (idx >= _frameFiles.Count) idx = _frameFiles.Count - 1;
        return idx;
    }

    /// <summary>アクセスされたフレームをLRUリストの先頭（最も新しい）に移動する。</summary>
    private void TouchFrame(int idx)
    {
        var node = _recentFrames.Find(idx);
        if (node == null) return;
        _recentFrames.Remove(node);
        _recentFrames.AddFirst(node);
    }

    /// <summary>キャッシュ件数が上限を超えている間、最も使われていないフレーム（リスト末尾）から破棄する。</summary>
    private void TrimCache()
    {
        while (_recentFrames.Count > TextureCacheLimit)
        {
            var tail = _recentFrames.Last;
            if (tail == null) break;
            _recentFrames.RemoveLast();
            if (_textureCache.Remove(tail.Value, out var texture))
            {
                try { texture.Dispose(); } catch { }
            }
        }
    }

    /// <summary>準備タスクが例外で終わっていないかを確認し、その場合は失敗状態に反映する。</summary>
    private void MonitorPrepareTask()
    {
        var task = _prepareTask;
        if (task == null) return;
        if (!task.IsCompleted) return;
        if (task.IsFaulted)
        {
            Volatile.Write(ref _asyncState, -1);
        }
    }

    // AverageFrameRate → AvgFrameRate → FrameRate の順に有効な値を探す（フィールドによって未設定の場合があるため）
    private static double GetFrameRate(VideoStream? stream) => stream == null
            ? 0
            : stream.AverageFrameRate > 0
            ? stream.AverageFrameRate
            : stream.AvgFrameRate > 0 ? stream.AvgFrameRate : stream.FrameRate > 0 ? stream.FrameRate : 0;
}
