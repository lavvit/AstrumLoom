using ManagedBass;

namespace AstrumLoom.Extend;

/// <summary>
/// ManagedBass（BASSライブラリ）を使ってピッチ・速度変更などに対応した音声再生を行う ISound 実装。
/// メインスレッド以外から生成された場合はロードを遅延させ、次の Pump 呼び出し時にメインスレッドで実体化する。
/// </summary>
public sealed class SoundExtend : ISound, IDisposable
{
    private static readonly object s_initLock = new();
    private static bool s_bassInitialized;

    private readonly bool _loopFlag;
    private readonly bool _prescanFlag;

    private int _stream;
    private bool _disposed;
    /// <summary>Load() 成功直後にキャッシュした元の再生周波数。Speed/Pitchの基準値として使う。</summary>
    private float _baseFreq;

    // IResourse 状態管理 (-1=Failed/Disposed, 0=Loading, 1=Ready)
    private int _asyncState = -1;
    private bool _deferred;
    private long _startTicks;
    private const int DefaultTimeoutMs = 60000;
    public int TimeoutMs { get; set; } = DefaultTimeoutMs;

    public string Path { get; private set; } = string.Empty;

    private static bool IsMainThread => Environment.CurrentManagedThreadId == AstrumCore.MainThreadId;

    /// <summary>
    /// 音声ファイルを開きます。呼び出しがメインスレッドでない場合は即座にロードせず、
    /// 次に Pump がメインスレッドから呼ばれたタイミングで実際の BASS ストリーム生成を行います。
    /// </summary>
    public SoundExtend(string filePath, bool loop = false, bool prescan = false)
    {
        Path = filePath;
        _loopFlag = loop;
        _prescanFlag = prescan;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            Volatile.Write(ref _asyncState, -1);
            return;
        }

        // ファイルが存在しない場合は失敗
        if (!File.Exists(Path))
        {
            Volatile.Write(ref _asyncState, -1);
            Log.Warning($"サウンドファイルが見つかりません: {Path}");
            return;
        }

        if (!IsMainThread)
        {
            // メインスレッドで後からロード
            _deferred = true;
            Volatile.Write(ref _asyncState, 0); // Loading
            return;
        }

        Load();
    }

    /// <summary>BASS を初期化し、実際にストリームを生成します。メインスレッドから呼ばれる想定。</summary>
    private void Load()
    {
        if (IsReady) return; // 既に Ready
        if (!File.Exists(Path))
        {
            Volatile.Write(ref _asyncState, -1);
            return;
        }

        try
        {
            EnsureBassInitialized();
            var flags = BassFlags.Default;
            if (_loopFlag) flags |= BassFlags.Loop;
            if (_prescanFlag) flags |= BassFlags.Prescan;

            int stream = Bass.CreateStream(Path, 0, 0, flags);
            if (stream == 0)
            {
                Volatile.Write(ref _asyncState, -1);
                return;
            }

            _stream = stream;
            Volatile.Write(ref _asyncState, 1); // Ready
            _startTicks = Environment.TickCount64;
            // OpusOriginalFrequency はBassOpus専用属性で非Opusストリームでは0のまま返ってくるため、
            // Speed/Pitchの基準周波数として使えない。ロード直後の実周波数を自前でキャッシュしておく。
            Bass.ChannelGetAttribute(_stream, ChannelAttribute.Frequency, out float baseFreq);
            _baseFreq = baseFreq;
        }
        catch
        {
            Volatile.Write(ref _asyncState, -1);
        }
    }

    /// <summary>遅延ロードの実行、およびロード中タイムアウトの監視を行います（メインスレッド専用）。</summary>
    public void Pump()
    {
        // メインスレッドのみが状態更新
        if (!IsMainThread) return;

        // Deferred ロード実行
        if (_deferred)
        {
            _deferred = false;
            Load();
            return;
        }

        // Loading 中のタイムアウト監視
        if (Volatile.Read(ref _asyncState) == 0)
        {
            long elapsed = Environment.TickCount64 - _startTicks;
            if (TimeoutMs > 0 && elapsed >= TimeoutMs)
            {
                Dispose();
            }
        }
    }

    public bool IsReady => Volatile.Read(ref _asyncState) == 1;
    public bool IsFailed => Volatile.Read(ref _asyncState) == -1;
    public bool Loaded
    {
        get
        {
            Pump(); // 呼び忘れ対策
            return Volatile.Read(ref _asyncState) != 0;
        }
    }
    public bool Enable => Loaded && _stream != 0;

    private void EnsureReadyForChannel()
    {
        EnsureNotDisposed();
        if (!Enable && !IsFailed)
        {
            if (File.Exists(Path) && !string.IsNullOrEmpty(Path))
            {
                if (IsMainThread)
                    Log.Warning($"サウンドが未ロードです。: {Path}");
            }
            return;
        }
    }

    public bool Playing => IsPlaying;
    public bool IsPlaying => Enable && Bass.ChannelIsActive(_stream) == PlaybackState.Playing;

    public bool Loop
    {
        get
        {
            if (!Enable) return _loopFlag;
            var current = Bass.ChannelFlags(_stream, 0, 0);
            return (current & BassFlags.Loop) == BassFlags.Loop;
        }
        set
        {
            if (!Enable)
            {
                // ロード前に意図だけ保存
                // ロード時に反映される
                typeof(SoundExtend)
                    .GetField("_loopFlag", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?
                    .SetValue(this, value);
                return;
            }
            var setFlags = value ? BassFlags.Loop : 0;
            var mask = BassFlags.Loop;
            Bass.ChannelFlags(_stream, setFlags, mask);
        }
    }

    public double Volume
    {
        get
        {
            if (!Enable) return 0f;
            Bass.ChannelGetAttribute(_stream, ChannelAttribute.Volume, out float vol);
            return vol;
        }
        set
        {
            if (!Enable) return;
            float v = Math.Clamp((float)value, 0f, 1f);
            if (!Bass.ChannelSetAttribute(_stream, ChannelAttribute.Volume, v))
            {
                throw new InvalidOperationException($"音量の設定に失敗しました: {Bass.LastError}");
            }
        }
    }

    public TimeSpan Duration
    {
        get
        {
            if (!Enable) return TimeSpan.Zero;
            long lengthBytes = Bass.ChannelGetLength(_stream);
            double seconds = Bass.ChannelBytes2Seconds(_stream, lengthBytes);
            return TimeSpan.FromSeconds(seconds);
        }
    }

    public TimeSpan Position
    {
        get
        {
            if (!Enable) return TimeSpan.Zero;
            long posBytes = Bass.ChannelGetPosition(_stream);
            double seconds = Bass.ChannelBytes2Seconds(_stream, posBytes);
            return TimeSpan.FromSeconds(seconds);
        }
        set
        {
            if (!Enable) return;
            long bytes = Bass.ChannelSeconds2Bytes(_stream, value.TotalSeconds);
            if (!Bass.ChannelSetPosition(_stream, bytes))
            {
                throw new InvalidOperationException($"シークに失敗しました: {Bass.LastError}");
            }
        }
    }

    /// <summary>再生を開始します。既に再生中なら一度 Stop してから開始し直します。</summary>
    public void Play(bool restart = false)
    {
        if (!Enable) return;
        if (IsPlaying) Stop();
        if (!Bass.ChannelPlay(_stream, restart))
        {
            throw new InvalidOperationException($"再生に失敗しました: {Bass.LastError}");
        }
        _played = true;
        _everStarted = true;
    }

    public void Pause()
    {
        if (!Enable) return;
        if (!Bass.ChannelPause(_stream))
        {
            if (Bass.LastError == Errors.NotPlaying)
                return; // 既に停止中なら無視
            throw new InvalidOperationException($"一時停止に失敗しました: {Bass.LastError}");
        }
    }

    public void Stop()
    {
        if (!Enable) return;
        if (!Bass.ChannelStop(_stream))
        {
            if (Bass.LastError == Errors.NotPlaying)
                return; // 既に停止中なら無視
            throw new InvalidOperationException($"停止に失敗しました: {Bass.LastError}");
        }
        Bass.ChannelSetPosition(_stream, 0);
    }

    // ISound 互換
    void ISound.Play() => Play(false);
    void ISound.Stop() => Pause();
    /// <summary>
    /// 1回目の呼び出しで再生を開始し、以降は毎フレーム Update() を呼ぶだけの「呼びっぱなしBGM」用ヘルパー。
    /// </summary>
    public void PlayStream()
    {
        if (!Enable) return;
        if (_played)
        {
            Update();
            return;
        }
        Play();
        _played = true;
    }
    private bool _played = false;
    /// <summary>Play()/PlayStream() を一度も経由せずに済んだかどうか。Update()単独呼び出しの救済に使う。</summary>
    private bool _everStarted = false;
    /// <summary>
    /// PlayStream の継続処理。ループしない曲が終端に近づいたら止め、ループ曲や巻き戻り時は再生フラグをリセットする。
    /// </summary>
    public void Update()
    {
        Pump();
        if (!Enable) return;
        if (!_everStarted)
        {
            // Play()/PlayStream() を挟まずUpdate()だけを毎フレーム直接呼んだ場合の救済。
            // このままだと_playedが永遠にfalseのままTime=0が続き、再生が一切始まらない。
            Play();
            return;
        }
        if (_played)
        {
            bool isPlaying = IsPlaying;
            if (isPlaying)
            {
                if (!Loop && Length - Time <= 16)
                {
                    Bass.ChannelStop(_stream);
                    return;
                }
            }
            else
            {
                if (Loop || Time < 16) // ループ時にフラグをリセットして再生
                    _played = false;
            }
        }
        else
        {
            if (Loop) // ループ時にフラグをリセットして再生
                _played = false;
            Time = 0;
        }
    }

    /// <summary>プロセス全体で一度だけ BASS ライブラリを初期化する（複数の SoundExtend で共有）。</summary>
    private static void EnsureBassInitialized()
    {
        if (s_bassInitialized) return;

        lock (s_initLock)
        {
            if (s_bassInitialized) return;

            if (!Bass.Init())
            {
                throw new InvalidOperationException($"BASS の初期化に失敗しました: {Bass.LastError}");
            }
            s_bassInitialized = true;
        }
    }

    private void EnsureNotDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SoundExtend));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        if (_stream != 0)
        {
            Bass.ChannelStop(_stream);
            Bass.StreamFree(_stream);
            _stream = 0;
        }

        Volatile.Write(ref _asyncState, -1);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public double Pan
    {
        get
        {
            EnsureReadyForChannel();
            if (!Enable) return 0;
            Bass.ChannelGetAttribute(_stream, ChannelAttribute.Pan, out float pan);
            return pan;
        }
        set
        {
            EnsureReadyForChannel();
            if (!Enable) return;
            float p = Math.Clamp((float)value, -1f, 1f);
            if (!Bass.ChannelSetAttribute(_stream, ChannelAttribute.Pan, p))
            {
                Log.Error($"パンの設定に失敗しました: {Bass.LastError}");
            }
        }
    }
    /// <summary>
    /// ピッチ（半音単位）。ChannelAttribute.Pitch は BassFx のテンポチャンネル専用属性で、
    /// このクラスが生成する素の BASS ストリームには存在しないため使えない（設定すると必ず失敗する）。
    /// BassFx を追加参照しない方針としたため、ここでは Frequency 属性を使った疑似ピッチ実装にしている。
    /// 再生速度(Speed)と基準周波数を共有しているため、Pitch を設定すると Speed による速度変化は上書きされる
    /// （テンポ非依存の本格的なピッチシフトが必要になった場合は ManagedBass.Fx の導入を検討すること）。
    /// </summary>
    public double Pitch
    {
        get
        {
            EnsureReadyForChannel();
            if (!Enable || _baseFreq <= 0) return 0;
            Bass.ChannelGetAttribute(_stream, ChannelAttribute.Frequency, out float freq);
            return 12.0 * Math.Log2(freq / _baseFreq);
        }
        set
        {
            EnsureReadyForChannel();
            if (!Enable || _baseFreq <= 0) return;
            float p = Math.Clamp((float)value, -12f, 12f);
            float newFreq = _baseFreq * (float)Math.Pow(2.0, p / 12.0);
            if (!Bass.ChannelSetAttribute(_stream, ChannelAttribute.Frequency, newFreq))
            {
                throw new InvalidOperationException($"ピッチの設定に失敗しました: {Bass.LastError}");
            }
        }
    }
    public double Speed
    {
        get
        {
            EnsureReadyForChannel();
            if (!Enable || _baseFreq <= 0) return 1;
            Bass.ChannelGetAttribute(_stream, ChannelAttribute.Frequency, out float freq);
            return freq / _baseFreq;
        }
        set
        {
            EnsureReadyForChannel();
            if (!Enable) return;
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "速度は正の値でなければなりません。");
            }
            if (_baseFreq <= 0) return;
            float newFreq = _baseFreq * (float)value;
            if (!Bass.ChannelSetAttribute(_stream, ChannelAttribute.Frequency, newFreq))
            {
                throw new InvalidOperationException($"速度の設定に失敗しました: {Bass.LastError}");
            }
        }
    }
    public int Length
    {
        get
        {
            EnsureReadyForChannel();
            if (!Enable) return 0;
            long lengthBytes = Bass.ChannelGetLength(_stream);
            double seconds = Bass.ChannelBytes2Seconds(_stream, lengthBytes);
            return (int)(seconds * 1000); // ミリ秒単位で返す
        }
    }
    public double Time
    {
        get
        {
            EnsureReadyForChannel();
            if (!Enable) return 0;
            long posBytes = Bass.ChannelGetPosition(_stream);
            double seconds = Bass.ChannelBytes2Seconds(_stream, posBytes);
            return seconds * 1000.0;
        }
        set
        {
            EnsureReadyForChannel();
            if (!Enable) return;
            float targetTime = (float)Math.Clamp(value / 1000.0f, 0, Length / 1000.0 - 0.001);
            long bytes = Bass.ChannelSeconds2Bytes(_stream, targetTime);
            if (!Bass.ChannelSetPosition(_stream, bytes))
            {
                Log.Error($"シークに失敗しました: {Bass.LastError}");
            }
        }
    }
}
