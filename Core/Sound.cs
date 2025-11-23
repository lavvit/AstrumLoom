namespace AstrumLoom;

public interface ISound
{
    string Path { get; }
    int Length { get; }

    bool IsReady { get; }
    bool IsFailed { get; }
    bool Loaded { get; }
    bool Enable { get; }

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

    void Pump();
    void Dispose();
}
public class Sound : IDisposable
{
    private ISound? _sound { get; set; } = null;
    private bool _disposed = false;
    public Sound() { }
    public Sound(string path, bool stream = false) => _sound = AstrumCore.Platform.LoadSound(path, stream);

    public void Play() => _sound?.Play();
    public void Stop() => _sound?.Stop();
    public void PlayStream() => _sound?.PlayStream();

    public void Pump() => _sound?.Pump();

    ~Sound() => Dispose(false);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _sound?.Dispose();
            }
            _sound = null;
            _disposed = true;
        }
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
