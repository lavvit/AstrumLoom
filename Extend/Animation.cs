namespace AstrumLoom.Extend;

public class Animation : AsyncLoadableBase, IMovie
{
    public string Path { get; private set; } = "";

    public AnimeProject Project { get; private set; } = new AnimeProject();

    public Animation(string path, bool isLoop = true)
    {
        Path = path;
        Loop = isLoop;
        Load();
    }

    public void Load()
        => LoadAsync(this, LoadAnim);
    public bool LoadAnim()
    {
        bool file = FileCheck(Path);
        return file;
    }

    ~Animation() { Dispose(); }
    public void Dispose()
    {
        // Clean up resources
        DisposeAsync(DisposeAnim);
        GC.SuppressFinalize(this);
    }
    private bool DisposeAnim() =>
        // Dispose animation resources here
        true;

    public void Pump()
    {
        PumpAsync();
        if (!IsMainThread) return; // メインスレッドでのみ触る
    }
    public bool Enable => LoadFinished;
    public bool IsReady => LoadReady;
    public bool IsFailed => LoadFailed;
    public bool Loaded => LoadFinished;

    public int Width => Project.Width;
    public int Height => Project.Height;
    public int Length { get; set; }
    public double Time { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public double Volume { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public double Pan { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public double Pitch { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public double Speed { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public bool IsPlaying => throw new NotImplementedException();

    public bool Loop { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public DrawOptions? Option { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public void Play() => throw new NotImplementedException();
    public void Stop() => throw new NotImplementedException();
    public void PlayStream() => throw new NotImplementedException();
    public void Draw(double x, double y, DrawOptions option) => throw new NotImplementedException();
}

public class AnimeProject
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Rate { get; private set; } = 1000;
    public double Scale { get; private set; } = 1.0;

    private List<ImageObject> imageObjects { get; set; } = [];
    private List<SoundObject> soundObjects { get; set; } = [];

}