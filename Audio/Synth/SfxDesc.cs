namespace AstrumLoom.Audio.Synth;

/// <summary>
/// 効果音 1 音分の記述子。値はすべてこの record が持ち、AudioCache がこれをハッシュしてキャッシュキーにする。
///
/// 「追加パラメータは 0 なら無効」規約: FM・デチューン・リングモジュレータ・ドライブ・ビットクラッシュ・
/// フィルタ・刻み再生・ビブラートは、対応する量が 0（または FilterKind.None）なら一切効かない。
/// これにより、SfxDesc.Default から必要な差分だけを with 式で書けば、後から項目を増やしても
/// 既存のプリセットの音が変わらない。
/// </summary>
public sealed record SfxDesc
{
    // --- 周波数 ---------------------------------------------------------
    public double FreqStart { get; init; } = 440;
    public double FreqEnd { get; init; } = 440;
    public SweepCurve FreqSweepCurve { get; init; } = SweepCurve.Linear;

    // --- 波形 -------------------------------------------------------------
    public WaveKind Wave { get; init; } = WaveKind.Square;
    /// <summary>Square/PulseTrain のデューティ比（0..1）。それ以外の波形では無視される。</summary>
    public double Duty { get; init; } = 0.5;

    // --- 長さ・音量・エンベロープ ------------------------------------------
    public double Duration { get; init; } = 0.18;
    public Adsr Envelope { get; init; } = Adsr.Default;
    public double Volume { get; init; } = 1.0;

    // --- 2op FM（0 なら無効）------------------------------------------------
    /// <summary>モジュレータ周波数 = キャリア周波数 * FmRatio。FmIndex が 0 なら FM 自体が無効。</summary>
    public double FmRatio { get; init; } = 0;
    /// <summary>変調指数（モジュレータの振れ幅）。0 で FM 無効。</summary>
    public double FmIndex { get; init; } = 0;
    /// <summary>変調指数が音の間にどこまで減衰するか（0..1、1 なら最後に 0 まで減衰）。FmIndex=0 のときは無視。</summary>
    public double FmIndexDecay { get; init; } = 0;

    // --- デチューン（セント、0 なら無効）-----------------------------------
    /// <summary>0 でなければ、元の音に加えてこのセント分ずらした音をもう 1 系統重ねる（うなり・厚み）。</summary>
    public double DetuneCents { get; init; } = 0;

    // --- リングモジュレータ（0 なら無効）------------------------------------
    public double RingModHz { get; init; } = 0;

    // --- ドライブ（歪み、0 なら無効）---------------------------------------
    /// <summary>0..1。0 で無効。上げるほど tanh 歪みが強くかかる。</summary>
    public double Drive { get; init; } = 0;

    // --- ビットクラッシュ（0 なら無効）--------------------------------------
    /// <summary>量子化ビット数。0 で無効（16bit のまま）。</summary>
    public int CrushBits { get; init; } = 0;
    /// <summary>間引き倍率。2 以上でサンプル&ホールドして疑似的にサンプルレートを落とす。0/1 で無効。</summary>
    public int CrushRateDivide { get; init; } = 0;

    // --- フィルタ（FilterKind.None なら無効）--------------------------------
    public FilterKind Filter { get; init; } = FilterKind.None;
    public double FilterCutoffStart { get; init; } = 4000;
    public double FilterCutoffEnd { get; init; } = 4000;
    public double FilterResonance { get; init; } = 0;

    // --- 刻み再生（トレモロ的なゲート、0 なら無効）---------------------------
    /// <summary>ゲートの周期（Hz）。0 で無効（常時開いた状態＝素通し）。</summary>
    public double GateHz { get; init; } = 0;
    /// <summary>ゲートが開いている比率（0..1）。GateHz=0 のときは無視。</summary>
    public double GateDuty { get; init; } = 0.5;

    // --- ビブラート（0 なら無効）---------------------------------------------
    public double VibratoHz { get; init; } = 0;
    /// <summary>ビブラートの深さ（セント）。VibratoHz=0 のときは無視。</summary>
    public double VibratoDepthCents { get; init; } = 0;

    // --- パン -------------------------------------------------------------
    public double Pan { get; init; } = 0;

    public static SfxDesc Default => new();
}

/// <summary>SfxDesc 1 層と、重ねるためのオフセット秒・音量。</summary>
public readonly record struct SfxLayer(SfxDesc Desc, double OffsetSeconds = 0, double Volume = 1.0);

/// <summary>効果音を構成する層（最大 3 層）。SfxBank の全プリセットはこれを返す。</summary>
public sealed class SfxLayers
{
    public IReadOnlyList<SfxLayer> Layers { get; }

    public SfxLayers(params SfxLayer[] layers)
    {
        if (layers.Length is 0 or > 3)
            throw new ArgumentException($"SfxLayers は 1〜3 層でなければなりません（{layers.Length} 層指定）。");
        Layers = layers;
    }

    public static implicit operator SfxLayers(SfxDesc single) => new(new SfxLayer(single));
}
