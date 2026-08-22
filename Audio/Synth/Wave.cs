namespace AstrumLoom.Audio.Synth;

/// <summary>波形の種類。SfxDesc.Wave に指定する。</summary>
public enum WaveKind
{
    Sine,
    Square,
    Saw,
    Triangle,
    WhiteNoise,
    PinkNoise,
    Metallic,       // 非整数倍音を重ねた金属音
    ShortNoise,     // LFSR による短周期ノイズ（8bit 風のジジジ音）
    TriangleNoise,  // 三角波にノイズを混ぜた、ノコギリより柔らかいノイズ
    PulseTrain,     // 短いパルスの列（Duty で間隔が変わる）
}

/// <summary>
/// 波形生成。位相 0..1 を受け取り -1..1 を返す純関数と、ノイズ系だけが持つ内部状態をまとめたもの。
///
/// 純音（Sine/Square/Saw/Triangle/PulseTrain）は位相だけから決まるので static メソッドで足りるが、
/// ノイズ系は「前のサンプル」を覚えていないと正しい色（帯域特性）が出ない。1 音につき 1 個の
/// <see cref="NoiseState"/> を持ち回して SfxRender から毎サンプル渡す設計にしている。
/// </summary>
public static class Wave
{
    /// <summary>位相 0..1 の純音・準純音を返す。ノイズ系はここでは呼べない（NoiseState が要る）。</summary>
    public static double Sample(WaveKind kind, double phase, double duty)
    {
        double p = phase - Math.Floor(phase); // 0..1 に正規化
        return kind switch
        {
            WaveKind.Sine => Math.Sin(p * Math.PI * 2),
            WaveKind.Square => p < Clamp01(duty) ? 1.0 : -1.0,
            WaveKind.Saw => p * 2.0 - 1.0,
            WaveKind.Triangle => 1.0 - 4.0 * Math.Abs(Math.Round(p - 0.25) - (p - 0.25)),
            WaveKind.PulseTrain => p < Clamp01(duty) * 0.25 ? 1.0 : -1.0, // 素の矩形より短いパルス幅
            WaveKind.Metallic => Metallic(p),
            // ノイズ系は状態が要るので Sample だけでは正しく鳴らない。呼び出し側のミスに早めに気付けるよう例外にする。
            WaveKind.WhiteNoise or WaveKind.PinkNoise or WaveKind.ShortNoise or WaveKind.TriangleNoise
                => throw new InvalidOperationException($"{kind} は Wave.Noise(NoiseState, ...) を使ってください。"),
            _ => 0.0,
        };
    }

    /// <summary>非整数倍音を数本重ねた金属音。ベルや衝突音の芯に使う。</summary>
    private static double Metallic(double p)
    {
        // 整数倍音だと普通の楽音になってしまうので、わざと 1.0 からずらした比率を使う。
        ReadOnlySpan<double> ratios = [1.0, 2.756, 5.404, 8.933, 13.34];
        ReadOnlySpan<double> weights = [1.0, 0.55, 0.33, 0.20, 0.12];
        double sum = 0, wsum = 0;
        for (int i = 0; i < ratios.Length; i++)
        {
            sum += Math.Sin(p * Math.PI * 2 * ratios[i]) * weights[i];
            wsum += weights[i];
        }
        return sum / wsum;
    }

    /// <summary>ノイズ系波形を 1 サンプル進める。state は音ごとに 1 個、毎サンプル同じインスタンスを渡すこと。</summary>
    public static double Noise(WaveKind kind, NoiseState state, double phaseStep) => kind switch
    {
        WaveKind.WhiteNoise => state.White(),
        WaveKind.PinkNoise => state.Pink(),
        WaveKind.ShortNoise => state.ShortLfsr(phaseStep),
        WaveKind.TriangleNoise => state.TriangleNoise(phaseStep),
        _ => throw new InvalidOperationException($"{kind} は Wave.Sample(kind, phase, duty) を使ってください。"),
    };

    public static bool IsNoise(WaveKind kind) =>
        kind is WaveKind.WhiteNoise or WaveKind.PinkNoise or WaveKind.ShortNoise or WaveKind.TriangleNoise;

    private static double Clamp01(double v) => Math.Clamp(v, 0.01, 0.99);
}

/// <summary>
/// ノイズ系波形が必要とする内部状態。1 音（1 レイヤー）につき 1 個作って使い回す。
/// 乱数は必ずシード固定（Randomize.Int/Double は共有インスタンスなので使わず、専用の Random を持つ）。
/// でないと同じ SfxDesc から毎回違う波形が出て、AudioCache のキャッシュが破綻する。
/// </summary>
public sealed class NoiseState
{
    private readonly Random _rng;

    // ピンクノイズ：Paul Kellet の近似式。数本のローパスを重ねて 1/f 特性を作る。
    private double _p0, _p1, _p2, _p3, _p4, _p5, _p6;

    // 短周期ノイズ：15bit LFSR（ファミコンのノイズチャンネルと同じ発想）。
    private uint _lfsr = 0x1;
    private double _lfsrPhaseAcc;
    private double _lfsrOut = 1.0;

    // 三角ノイズ用の位相アキュムレータと、直近のホールド値。
    private double _triPhaseAcc;
    private double _triOut;

    public NoiseState(int seed) => _rng = new Random(seed);

    public double White() => _rng.NextDouble() * 2.0 - 1.0;

    public double Pink()
    {
        double white = White();
        _p0 = 0.99886 * _p0 + white * 0.0555179;
        _p1 = 0.99332 * _p1 + white * 0.0750759;
        _p2 = 0.96900 * _p2 + white * 0.1538520;
        _p3 = 0.86650 * _p3 + white * 0.3104856;
        _p4 = 0.55000 * _p4 + white * 0.5329522;
        _p5 = -0.7616 * _p5 - white * 0.0168980;
        double pink = _p0 + _p1 + _p2 + _p3 + _p4 + _p5 + _p6 + white * 0.5362;
        _p6 = white * 0.115926;
        return Math.Clamp(pink * 0.11, -1.0, 1.0); // 経験的な正規化係数
    }

    /// <summary>phaseStep はこのサンプルが進める位相量（= freq / sampleRate）。ホールド周期の決定に使う。</summary>
    public double ShortLfsr(double phaseStep)
    {
        _lfsrPhaseAcc += phaseStep;
        if (_lfsrPhaseAcc >= 1.0)
        {
            _lfsrPhaseAcc -= Math.Floor(_lfsrPhaseAcc);
            // 15bit LFSR。タップは 15,14 ビット（ガロア方式）。
            uint bit = ((_lfsr >> 0) ^ (_lfsr >> 1)) & 1u;
            _lfsr = (_lfsr >> 1) | (bit << 14);
            _lfsrOut = (_lfsr & 1u) == 1u ? 1.0 : -1.0;
        }
        return _lfsrOut;
    }

    public double TriangleNoise(double phaseStep)
    {
        _triPhaseAcc += phaseStep;
        if (_triPhaseAcc >= 1.0)
        {
            _triPhaseAcc -= Math.Floor(_triPhaseAcc);
            _triOut = _rng.NextDouble() * 2.0 - 1.0;
        }
        // ホールドした乱数値へ向けて毎サンプル少し寄せる＝三角波でスムージングした階段状ノイズ。
        return _triOut;
    }
}
