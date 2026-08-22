namespace AstrumLoom.Audio.Synth;

/// <summary>
/// ADSR エンベロープ（秒指定）。効果音は「鍵盤を離す」概念が無いので、Release は
/// 「Attack+Decay+Sustain が終わったあとに自動で始まる」ものとして扱う（＝総尺 = A+D+S+R）。
/// </summary>
public readonly struct Adsr(double attack, double decay, double sustainLevel, double sustainTime, double release)
{
    public double Attack { get; } = Math.Max(0, attack);
    public double Decay { get; } = Math.Max(0, decay);
    /// <summary>Decay が終わったあとに維持する音量（0..1）。</summary>
    public double SustainLevel { get; } = Math.Clamp(sustainLevel, 0, 1);
    /// <summary>Sustain を維持する秒数。</summary>
    public double SustainTime { get; } = Math.Max(0, sustainTime);
    public double Release { get; } = Math.Max(0, release);

    /// <summary>この ADSR が鳴り終わるまでの総尺（秒）。</summary>
    public double TotalTime => Attack + Decay + SustainTime + Release;

    /// <summary>既定値。短いクリック的な効果音にちょうどいい程度の値。</summary>
    public static Adsr Default => new(0.004, 0.06, 0.65, 0.03, 0.10);

    /// <summary>時刻 t（秒、0 起点）でのエンベロープ値（0..1）。総尺を過ぎたら 0。</summary>
    public double ValueAt(double t)
    {
        if (t < 0) return 0;
        if (t < Attack)
            return Attack <= 0 ? 1.0 : t / Attack;
        t -= Attack;
        if (t < Decay)
            return Decay <= 0 ? SustainLevel : 1.0 + (SustainLevel - 1.0) * (t / Decay);
        t -= Decay;
        if (t < SustainTime)
            return SustainLevel;
        t -= SustainTime;
        if (t < Release)
            return Release <= 0 ? 0.0 : SustainLevel * (1.0 - t / Release);
        return 0.0;
    }
}

/// <summary>ピッチ掃引のカーブ形状。</summary>
public enum SweepCurve
{
    Linear,
    Exponential,
}

/// <summary>
/// 開始倍率→終了倍率のピッチ（または任意パラメータの）掃引。
/// 例えば FreqStart=800, FreqEnd=200 の「しぼむ音」は StartRatio=1, EndRatio=0.25 のように
/// 呼び出し側が比率へ変換して渡す使い方を想定しているが、SfxRender では周波数そのものを渡す。
/// </summary>
public static class Sweep
{
    /// <summary>progress（0..1、経過秒/総尺）に応じて start と end を補間する。</summary>
    public static double Value(double start, double end, double progress, SweepCurve curve)
    {
        progress = Math.Clamp(progress, 0, 1);
        if (curve == SweepCurve.Linear || start <= 0 || end <= 0)
            return start + (end - start) * progress;

        // 指数掃引：対数空間で線形補間する。start/end が 0 以下だと対数が取れないので上の分岐で弾く。
        double logStart = Math.Log(start);
        double logEnd = Math.Log(end);
        return Math.Exp(logStart + (logEnd - logStart) * progress);
    }
}
