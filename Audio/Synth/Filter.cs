namespace AstrumLoom.Audio.Synth;

/// <summary>フィルタの種類。None は「フィルタ無し」で、SfxDesc の 0=無効 規約に対応する。</summary>
public enum FilterKind
{
    None,
    LowPass,
    HighPass,
    BandPass,
}

/// <summary>
/// 共振つき state-variable フィルタ（Chamberlin 型）。カットオフを開始→終了へ掃引できる。
///
/// 共振（Resonance）を上げるとこの手のフィルタは簡単に発散する。ここでは
///   ・damping を 0 未満にしない（= Q を無限大にしない）
///   ・2 倍オーバーサンプルで走らせ、ナイキスト付近の不安定さを避ける
/// の 2 点で対策している。AudioSelfCheck が「共振最大でも発散しない」を検算する。
/// </summary>
public sealed class SvFilter
{
    private double _low, _band;

    /// <summary>
    /// 1 サンプル処理する。sampleRate は実際の駆動レート（呼び出し側で 2 倍オーバーサンプルする場合は
    /// 元のサンプルレートの 2 倍を渡すこと）。cutoffHz は毎サンプル変えてよい（掃引用）。
    /// resonance は 0..1（1 が最も鋭い。発散はしない上限にクランプ済み）。
    /// </summary>
    public double Process(double input, FilterKind kind, double cutoffHz, double resonance, double sampleRate)
    {
        if (kind == FilterKind.None) return input;

        double nyquist = sampleRate * 0.5;
        double f = 2.0 * Math.Sin(Math.PI * Math.Clamp(cutoffHz, 10.0, nyquist * 0.98) / sampleRate);
        // damping（=1/Q 相当）。resonance=1 でも 0 に触れないようクランプし、発散を防ぐ。
        double damping = Math.Clamp(2.0 - 1.98 * Math.Clamp(resonance, 0, 1), 0.02, 2.0);

        double notch = input - damping * _band;
        _low += f * _band;
        double high = notch - _low;
        _band += f * high;

        // フィルタ係数自体が発散方向に暴れても出力だけは必ず有限にしておく（最終防衛ライン）。
        _low = Clamp(_low);
        _band = Clamp(_band);

        return kind switch
        {
            FilterKind.LowPass => _low,
            FilterKind.HighPass => high,
            FilterKind.BandPass => _band,
            _ => input,
        };
    }

    private static double Clamp(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
        return Math.Clamp(v, -8.0, 8.0);
    }

    public void Reset() => _low = _band = 0;
}
