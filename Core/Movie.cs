namespace AstrumLoom;

// IMovie は共通メンバを明示的に含めつつ、ISound と ITexture の固有メンバにもアクセス可能
public interface IMovie : ISound, ITexture
{
}

public class Movie : IDisposable
{
    private bool _disposed;

    public Movie() { }

    public Movie(string path) => Inner = AstrumCore.Platform?.LoadMovie(path);

    public IMovie? Inner { get; private set; }

    public string? Path => Inner?.Path;
    public int Width => Inner?.Width ?? 0;
    public int Height => Inner?.Height ?? 0;
    public int Length => Inner?.Length ?? 0;

    public bool IsReady => Inner?.IsReady ?? false;
    public bool IsFailed => Inner?.IsFailed ?? false;
    public bool Loaded => Inner?.Loaded ?? false;
    public bool Enable => Inner?.Enable ?? false;

    public double Time
    {
        get => Inner?.Time ?? 0; set => Inner?.Time = value;
    }

    public double Volume
    {
        get => Inner?.Volume ?? 1.0; set => Inner?.Volume = value;
    }

    public bool IsPlaying => Inner?.IsPlaying ?? false;

    public bool Loop
    {
        get => Inner?.Loop ?? false; set => Inner?.Loop = value;
    }

    public DrawOptions Option = new();

    public double Speed
    {
        get => Inner?.Speed ?? 1.0; set => Inner?.Speed = value;
    }

    public (double X, double Y)? Scale
    {
        get => Option.Scale;
        set => Option.Scale = value ?? (1.0, 1.0);
    }

    /// <summary>0.0〜1.0 の再生位置。Length==0 のときは 0 扱い。</summary>
    public double Progress
    {
        get => Inner == null ? 0 : Inner.Length <= 0 ? 0 : Inner.Time / Inner.Length;
        set
        {
            if (Inner == null) return;
            if (Inner.Length <= 0) return;
            value = Math.Clamp(value, 0.0, 1.0);
            Inner.Time = Inner.Length * value;
        }
    }

    public void Play() => Inner?.Play();
    public void Stop() => Inner?.Stop();

    public void Pump() => Inner?.Pump();

    public void Draw(double x = 0, double y = 0) => Inner?.Draw(x, y);

    public void Draw(double x, double y, DrawOptions option)
        => Inner?.Draw(x, y, option);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Inner?.Dispose();
        Inner = null;
        GC.SuppressFinalize(this);
    }

    ~Movie()
    {
        Dispose();
    }
}
