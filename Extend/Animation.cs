namespace AstrumLoom.Extend;

/// <summary>
/// 独自形式のアニメーションプロジェクト（<see cref="AnimeProject"/>）を IMovie として扱うためのラッパー。
/// AsyncLoadableBase の非同期ロード基盤に乗せてファイルを読み込む。
/// </summary>
public class Animation : AsyncLoadableBase, IMovie
{
    /// <summary>読み込み元ファイルのパス。</summary>
    public string Path { get; private set; } = "";

    /// <summary>読み込まれたアニメーションのプロジェクトデータ。</summary>
    public AnimeProject Project { get; private set; } = new AnimeProject();

    public Animation(string path, bool isLoop = true)
    {
        Path = path;
        Loop = isLoop;
        Load();
    }

    /// <summary>非同期でのファイル読み込みを開始します。</summary>
    public void Load()
        => LoadAsync(this, LoadAnim);
    /// <summary>読み込みワーカースレッドから呼ばれる本体処理。ファイルの存在確認のみ行う。</summary>
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

    /// <summary>毎フレーム呼び出し、非同期ロードの完了通知などを処理します。</summary>
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

    public bool Loop { get; set; }
    public DrawOptions? Option { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public void Play() => throw new NotImplementedException();
    public void Stop() => throw new NotImplementedException();
    public void PlayStream() => throw new NotImplementedException();
    public void Draw(double x, double y, DrawOptions option) => throw new NotImplementedException();
}

/// <summary>
/// アニメーションプロジェクトのデータ。画像・サウンドオブジェクト群を保持する（現状未実装）。
/// </summary>
public class AnimeProject
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Rate { get; private set; } = 1000;
    public double Scale { get; private set; } = 1.0;

    private List<ImageObject> imageObjects { get; set; } = [];
    private List<SoundObject> soundObjects { get; set; } = [];

}