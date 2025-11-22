namespace AstrumLoom;

public class Animation : IDisposable
{
    public string Name = "";
    private readonly List<Texture> _frames = [];
    private Counter _counter = new();
    private bool _disposed;

    /// <summary>
    /// フレームを読み込んでアニメーションを作成します。
    /// dir は末尾に区切り文字を含めて渡してください。
    /// ファイル名は dir + prefix + index + ext の形式で連番を想定します。
    /// </summary>
    public Animation(string dir, string prefix = "\\", string ext = ".png", long interval = 1000000 / 60, bool isLoop = true)
    {
        int count = GetCount(dir, prefix, ext);
        for (int i = 0; i < count; i++)
        {
            string path = dir + prefix + i + ext;
            _frames.Add(new Texture(path));
        }
        Name = Path.GetFileNameWithoutExtension(dir);

        if (count == 0)
        {
            // フレームがない場合はダミーのカウンターを作る（Begin=End=0）
            _counter = new Counter(0, 0, interval, isLoop);
        }
        else
        {
            // カウントは 0..count-1 を表現するように設定する
            _counter = new Counter(0, count - 1, interval, isLoop);
        }
    }

    public override string ToString() => $"Animation(Name={Name}, Count={Count}, Interval={Interval}, IsLoop={IsLoop}, IsPlaying={IsPlaying}, CurrentIndex={CurrentIndex})";

    public static int GetCount(string dir, string prefix = "", string ext = ".png")
    {
        int num = 0;
        while (File.Exists(dir + prefix + num + ext))
        {
            num++;
        }
        return num;
    }

    public bool Loaded
    {
        get
        {
            foreach (var f in _frames)
            {
                if (!f.Loaded)
                    return false;
            }
            return true;
        }
    }

    public bool Enable
        => _frames.Count > 0 && Loaded;

    public (int Width, int Height) Size
        => _frames.Count > 0 ? (_frames[0].Width, _frames[0].Height) : (0, 0);

    public int Width
        => Size.Width;
    public int Height
        => Size.Height;

    /// <summary>
    /// フレーム数
    /// </summary>
    public int Count => _frames.Count;

    /// <summary>
    /// 現在のフレームインデックス。
    /// </summary>
    public int CurrentIndex => Count == 0 ? 0 : (int)_counter.Value;

    /// <summary>
    /// 現在のフレームのテクスチャ（存在しない場合は null）。
    /// </summary>
    public Texture? CurrentFrame => Count == 0 ? null : _frames[CurrentIndex];

    /// <summary>
    /// ループ設定。
    /// </summary>
    public bool IsLoop => _counter.IsLoop;

    /// <summary>
    /// 再生中かどうか。
    /// </summary>
    public bool IsPlaying => _counter.State == TimerState.Started;

    /// <summary>
    /// タイマー間隔（マイクロ秒）。
    /// </summary>
    public long Interval
    {
        get => _counter.Interval; set => _counter.ChangeInterval(value);
    }

    public void Pump()
    {
        if (_disposed) return;
        foreach (var f in _frames)
        {
            f.Pump();
        }
    }

    /// <summary>
    /// Tick を進めてフレームを更新します。戻り値は何フレーム進んだかです。
    /// </summary>
    public long Update() => _disposed ? 0 : _counter.Tick();

    public Texture? GetFrame(int index)
    {
        if (_disposed) return null;
        return index < 0 || index >= Count ? null : _frames[index];
    }

    /// <summary>
    /// 現在のフレームを描画します。
    /// </summary>
    public void Draw(double x = 0, double y = 0)
    {
        if (_disposed) return;
        var tex = CurrentFrame;
        if (tex == null) return;
        tex.Draw(x, y);
    }

    /// <summary>
    /// 指定位置に現在フレームを描画します（Rect 指定版）。
    /// </summary>
    public void Draw(double x, double y, LayoutUtil.Rect rect)
    {
        if (_disposed) return;
        var tex = CurrentFrame;
        if (tex == null) return;
        tex.Draw(x, y, rect);
    }

    /// <summary>
    /// アニメーションを開始します。
    /// </summary>
    public void Start() => _counter.Start();

    /// <summary>
    /// アニメーションを停止します。
    /// </summary>
    public void Stop() => _counter.Stop();

    /// <summary>
    /// アニメーションをリセットします（先頭フレームに戻す）。
    /// </summary>
    public void Reset() => _counter.Reset();

    /// <summary>
    /// 全フレームの不透明度を設定します。
    /// </summary>
    public void SetOpacity(double opacity)
    {
        foreach (var f in _frames) f.Opacity = opacity;
    }

    /// <summary>
    /// 全フレームの描画色を設定します。
    /// </summary>
    public void SetColor(Color color, Color? add = null)
    {
        foreach (var f in _frames) f.SetColor(color, add);
    }

    /// <summary>
    /// リソースを解放します。
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        foreach (var f in _frames) f?.Dispose();
        _frames.Clear();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}