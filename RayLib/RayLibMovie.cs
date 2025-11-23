using AstrumLoom;

namespace AstrumLoom.RayLib;

public class RayLibMovie : IMovie
{
    public string Path { get; private set; } = "";
    public int Width { get; private set; } = 0;
    public int Height { get; private set; } = 0;
    public int Length { get; private set; } = 0;

    public DrawOptions? Option { get; set; }

    public RayLibMovie(string path)
    {
        Path = path;
        // ここは後で FFmpeg 等で実装する想定
    }

    public bool IsReady => false;
    public bool IsFailed => true;
    public bool Loaded => true;  // ループ待ちで固まらないようにだけ true にしておく
    public bool Enable => false;

    public double Time { get; set; }
    public double Volume { get; set; } = 1.0;
    public double Pan { get; set; } = 0.0;
    public double Pitch { get; set; } = 1.0;
    public double Speed { get; set; } = 1.0;

    public bool IsPlaying => false;
    public bool Loop { get; set; }

    public void Play() { }
    public void Stop() { }
    public void Pump() { }
    public void PlayStream() { }

    public void Draw(double x, double y, DrawOptions? options) { }

    public void Dispose() { }
}
