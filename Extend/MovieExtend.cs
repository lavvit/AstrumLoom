using System.Diagnostics;
using FFMpegCore;

namespace AstrumLoom.Extend;

internal sealed class MovieExtend : IMovie, IDisposable
{
    private const int TextureCacheLimit = 6;

    private readonly string _path;
    private readonly string _workDir = string.Empty;
    private readonly string _framesDir = string.Empty;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _stateLock = new();
    private readonly object _cacheLock = new();
    private readonly Dictionary<int, ITexture> _textureCache = new();
    private readonly LinkedList<int> _recentFrames = new();

    private Task? _prepareTask;
    private SoundExtend? _sound;
    private string? _audioPath;
    private IReadOnlyList<string> _frameFiles = Array.Empty<string>();
    private double _frameDurationMs = 33.34;
    private double _timeMs;
    private bool _loop;
    private bool _playing;
    private double _volume = 1.0;
    private double _pan;
    private double _pitch = 1.0;
    private double _speed = 1.0;
    private Stopwatch _clock = new();
    private double _clockOffset;
    private volatile int _asyncState = -1; // -1=failed,0=loading,1=ready
    private bool _disposed;

    public MovieExtend(string path)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        if (!File.Exists(_path))
        {
            Log.Warning($"MovieExtend: file not found: {_path}");
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

    public string Path => _path;
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
            if (_sound != null) _sound.Volume = _volume;
        }
    }

    public double Pan
    {
        get => _sound?.Pan ?? _pan;
        set
        {
            _pan = Math.Clamp(value, -1.0, 1.0);
            if (_sound != null) _sound.Pan = _pan;
        }
    }

    public double Pitch
    {
        get => _sound?.Pitch ?? _pitch;
        set
        {
            _pitch = value;
            if (_sound != null) _sound.Pitch = _pitch;
        }
    }

    public double Speed
    {
        get => _sound?.Speed ?? _speed;
        set
        {
            _speed = value <= 0 ? 1.0 : value;
            if (_sound != null) _sound.Speed = _speed;
        }
    }

    public bool IsPlaying => _playing;

    public bool Loop
    {
        get => _loop;
        set
        {
            _loop = value;
            if (_sound != null) _sound.Loop = value;
        }
    }

    public void Play()
    {
        if (!Enable) return;
        if (_playing) return;

        if (_sound != null)
        {
            if (!_sound.Enable)
            {
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

        _playing = true;
    }

    public void Stop()
    {
        if (!_playing) return;
        _sound?.Stop();
        if (_clock.IsRunning) _clock.Reset();
        _playing = false;
    }

    public void PlayStream() => Play();

    public void Pump()
    {
        if (_disposed) return;
        _sound?.Pump();

        if (!Enable)
        {
            MonitorPrepareTask();
            return;
        }

        if (_playing)
        {
            if (_sound != null && _sound.Enable)
            {
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

    public void Draw(double x, double y, DrawOptions? options)
    {
        if (!Enable) return;
        var tex = GetTextureForCurrentFrame();
        tex?.Draw(x, y, options ?? Option);
    }

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

    private async Task PrepareAsync(CancellationToken token)
    {
        try
        {
            var analysis = await FFProbe.AnalyseAsync(_path).ConfigureAwait(false);
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

    private async Task ExtractFramesAsync(CancellationToken token)
    {
        string pattern = System.IO.Path.Combine(_framesDir, "frame_%08d.png");
        var args = FFMpegArguments
            .FromFileInput(_path)
            .OutputToFile(pattern, overwrite: true, options =>
            {
                options.WithCustomArgument("-an")
                    .WithCustomArgument("-vcodec png")
                    .WithCustomArgument("-pix_fmt rgba")
                    .WithCustomArgument("-vsync 0")
                    .ForceFormat("image2");
            });

        await args.ProcessAsynchronously(true).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();

        var files = Directory.GetFiles(_framesDir, "frame_*.png", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.Ordinal);
        _frameFiles = files;
    }

    private async Task ExtractAudioAsync(CancellationToken token)
    {
        _audioPath = System.IO.Path.Combine(_workDir, "audio.wav");
        var args = FFMpegArguments
            .FromFileInput(_path)
            .OutputToFile(_audioPath, overwrite: true, options =>
            {
                options.WithCustomArgument("-vn")
                    .WithCustomArgument("-acodec pcm_s16le")
                    .ForceFormat("wav");
            });

        await args.ProcessAsynchronously(true).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        _sound = new SoundExtend(_audioPath, loop: _loop, prescan: true);
    }

    private void ApplyAudioState()
    {
        if (_sound == null) return;
        _sound.Volume = _volume;
        _sound.Pan = _pan;
        _sound.Pitch = _pitch;
        _sound.Speed = _speed;
        _sound.Loop = _loop;
    }

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
            TrimCache();
            return texture;
        }
    }

    private int GetFrameIndex()
    {
        if (_frameFiles.Count == 0 || _frameDurationMs <= 0) return 0;
        double frame = _timeMs / _frameDurationMs;
        int idx = (int)Math.Floor(frame);
        if (idx < 0) idx = 0;
        if (idx >= _frameFiles.Count) idx = _frameFiles.Count - 1;
        return idx;
    }

    private void TouchFrame(int idx)
    {
        var node = _recentFrames.Find(idx);
        if (node == null) return;
        _recentFrames.Remove(node);
        _recentFrames.AddFirst(node);
    }

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

    private static double GetFrameRate(VideoStream? stream)
    {
        if (stream == null) return 0;
        if (stream.AverageFrameRate > 0) return stream.AverageFrameRate;
        if (stream.AvgFrameRate > 0) return stream.AvgFrameRate;
        if (stream.FrameRate > 0) return stream.FrameRate;
        return 0;
    }
}
