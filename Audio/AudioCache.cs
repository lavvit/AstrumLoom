using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using AstrumLoom.Audio.Bgm;
using AstrumLoom.Audio.Synth;

namespace AstrumLoom.Audio;

/// <summary>
/// 記述子（SfxLayers / BgmScore）から決定論的なハッシュ（SHA256）を作り、
/// `&lt;AppContext.BaseDirectory&gt;\.audiocache\&lt;hash&gt;.wav` へ合成結果を焼くキャッシュ層。
/// 既にファイルがあれば合成をスキップするので、2 回目以降のコストはファイル存在チェックだけになる。
///
/// ディレクトリを作れない環境（読み取り専用配置、権限不足等）では一時ディレクトリへフォールバックし、
/// それも駄目なら null を返す。呼び出し側（AudioEngine）はこれを「無音扱い」として扱い、
/// 例外でゲームを落とすことはしない。
/// </summary>
public static class AudioCache
{
    private const int SampleRate = SfxRender.SampleRate;

    private static string? _cacheDir;
    private static bool _dirResolutionFailed;

    public static string? GetOrRenderSfx(SfxId id)
    {
        var layers = SfxBank.Get(id);
        string key = "sfx:" + id + "|" + CanonLayers(layers);
        return GetOrRender(key, () => WavIo.EncodeMono(SfxRender.RenderLayers(layers), SampleRate));
    }

    /// <summary>プリセット以外の SfxLayers を鳴らしたい場合の入口。label はログ用の識別文字列（ハッシュには使わない）。</summary>
    public static string? GetOrRenderSfx(SfxLayers layers, string label)
    {
        string key = "sfxcustom:" + CanonLayers(layers);
        return GetOrRender(key, () => WavIo.EncodeMono(SfxRender.RenderLayers(layers), SampleRate), label);
    }

    public static string? GetOrRenderBgm(BgmScore score)
    {
        string key = "bgm:" + CanonScore(score);
        return GetOrRender(key, () => WavIo.EncodeStereo(Sequencer.Render(score), SampleRate), "bgm");
    }

    private static string? GetOrRender(string key, Func<byte[]> render, string label = "")
    {
        string? dir = EnsureDir();
        if (dir == null) return null;

        string hash = Sha256Hex(key);
        string path = Path.Combine(dir, hash + ".wav");
        if (File.Exists(path)) return path;

        try
        {
            byte[] wav = render();
            // 途中で落ちても壊れた（中途半端な）wav を掴まないよう、一時ファイル経由で置き換える。
            string tmp = path + $".tmp{Environment.CurrentManagedThreadId}_{Environment.TickCount64}";
            File.WriteAllBytes(tmp, wav);
            File.Move(tmp, path, overwrite: true);
            return path;
        }
        catch (Exception ex)
        {
            Log.Warning($"音の合成/書き出しに失敗しました（無音扱いにします）{(label.Length > 0 ? $" [{label}]" : "")}: {ex.Message}");
            return null;
        }
    }

    private static string? EnsureDir()
    {
        if (_cacheDir != null) return _cacheDir;
        if (_dirResolutionFailed) return null;

        try
        {
            string dir = Path.Combine(AstrumCore.AppPath, ".audiocache");
            Directory.CreateDirectory(dir);
            _cacheDir = dir;
            return dir;
        }
        catch (Exception ex1)
        {
            Log.Warning($"音声キャッシュディレクトリの作成に失敗（AppPath 配下）。一時ディレクトリへフォールバックします: {ex1.Message}");
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "AstrumLoom_AudioCache");
                Directory.CreateDirectory(dir);
                _cacheDir = dir;
                return dir;
            }
            catch (Exception ex2)
            {
                _dirResolutionFailed = true;
                Log.Warning($"音声キャッシュディレクトリを一時ディレクトリにも作成できませんでした。以後の合成音は無音扱いになります: {ex2.Message}");
                return null;
            }
        }
    }

    private static string Sha256Hex(string key)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var sb = new StringBuilder(hash.Length * 2);
        foreach (byte b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    // --- 決定論的な文字列化。フィールドの並び順を固定し、doubleは"R"（往復可能な表現）で書く。 ---

    private static string D(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    private static string CanonLayers(SfxLayers layers)
    {
        var sb = new StringBuilder();
        foreach (var layer in layers.Layers)
        {
            sb.Append(CanonDesc(layer.Desc)).Append(';')
              .Append(D(layer.OffsetSeconds)).Append(';')
              .Append(D(layer.Volume)).Append('|');
        }
        return sb.ToString();
    }

    private static string CanonDesc(SfxDesc d) => string.Join(',',
        D(d.FreqStart), D(d.FreqEnd), (int)d.FreqSweepCurve,
        (int)d.Wave, D(d.Duty),
        D(d.Duration), D(d.Envelope.Attack), D(d.Envelope.Decay), D(d.Envelope.SustainLevel),
        D(d.Envelope.SustainTime), D(d.Envelope.Release), D(d.Volume),
        D(d.FmRatio), D(d.FmIndex), D(d.FmIndexDecay),
        D(d.DetuneCents), D(d.RingModHz), D(d.Drive),
        d.CrushBits, d.CrushRateDivide,
        (int)d.Filter, D(d.FilterCutoffStart), D(d.FilterCutoffEnd), D(d.FilterResonance),
        D(d.GateHz), D(d.GateDuty), D(d.VibratoHz), D(d.VibratoDepthCents), D(d.Pan));

    private static string CanonScore(BgmScore s)
    {
        var sb = new StringBuilder();
        sb.Append(D(s.Bpm)).Append(';').Append(D(s.Beats)).Append(';').Append(D(s.Swing)).Append('|');
        foreach (var track in s.Tracks)
        {
            sb.Append((int)track.Instrument).Append(',')
              .Append(D(track.Volume)).Append(',')
              .Append(D(track.Pan)).Append(',')
              .Append(D(track.EchoDelayBeats)).Append(',')
              .Append(D(track.EchoGain)).Append(':');
            foreach (var note in track.Notes)
            {
                sb.Append(D(note.StartBeat)).Append('/')
                  .Append(D(note.LengthBeats)).Append('/')
                  .Append(note.MidiNote).Append('/')
                  .Append(D(note.Velocity)).Append(';');
            }
            sb.Append('|');
        }
        return sb.ToString();
    }
}
