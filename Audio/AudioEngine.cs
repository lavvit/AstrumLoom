using System.Collections.Concurrent;

using AstrumLoom.Audio.Bgm;

namespace AstrumLoom.Audio;

/// <summary>
/// AstrumLoom.Audio の公開 API の顔。static class Audio 経由で効果音と BGM を鳴らす。
///
/// 再生要求をキューに積み、メインループが描画の前に処理する。
/// </summary>
public static class Audio
{
    static Audio()
    {
        AstrumCore.MainThreadTick += Update;
        AstrumCore.ShuttingDown += Shutdown;
    }
    public static int MaxQueuedRequests { get; set; } = 4096;
    public static int MaxJobsPerUpdate { get; set; } = 128;
    private static int _queued;
    private static long _rejected;
    public static long RejectedRequests => Interlocked.Read(ref _rejected);
    private static void Enqueue(Action action)
    {
        if (Interlocked.Increment(ref _queued) > System.Math.Max(1, MaxQueuedRequests))
        {
            Interlocked.Decrement(ref _queued);
            Interlocked.Increment(ref _rejected);
            return;
        }
        _jobs.Enqueue(action);
    }
    private const int VoicesPerId = 4;
    /// <summary>ロード中でまだ鳴らせない要求を、諦めずに再挑戦し続ける上限秒数。無限に溜めないための保険。</summary>
    private const double PendingTimeoutSeconds = 2.0;

    private static readonly ConcurrentQueue<Action> _jobs = new();

    // ボイスプール。同じ効果音が重なって鳴らせるよう、id ごとに Sound を複数持ってラウンドロビンする。
    // どちらも Update() （メインスレッド）からしか触らないので lock は要らない。
    private static readonly Dictionary<SfxId, Sound[]> _voices = [];
    private static readonly Dictionary<SfxId, int> _nextVoice = [];

    // 未ロードで鳴らせなかった要求。Update() から毎フレーム再挑戦し、一定時間で諦める。
    private static readonly List<PendingPlay> _pending = [];

    private static Sound? _bgm;
    private static bool _bgmWanted;

    public static double MasterVolume { get; set; } = 1.0;
    public static double SfxVolume { get; set; } = 1.0;
    public static double BgmVolume { get; set; } = 1.0;
    public static bool Muted { get; set; } = false;

    private readonly record struct PendingPlay(SfxId Id, double Volume, double Pitch, double Pan, long Started);

    /// <summary>指定した効果音を先に合成・ロードしておく。ロード中の取りこぼしを避けたいなら Play() の前に呼ぶ。</summary>
    public static void Prewarm(params SfxId[] ids)
    {
        foreach (var id in ids)
            Enqueue(() => EnsureVoices(id));
    }

    /// <summary>SfxBank.All を丸ごと Prewarm する。</summary>
    public static void PrewarmAll() => Prewarm([.. SfxBank.All]);

    private static Sound[] EnsureVoices(SfxId id)
    {
        if (_voices.TryGetValue(id, out var voices)) return voices;

        string? path = AudioCache.GetOrRenderSfx(id);
        if (path == null)
        {
            // 合成/書き出しに失敗＝無音扱い。空配列を覚えておいて以降は何もしない。
            voices = [];
            _voices[id] = voices;
            return voices;
        }

        voices = new Sound[VoicesPerId];
        for (int i = 0; i < VoicesPerId; i++) voices[i] = new Sound(path);
        _voices[id] = voices;
        _nextVoice[id] = 0;
        return voices;
    }

    /// <summary>効果音を鳴らす。どのスレッドから呼んでもよい（実際の再生は次の Update() へ回る）。</summary>
    public static void Play(SfxId id, double volume = 1, double pitch = 1, double pan = 0)
        => Enqueue(() => RequestPlay(id, volume, pitch, pan));

    private static void RequestPlay(SfxId id, double volume, double pitch, double pan)
    {
        var voices = EnsureVoices(id);
        if (!TryPlayVoice(voices, id, volume, pitch, pan))
                    {
            if (_pending.Count >= System.Math.Max(1, MaxQueuedRequests)) Interlocked.Increment(ref _rejected);
            else _pending.Add(new PendingPlay(id, volume, pitch, pan, System.Diagnostics.Stopwatch.GetTimestamp()));
        }
    }

    /// <summary>いずれかのボイスが鳴らせれば true。全部未ロードなら false（まだ鳴らせない）。</summary>
    private static bool TryPlayVoice(Sound[] voices, SfxId id, double volume, double pitch, double pan)
    {
        if (voices.Length == 0) return true; // 無音扱いは「処理済み」として握りつぶす

        bool anyReady = false;
        foreach (var v in voices) { if (v.Enable) { anyReady = true; break; } }
        if (!anyReady) return false;

        int start = _nextVoice.TryGetValue(id, out int n) ? n : 0;
        int chosen = -1;
        // 鳴っていないボイスを優先して選ぶ。全部鳴っていたら次のものを使い、頭から鳴り直させる。
        for (int i = 0; i < voices.Length; i++)
        {
            int idx = (start + i) % voices.Length;
            if (voices[idx].Enable && !voices[idx].Playing) { chosen = idx; break; }
        }
        if (chosen < 0) chosen = start % voices.Length;
        if (!voices[chosen].Enable) return false;

        var voice = voices[chosen];
        voice.Volume = EffectiveVolume(volume * SfxVolume);
        voice.Pan = Math.Clamp(pan, -1, 1);
        voice.Speed = Math.Max(0.01, pitch);
        voice.Play();
        _nextVoice[id] = (chosen + 1) % voices.Length;
        return true;
    }

    /// <summary>BGM を再生する。既に鳴っている BGM があれば止めてから差し替える。</summary>
    public static void PlayBgm(BgmScore score, double volume = 1)
        => Enqueue(() =>
        {
            string? path = AudioCache.GetOrRenderBgm(score);
            _bgm?.Stop();
            _bgm?.Dispose();
            _bgm = path != null ? new Sound(path, stream: true) : null;
            BgmVolume = volume;
            _bgmWanted = path != null;
        });

    public static void StopBgm() => Enqueue(() =>
    {
        _bgm?.Stop();
        _bgmWanted = false;
    });

    /// <summary>メインループから自動で呼ばれる。独自ホストではメインスレッドから呼ぶ。</summary>
    public static void Update()
    {
        if (Environment.CurrentManagedThreadId != AstrumCore.MainThreadId) return;
        int count = System.Math.Min(_jobs.Count, System.Math.Max(1, MaxJobsPerUpdate));
        for (int i = 0; i < count && _jobs.TryDequeue(out var job); i++)
        {
            Interlocked.Decrement(ref _queued);
            try { job(); }
            catch (Exception e) { Log.Error("Audio の操作に失敗しました: " + e.Message); }
        }

        PumpPending();
        PumpBgm();
    }

    private static void PumpPending()
    {
        if (_pending.Count == 0) return;

        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            var p = _pending[i];
            var voices = EnsureVoices(p.Id);
            if (TryPlayVoice(voices, p.Id, p.Volume, p.Pitch, p.Pan))
            {
                _pending.RemoveAt(i);
                continue;
            }

            double waited = System.Diagnostics.Stopwatch.GetElapsedTime(p.Started).TotalSeconds;
            if (waited >= PendingTimeoutSeconds)
                _pending.RemoveAt(i); // 諦める。無限に溜めない。

        }
    }

    private static void PumpBgm()
    {
        if (_bgm == null) return;
        _bgm.Loop = true;
        _bgm.Volume = EffectiveVolume(BgmVolume);
        // Loop=true でも Play() 一発では2周目が来ない。毎フレーム PlayStream を呼ぶのが正しい使い方
        // （Sandbox\SoundDemo.cs と同じ罠）。
        if (_bgmWanted) _bgm.PlayStream();
    }

    private static double EffectiveVolume(double baseVolume)
        => Muted ? 0 : Math.Clamp(baseVolume * MasterVolume, 0, 1);

    /// <summary>全ボイス・BGM を破棄する。ゲーム終了時に呼ぶこと。</summary>
    public static void Shutdown()
    {
        while (_jobs.TryDequeue(out _)) Interlocked.Decrement(ref _queued);
        _pending.Clear();

        foreach (var voices in _voices.Values)
            foreach (var v in voices)
                v.Dispose();
        _voices.Clear();
        _nextVoice.Clear();

        _bgm?.Stop();
        _bgm?.Dispose();
        _bgm = null;
        _bgmWanted = false;
    }
}
