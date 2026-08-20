using Raylib_cs;

using static Raylib_cs.Raylib;

using RSound = Raylib_cs.Sound;
namespace AstrumLoom.RayLib;

/// <summary>
/// ISound の raylib 実装。AsyncLoadableBase の非同期ロード基盤に乗り、ファイル読み込みはバックグラウンドスレッドで
/// バイト列だけ取得し、実際の Sound/Music オブジェクト生成はメインスレッドの Pump() で行う。
/// streaming=true のときは Music（ストリーム再生）を、false のときは Sfx（単発再生）を主に使う。
/// </summary>
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
    /// <summary>Sfx/Musicのネイティブリソースを解放する。メインスレッド以外から呼ばれた場合はAstrumCoreにメインスレッドでの破棄を依頼する。</summary>
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
    /// <summary>非同期ロードを開始する。メインスレッドならLoadSfxを即実行、そうでなければLoadBackGroundでバイト列だけ先に読んでおく。
    /// streaming は Pump() の非同期完了処理でも参照するため、ここでフィールドに控えておく。</summary>
    public void Load(bool streaming = true)
    {
        _streaming = streaming;
        LoadAsync(this, () => LoadSfx(streaming), () => LoadBackGround(streaming));
    }
    /// <summary>メインスレッドから直接パスを読み込む経路。Sfx/Musicの両方をraylib APIで生成し、streaming=falseならMusicは即解放する。</summary>
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
    /// <summary>バックグラウンドスレッドから呼ばれる経路。raylibのネイティブAPIはメインスレッド専用なので、ここではファイルをバイト列として読むだけに留める。</summary>
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

    /// <summary>毎フレーム呼び出す。バックグラウンドで読み込んだバイト列が届いていれば、ここでSound/Musicへ変換して読み込みを完了させる。</summary>
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
                if (!_streaming)
                {
                    // LoadSfx（同期経路）と同じく、streaming=false 指定時は Music を常駐させない
                    UnloadMusicStream(Music);
                    Music = default;
                }

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
    // Load() 時の streaming 指定を Pump() の非同期完了処理からも参照するために保持する
    // （以前は書き込むだけで一度も読まれておらず、CS0414相当の死んだフィールドだった）。
    private bool _streaming = false;
    private double _time;
    private float _volume = 1.0f;
    private float _pan = 0.0f;
    private float _speed = 1.0f;
    /// <summary>
    /// 毎フレーム呼び出す。ストリーム再生（Music）ならraylibのUpdateMusicStreamを回して再生時間を取得し、
    /// 単発再生（Sfx）にはraylib側に再生時間APIが無いため、経過時間を自前で積算して代用する。
    /// </summary>
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
            }
            else
            {
                // 毎フレーム呼んで経過時間を積む（Raylib Sound には再生時間APIが無い）
                bool playing = IsSoundPlaying(Sfx);
                if (playing)
                {
                    _time += GetFrameTime() * 1000.0;
                    if (Length > 0 && _time > Length) _time = Length;
                }
                else
                {
                    // SEの自然な再生終了。_played を落とし、Loopなら即座に鳴らし直す
                    // （ここでリセットするだけだと else 節（本メソッド末尾）へは次フレームまで
                    // 到達しないため、ループ再開が1フレーム遅れるだけでなく、そもそもここで
                    // _played を戻さない限り一生else節に到達できなかった）。
                    _played = false;
                    _time = 0;
                    if (Loop)
                    {
                        PlaySound(Sfx);
                        _played = true;
                    }
                }
            }
        }
        else
        {
            if (Loop) // ループ時にフラグをリセットして再生
                _played = false;
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

    /// <summary>再生位置を先頭に戻して再生を開始します。ストリーム/単発のどちらを使うかは読み込み方式に応じて自動選択されます。</summary>
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
    /// <summary>再生を停止し、再生位置・状態をリセットします。</summary>
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
        _time = 0;
    }
    /// <summary>再生済みならストリーム更新のみ行い、未再生ならPlay()から開始します（BGMループ等の毎フレーム呼び出し向け）。</summary>
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
