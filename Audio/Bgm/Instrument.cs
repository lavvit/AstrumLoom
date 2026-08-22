using AstrumLoom.Audio.Synth;

namespace AstrumLoom.Audio.Bgm;

/// <summary>BgmTrack.Instrument に指定する音色。声部用（メロディ/ベース/パッド系）とドラム用がある。</summary>
public enum InstrumentKind
{
    // --- 声部 -----------------------------------------------------------
    SquareLead,
    PulseLead,
    SawBass,
    TriangleBass,
    Bell,
    Pad,
    Pluck,
    Organ,

    // --- ドラム（IsDrum が true。音高は無視して固定の音を鳴らす）-----------
    Kick,
    Snare,
    HihatClosed,
    HihatOpen,
    Tom,
    Crash,
}

/// <summary>
/// InstrumentKind ごとの音色テンプレートを、実際の音高・長さ・ベロシティに合わせた SfxDesc へ組み立てる。
/// Sequencer はここで作った SfxDesc を SfxRender.RenderSingle にそのまま渡すだけでよい。
/// </summary>
public static class Instrument
{
    public static bool IsDrum(InstrumentKind kind) => kind >= InstrumentKind.Kick;

    /// <summary>MIDI ノート番号（60=C4）を周波数（Hz）へ変換する。</summary>
    public static double NoteToFreq(int midiNote) => 440.0 * Math.Pow(2.0, (midiNote - 69) / 12.0);

    /// <summary>
    /// 1 音分の SfxDesc を作る。durationSeconds は「この音符に割り当てられた長さ」で、
    /// エンベロープの Sustain 部分の長さはここから A/D/R を引いた残りに自動で割り当てる
    /// （短い音符で ADSR が長すぎて途中で切れる、ということが起きないように）。
    /// </summary>
    public static SfxDesc BuildDesc(InstrumentKind kind, int midiNote, double durationSeconds, double velocity01)
    {
        double freq = NoteToFreq(midiNote);
        double vel = Math.Clamp(velocity01, 0, 1);
        durationSeconds = Math.Max(0.02, durationSeconds);

        return kind switch
        {
            InstrumentKind.SquareLead => Melodic(freq, freq, durationSeconds, vel, WaveKind.Square,
                duty: 0.5, attack: 0.004, decay: 0.05, sustainLevel: 0.75, release: 0.06),
            InstrumentKind.PulseLead => Melodic(freq, freq, durationSeconds, vel, WaveKind.PulseTrain,
                duty: 0.3, attack: 0.002, decay: 0.03, sustainLevel: 0.7, release: 0.05),
            InstrumentKind.SawBass => Melodic(freq, freq, durationSeconds, vel, WaveKind.Saw,
                duty: 0.5, attack: 0.006, decay: 0.08, sustainLevel: 0.85, release: 0.05,
                filter: FilterKind.LowPass, cutoffStart: freq * 6, cutoffEnd: freq * 3, resonance: 0.25),
            InstrumentKind.TriangleBass => Melodic(freq, freq, durationSeconds, vel, WaveKind.Triangle,
                duty: 0.5, attack: 0.008, decay: 0.10, sustainLevel: 0.9, release: 0.08),
            InstrumentKind.Bell => Melodic(freq, freq, durationSeconds, vel, WaveKind.Metallic,
                duty: 0.5, attack: 0.002, decay: 0.30, sustainLevel: 0.15, release: 0.35,
                fmRatio: 3.0, fmIndex: 1.4, fmIndexDecay: 1.0),
            InstrumentKind.Pad => Melodic(freq, freq, durationSeconds, vel, WaveKind.Saw,
                duty: 0.5, attack: 0.15, decay: 0.20, sustainLevel: 0.8, release: 0.35,
                detuneCents: 9, filter: FilterKind.LowPass, cutoffStart: freq * 4, cutoffEnd: freq * 4, resonance: 0.1),
            InstrumentKind.Pluck => Melodic(freq, freq, durationSeconds, vel, WaveKind.Triangle,
                duty: 0.5, attack: 0.002, decay: 0.14, sustainLevel: 0.05, release: 0.10),
            InstrumentKind.Organ => Melodic(freq, freq, durationSeconds, vel, WaveKind.Square,
                duty: 0.5, attack: 0.01, decay: 0.02, sustainLevel: 0.95, release: 0.03,
                detuneCents: 5),

            InstrumentKind.Kick => Drum(freqStart: 150, freqEnd: 42, duration: 0.16, vel: vel, wave: WaveKind.Sine,
                attack: 0.001, decay: 0.14, sustainLevel: 0, release: 0.02, drive: 0.25),
            InstrumentKind.Snare => Drum(freqStart: 220, freqEnd: 140, duration: 0.14, vel: vel, wave: WaveKind.ShortNoise,
                attack: 0.001, decay: 0.10, sustainLevel: 0, release: 0.03,
                filter: FilterKind.HighPass, cutoffStart: 900, cutoffEnd: 900, resonance: 0.1),
            InstrumentKind.HihatClosed => Drum(freqStart: 5500, freqEnd: 5500, duration: 0.045, vel: vel, wave: WaveKind.WhiteNoise,
                attack: 0.001, decay: 0.035, sustainLevel: 0, release: 0.01,
                filter: FilterKind.HighPass, cutoffStart: 6500, cutoffEnd: 6500, resonance: 0.05),
            InstrumentKind.HihatOpen => Drum(freqStart: 5500, freqEnd: 5500, duration: 0.28, vel: vel, wave: WaveKind.WhiteNoise,
                attack: 0.001, decay: 0.24, sustainLevel: 0, release: 0.03,
                filter: FilterKind.HighPass, cutoffStart: 6000, cutoffEnd: 6000, resonance: 0.05),
            InstrumentKind.Tom => Drum(freqStart: 180, freqEnd: 90, duration: 0.20, vel: vel, wave: WaveKind.Sine,
                attack: 0.001, decay: 0.18, sustainLevel: 0, release: 0.03),
            InstrumentKind.Crash => Drum(freqStart: 6000, freqEnd: 6000, duration: 0.8, vel: vel, wave: WaveKind.PinkNoise,
                attack: 0.002, decay: 0.7, sustainLevel: 0, release: 0.1,
                filter: FilterKind.HighPass, cutoffStart: 4000, cutoffEnd: 4000, resonance: 0.1),

            _ => SfxDesc.Default,
        };
    }

    private static SfxDesc Melodic(double freqStart, double freqEnd, double duration, double vel, WaveKind wave,
        double duty, double attack, double decay, double sustainLevel, double release,
        double fmRatio = 0, double fmIndex = 0, double fmIndexDecay = 0, double detuneCents = 0,
        FilterKind filter = FilterKind.None, double cutoffStart = 4000, double cutoffEnd = 4000, double resonance = 0)
    {
        double sustainTime = Math.Max(0, duration - attack - decay - release);
        return new SfxDesc
        {
            FreqStart = freqStart,
            FreqEnd = freqEnd,
            Wave = wave,
            Duty = duty,
            Duration = duration,
            Envelope = new Adsr(attack, decay, sustainLevel, sustainTime, release),
            Volume = 0.55 + 0.45 * vel,
            FmRatio = fmRatio,
            FmIndex = fmIndex,
            FmIndexDecay = fmIndexDecay,
            DetuneCents = detuneCents,
            Filter = filter,
            FilterCutoffStart = cutoffStart,
            FilterCutoffEnd = cutoffEnd,
            FilterResonance = resonance,
        };
    }

    private static SfxDesc Drum(double freqStart, double freqEnd, double duration, double vel, WaveKind wave,
        double attack, double decay, double sustainLevel, double release, double drive = 0,
        FilterKind filter = FilterKind.None, double cutoffStart = 4000, double cutoffEnd = 4000, double resonance = 0)
    {
        double sustainTime = Math.Max(0, duration - attack - decay - release);
        return new SfxDesc
        {
            FreqStart = freqStart,
            FreqEnd = freqEnd,
            Wave = wave,
            Duration = duration,
            Envelope = new Adsr(attack, decay, sustainLevel, sustainTime, release),
            Volume = 0.6 + 0.4 * vel,
            Drive = drive,
            Filter = filter,
            FilterCutoffStart = cutoffStart,
            FilterCutoffEnd = cutoffEnd,
            FilterResonance = resonance,
        };
    }
}
