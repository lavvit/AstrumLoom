using Raylib_cs;
using static Raylib_cs.Raylib;
using RSound = Raylib_cs.Sound;
namespace AstrumLoom.RayLib;

/// <summary>SEはSound、ストリームはMusicのみを所有する。ネイティブ操作はメインスレッド。</summary>
public class RayLibSound : AsyncLoadableBase, ISound
{
    public string Path { get; }
    public RSound Sfx { get; private set; }
    public Music Music { get; private set; }
    public int Frequency { get; private set; }
    public int Length { get; private set; }
    private bool _streaming, _played, _loop;
    private byte[]? _bytes;
    private double _time;
    private double? _startTime;
    private float _volume = 1, _pan, _speed = 1;
    private long _lastTicks = System.Diagnostics.Stopwatch.GetTimestamp();
    private bool HasSound => Sfx.FrameCount > 0;
    private bool HasMusic => Music.FrameCount > 0;
    public bool Enable => LoadReady && (HasSound || HasMusic);
    public bool IsReady => LoadReady;
    public bool IsFailed => LoadFailed;
    public bool Loaded => LoadFinished;
    public RayLibSound(string path, bool streaming = true) { Path = path; Load(streaming); }
    ~RayLibSound() => Dispose();
    public void Dispose() { DisposeAsync(DisposeSfx); GC.SuppressFinalize(this); }
    public bool DisposeSfx()
    {
        if (IsAudioDeviceReady())
        {
            if (HasSound) UnloadSound(Sfx);
            if (HasMusic) UnloadMusicStream(Music);
        }
        Sfx = default;
        Music = default;
        _bytes = null;
        _played = false;
        return true;
    }
    public void Load(bool streaming = true)
    {
        _streaming = streaming;
        LoadAsync(this, LoadNative, streaming ? null : () => { _bytes = File.ReadAllBytes(Path); return true; });
    }
    private bool LoadNative()
    {
        if (!FileCheck(Path)) return false;
        if (_streaming)
        {
            Music = LoadMusicStream(Path);
            if (!HasMusic) return false;
            var music = Music;
            music.Looping = _loop;
            Music = music;
            Frequency = (int)Music.Stream.SampleRate;
            Length = (int)System.Math.Round(GetMusicTimeLength(Music) * 1000.0);
        }
        else
        {
            var wave = _bytes != null ? LoadWaveFromMemory(System.IO.Path.GetExtension(Path), _bytes) : LoadWave(Path);
            try
            {
                if (wave.SampleCount == 0 || wave.SampleRate == 0) return false;
                Frequency = (int)wave.SampleRate;
                Length = (int)System.Math.Round(wave.SampleCount * 1000.0 / wave.SampleRate);
                Sfx = LoadSoundFromWave(wave);
            }
            finally { UnloadWave(wave); _bytes = null; }
            if (!HasSound) return false;
        }
        ApplyProperties();
        return true;
    }
    private void ApplyProperties()
    {
        if (HasSound)
        {
            SetSoundVolume(Sfx, _volume); SetSoundPan(Sfx, 0.5f - 0.5f * _pan); SetSoundPitch(Sfx, _speed);
        }
        if (HasMusic)
        {
            SetMusicVolume(Music, _volume); SetMusicPan(Music, 0.5f - 0.5f * _pan); SetMusicPitch(Music, _speed);
        }
    }
    public void Pump() => Update();
    public void Update()
    {
        if (!IsMainThread) return;
        PumpAsync();
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        double elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(_lastTicks, now).TotalMilliseconds;
        _lastTicks = now;
        if (!Enable || !_played) return;
        if (HasMusic)
        {
            UpdateMusicStream(Music);
            if (IsMusicStreamPlaying(Music)) _time = GetMusicTimePlayed(Music) * 1000.0;
            else _time = Length;
        }
        else if (IsSoundPlaying(Sfx)) _time = System.Math.Min(Length, _time + elapsed * _speed);
        else if (_loop) { PlaySound(Sfx); _time = 0; }
        else _time = Length;
    }
    public double Time
    {
        get => _time;
        set
        {
            if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            if (!Enable) return;
            _time = System.Math.Clamp(value, 0, Length);
            _startTime = _played ? null : _time;
            if (HasMusic) SeekMusicStream(Music, (float)(_time / 1000.0));
        }
    }
    public double Volume
    {
        get => _volume;
        set { _volume = (float)System.Math.Clamp(value, 0, 1); if (Enable) ApplyProperties(); }
    }
    public double Pan
    {
        get => _pan;
        set { _pan = (float)System.Math.Clamp(value, -1, 1); if (Enable) ApplyProperties(); }
    }
    public double Speed
    {
        get => _speed;
        set { _speed = (float)System.Math.Clamp(value, 1.0 / 64, 64); if (Enable) ApplyProperties(); }
    }
    /// <summary>周波数倍率。テンポ独立のピッチシフトではない。</summary>
    public double Pitch { get => Speed; set => Speed = value; }
    public bool IsPlaying => Enable && (HasMusic ? IsMusicStreamPlaying(Music) : IsSoundPlaying(Sfx));
    public bool Loop
    {
        get => _loop;
        set { _loop = value; if (HasMusic) { var music = Music; music.Looping = value; Music = music; } }
    }
    public void Play()
    {
        if (!Enable) return;
        if (HasMusic) { StopMusicStream(Music); PlayMusicStream(Music); }
        else PlaySound(Sfx);
        _time = _startTime ?? 0;
        _startTime = null;
        if (HasMusic && _time > 0) SeekMusicStream(Music, (float)(_time / 1000.0));
        _lastTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        _played = true;
    }
    public void Stop()
    {
        if (!Enable) return;
        if (HasMusic) StopMusicStream(Music); else StopSound(Sfx);
        _played = false;
        _time = 0;
        _startTime = null;
    }
    public void PlayStream()
    {
        Pump();
        if (Enable && !_played) Play();
    }
}
