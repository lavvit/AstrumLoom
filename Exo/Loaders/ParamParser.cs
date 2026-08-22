using System.Globalization;

namespace AstrumLoom.Exo.Loaders;

/// <summary>
/// exo / aup2 で共通して使われる「数値+中間点+イージング」形式のパラメータ行を解析するヘルパー。
/// exo: X=-400.0,-400.0,15@easing_normal@uf_easing,1 （プラグイン名で表現）
/// aup2: 透明度=100.00,0.00,直線移動,0 （日本語のイージング名で表現）
/// と表記が異なるため、末尾のイージング表現だけを個別に読み分ける。
/// </summary>
internal static class ParamParser
{
    /// <summary>Start/End値とイージング種別を保持する解析結果。</summary>
    public readonly struct Result(float start, float end, bool hasEnd, UfEasing easing)
    {
        /// <summary>開始値。</summary>
        public float Start { get; } = start;
        /// <summary>終了値（中間点が無ければ Start と同じ）。</summary>
        public float End { get; } = end;
        /// <summary>End値が明示されていたか。</summary>
        public bool HasEnd { get; } = hasEnd;
        /// <summary>イージング種別。読み取れなければ Linear。</summary>
        public UfEasing Easing { get; } = easing;
    }

    /// <summary>
    /// "key=" を取り除いた後の値部分を解析する。
    /// </summary>
    /// <param name="value">"key=" より後ろの文字列</param>
    public static Result Parse(string value)
    {
        string[] parts = value.Split(',');

        float start = float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float s) ? s : 0f;
        bool hasEnd = parts.Length > 1;
        float end = hasEnd && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float e) ? e : start;

        UfEasing easing = UfEasing.Linear;
        if (parts.Length > 2)
        {
            string easingRaw = parts[2];
            if (easingRaw.Contains('@'))
            {
                // exo形式: "15@easing_normal@uf_easing" のように数値の後にプラグイン名が続く。
                // 旧 Extend/ExoAnimation.cs の挙動に合わせ、プラグイン名に関係なく4番目の値をイージングIDとして読む。
                if (parts.Length > 3 && int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int easingId)
                    && Enum.IsDefined(typeof(UfEasing), easingId))
                    easing = (UfEasing)easingId;
            }
            else
            {
                // aup2形式: 日本語のイージング名（例: "直線移動"）。現状は線形移動のみ対応し、
                // 未知の名前は警告なしで Linear にフォールバックする（描画結果としては差が小さいため）。
                easing = easingRaw switch
                {
                    "直線移動" => UfEasing.Linear,
                    _ => UfEasing.Linear,
                };
            }
        }

        return new Result(start, end, hasEnd, easing);
    }
}
