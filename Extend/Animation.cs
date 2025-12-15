using AstrumLoom.Extend;

namespace AstrumLoom;

public class Animation : IDisposable
{
    public string Name = "";
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
        Name = Path.GetFileNameWithoutExtension(dir);
        _keyPrefix = dir.Replace('\\', '/').Replace('/', '_').ToLower();
        string originalKeyPrefix = _keyPrefix;
        for (int i = 0; i < count; i++)
        {
            string path = dir + prefix + i + ext;
            _keyPrefix = Skin.AddTexture($"anim_{originalKeyPrefix}_{i}", path).Replace($"_{i}", "");
        }
        Count = Frames.Length;

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

    private string _keyPrefix = "";
    public Texture[] Frames
        => [.. Skin.Textures.Where(kv => kv.Key.StartsWith($"{_keyPrefix}_")).OrderBy(kv => kv.Key).Select(kv => kv.Value)];

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
            foreach (var f in Frames)
            {
                if (!f.Loaded)
                    return false;
            }
            return true;
        }
    }

    public bool Enable
        => Count > 0 && Loaded;

    public (int Width, int Height) Size
        => Count > 0 ? (CurrentFrame?.Width ?? 0, CurrentFrame?.Height ?? 0) : (0, 0);

    public int Width
        => Size.Width;
    public int Height
        => Size.Height;

    /// <summary>
    /// フレーム数
    /// </summary>
    public int Count { get; }

    /// <summary>
    /// 現在のフレームインデックス。
    /// </summary>
    public int CurrentIndex => Count == 0 ? 0 : (int)_counter.Value;

    /// <summary>
    /// 現在のフレームのテクスチャ（存在しない場合は null）。
    /// </summary>
    public Texture? CurrentFrame => Count == 0 ? null : Skin.Texture($"{_keyPrefix}_{CurrentIndex}");

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
        foreach (var f in Frames)
        {
            f.Pump();
        }
    }

    /// <summary>
    /// Tick を進めてフレームを更新します。戻り値は何フレーム進んだかです。
    /// </summary>
    public long Update() => _disposed ? 0 : _counter.Tick();

    public Texture? GetFrame(int index) => _disposed ? null : index < 0 || index >= Count ? null :
        Skin.Texture($"{_keyPrefix}_{index}");

    /// <summary>
    /// 現在のフレームを描画します。
    /// </summary>
    public void Draw(double x = 0, double y = 0)
    {
        if (_disposed || !Enable) return;
        Pump();
        var tex = CurrentFrame;
        if (tex == null) return;
        tex.Draw(x, y);
    }

    /// <summary>
    /// 指定位置に現在フレームを描画します（Rect 指定版）。
    /// </summary>
    public void Draw(double x, double y, LayoutUtil.Rect rect)
    {
        if (_disposed || !Enable) return;
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
        foreach (var f in Frames) f.Opacity = opacity;
    }

    /// <summary>
    /// 全フレームの描画色を設定します。
    /// </summary>
    public void SetColor(Color color, Color? add = null)
    {
        foreach (var f in Frames) f.SetColor(color, add);
    }

    ~Animation()
    {
        Dispose();
    }
    /// <summary>
    /// リソースを解放します。
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        // 2) Skin.Textures から _keyPrefix を含むキーを全て削除する
        if (!string.IsNullOrEmpty(_keyPrefix) && Skin.Textures != null)
        {
            // キー一覧を安全に取得してから削除する
            var keysToRemove = Skin.Textures.Keys
                .Where(k => k != null && k.StartsWith($"{_keyPrefix}_"))
                .ToList();

            foreach (string? k in keysToRemove)
            {
                Skin.RemoveTexture(k);
            }
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public ReferencePoint Point
    {
        get => CurrentFrame?.Point ?? ReferencePoint.TopLeft;
        set
        {
            foreach (var f in Frames)
            {
                f.Point = value;
            }
        }
    }
}