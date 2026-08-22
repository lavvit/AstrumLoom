using AstrumLoom.Audio.Synth;

namespace AstrumLoom.Audio.Bgm;

/// <summary>
/// BgmScore をステレオ PCM（インターリーブ float、-1..1、44100Hz）へ焼く。
///
/// ループの継ぎ目でぷつっと切れないよう、ループ長より長め（最大 2 秒）のバッファへ全音を描き込んでから、
/// はみ出した末尾（余韻・エコー・リリース）をループ先頭へ折り返して加算し、ちょうどループ長に切り詰める。
/// こうしないと、音の減衰が長い Bell や Pad、EchoGain を使ったトラックで、ループ2周目の頭に
/// 「本来なら聞こえているはずの残響」が欠けて聞こえる（継ぎ目の段差として耳でも検算でも分かる）。
/// </summary>
public static class Sequencer
{
    public const int SampleRate = SfxRender.SampleRate;
    private const double MaxTailSeconds = 2.0;

    public static float[] Render(BgmScore score)
    {
        double secPerBeat = 60.0 / Math.Max(1, score.Bpm);
        int loopSamples = Math.Max(1, (int)Math.Round(score.Beats * secPerBeat * SampleRate));
        int tailSamples = Math.Min(loopSamples, (int)Math.Round(MaxTailSeconds * SampleRate));
        int extendedSamples = loopSamples + tailSamples;

        // L/R をインターリーブせず、まず別々の配列で合成してから最後に詰める（折り返し計算が単純になる）。
        var mixL = new float[extendedSamples];
        var mixR = new float[extendedSamples];

        foreach (var track in score.Tracks)
            RenderTrack(track, secPerBeat, score.Swing, extendedSamples, mixL, mixR);

        // 末尾のはみ出し分をループ先頭へ折り返して加算する。
        for (int i = 0; i < tailSamples; i++)
        {
            mixL[i] += mixL[loopSamples + i];
            mixR[i] += mixR[loopSamples + i];
        }

        var result = new float[loopSamples * 2];
        for (int i = 0; i < loopSamples; i++)
        {
            result[i * 2 + 0] = (float)SfxRender.SoftClip(mixL[i]);
            result[i * 2 + 1] = (float)SfxRender.SoftClip(mixR[i]);
        }
        return result;
    }

    private static void RenderTrack(BgmTrack track, double secPerBeat, double swing, int extendedSamples,
        float[] mixL, float[] mixR)
    {
        var trackBuf = new float[extendedSamples];

        foreach (var note in track.Notes)
        {
            double startBeat = ApplySwing(note.StartBeat, swing);
            int startSample = (int)Math.Round(startBeat * secPerBeat * SampleRate);
            if (startSample >= extendedSamples) continue;

            double lengthSeconds = Math.Max(0.02, note.LengthBeats * secPerBeat);
            var desc = Instrument.BuildDesc(track.Instrument, note.MidiNote, lengthSeconds, note.Velocity);
            float[] rendered = SfxRender.RenderSingle(desc);

            for (int i = 0; i < rendered.Length; i++)
            {
                int di = startSample + i;
                if (di >= extendedSamples) break;
                trackBuf[di] += rendered[i];
            }
        }

        // ディレイ（フィードバック無しの単発エコー）。EchoDelayBeats/EchoGain が 0 なら無効。
        if (track.EchoDelayBeats > 0 && track.EchoGain > 0)
        {
            int delaySamples = (int)Math.Round(track.EchoDelayBeats * secPerBeat * SampleRate);
            if (delaySamples > 0 && delaySamples < extendedSamples)
            {
                var echo = new float[extendedSamples];
                for (int i = delaySamples; i < extendedSamples; i++)
                    echo[i] = trackBuf[i - delaySamples] * (float)track.EchoGain;
                for (int i = 0; i < extendedSamples; i++)
                    trackBuf[i] += echo[i];
            }
        }

        // 等パワーパン則。Pan=0 で両chとも約0.707（フルボリューム同士を単純に足すよりナチュラル）。
        double panRad = (Math.Clamp(track.Pan, -1, 1) + 1.0) * (Math.PI / 4.0); // 0..pi/2
        double gainL = Math.Cos(panRad) * track.Volume;
        double gainR = Math.Sin(panRad) * track.Volume;

        for (int i = 0; i < extendedSamples; i++)
        {
            mixL[i] += (float)(trackBuf[i] * gainL);
            mixR[i] += (float)(trackBuf[i] * gainR);
        }
    }

    /// <summary>裏拍（8分音符のオフビート）だけ少し後ろへずらしてシャッフル感を出す。Swing=0 で無効。</summary>
    private static double ApplySwing(double startBeat, double swing)
    {
        if (swing <= 0) return startBeat;
        double frac = startBeat - Math.Floor(startBeat);
        bool offbeat = Math.Abs(frac - 0.5) < 0.01;
        return offbeat ? startBeat + swing * (1.0 / 3.0) * 0.5 : startBeat;
    }
}
