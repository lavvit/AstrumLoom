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
    public ISound _sound;
    private bool _disposed = false;
    public Sound(string path, bool stream = false) => _sound = GameRunner.Platform.LoadSound(path, stream);

    public void Play() => _sound.Play();
    public void Stop() => _sound.Stop();
    public void PlayStream() => _sound.PlayStream();

    public void Pump() => _sound.Pump();

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
                _sound.Dispose();
            }
            _disposed = true;
        }
    }

    public string Path => _sound.Path;
    public int Length => _sound.Length;
    public bool IsReady => _sound.IsReady;
    public bool IsFailed => _sound.IsFailed;
    public bool Loaded => _sound.Loaded;
    public bool Enable => _sound.Enable;

    public double Time
    {
        get => _sound.Time;
        set => _sound.Time = value;
    }
    public double Volume
    {
        get => _sound.Volume;
        set => _sound.Volume = value;
    }
    public double Pan
    {
        get => _sound.Pan;
        set => _sound.Pan = value;
    }
    public double Pitch
    {
        get => _sound.Pitch;
        set => _sound.Pitch = value;
    }
    public double Speed
    {
        get => _sound.Speed;
        set => _sound.Speed = value;
    }
    public bool IsPlaying => _sound.IsPlaying;
    public bool Loop
    {
        get => _sound.Loop;
        set => _sound.Loop = value;
    }
}
