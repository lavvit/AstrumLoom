namespace AstrumLoom;

/// <summary>プラットフォームが実装するサウンド1つ分の実体。Soundクラスがこれをラップする。</summary>
public interface ISound : IResourse
{
    int Length { get; }

    double Time { get; set; }
    double Volume { get; set; }
    double Pan { get; set; }
    double Pitch { get; set; }
    double Speed { get; set; }

    bool IsPlaying { get; }
    bool Loop { get; set; }

    void Play();
    void Stop();

    void PlayStream();
}
/// <summary>サウンドのラッパー。実体（ISound）がnull（未ロード）でも安全な既定値を返し、破棄はメインスレッドへ回す。</summary>
public class Sound : IDisposable
{
    private ISound? _sound;
    private readonly long _id = Interlocked.Increment(ref _nextId);
    private static long _nextId;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, WeakReference<Sound>> Live = new();
    public Sound() { }
    public Sound(string path, bool stream = false)
    {
        _sound = AstrumCore.Platform.LoadSound(path, stream);
        Live[_id] = new(this);
    }
    public void Play() => _sound?.Play();
    public void Stop() => _sound?.Stop();
    public void PlayStream() => _sound?.PlayStream();
    public void Pump() => _sound?.Pump();
    internal static void PumpAll()
    {
        foreach (var pair in Live)
        {
            if (pair.Value.TryGetTarget(out var sound)) sound.Pump();
            else Live.TryRemove(pair.Key, out _);
        }
    }
    internal static void DisposeAll()
    {
        foreach (var pair in Live)
            if (pair.Value.TryGetTarget(out var sound)) sound.Dispose();
        Live.Clear();
    }
    ~Sound() => Dispose(false);
    public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
    protected virtual void Dispose(bool disposing)
    {
        Live.TryRemove(_id, out _);
        var sound = Interlocked.Exchange(ref _sound, null);
        if (sound != null) AstrumCore.RequestDispose(sound);
    }
    public string Path => _sound?.Path ?? "";
    public int Length => _sound?.Length ?? 0;
    public bool IsReady => _sound?.IsReady ?? false;
    public bool IsFailed => _sound?.IsFailed ?? false;
    public bool Loaded => _sound?.Loaded ?? false;
    public bool Enable => _sound?.Enable ?? false;

    public double Time
    {
        get => _sound?.Time ?? 0;
        set => _sound?.Time = value;
    }
    public double Volume
    {
        get => _sound?.Volume ?? 0;
        set => _sound?.Volume = value;
    }
    public double Pan
    {
        get => _sound?.Pan ?? 0;
        set => _sound?.Pan = value;
    }
    public double Pitch
    {
        get => _sound?.Pitch ?? 1;
        set => _sound?.Pitch = value;
    }
    public double Speed
    {
        get => _sound?.Speed ?? 1;
        set => _sound?.Speed = value;
    }
    public bool Playing => _sound?.IsPlaying ?? false;
    public bool Loop
    {
        get => _sound?.Loop ?? false;
        set => _sound?.Loop = value;
    }
    public double Progress
    {
        get => _sound == null || _sound.Length <= 0 ? 0 : _sound.Time / _sound.Length;
        set
        {
            if (_sound == null || _sound.Length <= 0) return;
            _sound.Time = _sound.Length * value;
        }
    }
}
