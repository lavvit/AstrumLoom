using System.Text;

using AstrumLoom.Audio.Bgm;
using AstrumLoom.Audio.Synth;

namespace AstrumLoom.Audio;

/// <summary>
/// ゲームを起動せずに（描画にもバックエンドにも依らず）AstrumLoom.Audio の合成結果を検算する。
/// PCM を直接調べるだけなので、Sound や IGamePlatform には一切触らない。
///
/// ピーク値や実効値だけでは「音色が違う」ことを検出できない点に注意している。
/// 例えば矩形波はデューティ比を変えてもピークも実効値もほぼ変わらないので、
/// 新しいプリセットを足すときに波形を配線し忘れても、その手の検算だけでは全部 OK に見えてしまう。
/// ここではサンプル列そのもの（のハッシュ）を突き合わせて「2つの音が完全に同一」を見つける。
/// </summary>
public static class AudioSelfCheck
{
    private const double SilenceRmsThreshold = 0.004;
    private const double ClipTolerance = 1.001;
    private const double SeamTolerance = 0.12;
    private const double WavRoundTripTolerance = 2.0 / short.MaxValue + 1e-6;

    public static bool Verify(out string detail)
    {
        var sb = new StringBuilder();
        bool ok = true;

        ok &= CheckPresetsDistinctAndSane(sb);
        ok &= CheckFilterStability(sb);
        ok &= CheckBgmLoop(sb);
        ok &= CheckWavRoundTrip(sb);

        detail = sb.Length == 0 ? "すべての検算に合格しました。" : sb.ToString().TrimEnd();
        return ok;
    }

    // --- 1. 全プリセット: 同一検出 / NaN・Inf件数 / 無音でない / クリップしていない --------------------

    /// <summary>
    /// プリセット間の実効値のばらつき下限。中央値の 1/8 未満なら「他の音に埋もれて聞こえない」とみなして赤くする。
    ///
    /// 根拠（机上で確認・実行はしていない）: 例えば Shot の実効値が 0.15 で中央値が 0.12 だとすると、
    /// このプリセットの Volume だけを 0.05 に落とすと実効値はおおむね比例して 0.15 * (0.05/0.8) ≒ 0.009 まで下がる。
    /// 中央値 0.12 の 1/8 は 0.015 なので 0.009 < 0.015 となり、この検算は確実に赤くなる。
    /// 実際に踏んだ Whoosh のバグ（実効値 0.0186、他は概ね 0.1〜0.3 台）もこの閾値で確実に引っかかる大きさだった。
    /// </summary>
    private const double LevelOutlierRatio = 1.0 / 8.0;

    private static bool CheckPresetsDistinctAndSane(StringBuilder sb)
    {
        bool ok = true;
        var byHash = new Dictionary<ulong, List<SfxId>>();
        var rmsById = new Dictionary<SfxId, double>();

        foreach (var id in SfxBank.All)
        {
            float[] raw = SfxRender.RenderLayers(SfxBank.Get(id));

            // ★ NaN/Inf は「潰す前」に数える。潰した後のコピーで数えると必ず0件になり検算の意味がなくなる。
            int badCount = 0;
            foreach (float s in raw)
                if (float.IsNaN(s) || float.IsInfinity(s)) badCount++;

            if (badCount > 0)
            {
                ok = false;
                sb.AppendLine($"[プリセット {id}] NaN/Inf が {badCount} サンプル含まれています。");
            }

            float[] clean = Sanitize(raw);

            double rms = Rms(clean);
            if (rms < SilenceRmsThreshold)
            {
                ok = false;
                sb.AppendLine($"[プリセット {id}] 実効値 {rms:0.000000} が閾値未満（無音とみなせる）。");
            }
            rmsById[id] = rms;

            double peak = Peak(clean);
            if (peak > ClipTolerance)
            {
                ok = false;
                sb.AppendLine($"[プリセット {id}] ピーク {peak:0.000} がクリップ範囲を超えています。");
            }

            ulong hash = HashSamples(clean);
            if (!byHash.TryGetValue(hash, out var list))
                byHash[hash] = list = [];
            list.Add(id);
        }

        foreach (var (_, ids) in byHash)
        {
            if (ids.Count <= 1) continue;
            ok = false;
            sb.AppendLine($"[プリセット重複] {string.Join(", ", ids)} が完全に同一の波形になっています。配線し忘れの疑いがあります。");
        }

        // プリセット間のレベル差が極端でないか（中央値の 1/8 未満なら「他の音に埋もれて聞こえない」）。
        if (rmsById.Count > 0)
        {
            var sorted = rmsById.Values.OrderBy(v => v).ToArray();
            double median = sorted[sorted.Length / 2];
            if (sorted.Length % 2 == 0 && sorted.Length > 1)
                median = (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2.0;

            double floor = median * LevelOutlierRatio;
            foreach (var (id, rms) in rmsById)
            {
                if (rms >= floor) continue;
                ok = false;
                sb.AppendLine($"[プリセット {id}] 実効値 {rms:0.000000} が全プリセット中央値 {median:0.000000} の 1/8（{floor:0.000000}）未満です。他の音に埋もれて聞こえません。");
            }
        }

        return ok;
    }

    // --- 2. フィルタ: 共振最大でも発散しない ------------------------------------------------------

    private static bool CheckFilterStability(StringBuilder sb)
    {
        var filter = new SvFilter();
        var rng = new Random(12345); // 検算専用の固定シード。乱数系列は Randomize と共有しない。
        bool ok = true;

        for (int i = 0; i < 8000; i++)
        {
            // インパルス + ホワイトノイズという、フィルタにとって最も過酷な入力を共振最大で流し続ける。
            double input = i == 0 ? 1.0 : (rng.NextDouble() * 2.0 - 1.0) * 0.2;
            double cutoff = 200 + 15000.0 * ((i % 400) / 400.0); // カットオフも掃引させる
            double output = filter.Process(input, FilterKind.LowPass, cutoff, resonance: 1.0, sampleRate: SfxRender.SampleRate * 2);

            if (double.IsNaN(output) || double.IsInfinity(output) || Math.Abs(output) > 1000.0)
            {
                ok = false;
                sb.AppendLine($"[フィルタ] 共振最大時にサンプル {i} で発散しました（出力 {output}）。");
                break;
            }
        }

        return ok;
    }

    // --- 3. BGM: 長さちょうど・ループ継ぎ目の段差が小さい ------------------------------------------

    private static bool CheckBgmLoop(StringBuilder sb)
    {
        bool ok = true;
        var score = BuildSelfCheckScore();

        double secPerBeat = 60.0 / score.Bpm;
        int expectedSamples = (int)Math.Round(score.Beats * secPerBeat * Sequencer.SampleRate);

        float[] pcm = Sequencer.Render(score);
        int actualSamples = pcm.Length / 2;

        if (actualSamples != expectedSamples)
        {
            ok = false;
            sb.AppendLine($"[BGM] 長さが期待値と一致しません（期待 {expectedSamples} サンプル、実際 {actualSamples} サンプル）。");
        }

        if (actualSamples > 0)
        {
            double diffL = Math.Abs(pcm[0] - pcm[(actualSamples - 1) * 2]);
            double diffR = Math.Abs(pcm[1] - pcm[(actualSamples - 1) * 2 + 1]);
            if (diffL > SeamTolerance || diffR > SeamTolerance)
            {
                ok = false;
                sb.AppendLine($"[BGM] ループ継ぎ目の段差が大きすぎます（L差 {diffL:0.000}, R差 {diffR:0.000}）。");
            }
        }

        return ok;
    }

    /// <summary>
    /// ループの折り返しを実際に働かせるための検算用スコア。Bell/Pad は release が長く、
    /// ループ終端をまたいで余韻が残るように意図的に配置している（末尾ぎりぎりで音を鳴らす）。
    /// </summary>
    private static BgmScore BuildSelfCheckScore()
    {
        const double bpm = 120;
        const double beats = 8;

        var lead = new BgmTrack
        {
            Instrument = InstrumentKind.Bell,
            Volume = 0.8,
            EchoDelayBeats = 0.5,
            EchoGain = 0.35,
            Notes =
            [
                new BgmNote(0, 1, NoteName.Parse("C4")),
                new BgmNote(2, 1, NoteName.Parse("E4")),
                // ループ終端ぎりぎりで鳴らし、余韻がループを跨ぐ状況を作る。
                new BgmNote(beats - 0.5, 2.0, NoteName.Parse("G4")),
            ],
        };
        var drums = new BgmTrack
        {
            Instrument = InstrumentKind.Kick,
            Volume = 0.9,
            Notes = BgmBuild.DrumPattern("x...x...x...x...", 0.5),
        };

        return new BgmScore { Bpm = bpm, Beats = beats, Tracks = [lead, drums] };
    }

    // --- 4. WAV 往復（float[] → WAV → 読み戻し）が16bit量子化誤差の範囲に収まる -----------------------

    private static bool CheckWavRoundTrip(StringBuilder sb)
    {
        bool ok = true;
        float[] original = Sanitize(SfxRender.RenderLayers(SfxBank.Get(SfxId.Chime)));

        byte[] wav = WavIo.EncodeMono(original, SfxRender.SampleRate);
        float[] roundTripped = WavIo.Decode(wav, out int sr, out int ch);

        if (sr != SfxRender.SampleRate || ch != 1)
        {
            ok = false;
            sb.AppendLine($"[WAV往復] サンプルレート/チャンネル数が変わりました（{sr}Hz, {ch}ch）。");
        }

        if (roundTripped.Length != original.Length)
        {
            ok = false;
            sb.AppendLine($"[WAV往復] サンプル数が変わりました（元 {original.Length}, 読み戻し {roundTripped.Length}）。");
        }
        else
        {
            double maxError = 0;
            for (int i = 0; i < original.Length; i++)
                maxError = Math.Max(maxError, Math.Abs(original[i] - roundTripped[i]));

            if (maxError > WavRoundTripTolerance)
            {
                ok = false;
                sb.AppendLine($"[WAV往復] 量子化誤差が想定より大きい（最大誤差 {maxError:0.000000}）。");
            }
        }

        return ok;
    }

    // --- 補助 -------------------------------------------------------------------------------------

    private static float[] Sanitize(float[] raw)
    {
        var clean = new float[raw.Length];
        for (int i = 0; i < raw.Length; i++)
            clean[i] = float.IsNaN(raw[i]) || float.IsInfinity(raw[i]) ? 0 : raw[i];
        return clean;
    }

    private static double Rms(float[] s)
    {
        if (s.Length == 0) return 0;
        double sum = 0;
        foreach (float v in s) sum += (double)v * v;
        return Math.Sqrt(sum / s.Length);
    }

    private static double Peak(float[] s)
    {
        double peak = 0;
        foreach (float v in s) peak = Math.Max(peak, Math.Abs(v));
        return peak;
    }

    /// <summary>FNV-1a でサンプル列をハッシュする。プリセット同士の完全一致検出専用（暗号用途ではない）。</summary>
    private static ulong HashSamples(float[] samples)
    {
        unchecked
        {
            ulong h = 14695981039346656037UL;
            foreach (float s in samples)
            {
                uint bits = (uint)BitConverter.SingleToInt32Bits(s);
                h ^= bits;
                h *= 1099511628211UL;
            }
            return h;
        }
    }
}
