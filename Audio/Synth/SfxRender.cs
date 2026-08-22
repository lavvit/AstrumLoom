namespace AstrumLoom.Audio.Synth;

/// <summary>
/// SfxDesc / SfxLayers を実際の PCM（float、-1..1、モノラル、44100Hz）へ変換する。
///
/// 1 層のレンダリングは純粋関数（同じ SfxDesc からは常に同じサンプル列が出る）。
/// ノイズ系波形の乱数だけは例外的に状態を持つが、その乱数のシード自体を SfxDesc の内容から
/// 決定論的に導出しているので、結果として全体はやはり純粋になる（AudioCache のキャッシュが前提にしている性質）。
/// </summary>
public static class SfxRender
{
    public const int SampleRate = 44100;

    /// <summary>最大 3 層を合成してモノラル PCM を作る。層ごとのオフセット秒だけずらして加算し、最後にソフトクリップする。</summary>
    public static float[] RenderLayers(SfxLayers layers)
    {
        var rendered = new (float[] Samples, int OffsetSamples, double Volume)[layers.Layers.Count];
        int totalLen = 0;
        for (int i = 0; i < layers.Layers.Count; i++)
        {
            var layer = layers.Layers[i];
            float[] samples = RenderSingle(layer.Desc, StableSeed(layer.Desc, i));
            int offset = (int)Math.Round(Math.Max(0, layer.OffsetSeconds) * SampleRate);
            rendered[i] = (samples, offset, layer.Volume);
            totalLen = Math.Max(totalLen, offset + samples.Length);
        }

        var mix = new float[Math.Max(1, totalLen)];
        foreach (var (samples, offset, volume) in rendered)
        {
            for (int i = 0; i < samples.Length; i++)
            {
                int di = offset + i;
                if (di >= mix.Length) break;
                mix[di] += (float)(samples[i] * volume);
            }
        }

        for (int i = 0; i < mix.Length; i++)
            mix[i] = (float)SoftClip(mix[i]);

        return mix;
    }

    /// <summary>1 つの SfxDesc をモノラル PCM に焼く。シードは SfxDesc の内容から自動で決める。</summary>
    public static float[] RenderSingle(SfxDesc d) => RenderSingle(d, StableSeed(d, 0));

    /// <summary>1 つの SfxDesc をモノラル PCM に焼く（シード指定版。BGM のように同じ音色を多数鳴らすときに使う）。</summary>
    public static float[] RenderSingle(SfxDesc d, int seed)
    {
        int n = Math.Max(1, (int)Math.Round(d.Duration * SampleRate));
        var buffer = new float[n];

        NoiseState? noise = Wave.IsNoise(d.Wave) ? new NoiseState(seed) : null;
        NoiseState? noise2 = d.DetuneCents != 0 && Wave.IsNoise(d.Wave) ? new NoiseState(seed ^ 0x5bd1e995) : null;

        double phaseAcc = 0, phaseAcc2 = 0, modPhaseAcc = 0, ringPhaseAcc = 0;
        var filter = d.Filter != FilterKind.None ? new SvFilter() : null;

        // ビットクラッシュのサンプル&ホールド用。
        int crushDivide = Math.Max(1, d.CrushRateDivide);
        double crushHeld = 0;
        int crushCounter = 0;

        for (int i = 0; i < n; i++)
        {
            double t = i / (double)SampleRate;
            double progress = d.Duration > 0 ? Math.Clamp(t / d.Duration, 0, 1) : 0;

            double freq = Sweep.Value(d.FreqStart, d.FreqEnd, progress, d.FreqSweepCurve);
            if (d.VibratoHz != 0 && d.VibratoDepthCents != 0)
            {
                double vibRatio = Math.Pow(2.0, d.VibratoDepthCents / 1200.0 * Math.Sin(2 * Math.PI * d.VibratoHz * t));
                freq *= vibRatio;
            }
            freq = Math.Max(1, freq);
            double phaseStep = freq / SampleRate;
            phaseAcc += phaseStep;

            // 2op FM: モジュレータでキャリアの位相を歪ませる。どの波形でも使える一般化のため
            // 「位相へ加算」という形でかけている（正弦波キャリアに対する教科書的な FM と同じ式になる）。
            double carrierPhase = phaseAcc;
            if (d.FmIndex != 0)
            {
                double modFreq = Math.Max(0.01, freq * (d.FmRatio <= 0 ? 1 : d.FmRatio));
                modPhaseAcc += modFreq / SampleRate;
                double modVal = Math.Sin(2 * Math.PI * modPhaseAcc);
                double indexNow = d.FmIndex * (1.0 - Math.Clamp(d.FmIndexDecay, 0, 1) * progress);
                carrierPhase += indexNow * modVal / (2 * Math.PI);
            }

            double osc = ComputeOsc(d.Wave, carrierPhase, d.Duty, noise, phaseStep);

            if (d.DetuneCents != 0)
            {
                double freq2 = freq * Math.Pow(2.0, d.DetuneCents / 1200.0);
                phaseAcc2 += freq2 / SampleRate;
                double osc2 = ComputeOsc(d.Wave, phaseAcc2, d.Duty, noise2, freq2 / SampleRate);
                osc = (osc + osc2) * 0.5;
            }

            if (d.RingModHz != 0)
            {
                ringPhaseAcc += d.RingModHz / SampleRate;
                osc *= Math.Sin(2 * Math.PI * ringPhaseAcc);
            }

            double env = d.Envelope.TotalTime > 0 ? d.Envelope.ValueAt(t) : 1.0;
            double sample = osc * env;

            if (d.GateHz != 0)
            {
                double gatePhase = (t * d.GateHz) % 1.0;
                if (gatePhase >= Math.Clamp(d.GateDuty, 0, 1)) sample = 0;
            }

            if (d.Drive > 0)
            {
                double amount = 1.0 + d.Drive * 12.0;
                sample = Math.Tanh(sample * amount) / Math.Tanh(amount);
            }

            if (d.CrushRateDivide > 1)
            {
                if (crushCounter == 0) crushHeld = sample;
                crushCounter = (crushCounter + 1) % crushDivide;
                sample = crushHeld;
            }

            if (d.CrushBits is > 0 and < 16)
            {
                double levels = Math.Pow(2, d.CrushBits) - 1;
                sample = Math.Round((sample * 0.5 + 0.5) * levels) / levels * 2.0 - 1.0;
            }

            if (filter != null)
            {
                // 2 倍オーバーサンプル: 入力をゼロ次ホールドしたまま SVF を 2 回通す。
                // カットオフの掃引も 2 回とも同じ progress の値を使う（サンプル内では十分滑らか）。
                double cutoff = Sweep.Value(d.FilterCutoffStart, d.FilterCutoffEnd, progress, SweepCurve.Linear);
                double resonance = Math.Clamp(d.FilterResonance, 0, 1);
                double os1 = filter.Process(sample, d.Filter, cutoff, resonance, SampleRate * 2);
                sample = filter.Process(os1, d.Filter, cutoff, resonance, SampleRate * 2);
            }

            buffer[i] = (float)Math.Clamp(sample * d.Volume, -4.0, 4.0);
        }

        return buffer;
    }

    private static double ComputeOsc(WaveKind wave, double phase, double duty, NoiseState? noise, double phaseStep)
        => Wave.IsNoise(wave) ? Wave.Noise(wave, noise!, phaseStep) : Wave.Sample(wave, phase, duty);

    /// <summary>tanh によるソフトクリップ。層を重ねた後の -1..1 収めに使う。</summary>
    public static double SoftClip(double x) => Math.Tanh(x);

    /// <summary>
    /// SfxDesc の内容から決定論的にノイズのシードを作る。
    /// HashCode.Combine や string.GetHashCode はプロセスごとに値が変わりうる（ハッシュランダム化）ため、
    /// ここでは FNV-1a を自前で回して安定させている。これが揺れると同じ音が毎回違う波形になり、
    /// AudioCache のキャッシュ（1 回焼いたら 2 回目以降は合成しない）が成立しなくなる。
    /// </summary>
    private static int StableSeed(SfxDesc d, int layerIndex)
    {
        unchecked
        {
            long h = 1469598103934665603L;
            void Mix(long v) { h ^= v; h *= 1099511628211L; }
            void MixD(double v) => Mix(BitConverter.DoubleToInt64Bits(v));

            MixD(d.FreqStart); MixD(d.FreqEnd); Mix((long)d.FreqSweepCurve);
            Mix((long)d.Wave); MixD(d.Duty);
            MixD(d.Duration); MixD(d.Volume);
            MixD(d.Envelope.Attack); MixD(d.Envelope.Decay); MixD(d.Envelope.SustainLevel);
            MixD(d.Envelope.SustainTime); MixD(d.Envelope.Release);
            MixD(d.FmRatio); MixD(d.FmIndex); MixD(d.FmIndexDecay);
            MixD(d.DetuneCents); MixD(d.RingModHz); MixD(d.Drive);
            Mix(d.CrushBits); Mix(d.CrushRateDivide);
            Mix((long)d.Filter); MixD(d.FilterCutoffStart); MixD(d.FilterCutoffEnd); MixD(d.FilterResonance);
            MixD(d.GateHz); MixD(d.GateDuty);
            MixD(d.VibratoHz); MixD(d.VibratoDepthCents);
            MixD(d.Pan);
            Mix(layerIndex);

            return (int)(h ^ (h >> 32));
        }
    }
}
