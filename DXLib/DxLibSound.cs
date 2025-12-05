using static DxLibDLL.DX;
namespace AstrumLoom.DXLib;

public class DxLibSound : ISound
{
    public string Path { get; private set; } = "";
    public int Handle { get; private set; } = -1;
    public int Frequency { get; private set; } = 0;
    public int Length { get; private set; } = 0;

    public DxLibSound(string path, bool streaming = true)
    {
        Path = path;
        Load();
    }
    ~DxLibSound()
    {
        Dispose();
    }
    public void Dispose()
    {
        if (Handle > 0)
        {
            DeleteSoundMem(Handle);
        }
        Handle = -1;
        _asyncState = -1;
        GC.SuppressFinalize(this);
    }
    #region 読み込み
    public void Load()
    {
        if (!File.Exists(Path))
        {
            Log.Debug($"Sound: not found: {Path}");
            Volatile.Write(ref _asyncState, -1);
            Handle = -1;
            return;
        }
        // メインスレッドでのみ触る
        if (!IsMainThread)
        {
            _deferred = true;
            _asyncState = 0;   // Loading扱い
            return;
        }
        else
        {
            int handle = LoadSoundMem(Path);
            if (handle < 0)
            {
                Log.Debug($"Sound: Load failed: {Path}");
                Volatile.Write(ref _asyncState, -1);
                Handle = -1;
                return;
            }

            Handle = handle;
            _startTicks = Environment.TickCount64;

            // 長さ取得
            long l = GetSoundTotalTime(Handle);
            Length = (int)l;
            Frequency = GetFrequency();

            Volatile.Write(ref _asyncState, (CheckHandleASyncLoad(Handle) == 0) ? 1 : 0);
        }
    }

    // 0=Loading, 1=Ready, -1=Failed
    private int _asyncState = -1;
    public bool IsReady => Volatile.Read(ref _asyncState) == 1;
    public bool IsFailed => Volatile.Read(ref _asyncState) == -1;
    public bool Loaded
    {
        get
        {
            Pump(); // 毎フレーム呼ぶのを忘れた場合に備えてここでも呼ぶ
            return Volatile.Read(ref _asyncState) != 0;
        }
    }
    public bool Enable => Handle > 0 && Loaded;
    private static bool IsMainThread => Environment.CurrentManagedThreadId == AstrumCore.MainThreadId;
    private bool _deferred;
    private long _startTicks;
    private const int DefaultTimeoutMs = 60000;
    public int TimeoutMs { get; set; } = DefaultTimeoutMs;

    public void Pump()
    {
        // メインスレッドでのみ触る
        if (!IsMainThread) return;

        if (Handle > 0)
        {
            if (Length == 0)
            {
                // 長さ取得
                long l = GetSoundTotalTime(Handle);
                Length = (int)l;
            }
            if (Frequency == 0)
            {
                Frequency = GetFrequency();
            }
        }

        // 保留中ならメインスレッドでロード開始
        if (_deferred)
        {
            _deferred = false;
            Load();
            return;
        }
        if (Volatile.Read(ref _asyncState) != 0) return; // Loading 以外は何もしない

        // 非同期ロードの完了待ち
        if (CheckHandleASyncLoad(Handle) == 0)
        {
            Volatile.Write(ref _asyncState, 1); // Ready
            return;
        }
        // タイムアウト判定
        long elapsed = Environment.TickCount64 - _startTicks;
        if (TimeoutMs > 0 && elapsed >= TimeoutMs)
        {
            Log.Debug($"Sound: Load timeout: {Path}");
            Dispose();
            return;
        }
    }
    #endregion
    #region プロパティ
    private bool _played = false;
    private bool _streaming = false;
    private long _time;
    private float _volume = 1.0f;
    private float _pan = 0.0f;
    private float _speed = 1.0f;
    public void Update()
    {
        Pump();
        if (!Enable) return;
        if (_played)
        {
            bool playing = CheckSoundMem(Handle) != 0;
            if (playing)
            {
                _streaming = true;
                _time = GetSoundCurrentTime(Handle);
                _speed = (float)GetFrequency() / Frequency;
                return;
            }
            if (Loop) // ループ時にフラグをリセットして再生
                _played = false;
        }
        else
        {
            _streaming = false;
            _time = 0;
        }
    }
    public double Time
    {
        get => _time;
        set
        {
            if (Math.Abs(_time - value) < 16.0) return;
            _time = (long)Math.Clamp(value, 0, Length);
            SetSoundCurrentTime(_time, Handle);
        }
    }
    public double Volume
    {
        get => _volume;
        set
        {
            _volume = (float)Math.Max(value, 0.0);
            ChangeVolumeSoundMem((int)(_volume * 255), Handle);
        }
    }
    public double Pan
    {
        get => _pan;
        set
        {
            _pan = (float)Math.Clamp(value, -1.0, 1.0);
            ChangePanSoundMem((int)(_pan * 255.0), Handle);
        }
    }
    public double Speed
    {
        get => _speed;
        set
        {
            double max = 64.0;
            _speed = (float)Math.Clamp(value, 1.0 / max, max);
            ResetFrequencySoundMem(Handle);
            float frequency = GetFrequencySoundMem(Handle);
            SetFrequencySoundMem((int)(frequency * _speed), Handle);
        }
    }
    private int GetFrequency()
    {
        int freq = GetFrequencySoundMem(Handle);
        if (freq > 0) return freq;
        long sample = GetSoundTotalSample(Handle);
        long len = GetSoundTotalSample(Handle);
        return sample > -1 ? (int)((double)sample / len) : 44100;
    }
    public double Pitch
    {
        get => Speed; // DxLib does not support pitch control
        set => Speed = value;
    }
    public bool IsPlaying => CheckSoundMem(Handle) != 0;
    public bool Loop { get; set; } = false;
    #endregion

    public void Play()
    {
        if (!Enable) return;
        _time = 0;
        PlaySoundMem(Handle, DX_PLAYTYPE_BACK, TRUE);
        _played = true;
    }
    public void Stop()
    {
        if (!Enable) return;
        StopSoundMem(Handle);
        _played = false;
    }
    public void PlayStream()
    {
        if (!Enable) return;
        if (_played)
        {
            Update();
            return;
        }
        Play();
    }
}
