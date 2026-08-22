namespace AstrumLoom.Audio.Bgm;

/// <summary>曲の記述子1つのノート。StartBeat/LengthBeats は拍単位（4/4 なら 1 拍 = 四分音符）。</summary>
public sealed record BgmNote(double StartBeat, double LengthBeats, int MidiNote, double Velocity = 1.0);

/// <summary>1 声部。Instrument で音色、Notes で譜面を持つ。EchoDelayBeats/EchoGain は 0 なら無効（規約は SfxDesc と共通）。</summary>
public sealed class BgmTrack
{
    public InstrumentKind Instrument { get; init; }
    public double Volume { get; init; } = 1.0;
    /// <summary>-1..1。</summary>
    public double Pan { get; init; } = 0.0;
    /// <summary>ディレイの時間（拍）。0 で無効。</summary>
    public double EchoDelayBeats { get; init; } = 0;
    /// <summary>ディレイのゲイン（0..1想定、1回だけ重ねる簡易エコー）。EchoDelayBeats=0 のときは無視。</summary>
    public double EchoGain { get; init; } = 0;
    public List<BgmNote> Notes { get; init; } = [];
}

/// <summary>曲全体の記述子。Sequencer がこれをステレオ PCM へ焼く。</summary>
public sealed class BgmScore
{
    public double Bpm { get; init; } = 120;
    /// <summary>1 ループぶんの拍数。4/4 で 4 小節なら 16。</summary>
    public double Beats { get; init; } = 16;
    /// <summary>スウィング量（0..1）。0 で無効（偶数拍を後ろへ少しずらすシャッフル感）。</summary>
    public double Swing { get; init; } = 0;
    public List<BgmTrack> Tracks { get; init; } = [];
}

/// <summary>音名文字列（"C4", "A#3", "Bb2" など）を MIDI ノート番号へ変換する。C4 = 60（MIDI 標準）。</summary>
public static class NoteName
{
    private static readonly Dictionary<char, int> BaseSemitone = new()
    {
        ['C'] = 0,
        ['D'] = 2,
        ['E'] = 4,
        ['F'] = 5,
        ['G'] = 7,
        ['A'] = 9,
        ['B'] = 11,
    };

    public static int Parse(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new FormatException("音名が空です。");

        int i = 0;
        char letter = char.ToUpperInvariant(name[i++]);
        if (!BaseSemitone.TryGetValue(letter, out int semitone))
            throw new FormatException($"不明な音名です: {name}");

        int accidental = 0;
        if (i < name.Length && (name[i] == '#' || name[i] == 'b'))
        {
            accidental = name[i] == '#' ? 1 : -1;
            i++;
        }

        if (i >= name.Length || !int.TryParse(name[i..], out int octave))
            throw new FormatException($"オクターブが読めません: {name}");

        // MIDI: C4 = 60。 (octave+1)*12 が「その オクターブの C」の番号。
        return (octave + 1) * 12 + semitone + accidental;
    }
}

/// <summary>BgmTrack.Notes を手書きするための組み立てヘルパ。</summary>
public static class BgmBuild
{
    /// <summary>
    /// (音名 or null=休符, 長さ拍) の並びから、開始拍を自動で積み上げて BgmNote の列を作る。
    /// 例: Melody([("C4", 1), (null, 0.5), ("E4", 0.5), ("G4", 2)])
    /// </summary>
    public static List<BgmNote> Melody((string? Note, double LengthBeats)[] steps, double startBeat = 0, double velocity = 1.0)
    {
        var list = new List<BgmNote>();
        double t = startBeat;
        foreach (var (note, len) in steps)
        {
            if (!string.IsNullOrEmpty(note))
                list.Add(new BgmNote(t, len, NoteName.Parse(note), velocity));
            t += len;
        }
        return list;
    }

    /// <summary>
    /// ドラムパターン文字列からノートを並べる。'x'=通常、'X'=アクセント、それ以外（'.'等）は休符。
    /// MidiNote はドラムでは無視されるので固定値 60 を積む。
    /// 例: DrumPattern("x...x...x...x...", 0.25) は 16 分音符でキックを打つ 1 小節分。
    /// </summary>
    public static List<BgmNote> DrumPattern(string pattern, double stepBeats, double startBeat = 0,
        double velocity = 1.0, double accentVelocity = 1.0)
    {
        var list = new List<BgmNote>();
        double t = startBeat;
        foreach (char c in pattern)
        {
            if (c is 'x' or 'X')
                list.Add(new BgmNote(t, stepBeats, 60, c == 'X' ? accentVelocity : velocity));
            t += stepBeats;
        }
        return list;
    }
}
