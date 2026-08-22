using AstrumLoom.Audio.Synth;

namespace AstrumLoom.Audio;

/// <summary>効果音の識別子。SfxBank.Get(id) で SfxLayers（合成レシピ）が引ける。</summary>
public enum SfxId
{
    Shot,
    ShotHeavy,
    Hit,
    Explode,
    ExplodeBig,
    Damage,
    Pickup,
    Coin,
    PowerUp,
    Decide,
    Cancel,
    Cursor,
    Warning,
    Charge,
    Laser,
    Whoosh,
    Bounce,
    Break,
    Heal,
    Alarm,
    Chime,
    Thud,
    Sparkle,
    Zap,
}

/// <summary>
/// SfxId ごとのプリセット（合成レシピ）。ここに並んでいるのは「値」であって音そのものではない。
/// 実際の PCM は SfxRender が作り、AudioCache がそれをディスクへ焼いてから鳴らす。
///
/// どのプリセットも本当に音色が違うことを AudioSelfCheck が検算する（波形・フィルタ・FM・
/// リングモジュレータ・ビットクラッシュを使い分けて、単なる周波数違いに寄らないようにしている）。
/// </summary>
public static class SfxBank
{
    public static SfxLayers Get(SfxId id) => id switch
    {
        SfxId.Shot => new SfxDesc
        {
            FreqStart = 950,
            FreqEnd = 480,
            Wave = WaveKind.Square,
            Duty = 0.4,
            Duration = 0.08,
            Envelope = new Adsr(0.001, 0.03, 0.2, 0.01, 0.03),
            Volume = 0.8,
        },

        SfxId.ShotHeavy => new SfxLayers(
            new SfxLayer(new SfxDesc
            {
                FreqStart = 260,
                FreqEnd = 90,
                Wave = WaveKind.Saw,
                Duration = 0.16,
                Envelope = new Adsr(0.001, 0.06, 0.3, 0.03, 0.06),
                Drive = 0.35,
                Volume = 0.9,
            }),
            new SfxLayer(new SfxDesc
            {
                FreqStart = 4000,
                Wave = WaveKind.ShortNoise,
                Duration = 0.05,
                Envelope = new Adsr(0.0005, 0.02, 0, 0, 0.02),
                Volume = 0.6,
            }, OffsetSeconds: 0, Volume: 0.8)),

        SfxId.Hit => new SfxLayers(
            new SfxLayer(new SfxDesc
            {
                FreqStart = 3200,
                Wave = WaveKind.WhiteNoise,
                Duration = 0.06,
                Envelope = new Adsr(0.0005, 0.03, 0, 0, 0.025),
                Filter = FilterKind.BandPass,
                FilterCutoffStart = 2500,
                FilterCutoffEnd = 1400,
                FilterResonance = 0.35,
                Volume = 0.85,
            }),
            new SfxLayer(new SfxDesc
            {
                FreqStart = 180,
                FreqEnd = 90,
                Wave = WaveKind.Triangle,
                Duration = 0.08,
                Envelope = new Adsr(0.001, 0.06, 0, 0, 0.02),
                Volume = 0.6,
            })),

        SfxId.Explode => new SfxDesc
        {
            FreqStart = 3000,
            Wave = WaveKind.WhiteNoise,
            Duration = 0.45,
            Envelope = new Adsr(0.002, 0.35, 0.05, 0.02, 0.08),
            Filter = FilterKind.LowPass,
            FilterCutoffStart = 5200,
            FilterCutoffEnd = 250,
            FilterResonance = 0.2,
            Drive = 0.25,
            Volume = 1.0,
        },

        SfxId.ExplodeBig => new SfxLayers(
            new SfxLayer(new SfxDesc
            {
                FreqStart = 3200,
                Wave = WaveKind.WhiteNoise,
                Duration = 0.9,
                Envelope = new Adsr(0.002, 0.75, 0.08, 0.03, 0.10),
                Filter = FilterKind.LowPass,
                FilterCutoffStart = 5000,
                FilterCutoffEnd = 150,
                FilterResonance = 0.3,
                Drive = 0.4,
                Volume = 1.0,
            }),
            new SfxLayer(new SfxDesc
            {
                FreqStart = 90,
                FreqEnd = 35,
                Wave = WaveKind.Sine,
                Duration = 0.7,
                Envelope = new Adsr(0.002, 0.55, 0.1, 0.03, 0.05),
                Drive = 0.5,
                Volume = 0.9,
            }, OffsetSeconds: 0.02)),

        SfxId.Damage => new SfxDesc
        {
            FreqStart = 700,
            FreqEnd = 140,
            FreqSweepCurve = SweepCurve.Exponential,
            Wave = WaveKind.Saw,
            Duration = 0.30,
            Envelope = new Adsr(0.001, 0.10, 0.35, 0.10, 0.09),
            DetuneCents = 18,
            Volume = 0.85,
        },

        SfxId.Pickup => new SfxDesc
        {
            FreqStart = 420,
            FreqEnd = 1100,
            Wave = WaveKind.Triangle,
            Duration = 0.16,
            Envelope = new Adsr(0.002, 0.05, 0.5, 0.05, 0.05),
            Volume = 0.75,
        },

        SfxId.Coin => new SfxLayers(
            new SfxLayer(new SfxDesc
            {
                FreqStart = 1250,
                Wave = WaveKind.Square,
                Duty = 0.5,
                Duration = 0.06,
                Envelope = new Adsr(0.001, 0.02, 0.3, 0.01, 0.03),
                Volume = 0.7,
            }),
            new SfxLayer(new SfxDesc
            {
                FreqStart = 1660,
                Wave = WaveKind.Square,
                Duty = 0.5,
                Duration = 0.14,
                Envelope = new Adsr(0.001, 0.09, 0.2, 0.02, 0.06),
                Volume = 0.75,
            }, OffsetSeconds: 0.06)),

        SfxId.PowerUp => new SfxDesc
        {
            FreqStart = 220,
            FreqEnd = 1400,
            Wave = WaveKind.Saw,
            Duration = 0.5,
            Envelope = new Adsr(0.01, 0.10, 0.7, 0.25, 0.10),
            VibratoHz = 11,
            VibratoDepthCents = 25,
            Filter = FilterKind.LowPass,
            FilterCutoffStart = 1200,
            FilterCutoffEnd = 6000,
            FilterResonance = 0.3,
            Volume = 0.8,
        },

        SfxId.Decide => new SfxDesc
        {
            FreqStart = 660,
            FreqEnd = 990,
            Wave = WaveKind.Square,
            Duty = 0.5,
            Duration = 0.08,
            Envelope = new Adsr(0.001, 0.02, 0.4, 0.02, 0.03),
            Volume = 0.7,
        },

        SfxId.Cancel => new SfxDesc
        {
            FreqStart = 500,
            FreqEnd = 260,
            Wave = WaveKind.Square,
            Duty = 0.5,
            Duration = 0.10,
            Envelope = new Adsr(0.001, 0.03, 0.3, 0.03, 0.04),
            Volume = 0.65,
        },

        SfxId.Cursor => new SfxDesc
        {
            FreqStart = 1800,
            Wave = WaveKind.Triangle,
            Duration = 0.03,
            Envelope = new Adsr(0.0005, 0.015, 0, 0, 0.012),
            Volume = 0.5,
        },

        SfxId.Warning => new SfxDesc
        {
            FreqStart = 880,
            Wave = WaveKind.Square,
            Duty = 0.5,
            Duration = 0.5,
            Envelope = new Adsr(0.005, 0.02, 0.8, 0.40, 0.05),
            GateHz = 8,
            GateDuty = 0.5,
            Volume = 0.75,
        },

        SfxId.Charge => new SfxDesc
        {
            FreqStart = 90,
            FreqEnd = 620,
            FreqSweepCurve = SweepCurve.Exponential,
            Wave = WaveKind.Saw,
            Duration = 0.9,
            Envelope = new Adsr(0.05, 0.05, 0.85, 0.65, 0.10),
            Filter = FilterKind.LowPass,
            FilterCutoffStart = 400,
            FilterCutoffEnd = 5000,
            FilterResonance = 0.55,
            Volume = 0.8,
        },

        SfxId.Laser => new SfxDesc
        {
            FreqStart = 1800,
            FreqEnd = 220,
            FreqSweepCurve = SweepCurve.Exponential,
            Wave = WaveKind.PulseTrain,
            Duty = 0.3,
            Duration = 0.14,
            Envelope = new Adsr(0.001, 0.05, 0.2, 0.02, 0.06),
            RingModHz = 340,
            Volume = 0.8,
        },

        SfxId.Whoosh => new SfxDesc
        {
            FreqStart = 200,
            Wave = WaveKind.PinkNoise,
            Duration = 0.35,
            Envelope = new Adsr(0.05, 0.10, 0.6, 0.10, 0.15),
            Filter = FilterKind.BandPass,
            FilterCutoffStart = 300,
            FilterCutoffEnd = 3200,
            FilterResonance = 0.4,
            Volume = 0.7,
        },

        SfxId.Bounce => new SfxDesc
        {
            FreqStart = 220,
            FreqEnd = 660,
            FreqSweepCurve = SweepCurve.Exponential,
            Wave = WaveKind.Sine,
            Duration = 0.09,
            Envelope = new Adsr(0.001, 0.04, 0.1, 0.01, 0.03),
            Volume = 0.7,
        },

        SfxId.Break => new SfxLayers(
            new SfxLayer(new SfxDesc
            {
                FreqStart = 2600,
                Wave = WaveKind.ShortNoise,
                Duration = 0.12,
                Envelope = new Adsr(0.001, 0.09, 0, 0, 0.02),
                Volume = 0.8,
            }),
            new SfxLayer(new SfxDesc
            {
                FreqStart = 900,
                FreqEnd = 300,
                Wave = WaveKind.TriangleNoise,
                Duration = 0.18,
                Envelope = new Adsr(0.001, 0.14, 0, 0, 0.03),
                Volume = 0.55,
            }, OffsetSeconds: 0.01)),

        SfxId.Heal => new SfxDesc
        {
            FreqStart = 660,
            FreqEnd = 990,
            Wave = WaveKind.Triangle,
            Duration = 0.55,
            Envelope = new Adsr(0.03, 0.10, 0.6, 0.25, 0.20),
            FmRatio = 2.0,
            FmIndex = 0.4,
            FmIndexDecay = 0.8,
            VibratoHz = 5,
            VibratoDepthCents = 12,
            Volume = 0.65,
        },

        SfxId.Alarm => new SfxDesc
        {
            FreqStart = 700,
            FreqEnd = 900,
            Wave = WaveKind.Square,
            Duty = 0.5,
            Duration = 0.6,
            Envelope = new Adsr(0.005, 0.02, 0.9, 0.50, 0.05),
            GateHz = 4,
            GateDuty = 0.55,
            Volume = 0.8,
        },

        SfxId.Chime => new SfxDesc
        {
            FreqStart = 1320,
            Wave = WaveKind.Metallic,
            Duration = 1.1,
            Envelope = new Adsr(0.002, 0.5, 0.1, 0.1, 0.6),
            FmRatio = 3.5,
            FmIndex = 0.6,
            FmIndexDecay = 1.0,
            Volume = 0.6,
        },

        SfxId.Thud => new SfxDesc
        {
            FreqStart = 130,
            FreqEnd = 55,
            Wave = WaveKind.Sine,
            Duration = 0.12,
            Envelope = new Adsr(0.001, 0.09, 0, 0, 0.02),
            Drive = 0.3,
            Volume = 0.75,
        },

        SfxId.Sparkle => new SfxLayers(
            new SfxLayer(new SfxDesc
            {
                FreqStart = 2400,
                Wave = WaveKind.Metallic,
                Duration = 0.35,
                Envelope = new Adsr(0.001, 0.20, 0.05, 0.05, 0.10),
                VibratoHz = 18,
                VibratoDepthCents = 40,
                Volume = 0.55,
            }),
            new SfxLayer(new SfxDesc
            {
                FreqStart = 3600,
                Wave = WaveKind.Metallic,
                Duration = 0.30,
                Envelope = new Adsr(0.001, 0.18, 0.05, 0.04, 0.08),
                DetuneCents = 14,
                Volume = 0.4,
            }, OffsetSeconds: 0.03)),

        SfxId.Zap => new SfxDesc
        {
            FreqStart = 1400,
            FreqEnd = 300,
            Wave = WaveKind.Square,
            Duty = 0.5,
            Duration = 0.12,
            Envelope = new Adsr(0.001, 0.04, 0.15, 0.02, 0.05),
            RingModHz = 220,
            CrushBits = 5,
            CrushRateDivide = 3,
            Volume = 0.75,
        },

        _ => throw new ArgumentOutOfRangeException(nameof(id), id, null),
    };

    /// <summary>定義済み全プリセットの一覧。Prewarm や自己検算で使う。</summary>
    public static IReadOnlyList<SfxId> All { get; } = Enum.GetValues<SfxId>();
}
