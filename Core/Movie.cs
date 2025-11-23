namespace AstrumLoom;

// IMovie は共通メンバを明示的に含めつつ、ISound と ITexture の固有メンバにもアクセス可能
public interface IMovie : ISound, ITexture
{
}

public class Movie : IDisposable
{
    private IMovie? _movie;
    private bool _disposed;

    public Movie() { }

    public Movie(string path)
    {
        _movie = AstrumCore.Platform?.LoadMovie(path);
    }

    public IMovie? Inner => _movie;

    public string? Path => _movie?.Path;
    public int Width => _movie?.Width ?? 0;
    public int Height => _movie?.Height ?? 0;
    public int Length => _movie?.Length ?? 0;

    public bool IsReady => _movie?.IsReady ?? false;
    public bool IsFailed => _movie?.IsFailed ?? false;
    public bool Loaded => _movie?.Loaded ?? false;
    public bool Enable => _movie?.Enable ?? false;

    public double Time
    {
        get => _movie?.Time ?? 0;
        set { if (_movie != null) _movie.Time = value; }
    }

    public double Volume
    {
        get => _movie?.Volume ?? 1.0;
        set { if (_movie != null) _movie.Volume = value; }
    }

    public bool IsPlaying => _movie?.IsPlaying ?? false;

    public bool Loop
    {
        get => _movie?.Loop ?? false;
        set { if (_movie != null) _movie.Loop = value; }
    }

    public DrawOptions? Option
    {
        get => _movie?.Option;
        set { if (_movie != null) _movie.Option = value; }
    }

    public double Speed
    {
        get => _movie?.Speed ?? 1.0;
        set { if (_movie != null) _movie.Speed = value; }
    }

    public (double X, double Y)? Scale
    {
        get => _movie?.Option?.Scale;
        set
        {
            if (_movie?.Option != null && value != null)
            {
                var opt = _movie.Option.Value;
                opt.Scale = value.Value;
                _movie.Option = opt;
            }
            else if (_movie?.Option != null)
            {
                var opt = _movie.Option.Value;
                opt.Scale = (1.0, 1.0);
                _movie.Option = opt;
            }
        }
    }

    /// <summary>0.0〜1.0 の再生位置。Length==0 のときは 0 扱い。</summary>
    public double Progress
    {
        get
        {
            if (_movie == null) return 0;
            if (_movie.Length <= 0) return 0;
            return _movie.Time / _movie.Length;
        }
        set
        {
            if (_movie == null) return;
            if (_movie.Length <= 0) return;
            value = Math.Clamp(value, 0.0, 1.0);
            _movie.Time = _movie.Length * value;
        }
    }

    public void Play() => _movie?.Play();
    public void Stop() => _movie?.Stop();

    public void Pump() => _movie?.Pump();

    public void Draw(double x = 0, double y = 0) => _movie?.Draw(x, y);

    public void Draw(double x, double y, DrawOptions? options)
        => _movie?.Draw(x, y, options);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _movie?.Dispose();
        _movie = null;
        GC.SuppressFinalize(this);
    }

    ~Movie()
    {
        Dispose();
    }
}
