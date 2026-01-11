using Raylib_cs;

using static Raylib_cs.Raylib;

using RSound = Raylib_cs.Sound;
namespace AstrumLoom.RayLib;

public class RayLibSound : AsyncLoadableBase, ISound
{
    public string Path { get; private set; } = "";
    public RSound Sfx { get; private set; }
    public Music Music { get; private set; }
    public int Frequency { get; private set; } = 0;
    public int Length { get; private set; } = 0;

    public RayLibSound(string path, bool streaming = true)
    {
        Path = path;
        Load(streaming);
    }
    ~RayLibSound()
    {
        Dispose();
    }
    public void Dispose()
    {
        DisposeAsync(DisposeSfx);
        GC.SuppressFinalize(this);
    }
    public bool DisposeSfx()
    {
        if (!Raylib.IsWindowReady())
        {
            Log.Debug($"Sound dispose skipped: window not ready : {Path}");
            // ウィンドウ未準備でもマネージ側は終了扱いにしてファイナライザ再入を避ける
            Sfx = default;
            Music = default;
            return true;
        }

        if (IsMainThread)
        {
            try
            {
                if (_loaded)
                    UnloadSound(Sfx);
                if (_streamloaded)
                    UnloadMusicStream(Music);
                Sfx = default;
                Music = default;
            }
            catch { Log.Error($"Failed to unload sound: {Path}"); }
        }
        else
            AstrumCore.RequestDispose(this);
        return true;
    }
    private bool _loaded => Sfx.FrameCount > 0;
    private bool _streamloaded => Music.FrameCount > 0;

    #region 読み込み
    public void Load(bool streaming = true)
        => LoadAsync(this, () => LoadSfx(streaming), () => LoadBackGround(streaming));
    private bool LoadSfx(bool streaming)
    {
        bool file = FileCheck(Path);
        if (!file) return false;

        // PNG/JPG/BMP等そのままOK
        Sfx = LoadSound(Path);
        Music = LoadMusicStream(Path);

        // 初期状態をセット
        if (!_loaded)
        {
            return false;
        }

        // 長さ取得
        float l = GetMusicTimeLength(Music) * 1000.0f;
        Length = (int)l;

        if (!streaming)
        {
            // メモリを節約するために Music を解放
            UnloadMusicStream(Music);
            Music = default;
        }
        return true;
    }
    private bool LoadBackGround(bool streaming)
    {
        try
        {
            _pendingBytes = File.ReadAllBytes(Path);
            _pendingExt = System.IO.Path.GetExtension(Path).ToLowerInvariant();
            return true;
        }
        catch
        {
            _pendingBytes = null;
            return false;
        }
    }
    private byte[]? _pendingBytes;
    private string? _pendingExt; // ".png" ".ogg" など

    public bool Enable => LoadFinished && _loaded;
    public bool IsReady => LoadReady;
    public bool IsFailed => LoadFailed;
    public bool Loaded => LoadFinished;

    public void Pump()
    {
        PumpAsync();
        if (!IsMainThread) return; // メインスレッドでのみ触る

        // 非同期ロードの完了待ち
        if (_pendingBytes != null)
        {
            try
            {
                // バイト列 → Wave → Sound
                var wave = LoadWaveFromMemory(_pendingExt ?? ".wav", _pendingBytes);
                Sfx = LoadSoundFromWave(wave);
                UnloadWave(wave);

                // BGM用に Music も（ファイルパスからでOK）※必要なら別APIに分けても良い
                Music = LoadMusicStream(Path);

                WriteState(State_Success);
            }
            catch { WriteState(State_Failed); }
            finally { _pendingBytes = null; _pendingExt = null; }
            return;
        }

        if (_loaded)
        {
            if (Length == 0)
            {
                // サイズ取得
                float l = GetMusicTimeLength(Music) * 1000.0f;
                Length = (int)l;
            }
            if (Frequency == 0)
            {
                //Frequency = GetFrequency();
            }
        }
    }
    #endregion
    #region プロパティ
    private bool _played = false;
    private bool _streaming = false;
    private double _time;
    private float _volume = 1.0f;
    private float _pan = 0.0f;
    private float _speed = 1.0f;
    public void Update()
    {
        Pump();
        if (!Enable) return;
        if (_played)
        {
            if (_streamloaded)
            {
                UpdateMusicStream(Music);

                bool playing = IsMusicStreamPlaying(Music);
                if (playing && _streamloaded)
                {
                    _streaming = true;
                    _time = Math.Clamp(GetMusicTimePlayed(Music) * 1000.0 - 0.5, 0, Length);
                    // 任意: ループポイント処理（TimeがEndを越えたらStartにSeek）
                    if (Loop)
                    {/*
                        double end = (LoopEndMs >= 0 ? LoopEndMs : Length);
                        if (end - _timeMs <= 16 && end > LoopStartMs)
                            Time = LoopStartMs;*/
                    }
                    else
                    {
                        if (Length - _time <= 16)
                        {
                            StopMusicStream(Music);
                            return;
                        }
                    }
                    return;
                }
                _streaming = false;
            }
            else
            {
                // 毎フレーム呼んで経過時間を積む（Raylib Sound には再生時間APIが無い）
                bool playing = IsSoundPlaying(Sfx);
                if (playing)
                {
                    _streaming = true;
                    _time += GetFrameTime() * 1000.0;
                    if (Length > 0 && _time > Length) _time = Length;
                }
                _streaming = false;
            }
        }
        else
        {
            if (Loop) // ループ時にフラグをリセットして再生
                _played = false;
            _streaming = false;
            _time = 0;
        }
    }
    public double Time
    {
        get => _time;
        set
        {
            if (!Enable) return;
            if (Math.Abs(_time - value) < 16.0) return;
            _time = Math.Clamp(value, 0, Length);
            if (_streamloaded)
                SeekMusicStream(Music, (float)_time / 1000.0f);
        }
    }
    public double Volume
    {
        get => _volume;
        set
        {
            if (!Enable) return;
            _volume = (float)Math.Max(value, 0.0);
            SetSoundVolume(Sfx, _volume);
            if (_streamloaded)
                SetMusicVolume(Music, _volume);
        }
    }
    public double Pan
    {
        get => _pan;
        set
        {
            if (!Enable) return;
            _pan = (float)Math.Clamp(value, -1.0, 1.0);
            SetSoundPan(Sfx, 0.5f + 0.5f * -_pan);
            if (_streamloaded)
                SetMusicPan(Music, 0.5f + 0.5f * -_pan);
        }
    }
    public double Speed
    {
        get => _speed;
        set
        {
            if (!Enable) return;
            double max = 64.0;
            _speed = (float)Math.Clamp(value, 1.0 / max, max);
            SetSoundPitch(Sfx, _speed);
            if (_streamloaded)
                SetMusicPitch(Music, _speed);
        }
    }
    public double Pitch
    {
        get => Speed;
        set => Speed = value;
    }
    public bool IsPlaying => _streamloaded ? IsMusicStreamPlaying(Music) != 0 : IsSoundPlaying(Sfx) != 0;
    public bool Loop { get; set; } = false;
    #endregion

    public void Play()
    {
        if (!Enable) return;
        _time = 0;
        if (_streamloaded)
        {
            PlayMusicStream(Music);
        }
        else
        {
            PlaySound(Sfx);
        }
        _played = true;
    }
    public void Stop()
    {
        if (!Enable) return;
        if (_streamloaded)
        {
            StopMusicStream(Music);
            SeekMusicStream(Music, 0.0f);
        }
        else
        {
            StopSound(Sfx);
        }
        _played = false;
        _streaming = false;
        _time = 0;
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
