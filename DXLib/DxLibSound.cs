using static DxLibDLL.DX;
namespace AstrumLoom.DXLib;

public class DxLibSound : AsyncLoadableBase, ISound
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
        DisposeAsync(DisposeSfx);
        GC.SuppressFinalize(this);
    }
    private bool DisposeSfx()
    {
        if (IsMainThread)
        {
            try
            {
                if (Handle > 0)
                    DeleteSoundMem(Handle);
                Handle = -1;
                return true;
            }
            catch { Log.Error($"Failed to unload texture: {Path}"); }
        }
        else
        {
            //Log.Debug($"Texture dispose skipped: not main thread : {Path}");
            AstrumCore.RequestDispose(this);
            return true;
        }
        return false;
    }
    #region 読み込み
    // 第1引数に this（IDisposable）を渡さないと AsyncLoadableBase._obj が null のままになり、
    // Dispose() → DisposeAsync が Host.cs の「_obj == null なら即 return」ガードに引っかかって
    // DisposeSfx が一度も呼ばれない＝DeleteSoundMem に到達せずハンドルが解放されない（DxLibTexture と同型の不具合）。
    public void Load() => LoadAsync(this, LoadSfx);
    private bool LoadSfx()
    {
        bool file = FileCheck(Path);
        if (!file) return false;

        int handle = LoadSoundMem(Path);
        if (handle < 0)
        {
            Log.Debug($"Sound: Load failed: {Path}");
            Handle = -1;
            return false;
        }

        Handle = handle;

        // 長さ取得
        long l = GetSoundTotalTime(Handle);
        Length = (int)l;
        Frequency = GetFrequency();

        WriteState((CheckHandleASyncLoad(Handle) == 0) ? State_Success : State_Loading);
        return true;
    }
    public bool Enable => LoadFinished && Handle > 0;
    public bool IsReady => LoadReady;
    public bool IsFailed => LoadFailed;
    public bool Loaded => LoadFinished;

    public void Pump()
    {
        PumpAsync();
        if (!IsMainThread) return; // メインスレッドでのみ触る

        // 非同期ロードの完了待ち
        if (Loading && CheckHandleASyncLoad(Handle) == 0)
        {
            WriteState(State_Success);
            return;
        }

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
